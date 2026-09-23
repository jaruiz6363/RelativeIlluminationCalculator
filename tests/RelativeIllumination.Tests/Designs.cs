using System;
using System.IO;
using System.Linq;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Glass;
using RelativeIllumination.Core.Illumination;
using RelativeIllumination.Core.Models;
using RelativeIllumination.Core.RayTrace;
using RelativeIllumination.IO;

namespace RelativeIllumination.Tests;

/// <summary>Systems whose relative illumination is known exactly, and the lens files in the fixtures.</summary>
internal static class Designs
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "lenses", name);

    private static readonly Lazy<GlassCatalog> Catalog = new(CatalogLocator.LoadBundled);

    /// <summary>Opens a fixture lens and makes it ready to trace at its primary wavelength.</summary>
    public static TraceSystem Open(string name, bool clipAutomatic = false)
    {
        var system = LensFile.Read(Fixture(name), Catalog.Value);
        double lambda = system.Wavelengths[system.PrimaryWavelengthIndex].Value;
        return new TraceSystem(system, IndexResolver.Build(system, Catalog.Value, lambda), lambda, clipAutomatic);
    }

    /// <summary>A system in air: every index 1.</summary>
    public static TraceSystem InAir(OpticalSystem system) =>
        new(system, Enumerable.Repeat(1.0, system.Surfaces.Count).ToArray(), 0.55);

    private static OpticalSystem Shell(FieldType fields, double epd)
    {
        var s = new OpticalSystem
        {
            Title = "test",
            FieldType = fields,
            Aperture = new Aperture(ApertureType.EPD, epd),
        };
        s.Wavelengths.Add(new Wavelength(0.55, 1.0, isPrimary: true));
        s.Fields.Add(new Field(0.0));
        return s;
    }

    /// <summary>
    /// An ideal thin lens of focal length <paramref name="f"/> with the stop on it, radius
    /// <paramref name="a"/>, and the image plane at <paramref name="imageDistance"/>. Object at
    /// infinity when <paramref name="objectDistance"/> is infinite.
    /// </summary>
    public static OpticalSystem IdealLensStopAtLens(double f, double a, double objectDistance, double imageDistance,
                                                    double obscuration = 0.0, double objectRadius = double.PositiveInfinity,
                                                    double imageRadius = double.PositiveInfinity)
    {
        bool infinite = double.IsInfinity(objectDistance);
        var s = Shell(infinite ? FieldType.ObjectAngle : FieldType.ObjectHeight, 2.0 * a);
        s.Surfaces.Add(new Surface { Index = 0, Thickness = objectDistance, Radius = objectRadius });
        s.Surfaces.Add(new Surface
        {
            Index = 1, Type = SurfaceType.Paraxial, FocalLength = f, Thickness = imageDistance,
            IsStop = true, ObscurationRadius = obscuration,
        });
        s.Surfaces.Add(new Surface { Index = 2, Radius = imageRadius });
        return s;
    }

    /// <summary>
    /// An ideal thin lens with the object at infinity and the stop BEHIND it, a distance
    /// <paramref name="d"/> toward the focus, radius <paramref name="a"/>. The entrance pupil is
    /// then a virtual image of the stop, and nothing but the stop limits the beam.
    /// </summary>
    public static OpticalSystem IdealLensStopBehind(double f, double d, double a)
    {
        // The axial marginal ray converges from height EPD/2 at the lens to the focus, so at the
        // stop it stands at (EPD/2)(1 - d/f): that is the EPD which fills a stop of radius a.
        var s = Shell(FieldType.ObjectAngle, 2.0 * a / (1.0 - d / f));
        s.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        s.Surfaces.Add(new Surface { Index = 1, Type = SurfaceType.Paraxial, FocalLength = f, Thickness = d });
        s.Surfaces.Add(new Surface { Index = 2, Thickness = f - d, IsStop = true });
        s.Surfaces.Add(new Surface { Index = 3 });
        return s;
    }

    /// <summary>
    /// A paraboloidal mirror with the object at infinity, the stop on the mirror and a central
    /// obscuration: a folded system, traced back along -z after the reflection.
    /// </summary>
    public static OpticalSystem Paraboloid(double focal, double a, double obscuration)
    {
        var s = Shell(FieldType.ObjectAngle, 2.0 * a);
        s.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        s.Surfaces.Add(new Surface
        {
            Index = 1, Radius = -2.0 * focal, Conic = -1.0, Material = "MIRROR",
            Thickness = -focal, IsStop = true, ObscurationRadius = obscuration,
        });
        s.Surfaces.Add(new Surface { Index = 2 });
        return s;
    }

    /// <summary>
    /// Illuminance at distance <paramref name="k"/> from a uniformly bright disk of radius
    /// <paramref name="a"/>, at lateral offset <paramref name="x"/>, on a plane parallel to the
    /// disk - Foote's exact result as Gardner quotes it (J. Res. NBS 39, 213, 1947, Eq. 7), up to
    /// the constant pi L / 2.
    /// </summary>
    public static double Disk(double k, double a, double x)
    {
        double s = k * k + x * x;
        return 1.0 - (s - a * a) / Math.Sqrt((s + a * a) * (s + a * a) - 4.0 * a * a * x * x);
    }

    /// <summary>The fields to measure, all in y.</summary>
    public static (double X, double Y)[] Fields(params double[] y) => y.Select(v => (0.0, v)).ToArray();

    /// <summary>Fine sampling, for tests that compare against exact answers.</summary>
    public static IlluminationOptions Fine() => new() { BaseCells = 32, MaxDepth = 5, Bisections = 16 };
}
