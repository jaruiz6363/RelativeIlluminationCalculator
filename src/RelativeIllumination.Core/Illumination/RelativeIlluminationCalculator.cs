using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Paraxial;
using RelativeIllumination.Core.RayTrace;

namespace RelativeIllumination.Core.Illumination;

/// <summary>How to measure.</summary>
public sealed class IlluminationOptions
{
    /// <summary>Trace from the object point and map the pupil to image space (Rimmer's practical method).</summary>
    public bool Forward { get; set; } = true;

    /// <summary>Trace backward from the image point on a grid of direction cosines (the rigorous method).</summary>
    public bool Reverse { get; set; } = true;

    /// <summary>
    /// Refer forward-traced ray directions to the image point through the reference sphere
    /// (Rimmer's Eq. 3, taken exactly rather than to first order). Without it, an aberrated
    /// ray's own direction is used, which Rimmer's Topogon example puts 2 points high at 35°.
    /// </summary>
    public bool RimmerCorrection { get; set; } = true;

    public RadianceModel Source { get; set; } = RadianceModel.Lambertian;

    /// <summary>Coarse cells along each side of the sampling window.</summary>
    public int BaseCells { get; set; } = 32;

    /// <summary>Levels of refinement at the edge of the transmitted region.</summary>
    public int MaxDepth { get; set; } = 4;

    /// <summary>Bisection steps locating the edge within a finest cell.</summary>
    public int Bisections { get; set; } = 12;

    /// <summary>Starting half-width of the forward window, in paraxial pupil radii about the chief ray.</summary>
    public double PupilWindow { get; set; } = 1.5;

    /// <summary>Compute Siew's distortion breakdown alongside the forward method.</summary>
    public bool Breakdown { get; set; } = true;
}

/// <summary>One method's measurement at one field.</summary>
public sealed class MethodResult
{
    /// <summary>
    /// Radiance-weighted projected solid angle of the transmitted cone at the image point,
    /// measured about the local image-surface normal. Illuminance is this times the radiance
    /// (times (n'/n)² if image and object media differ).
    /// </summary>
    public double ProjectedSolidAngle { get; init; }

    /// <summary>
    /// Illuminance relative to the brightest field measured - the axis, unless a field off axis
    /// is brighter - so it never exceeds 1, as OpticStudio reports it.
    /// </summary>
    public double RelativeIllumination { get; set; }

    /// <summary>Illuminance relative to the axial field, as Rimmer and Gardner define it; may exceed 1.</summary>
    public double RelativeToAxis { get; set; }

    /// <summary>
    /// Siew's effective F/number (Proc. SPIE 5867, 586701, 2005, Eq. 11): that of a perfect system
    /// with a circular exit pupil giving the same illuminance, sqrt(pi / (4 n'^2 PSA)), with n' the
    /// image-space index. It is the Effective F/# OpticStudio reports (there with transmission
    /// folded into the PSA, which this program does not yet include).
    /// </summary>
    public double EffectiveFNumber { get; init; }

    /// <summary>Rimmer's effective F/number in the meridional direction, 1 / (m2 - m1).</summary>
    public double FNumberT { get; init; }

    /// <summary>The same in the sagittal direction.</summary>
    public double FNumberS { get; init; }

    /// <summary>Area of the transmitted region in the sampled parameter space.</summary>
    public double ParameterArea { get; init; }

    public int Rays { get; init; }
}

/// <summary>
/// Siew's reading of relative illumination (Opt. Eng. 56(4) 049701, 2017, Eq. 12):
/// RI ≈ [S(y)/S(0)] cos⁴θ / {(1+D)[(1+D) + y dD/dy]}.
/// Valid in the limit of a small aperture; a diagnostic of WHERE the fall-off comes from, not a
/// replacement for the measured value.
/// </summary>
public sealed class SiewBreakdown
{
    /// <summary>S(y)/S(0): transmitted beam area at the entrance pupil, off axis over on axis.</summary>
    public double PupilAreaRatio { get; init; }

    /// <summary>cos⁴ of the real chief ray's angle in object space.</summary>
    public double Cos4 { get; init; }

    /// <summary>Distortion D: (real - paraxial) / paraxial image height.</summary>
    public double Distortion { get; init; }

    /// <summary>y dD/dy, the "differential distortion" term.</summary>
    public double DifferentialDistortion { get; init; }

    public double Estimate { get; init; }
}

/// <summary>Everything measured at one field point.</summary>
public sealed class FieldIllumination
{
    public double FieldX { get; init; }
    public double FieldY { get; init; }

    /// <summary>Where the real chief ray meets the image surface - the point illuminance is evaluated at.</summary>
    public Vec3 ImagePoint { get; init; }

    public double ImageHeight => Math.Sqrt(ImagePoint.X * ImagePoint.X + ImagePoint.Y * ImagePoint.Y);

    /// <summary>Angle of the real chief ray to the axis in object space, degrees.</summary>
    public double ChiefAngleObjectDeg { get; init; }

    /// <summary>Angle of the real chief ray to the image-surface normal, degrees.</summary>
    public double ChiefAngleImageDeg { get; init; }

    public MethodResult? Forward { get; init; }
    public MethodResult? Reverse { get; init; }
    public SiewBreakdown? Siew { get; set; }

    /// <summary>Why nothing could be measured here, when that is so.</summary>
    public string? Failure { get; init; }

    public bool Ok => Failure == null;
}

/// <summary>
/// Relative illumination by the projected solid angle of the transmitted cone in image space.
///
/// <para>For a Lambertian object of uniform radiance L, radiance is conserved along every ray
/// through a lossless system, so the illuminance at an image point is L (n'/n)² ∬ dl dm: the
/// area of the transmitted cone in image-space direction cosines, measured about the image
/// surface's normal (Rimmer 1986). That single quantity carries the cos⁴ law, the obliquity of
/// the pupil, pupil aberrations, distortion and vignetting together, at any conjugate and for
/// any object shape. Two independent measurements of it are made:</para>
///
/// <list type="bullet">
/// <item><b>Reverse</b>: rays leave the image point on a grid of direction cosines and are traced
/// backward; the area is that of the directions that get out into object space. Rimmer's
/// "straightforward way", and exact - no approximation beyond the sampling.</item>
/// <item><b>Forward</b>: rays leave the object point and are mapped to their image-space
/// directions. They do not all pass through the image point, so each is referred to it through
/// the reference sphere (Rimmer Eq. 3). This carries the approximation that one object point
/// images to one image point, and it is Rimmer's practical method.</item>
/// </list>
///
/// <para>The two agree when that approximation holds, and their difference measures how far it
/// fails.</para>
/// </summary>
public static class RelativeIlluminationCalculator
{
    public static IReadOnlyList<FieldIllumination> Compute(TraceSystem ts,
                                                           IReadOnlyList<(double X, double Y)> fields,
                                                           IlluminationOptions? options = null)
    {
        if (ts == null) throw new ArgumentNullException(nameof(ts));
        if (fields == null) throw new ArgumentNullException(nameof(fields));
        var opt = options ?? new IlluminationOptions();

        var axis = Measure(ts, 0.0, 0.0, opt);
        if (!axis.Ok) throw new InvalidOperationException("The axial field could not be measured: " + axis.Failure);

        var results = new FieldIllumination[fields.Count];
        Parallel.For(0, fields.Count, k =>
        {
            var (x, y) = fields[k];
            results[k] = x == 0.0 && y == 0.0 ? axis : Measure(ts, x, y, opt);
        });

        foreach (var r in results.Where(r => r.Ok))
        {
            if (r.Forward != null && axis.Forward != null)
                r.Forward.RelativeToAxis = r.Forward.ProjectedSolidAngle / axis.Forward.ProjectedSolidAngle;
            if (r.Reverse != null && axis.Reverse != null)
                r.Reverse.RelativeToAxis = r.Reverse.ProjectedSolidAngle / axis.Reverse.ProjectedSolidAngle;
        }

        // Relative illumination is reported against the brightest field measured, the axis
        // included, so it never exceeds 1. Each method is divided by its own brightest value.
        double peakF = results.Where(r => r.Ok && r.Forward != null).Select(r => r.Forward!.RelativeToAxis).Append(1.0).Max();
        double peakR = results.Where(r => r.Ok && r.Reverse != null).Select(r => r.Reverse!.RelativeToAxis).Append(1.0).Max();
        foreach (var r in results.Where(r => r.Ok))
        {
            if (r.Forward != null) r.Forward.RelativeIllumination = r.Forward.RelativeToAxis / peakF;
            if (r.Reverse != null) r.Reverse.RelativeIllumination = r.Reverse.RelativeToAxis / peakR;
        }

        if (opt.Breakdown && opt.Forward)
            foreach (var r in results.Where(r => r.Ok))
                r.Siew = Siew(ts, r, axis);

        return results;
    }

    /// <summary>Measures one field point, unnormalised.</summary>
    public static FieldIllumination Measure(TraceSystem ts, double fieldX, double fieldY, IlluminationOptions opt)
    {
        var aim = AimChief(ts, fieldX, fieldY);
        if (!aim.Ok)
            return new FieldIllumination { FieldX = fieldX, FieldY = fieldY, Failure = "the chief ray cannot be aimed at the stop centre" };

        var (start, dir) = ts.Launch(fieldX, fieldY, aim.Px, aim.Py);
        var chief = ts.TraceForward(start, dir, apertures: false);
        if (!chief.Ok)
            return new FieldIllumination { FieldX = fieldX, FieldY = fieldY, Failure = $"the chief ray fails at surface {chief.FailedAt} ({chief.Status})" };

        var q = chief.ImagePoint;
        var normal = ts.ImageNormal(q);
        if (normal.Dot(chief.Direction) < 0.0) normal = -normal;
        var (e1, e2) = Frame(normal);

        MethodResult? forward = opt.Forward ? MeasureForward(ts, fieldX, fieldY, aim.Px, aim.Py, q, e1, e2, opt) : null;
        MethodResult? reverse = opt.Reverse ? MeasureReverse(ts, q, chief.Direction, normal, e1, e2, opt) : null;

        return new FieldIllumination
        {
            FieldX = fieldX,
            FieldY = fieldY,
            ImagePoint = q,
            ChiefAngleObjectDeg = Math.Acos(Math.Min(1.0, Math.Abs(dir.Z))) * 180.0 / Math.PI,
            ChiefAngleImageDeg = Math.Acos(Math.Min(1.0, normal.Dot(chief.Direction))) * 180.0 / Math.PI,
            Forward = forward,
            Reverse = reverse,
        };
    }

    // ── Forward: object point → image-space directions ───────────────────────────────

    private static MethodResult MeasureForward(TraceSystem ts, double fx, double fy, double px0, double py0,
                                               Vec3 q, Vec3 e1, Vec3 e2, IlluminationOptions opt)
    {
        bool finite = !ts.InfiniteConjugate;
        bool weighted = finite && !opt.Source.IsUniform;
        Vec3 objNormal = finite ? ts.ObjectNormal(ts.ObjectPoint(fx, fy)) : Vec3.UnitZ;

        PupilSample Sample(double px, double py)
        {
            var (s, d) = ts.Launch(fx, fy, px, py);
            var ray = ts.TraceForward(s, d);
            if (!ray.Ok) return PupilSample.Blocked;
            var k = opt.RimmerCorrection ? ReferToImagePoint(ts, q, ray.ImagePoint, ray.Direction) : ray.Direction;
            double w = weighted ? opt.Source.Weight(objNormal, d) : 1.0;
            return new PupilSample(true, k.Dot(e1), k.Dot(e2), w);
        }

        var integral = Widening(Sample, px0, py0, opt.PupilWindow, 16.0, opt);
        return ToResult(integral, ts);
    }

    /// <summary>
    /// The direction of a forward-traced ray as seen from the image point: the unit vector from
    /// where the ray crosses the reference sphere (centred on the image point, through the
    /// axial exit pupil) to the image point. Rimmer's Eq. 3 is the first-order form of this; an
    /// unaberrated ray, which passes through the image point, is unchanged.
    /// </summary>
    public static Vec3 ReferToImagePoint(TraceSystem ts, Vec3 q, Vec3 hit, Vec3 d)
    {
        if (double.IsInfinity(ts.ExitPupilZ)) return d;          // telecentric: the correction vanishes
        var e = new Vec3(0.0, 0.0, ts.ExitPupilZ);
        var toPupil = e - q;
        double r2 = toPupil.Dot(toPupil);
        if (r2 < 1e-18) return d;

        var w = hit - q;
        double b = w.Dot(d);
        double disc = b * b - (w.Dot(w) - r2);
        if (disc < 0.0) return d;
        double s = Math.Sqrt(disc);
        var p1 = hit + (-b - s) * d;
        var p2 = hit + (-b + s) * d;
        var p = (p1 - q).Dot(toPupil) >= (p2 - q).Dot(toPupil) ? p1 : p2;
        var k = (q - p).Normalized();
        return k.Dot(d) > 0.0 ? k : d;
    }

    // ── Reverse: image point → object space ──────────────────────────────────────────

    private static MethodResult MeasureReverse(TraceSystem ts, Vec3 q, Vec3 chiefDir, Vec3 normal,
                                               Vec3 e1, Vec3 e2, IlluminationOptions opt)
    {
        bool weighted = !ts.InfiniteConjugate && !opt.Source.IsUniform;

        PupilSample Sample(double a, double b)
        {
            double c2 = 1.0 - a * a - b * b;
            if (c2 <= 0.0) return PupilSample.Blocked;
            var arriving = a * e1 + b * e2 + Math.Sqrt(c2) * normal;
            var ray = ts.TraceReverse(q, -arriving);
            if (!ray.Ok) return PupilSample.Blocked;
            double w = weighted ? opt.Source.Weight(ts.ObjectNormal(ray.ObjectPoint), ray.Direction) : 1.0;
            return new PupilSample(true, a, b, w);
        }

        // Start from the axial image-space aperture; the window grows if the cone reaches its edge.
        double u = ts.Paraxial.U[ts.LastOptical];
        double na = Math.Abs(u) / Math.Sqrt(1.0 + u * u);
        double half = Math.Max(1.5 * na, 1e-3);
        var integral = Widening(Sample, chiefDir.Dot(e1), chiefDir.Dot(e2), half, 1.0, opt);
        return ToResult(integral, ts);
    }

    // ── Shared ───────────────────────────────────────────────────────────────────────

    private static PupilIntegral Widening(Func<double, double, PupilSample> sample, double cx, double cy,
                                          double half, double maxHalf, IlluminationOptions opt)
    {
        int rays = 0;
        while (true)
        {
            var integral = new PupilIntegrator(sample, cx, cy, half, opt.BaseCells, opt.MaxDepth, opt.Bisections).Integrate();
            rays += integral.Evaluations;
            if (!integral.TouchesWindow || half >= maxHalf)
                return new PupilIntegral
                {
                    WeightedArea = integral.WeightedArea, MappedArea = integral.MappedArea,
                    ParameterArea = integral.ParameterArea,
                    UMin = integral.UMin, UMax = integral.UMax, VMin = integral.VMin, VMax = integral.VMax,
                    TouchesWindow = integral.TouchesWindow, Evaluations = rays,
                };
            half = Math.Min(2.0 * half, maxHalf);
        }
    }

    private static MethodResult ToResult(PupilIntegral i, TraceSystem ts) => new()
    {
        ProjectedSolidAngle = i.WeightedArea,
        EffectiveFNumber = EffectiveFNumber(i.WeightedArea, Math.Abs(ts.Indices[ts.LastOptical])),
        FNumberT = i.VMax > i.VMin ? 1.0 / (i.VMax - i.VMin) : double.PositiveInfinity,
        FNumberS = i.UMax > i.UMin ? 1.0 / (i.UMax - i.UMin) : double.PositiveInfinity,
        ParameterArea = i.ParameterArea,
        Rays = i.Evaluations,
    };

    /// <summary>Siew 2005, Eq. 11: f/# = sqrt(pi / (4 n'^2 PSA)); infinite when nothing gets through.</summary>
    public static double EffectiveFNumber(double projectedSolidAngle, double imageIndex) =>
        projectedSolidAngle > 0.0
            ? Math.Sqrt(Math.PI / (4.0 * imageIndex * imageIndex * projectedSolidAngle))
            : double.PositiveInfinity;

    /// <summary>
    /// Two unit vectors spanning the plane normal to <paramref name="n"/>: for a flat image they
    /// are x and y, so the direction cosines are the familiar (l, m). For a curved image this is
    /// Rimmer's Eq. 1 - direction cosines taken about the surface normal at the image point.
    /// </summary>
    public static (Vec3 E1, Vec3 E2) Frame(Vec3 n)
    {
        var e1 = Vec3.UnitX - n.Dot(Vec3.UnitX) * n;
        if (e1.Length < 1e-9) e1 = Vec3.UnitY - n.Dot(Vec3.UnitY) * n;
        e1 = e1.Normalized();
        return (e1, n.Cross(e1).Normalized());
    }

    /// <summary>The pupil coordinates of the ray through the centre of the stop.</summary>
    public static (bool Ok, double Px, double Py) AimChief(TraceSystem ts, double fx, double fy)
    {
        double px = 0.0, py = 0.0;
        double tol = 1e-12 * (1.0 + ts.StopRadius);
        const double h = 1e-6;

        for (int it = 0; it < 40; it++)
        {
            if (!StopHit(ts, fx, fy, px, py, out double x, out double y)) return (false, 0, 0);
            if (Math.Sqrt(x * x + y * y) < tol) return (true, px, py);

            if (!StopHit(ts, fx, fy, px + h, py, out double xa, out double ya)) return (false, 0, 0);
            if (!StopHit(ts, fx, fy, px, py + h, out double xb, out double yb)) return (false, 0, 0);
            double j11 = (xa - x) / h, j21 = (ya - y) / h;
            double j12 = (xb - x) / h, j22 = (yb - y) / h;
            double det = j11 * j22 - j12 * j21;
            if (Math.Abs(det) < 1e-30) return (false, 0, 0);

            double dpx = -(j22 * x - j12 * y) / det;
            double dpy = -(-j21 * x + j11 * y) / det;
            double len = Math.Sqrt(dpx * dpx + dpy * dpy);
            if (len > 0.5) { dpx *= 0.5 / len; dpy *= 0.5 / len; }
            px += dpx;
            py += dpy;
        }

        return StopHit(ts, fx, fy, px, py, out double fxr, out double fyr)
               && Math.Sqrt(fxr * fxr + fyr * fyr) < 1e-6 * (1.0 + ts.StopRadius)
            ? (true, px, py)
            : (false, 0, 0);
    }

    private static bool StopHit(TraceSystem ts, double fx, double fy, double px, double py, out double x, out double y)
    {
        var (s, d) = ts.Launch(fx, fy, px, py);
        var ray = ts.TraceForward(s, d, apertures: false);
        x = ray.StopX;
        y = ray.StopY;
        return ray.Ok || ray.FailedAt > ts.StopIndex;
    }

    // ── Siew's breakdown ─────────────────────────────────────────────────────────────

    private static SiewBreakdown? Siew(TraceSystem ts, FieldIllumination r, FieldIllumination axis)
    {
        if (r.Forward == null || axis.Forward == null || !(axis.Forward.ParameterArea > 0.0)) return null;

        double ratio = r.Forward.ParameterArea / axis.Forward.ParameterArea;
        double cos = Math.Cos(r.ChiefAngleObjectDeg * Math.PI / 180.0);
        double cos4 = cos * cos * cos * cos;

        double magnitude = Math.Sqrt(r.FieldX * r.FieldX + r.FieldY * r.FieldY);
        double d = 0.0, ydd = 0.0;
        if (magnitude > 0.0)
        {
            d = Distortion(ts, r.FieldX, r.FieldY) ?? double.NaN;
            const double eps = 1e-3;
            var (xp, yp) = ScaleField(ts, r.FieldX, r.FieldY, 1.0 + eps);
            var (xm, ym) = ScaleField(ts, r.FieldX, r.FieldY, 1.0 - eps);
            double? dp = Distortion(ts, xp, yp), dm = Distortion(ts, xm, ym);
            ydd = dp.HasValue && dm.HasValue ? (dp.Value - dm.Value) / (2.0 * eps) : double.NaN;
        }

        double estimate = ratio * cos4 / ((1.0 + d) * ((1.0 + d) + ydd));
        return new SiewBreakdown
        {
            PupilAreaRatio = ratio,
            Cos4 = cos4,
            Distortion = d,
            DifferentialDistortion = ydd,
            Estimate = estimate,
        };
    }

    /// <summary>
    /// Scales a field by a factor in the variable the paraxial image height is proportional to:
    /// the object height, or the tangent of the field angle.
    /// </summary>
    private static (double X, double Y) ScaleField(TraceSystem ts, double fx, double fy, double factor)
    {
        if (ts.System.FieldType == FieldType.ObjectHeight) return (fx * factor, fy * factor);
        static double S(double deg, double f) => Math.Atan(Math.Tan(deg * Math.PI / 180.0) * f) * 180.0 / Math.PI;
        return (S(fx, factor), S(fy, factor));
    }

    /// <summary>(real - paraxial) / paraxial chief-ray height on the image surface.</summary>
    private static double? Distortion(TraceSystem ts, double fx, double fy)
    {
        var aim = AimChief(ts, fx, fy);
        if (!aim.Ok) return null;
        var (s, d) = ts.Launch(fx, fy, aim.Px, aim.Py);
        var chief = ts.TraceForward(s, d, apertures: false);
        if (!chief.Ok) return null;

        double real = Math.Sqrt(chief.ImagePoint.X * chief.ImagePoint.X + chief.ImagePoint.Y * chief.ImagePoint.Y);
        double field;
        if (ts.System.FieldType == FieldType.ObjectHeight)
        {
            field = Math.Sqrt(fx * fx + fy * fy);
        }
        else
        {
            // The paraxial image height is linear in the tangent of the field angle, so an
            // off-meridian field is taken at the angle whose tangent is the vector length.
            double tx = Math.Tan(fx * Math.PI / 180.0), ty = Math.Tan(fy * Math.PI / 180.0);
            field = Math.Atan(Math.Sqrt(tx * tx + ty * ty)) * 180.0 / Math.PI;
        }
        double paraxial = Math.Abs(ParaxialTrace.Trace(ts.System, ts.Indices, field).ImageHeight);
        return paraxial > 0.0 ? (real - paraxial) / paraxial : null;
    }
}
