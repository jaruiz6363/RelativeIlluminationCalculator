using System;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Models;

namespace RelativeIllumination.Core.RayTrace;

/// <summary>
/// Which rays each surface lets through.
///
/// <para>Relative illumination is decided by exactly this, so the rules are stated rather than
/// inherited from whatever a lens file happened to store:</para>
/// <list type="bullet">
/// <item>The <b>stop</b> always clips, at the radius the system aperture implies (see
/// <see cref="TraceSystem"/>). That is what makes the pupil a physical one: an off-axis beam is
/// whatever gets through the real diaphragm, which is ray aiming by construction rather than an
/// option. A program that launches off-axis rays into the paraxial entrance pupil instead
/// cannot see the pupil grow or shrink with field, and that growth is the whole of the
/// Slyusarev effect.</item>
/// <item>A <b>fixed</b> semi-diameter clips; an automatic one does not, unless the file reduced it
/// with a clear-aperture percentage below 100, or the caller asks for automatic ones to be
/// treated as hard (<see cref="ClipAutomatic"/>). An automatic semi-diameter is solved to pass
/// the beams of the file's own fields, so clipping at it would vignette by definition at
/// exactly those fields and nowhere else.</item>
/// <item>A clear aperture (CLAP) and a floating aperture (FLAP) clip at their outer radius; an
/// annular inner radius and an obscuration block everything inside them. Rimmer: "for systems
/// with obstructions, the rays limited by the obstructions also have to be considered."</item>
/// <item>A mechanical semi-diameter (MEMA) never clips: it is the drawn edge of the part.</item>
/// <item>The image surface never clips.</item>
/// </list>
/// </summary>
public sealed class ApertureModel
{
    private readonly double[] _outer;
    private readonly double[] _inner;

    /// <summary>Whether automatic semi-diameters were treated as hard apertures.</summary>
    public bool ClipAutomatic { get; }

    public ApertureModel(OpticalSystem system, double stopRadius, bool clipAutomatic)
    {
        ClipAutomatic = clipAutomatic;
        int count = system.Surfaces.Count;
        _outer = new double[count];
        _inner = new double[count];
        int stop = system.StopSurfaceIndex;

        for (int i = 0; i < count; i++)
        {
            _outer[i] = double.PositiveInfinity;
            if (i == 0 || i == count - 1) continue;          // object and image never clip

            var s = system.Surfaces[i];
            double outer = double.PositiveInfinity;

            if (i == stop)
            {
                outer = stopRadius;
            }
            else if (s.SemiDiameter > 0.0)
            {
                bool reduced = s.ClearAperturePercent > 0.0 && s.ClearAperturePercent < 100.0;
                if (s.SemiDiameterMode == SemiDiameterMode.Fixed || clipAutomatic || reduced)
                {
                    double pct = s.ClearAperturePercent > 0.0 ? s.ClearAperturePercent : 100.0;
                    outer = s.SemiDiameter * pct / 100.0;
                }
            }

            if (s.ClapOuterRadius > 0.0) outer = Math.Min(outer, s.ClapOuterRadius);
            if (s.FloatingApertureRadius > 0.0) outer = Math.Min(outer, s.FloatingApertureRadius);

            _outer[i] = outer;
            _inner[i] = Math.Max(s.ObscurationRadius, s.InnerRadius);
        }
    }

    /// <summary>Outer clipping radius of surface <paramref name="i"/>; infinity for none.</summary>
    public double Outer(int i) => _outer[i];

    /// <summary>Radius inside which surface <paramref name="i"/> blocks; zero for none.</summary>
    public double Inner(int i) => _inner[i];

    /// <summary>Whether surface <paramref name="i"/> clips anything at all.</summary>
    public bool Clips(int i) => !double.IsPositiveInfinity(_outer[i]) || _inner[i] > 0.0;

    /// <summary>Whether a ray meeting surface <paramref name="i"/> at local (x, y) gets through.</summary>
    public bool Passes(int i, double x, double y)
    {
        double r2 = x * x + y * y;
        double outer = _outer[i], inner = _inner[i];
        if (!double.IsPositiveInfinity(outer) && r2 > outer * outer) return false;
        if (inner > 0.0 && r2 < inner * inner) return false;
        return true;
    }
}
