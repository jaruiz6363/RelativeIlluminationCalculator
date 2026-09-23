using System;

using RelativeIllumination.Core.RayTrace;

namespace RelativeIllumination.Core.Illumination;

/// <summary>
/// How bright the object is along a ray, relative to a Lambertian object of uniform radiance.
///
/// <para>For a Lambertian object the image illuminance is the radiance times the projected
/// solid angle of the transmitted cone, and nothing about the object - its distance, its
/// curvature, its tilt - enters except through which rays get through. A source that is not
/// Lambertian breaks that: its radiance depends on the direction of the ray relative to the
/// object surface's normal, so the solid angle must be weighted ray by ray, and the object
/// surface's shape now matters through its normal. That is what this supplies.</para>
/// </summary>
public abstract class RadianceModel
{
    /// <summary>Radiance of a ray leaving the object surface, relative to the model's on-normal value.</summary>
    /// <param name="objectNormal">Unit normal of the object surface at the ray's origin, toward the lens.</param>
    /// <param name="direction">Unit direction of the ray, from the object into the lens.</param>
    public abstract double Weight(Vec3 objectNormal, Vec3 direction);

    /// <summary>Whether the weight is 1 for every ray, so it need not be evaluated.</summary>
    public virtual bool IsUniform => false;

    public abstract string Describe();

    public static readonly RadianceModel Lambertian = new LambertianRadiance();

    /// <summary>
    /// Parses "lambertian" or "cos:N" - radiance falling as cos^N of the angle from the object
    /// normal, so that the radiant intensity of a surface element goes as cos^(N+1).
    /// </summary>
    public static RadianceModel Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("lambertian", StringComparison.OrdinalIgnoreCase))
            return Lambertian;
        if (text.StartsWith("cos:", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text.Substring(4), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double n)
            && n >= 0.0)
            return n == 0.0 ? Lambertian : new CosinePowerRadiance(n);
        throw new FormatException($"'{text}' is not a source model. Use 'lambertian' or 'cos:N'.");
    }

    private sealed class LambertianRadiance : RadianceModel
    {
        public override double Weight(Vec3 objectNormal, Vec3 direction) => 1.0;
        public override bool IsUniform => true;
        public override string Describe() => "Lambertian, uniform radiance";
    }
}

/// <summary>Radiance proportional to cos^N of the angle between the ray and the object normal.</summary>
public sealed class CosinePowerRadiance : RadianceModel
{
    public double Power { get; }

    public CosinePowerRadiance(double power) { Power = power; }

    public override double Weight(Vec3 objectNormal, Vec3 direction)
    {
        double c = Math.Abs(objectNormal.Dot(direction));
        return Math.Pow(Math.Min(1.0, c), Power);
    }

    public override string Describe() => $"radiance ∝ cos^{Power} from the object normal";
}
