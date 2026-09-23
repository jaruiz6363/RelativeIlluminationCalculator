using System;
using System.Collections.Generic;
using System.Linq;

using RelativeIllumination.Core.Illumination;
using RelativeIllumination.Core.RayTrace;

namespace RelativeIllumination.Optiland;

/// <summary>What the Optiland cross-check found at one field.</summary>
public sealed class OptilandField
{
    public double FieldY { get; init; }

    /// <summary>Illuminance relative to the brightest field measured, from Optiland's rays; never above 1.</summary>
    public double RelativeIllumination { get; set; }

    /// <summary>Illuminance relative to the axial field; may exceed 1.</summary>
    public double RelativeToAxis { get; set; }

    /// <summary>Where the chief ray met the image surface.</summary>
    public Vec3 ImagePoint { get; init; }

    /// <summary>Projected solid angle of the transmitted cone, unnormalised.</summary>
    public double SolidAngle { get; init; }

    /// <summary>Siew's effective F/# from that solid angle (Proc. SPIE 5867, 2005, Eq. 11), as the calculator reports it.</summary>
    public double EffectiveFNumber { get; init; }

    public int Rays { get; init; }

    public string? Failure { get; init; }

    public bool Ok => Failure == null;
}

/// <summary>
/// Relative illumination measured from Optiland's ray trace by this program's forward method:
/// the same <see cref="PupilIntegrator"/>, the same reference-sphere correction (Rimmer Eq. 3),
/// the same frame about the image-surface normal, and Siew's effective F/# from the same formula.
/// Only the rays come from Optiland.
///
/// <para>An independent check of this program's own tracer: the lens is the same, the method is
/// the same, only the rays come from somewhere else. It is NOT a check of the method itself -
/// for that, compare either against the exact answers in the test suite or against this
/// program's backward trace, which Optiland cannot do.</para>
///
/// <para>The parameter space is Optiland's normalised pupil. With iterative ray aiming - which
/// the bridge always sets - that is the real stop, so the unit disk is exactly the aperture the
/// stop allows, and points outside it are blocked without being traced. The integrator asks for
/// its rays a level at a time, so a field costs a few dozen calls into Python, not one per ray.</para>
/// </summary>
public static class OptilandIllumination
{
    public sealed class Options
    {
        /// <summary>Coarse cells along each side of the pupil.</summary>
        public int BaseCells { get; set; } = 32;

        /// <summary>How many times a cell near the boundary may be split in four.</summary>
        public int MaxDepth { get; set; } = 4;

        /// <summary>Bisection steps locating the boundary on a finest-level edge.</summary>
        public int Bisections { get; set; } = 12;

        /// <summary>Refer each ray to the image point through the reference sphere.</summary>
        public bool RimmerCorrection { get; set; } = true;
    }

    /// <summary>
    /// Measures every field. <paramref name="ts"/> supplies the geometry this program already
    /// knows - image surface, exit pupil - while every ray comes from Optiland.
    /// </summary>
    public static IReadOnlyList<OptilandField> Compute(TraceSystem ts, OptilandOptic optic,
                                                       IReadOnlyList<double> fields, Options? options = null)
    {
        if (ts == null) throw new ArgumentNullException(nameof(ts));
        if (optic == null) throw new ArgumentNullException(nameof(optic));
        var opt = options ?? new Options();

        var results = fields.Select(f => Measure(ts, optic, f, opt)).ToList();
        // The axis is what everything is divided by, so it is measured even when not asked for.
        var axis = results.FirstOrDefault(r => r.FieldY == 0.0 && r.Ok) ?? Measure(ts, optic, 0.0, opt);
        if (!axis.Ok) axis = null;
        // Reported against the brightest field measured, the axis included, as the calculator does.
        double peak = results.Where(r => r.Ok).Select(r => r.SolidAngle).Append(axis?.SolidAngle ?? 0.0).Max();
        foreach (var r in results.Where(r => r.Ok))
        {
            r.RelativeToAxis = axis != null && axis.SolidAngle > 0 ? r.SolidAngle / axis.SolidAngle : double.NaN;
            r.RelativeIllumination = peak > 0 ? r.SolidAngle / peak : double.NaN;
        }
        return results;
    }

    private static OptilandField Measure(TraceSystem ts, OptilandOptic optic, double fieldY, Options opt)
    {
        // The chief ray. Optiland is aiming iteratively, so pupil (0, 0) is the centre of the
        // real stop, as it is in this program.
        var chief = optic.Trace(fieldY, new[] { 0.0 }, new[] { 0.0 });

        Vec3 q, chiefDir;
        if (chief.Passed(0))
        {
            q = new Vec3(chief.X[0], chief.Y[0], chief.Z[0]);
            chiefDir = new Vec3(chief.L[0], chief.M[0], chief.N[0]).Normalized();
        }
        else
        {
            // An obscuration blocks the chief ray - it goes through the middle of the stop,
            // which is exactly where a central obstruction sits. The image point is still
            // wanted, so it comes from this program's own chief trace, which ignores apertures.
            // The pupil, hole and all, is then measured like any other.
            var aim = RelativeIlluminationCalculator.AimChief(ts, 0.0, fieldY);
            if (!aim.Ok)
                return new OptilandField { FieldY = fieldY, Failure = "the chief ray cannot be aimed at the stop" };
            var (start, dir) = ts.Launch(0.0, fieldY, aim.Px, aim.Py);
            var own = ts.TraceForward(start, dir, apertures: false);
            if (!own.Ok)
                return new OptilandField { FieldY = fieldY, Failure = "the chief ray does not reach the image" };
            q = own.ImagePoint;
            chiefDir = own.Direction;
        }
        var normal = ts.ImageNormal(q);
        if (normal.Dot(chiefDir) < 0.0) normal = -normal;
        var (e1, e2) = RelativeIlluminationCalculator.Frame(normal);

        IReadOnlyList<PupilSample> Sample(IReadOnlyList<(double X, double Y)> points)
        {
            var result = new PupilSample[points.Count];
            var index = new List<int>(points.Count);
            var px = new List<double>(points.Count);
            var py = new List<double>(points.Count);
            for (int k = 0; k < points.Count; k++)
            {
                var (x, y) = points[k];
                result[k] = PupilSample.Blocked;
                if (x * x + y * y >= 1.0) continue;          // beyond the stop
                index.Add(k);
                px.Add(x);
                py.Add(y);
            }
            if (index.Count == 0) return result;

            var batch = optic.Trace(fieldY, px, py);
            for (int m = 0; m < index.Count; m++)
            {
                if (!batch.Passed(m)) continue;
                var hit = new Vec3(batch.X[m], batch.Y[m], batch.Z[m]);
                var d = new Vec3(batch.L[m], batch.M[m], batch.N[m]).Normalized();
                var k = opt.RimmerCorrection ? RelativeIlluminationCalculator.ReferToImagePoint(ts, q, hit, d) : d;
                result[index[m]] = new PupilSample(true, k.Dot(e1), k.Dot(e2), 1.0);
            }
            return result;
        }

        var integral = new PupilIntegrator(Sample, 0.0, 0.0, 1.0, opt.BaseCells, opt.MaxDepth, opt.Bisections).Integrate();

        return new OptilandField
        {
            FieldY = fieldY,
            ImagePoint = q,
            SolidAngle = integral.WeightedArea,
            EffectiveFNumber = RelativeIlluminationCalculator.EffectiveFNumber(
                integral.WeightedArea, Math.Abs(ts.Indices[ts.LastOptical])),
            Rays = integral.Evaluations + 1,
        };
    }
}
