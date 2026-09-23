using System;

using RelativeIllumination.Core.Enums;

namespace RelativeIllumination.Core.Models;

/// <summary>
/// One surface of the prescription.
///
/// Curvature rather than radius is the stored quantity, because a plane is c = 0 and needs
/// no special case, whereas r = infinity does. <see cref="Radius"/> is a view onto it for
/// reading and printing, where radius is what an optical prescription conventionally shows.
/// </summary>
public class Surface
{
    /// <summary>Position in the system, 0 = object.</summary>
    public int Index { get; set; }

    public SurfaceType Type { get; set; } = SurfaceType.Standard;

    /// <summary>1/radius, in reciprocal lens units. Zero is a plane.</summary>
    public double Curvature { get; set; }

    /// <summary>
    /// Radius of curvature. Infinite for a plane, in both directions: reading infinity back
    /// gives c = 0, so a plane round-trips instead of producing a division by zero.
    /// </summary>
    public double Radius
    {
        get => Math.Abs(Curvature) < 1e-15 ? double.PositiveInfinity : 1.0 / Curvature;
        set => Curvature = double.IsInfinity(value) || value == 0.0 ? 0.0 : 1.0 / value;
    }

    /// <summary>Axial distance to the next surface.</summary>
    public double Thickness { get; set; }

    /// <summary>Conic constant. 0 = sphere, −1 = paraboloid, &lt; −1 = hyperboloid.</summary>
    public double Conic { get; set; }

    /// <summary>
    /// Even-asphere coefficients. Index k multiplies r^(2k+2), so [0] is the r² term, [1] is
    /// r⁴, and so on. The r² term is separate from curvature and some formats do not write it.
    /// </summary>
    public double[] AsphericCoefficients { get; set; } = new double[8];

    /// <summary>Catalog glass name, or null/empty for air. "MIRROR" reflects.</summary>
    public string? Material { get; set; }

    /// <summary>Catalog the material was resolved from, when a file names one.</summary>
    public string? CatalogName { get; set; }

    /// <summary>True when this surface is the aperture stop.</summary>
    public bool IsStop { get; set; }

    /// <summary>
    /// Tilt about the x axis, in radians, as a coordinate break states it. Read so a file's
    /// structure survives; the illumination engine refuses a system that actually tilts.
    /// </summary>
    public double TiltX { get; set; }

    public bool IsMirror => !string.IsNullOrEmpty(Material)
                            && Material!.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);

    // ── Apertures ────────────────────────────────────────────────────────────────
    // Relative illumination is decided by which rays get through, so which of these a surface
    // carries matters more here than anywhere else. See Illumination/ApertureModel.cs for which
    // of them clip and why.

    /// <summary>Clear semi-diameter.</summary>
    public double SemiDiameter { get; set; }

    public SemiDiameterMode SemiDiameterMode { get; set; } = SemiDiameterMode.Auto;

    /// <summary>Clear aperture as a percentage of the solved semi-diameter; 100 = full.</summary>
    public double ClearAperturePercent { get; set; } = 100.0;

    /// <summary>Central obstruction radius, 0 for none. Rays inside it are blocked.</summary>
    public double ObscurationRadius { get; set; }

    /// <summary>Floating aperture radius (Zemax FLAP), 0 for none. Rays outside it are blocked.</summary>
    public double FloatingApertureRadius { get; set; }

    /// <summary>Clear-aperture outer radius (Zemax CLAP), 0 for none. Rays outside it are blocked.</summary>
    public double ClapOuterRadius { get; set; }

    /// <summary>Inner radius of an annular clear aperture, 0 for none. Rays inside it are blocked.</summary>
    public double InnerRadius { get; set; }

    /// <summary>
    /// Mechanical semi-diameter (Zemax MEMA). The drawn edge of the part, which does NOT
    /// vignette: kept apart from <see cref="ClapOuterRadius"/> so a stock-lens file's mechanical
    /// outline is never mistaken for an optical stop.
    /// </summary>
    public double MechanicalSemiDiameter { get; set; }

    /// <summary>Free-text note carried through from the file.</summary>
    public string? Comment { get; set; }

    // ── Model ("fictitious") glass ────────────────────────────────────────────────
    // Some files give dispersion directly instead of naming a catalog glass.

    public bool ModelIndexEnabled { get; set; }
    public double ModelNd { get; set; }
    public double ModelVd { get; set; }
    public double ModelDPgF { get; set; }

    /// <summary>Focal length of an ideal thin lens, for <see cref="SurfaceType.Paraxial"/>.</summary>
    public double FocalLength { get; set; }

    // ── Format-specific extras ───────────────────────────────────────────────────
    // Readers set these; the analysis does not use them, but dropping them would lose
    // information when a file is opened and its prescription printed.

    /// <summary>
    /// Numbered surface parameters as a format wrote them (coordinate-break tilts, ABCD
    /// terms, and so on). Kept so an opened file prints back what it said, even for a
    /// surface type this program does not analyse.
    /// </summary>
    public double[] Parameters { get; } = new double[8];

    /// <summary>Integer surface settings, same purpose as <see cref="Parameters"/>.</summary>
    public int[] Settings { get; } = new int[8];

    /// <summary>
    /// The thickness after this surface is solved to put the paraxial marginal ray on
    /// axis. Recorded because the stored thickness alone does not say it was solved.
    /// </summary>
    public bool HasMarginalRaySolve { get; set; }

    public void SetParameter(int index, double value)
    {
        if (index >= 0 && index < Parameters.Length) Parameters[index] = value;
    }

    public void SetSetting(int index, int value)
    {
        if (index >= 0 && index < Settings.Length) Settings[index] = value;
    }

    /// <summary>
    /// The curvature the surface actually has at its vertex, which is what sets its paraxial
    /// power. The even-asphere polynomial starts at r², and that first term is a curvature
    /// change: z = (c/2 + A2) r² + ..., so a nonzero A2 moves the vertex curvature to c + 2 A2.
    /// </summary>
    public double VertexCurvature =>
        Curvature + 2.0 * (AsphericCoefficients.Length > 0 ? AsphericCoefficients[0] : 0.0);

    /// <summary>True when any even-asphere coefficient is nonzero.</summary>
    public bool HasPolynomial
    {
        get
        {
            foreach (double a in AsphericCoefficients) if (a != 0.0) return true;
            return false;
        }
    }

    /// <summary>
    /// Sag z(r) along the axis, positive toward the image. NaN where the conic has no real
    /// surface, which is a question about the geometry rather than a rounding artefact.
    /// </summary>
    public double Sag(double r)
    {
        double r2 = r * r;
        double sag = 0.0;

        if (Curvature != 0.0)
        {
            double disc = 1.0 - (1.0 + Conic) * Curvature * Curvature * r2;
            if (disc < 0.0) return double.NaN;
            sag = Curvature * r2 / (1.0 + Math.Sqrt(disc));
        }

        double rp = r2;                                   // r², then r⁴, r⁶ …
        for (int k = 0; k < AsphericCoefficients.Length; k++)
        {
            sag += AsphericCoefficients[k] * rp;
            rp *= r2;
        }
        return sag;
    }

    /// <summary>dz/dr of the sag: the conic part in closed form, then the polynomial.</summary>
    public double SagSlope(double r)
    {
        double slope = 0.0;
        double c = Curvature;
        if (c != 0.0)
        {
            double disc = 1.0 - (1.0 + Conic) * c * c * r * r;
            if (disc <= 0.0) return double.NaN;
            slope = c * r / Math.Sqrt(disc);
        }

        double rp = r;                                   // r, then r³, r⁵ ...
        for (int k = 0; k < AsphericCoefficients.Length; k++)
        {
            slope += 2.0 * (k + 1) * AsphericCoefficients[k] * rp;
            rp *= r * r;
        }
        return slope;
    }
}
