using System;
using System.Collections.Generic;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Models;
using RelativeIllumination.Core.Paraxial;

namespace RelativeIllumination.Core.RayTrace;

/// <summary>Why a ray did not arrive.</summary>
public enum RayStatus
{
    Ok = 0,

    /// <summary>The ray did not meet a surface, or met it outside where the surface exists.</summary>
    Missed = 1,

    /// <summary>An aperture blocked it.</summary>
    Vignetted = 2,

    /// <summary>Total internal reflection.</summary>
    TotalInternalReflection = 3,
}

/// <summary>A ray traced from object space to the image surface.</summary>
public struct ForwardRay
{
    public RayStatus Status;

    /// <summary>The surface where it failed, when it did.</summary>
    public int FailedAt;

    /// <summary>Where it met the image surface, in the global frame.</summary>
    public Vec3 ImagePoint;

    /// <summary>Its direction of arrival at the image surface.</summary>
    public Vec3 Direction;

    /// <summary>Its intercept on the stop surface, in that surface's own vertex frame.</summary>
    public double StopX, StopY;

    public readonly bool Ok => Status == RayStatus.Ok;
}

/// <summary>A ray traced backward from an image point into object space.</summary>
public struct ReverseRay
{
    public RayStatus Status;
    public int FailedAt;

    /// <summary>Where it met the object surface (finite conjugate), in the global frame.</summary>
    public Vec3 ObjectPoint;

    /// <summary>
    /// Its direction in object space, pointing FROM the object INTO the lens - the direction the
    /// light it stands for actually travelled.
    /// </summary>
    public Vec3 Direction;

    public readonly bool Ok => Status == RayStatus.Ok;
}

/// <summary>
/// A lens made ready to trace at one wavelength: surface vertices placed on the axis, indices
/// resolved, the stop sized from the system aperture, and the apertures that clip decided.
///
/// <para>The trace is exact and sequential - no series, no small-angle approximation - and runs
/// in either direction through the same intersection and refraction code. Tracing BACKWARD from
/// an image point is what Rimmer calls the "straightforward way" to measure relative
/// illumination, and what Gardner and Reshidko &amp; Sasián call rigorous: rays are placed
/// directly on a grid of image-space direction cosines, so nothing about the object side has to
/// be assumed. See <c>docs/method.md</c>.</para>
///
/// <para>Global frame: origin at surface 1's vertex, z toward the image. Each surface's vertex
/// sits at the running sum of the thicknesses before it, which also holds through mirrors,
/// because a file stores the thicknesses after a mirror as negative.</para>
/// </summary>
public sealed class TraceSystem
{
    /// <summary>Object distances at or beyond this are treated as infinite.</summary>
    private const double InfiniteObject = 1e10;

    public OpticalSystem System { get; }

    /// <summary>Wavelength traced, in micrometres.</summary>
    public double WavelengthUm { get; }

    /// <summary>Index of the medium after each surface.</summary>
    public double[] Indices { get; }

    /// <summary>Global z of each surface's vertex. Entry 0 is the object vertex (finite only).</summary>
    public double[] VertexZ { get; }

    public ParaxialResult Paraxial { get; }

    public int StopIndex { get; }
    public int ImageIndex { get; }
    public int LastOptical { get; }

    public bool InfiniteConjugate { get; }

    /// <summary>
    /// Object space is telecentric: the entrance pupil is at infinity, so pupil coordinates are
    /// direction tangents about the axis rather than points on a pupil plane.
    /// </summary>
    public bool Telecentric { get; }

    /// <summary>Global z of the paraxial entrance pupil.</summary>
    public double EntrancePupilZ { get; }

    /// <summary>
    /// The unit of pupil coordinates: the paraxial entrance pupil radius, or for a telecentric
    /// object space the tangent of the object-space marginal angle.
    /// </summary>
    public double PupilUnit { get; }

    /// <summary>Global z of the paraxial exit pupil, or infinity when image space is telecentric.</summary>
    public double ExitPupilZ { get; }

    /// <summary>Radius of the stop, from the real on-axis marginal ray the system aperture defines.</summary>
    public double StopRadius { get; }

    public ApertureModel Apertures { get; }

    /// <summary>Things the caller should know about how the lens was interpreted.</summary>
    public List<string> Notes { get; } = new();

    private readonly Surface[] _s;
    private readonly double _startZ;

    public TraceSystem(OpticalSystem system, double[] indices, double wavelengthUm,
                       bool clipAutomaticApertures = false)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        WavelengthUm = wavelengthUm;
        Validate(system);

        int count = system.Surfaces.Count;
        _s = system.Surfaces.ToArray();
        ImageIndex = count - 1;
        LastOptical = system.LastOpticalSurface();

        StopIndex = system.StopSurfaceIndex;
        if (StopIndex < 1 || StopIndex > LastOptical)
        {
            StopIndex = 1;
            Notes.Add("No stop is marked; surface 1 is taken as the stop.");
        }

        double t0 = _s[0].Thickness;
        InfiniteConjugate = double.IsInfinity(t0) || Math.Abs(t0) >= InfiniteObject;

        VertexZ = new double[count];
        VertexZ[0] = InfiniteConjugate ? double.NegativeInfinity : -t0;
        for (int i = 2; i < count; i++)
            VertexZ[i] = VertexZ[i - 1] + _s[i - 1].Thickness;

        double field = system.MaxFieldY();
        Paraxial = ParaxialTrace.Trace(system, indices, field);

        EntrancePupilZ = Paraxial.EntrancePupilPosition;
        Telecentric = !InfiniteConjugate
                      && (system.TelecentricObjectSpace || Math.Abs(EntrancePupilZ) > 1e8);
        if (Telecentric)
        {
            if (system.Aperture.Type != ApertureType.ObjectSpaceNA)
                throw new NotSupportedException(
                    "A telecentric object space needs its aperture stated as an object-space NA.");
            double n0 = Math.Abs(indices[0]) > 0 ? Math.Abs(indices[0]) : 1.0;
            double sinU = system.Aperture.Value / n0;
            if (sinU <= 0.0 || sinU >= 1.0)
                throw new NotSupportedException($"Object-space NA {system.Aperture.Value} is not usable.");
            PupilUnit = sinU / Math.Sqrt(1.0 - sinU * sinU);
        }
        else
        {
            PupilUnit = 0.5 * Paraxial.Epd;
            if (!(PupilUnit > 0.0))
                throw new NotSupportedException("The system aperture gives no entrance pupil.");
        }

        ExitPupilZ = double.IsInfinity(Paraxial.ExitPupilPosition)
            ? double.PositiveInfinity
            : VertexZ[ImageIndex] + Paraxial.ExitPupilPosition;

        double margin = 1.0 + 2.0 * Math.Max(_s.Length > 1 ? _s[1].SemiDiameter : 0.0, PupilUnit);
        _startZ = Math.Min(0.0, Telecentric ? 0.0 : EntrancePupilZ) - margin;

        StopRadius = SizeStop();
        Apertures = new ApertureModel(system, StopRadius, clipAutomaticApertures);
    }

    /// <summary>Refuses what this engine does not model, rather than computing a plausible wrong answer.</summary>
    private static void Validate(OpticalSystem system)
    {
        if (system.Surfaces.Count < 3)
            throw new NotSupportedException("A system needs an object, at least one surface and an image.");
        if (system.IsAfocal)
            throw new NotSupportedException("Afocal systems are not supported: relative illumination " +
                                            "is defined here on an image surface at finite distance.");

        for (int i = 1; i < system.Surfaces.Count; i++)
        {
            var s = system.Surfaces[i];
            switch (s.Type)
            {
                case SurfaceType.CoordinateBreak:
                    for (int k = 0; k < 5; k++)
                        if (s.Parameters[k] != 0.0)
                            throw new NotSupportedException(
                                $"Surface {i} is a coordinate break that decentres or tilts; only " +
                                "rotationally symmetric systems are supported so far.");
                    break;
                case SurfaceType.Abcd:
                case SurfaceType.Unsupported:
                    throw new NotSupportedException($"Surface {i} is of a type this program cannot trace.");
            }

            if (i < system.Surfaces.Count - 1
                && (double.IsInfinity(s.Thickness) || double.IsNaN(s.Thickness)))
                throw new NotSupportedException($"Surface {i} has an infinite thickness.");
        }
    }

    /// <summary>
    /// The stop radius: where the real on-axis ray through the edge of the paraxial entrance
    /// pupil meets the stop. Sizing it from a real ray makes the on-axis beam exactly the one
    /// the system aperture describes, and from then on the stop - not the paraxial pupil -
    /// decides what gets through at every field.
    /// </summary>
    private double SizeStop()
    {
        var (start, dir) = Launch(0.0, 0.0, 0.0, 1.0);
        var ray = TraceForward(start, dir, apertures: false);
        double r = Math.Sqrt(ray.StopX * ray.StopX + ray.StopY * ray.StopY);
        if (ray.Status == RayStatus.Ok || ray.FailedAt > StopIndex)
        {
            if (r > 0.0) return r;
        }
        Notes.Add("The real on-axis marginal ray did not reach the stop; the stop is sized paraxially.");
        return Math.Abs(Paraxial.Y[StopIndex]);
    }

    // ── Launching ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The object point of a field, in the global frame, for a finite conjugate. On a curved
    /// object surface it sits at the surface's sag, not on the vertex plane.
    /// </summary>
    public Vec3 ObjectPoint(double fieldX, double fieldY)
    {
        if (InfiniteConjugate) throw new InvalidOperationException("The object is at infinity.");

        double hx, hy;
        if (System.FieldType == FieldType.ObjectHeight)
        {
            hx = fieldX;
            hy = fieldY;
        }
        else
        {
            // An angle field at a finite conjugate names the object point its paraxial chief ray
            // comes from, the convention the paraxial trace uses.
            double span = Math.Abs(_s[0].Thickness) + EntrancePupilZ;
            hx = -Math.Tan(fieldX * Math.PI / 180.0) * span;
            hy = -Math.Tan(fieldY * Math.PI / 180.0) * span;
        }

        double sag = _s[0].Sag(Math.Sqrt(hx * hx + hy * hy));
        if (double.IsNaN(sag)) throw new InvalidOperationException("The field lies outside the object surface.");
        return new Vec3(hx, hy, VertexZ[0] + sag);
    }

    /// <summary>Unit normal of the object surface at a point on it, pointing toward the lens.</summary>
    public Vec3 ObjectNormal(Vec3 objectPoint) => SurfaceNormal(_s[0], objectPoint.X, objectPoint.Y);

    /// <summary>
    /// A ray of the given field and pupil coordinates, as a start point in object space and a
    /// direction. Pupil coordinates are in units of <see cref="PupilUnit"/> and are NOT limited
    /// to the unit circle: the stop decides what passes.
    /// </summary>
    public (Vec3 Start, Vec3 Direction) Launch(double fieldX, double fieldY, double px, double py)
    {
        if (InfiniteConjugate)
        {
            if (System.FieldType == FieldType.ObjectHeight)
                throw new NotSupportedException("An object at infinity cannot have its field stated as a height.");
            var u = new Vec3(Math.Tan(fieldX * Math.PI / 180.0), Math.Tan(fieldY * Math.PI / 180.0), 1.0).Normalized();
            var p = new Vec3(px * PupilUnit, py * PupilUnit, EntrancePupilZ);
            double back = (p.Z - _startZ) / u.Z;
            return (p - back * u, u);
        }

        var o = ObjectPoint(fieldX, fieldY);
        if (Telecentric)
            return (o, new Vec3(px * PupilUnit, py * PupilUnit, 1.0).Normalized());

        var pupil = new Vec3(px * PupilUnit, py * PupilUnit, EntrancePupilZ);
        return (o, (pupil - o).Normalized());
    }

    // ── Tracing ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Traces a ray from object space through every surface to the image surface.
    /// <paramref name="apertures"/> false ignores every aperture, which is how a chief ray is
    /// traced even where it is itself vignetted.
    /// </summary>
    public ForwardRay TraceForward(Vec3 start, Vec3 direction, bool apertures = true)
    {
        var result = new ForwardRay { Status = RayStatus.Ok };
        Vec3 pos = start, dir = direction.Normalized();

        for (int i = 1; i <= LastOptical; i++)
        {
            var s = _s[i];
            var local = pos - new Vec3(0, 0, VertexZ[i]);
            if (!Intersect(s, ref local, dir)) return Fail(ref result, RayStatus.Missed, i);

            if (i == StopIndex) { result.StopX = local.X; result.StopY = local.Y; }
            if (apertures && !Apertures.Passes(i, local.X, local.Y))
                return Fail(ref result, RayStatus.Vignetted, i);

            if (!Redirect(s, local, Indices[i - 1], Indices[i], reverse: false, ref dir))
                return Fail(ref result, RayStatus.TotalInternalReflection, i);

            pos = local + new Vec3(0, 0, VertexZ[i]);
        }

        var img = pos - new Vec3(0, 0, VertexZ[ImageIndex]);
        if (!Intersect(_s[ImageIndex], ref img, dir)) return Fail(ref result, RayStatus.Missed, ImageIndex);
        result.ImagePoint = img + new Vec3(0, 0, VertexZ[ImageIndex]);
        result.Direction = dir;
        return result;

        static ForwardRay Fail(ref ForwardRay r, RayStatus status, int at)
        {
            r.Status = status;
            r.FailedAt = at;
            return r;
        }
    }

    /// <summary>
    /// Traces a ray BACKWARD: from a point on the image surface, travelling in
    /// <paramref name="direction"/> (away from the image, toward the lens), through every
    /// surface in reverse order and out into object space. For a finite conjugate it must then
    /// meet the object surface.
    /// </summary>
    public ReverseRay TraceReverse(Vec3 imagePoint, Vec3 direction, bool apertures = true)
    {
        var result = new ReverseRay { Status = RayStatus.Ok };
        Vec3 pos = imagePoint, dir = direction.Normalized();

        for (int i = LastOptical; i >= 1; i--)
        {
            var s = _s[i];
            var local = pos - new Vec3(0, 0, VertexZ[i]);
            if (!Intersect(s, ref local, dir)) return Fail(ref result, RayStatus.Missed, i);
            if (apertures && !Apertures.Passes(i, local.X, local.Y))
                return Fail(ref result, RayStatus.Vignetted, i);

            // Backward the ray leaves the medium after surface i and enters the one before it.
            if (!Redirect(s, local, Indices[i], Indices[i - 1], reverse: true, ref dir))
                return Fail(ref result, RayStatus.TotalInternalReflection, i);

            pos = local + new Vec3(0, 0, VertexZ[i]);
        }

        if (!InfiniteConjugate)
        {
            var obj = pos - new Vec3(0, 0, VertexZ[0]);
            if (!Intersect(_s[0], ref obj, dir)) return Fail(ref result, RayStatus.Missed, 0);
            pos = obj + new Vec3(0, 0, VertexZ[0]);
        }

        result.ObjectPoint = pos;
        result.Direction = -dir;
        return result;

        static ReverseRay Fail(ref ReverseRay r, RayStatus status, int at)
        {
            r.Status = status;
            r.FailedAt = at;
            return r;
        }
    }

    /// <summary>Unit normal of the image surface at a global point on it, along +z at the vertex.</summary>
    public Vec3 ImageNormal(Vec3 imagePoint) => SurfaceNormal(_s[ImageIndex], imagePoint.X, imagePoint.Y);

    private static Vec3 SurfaceNormal(Surface s, double x, double y)
    {
        double r = Math.Sqrt(x * x + y * y);
        if (r < 1e-14 || s.Type == SurfaceType.Paraxial) return Vec3.UnitZ;
        double sp = s.SagSlope(r);
        return new Vec3(-sp * x / r, -sp * y / r, 1.0).Normalized();
    }

    /// <summary>
    /// Moves <paramref name="p"/> (in the surface's vertex frame) along <paramref name="d"/> to
    /// where it meets the surface. A conic is met in closed form, taking the root on the branch
    /// through the vertex; polynomial terms are then added by Newton iteration from there.
    /// </summary>
    private static bool Intersect(Surface s, ref Vec3 p, Vec3 d)
    {
        double c = s.Type == SurfaceType.Paraxial ? 0.0 : s.Curvature;
        double t;
        if (c == 0.0)
        {
            if (Math.Abs(d.Z) < 1e-15) return false;
            t = -p.Z / d.Z;
        }
        else
        {
            double kk = 1.0 + s.Conic;
            double a = c * (d.X * d.X + d.Y * d.Y + kk * d.Z * d.Z);
            double b = 2.0 * (c * (p.X * d.X + p.Y * d.Y + kk * p.Z * d.Z) - d.Z);
            double cc = c * (p.X * p.X + p.Y * p.Y + kk * p.Z * p.Z) - 2.0 * p.Z;
            double disc = b * b - 4.0 * a * cc;
            if (disc < 0.0) return false;

            // Both roots, in the numerically stable form. The one wanted is on the branch of the
            // conic through the vertex, and first along the ray. "Nearest the vertex plane" is
            // NOT a safe rule: a ray starting near the centre of curvature - one leaving a stop
            // that sits inside a deep meniscus - meets the far hemisphere first by that measure.
            double q = -0.5 * (b + (b >= 0.0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
            double t1 = q != 0.0 ? cc / q : double.NaN;
            double t2 = a != 0.0 ? q / a : double.NaN;
            bool ok1 = OnVertexBranch(p + t1 * d), ok2 = OnVertexBranch(p + t2 * d);
            if (ok1 && ok2)
            {
                double lo = Math.Min(t1, t2), hi = Math.Max(t1, t2);
                t = lo >= -1e-9 ? lo : hi >= -1e-9 ? hi : hi;       // first ahead; else the nearer behind
            }
            else if (ok1) t = t1;
            else if (ok2) t = t2;
            else return false;

            bool OnVertexBranch(Vec3 h) =>
                !double.IsNaN(h.Z) && 1.0 - kk * c * h.Z >= -1e-12;
        }

        var hit = p + t * d;

        if (s.Type != SurfaceType.Paraxial && s.HasPolynomial)
        {
            bool converged = false;
            for (int k = 0; k < 50; k++)
            {
                double r = Math.Sqrt(hit.X * hit.X + hit.Y * hit.Y);
                double sag = s.Sag(r);
                if (double.IsNaN(sag)) return false;
                double f = hit.Z - sag;
                double sp = s.SagSlope(r);
                if (double.IsNaN(sp)) return false;
                double drdt = r > 1e-14 ? (hit.X * d.X + hit.Y * d.Y) / r : 0.0;
                double df = d.Z - sp * drdt;
                if (Math.Abs(df) < 1e-15) return false;
                double step = f / df;
                hit = hit - step * d;
                if (Math.Abs(step) < 1e-12) { converged = true; break; }
            }
            if (!converged) return false;
        }
        else if (c != 0.0)
        {
            // Off the end of the conic (the other sheet of a hyperboloid, or past the rim of a
            // sphere) there is no real surface to meet.
            double r = Math.Sqrt(hit.X * hit.X + hit.Y * hit.Y);
            double sag = s.Sag(r);
            if (double.IsNaN(sag) || Math.Abs(hit.Z - sag) > 1e-6 * (1.0 + Math.Abs(sag))) return false;
        }

        p = hit;
        return true;
    }

    /// <summary>
    /// Refracts, reflects or - for an ideal paraxial surface - deviates the ray at local point
    /// <paramref name="p"/>. <paramref name="nFrom"/> is the medium the ray is leaving.
    /// </summary>
    private static bool Redirect(Surface s, Vec3 p, double nFrom, double nTo, bool reverse, ref Vec3 d)
    {
        if (s.Type == SurfaceType.Paraxial)
            return IdealLens(s, p, nFrom, nTo, reverse, ref d);

        var normal = SurfaceNormal(s, p.X, p.Y);

        if (s.IsMirror)
        {
            d = (d - 2.0 * d.Dot(normal) * normal).Normalized();
            return true;
        }

        if (Math.Abs(nFrom - nTo) < 1e-15) return true;
        if (Math.Abs(nTo) < 1e-15) return false;

        double mu = nFrom / nTo;
        double cosI = -d.Dot(normal);
        if (cosI < 0.0) { normal = -normal; cosI = -cosI; }    // normal must oppose the ray
        double k = 1.0 - mu * mu * (1.0 - cosI * cosI);
        if (k < 0.0) return false;
        double cosT = Math.Sqrt(k);
        d = (mu * d + (mu * cosI - cosT) * normal).Normalized();
        return true;
    }

    /// <summary>
    /// An ideal thin lens of focal length f: a ray's reduced slope changes by its height times
    /// the power, n' t' = n t - h n / f, in each direction. Backward the same relation is solved
    /// for the object-side slope, so a lens traced in reverse is the same lens.
    /// </summary>
    private static bool IdealLens(Surface s, Vec3 p, double nFrom, double nTo, bool reverse, ref Vec3 d)
    {
        if (Math.Abs(d.Z) < 1e-15) return false;
        double f = s.FocalLength;
        if (double.IsInfinity(f) || Math.Abs(f) < 1e-15) return true;

        double tx = d.X / d.Z, ty = d.Y / d.Z;
        double nObj = reverse ? nTo : nFrom;       // object-side medium
        double nImg = reverse ? nFrom : nTo;       // image-side medium
        if (!reverse)
        {
            tx = (nObj * tx - p.X * nObj / f) / nImg;
            ty = (nObj * ty - p.Y * nObj / f) / nImg;
        }
        else
        {
            tx = (nImg * tx + p.X * nObj / f) / nObj;
            ty = (nImg * ty + p.Y * nObj / f) / nObj;
        }
        double sign = d.Z >= 0.0 ? 1.0 : -1.0;
        d = (sign * new Vec3(tx, ty, 1.0)).Normalized();
        return true;
    }
}
