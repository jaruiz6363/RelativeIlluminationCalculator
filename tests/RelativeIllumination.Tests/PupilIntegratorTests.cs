using System;

using RelativeIllumination.Core.Illumination;
using Xunit;

namespace RelativeIllumination.Tests;

/// <summary>The area measurement alone, on regions of known area, with the identity map.</summary>
public class PupilIntegratorTests
{
    private static PupilIntegral Measure(Func<double, double, bool> inside, double half = 1.5) =>
        new PupilIntegrator((x, y) => inside(x, y) ? new PupilSample(true, x, y, 1.0) : PupilSample.Blocked,
                            0.0, 0.0, half, baseCells: 32, maxDepth: 5, bisections: 16).Integrate();

    [Fact]
    public void Disk()
    {
        var r = Measure((x, y) => x * x + y * y <= 1.0);
        Assert.True(Math.Abs(r.MappedArea / Math.PI - 1) < 1e-5, $"{r.MappedArea}");
        Assert.False(r.TouchesWindow);
    }

    [Fact]
    public void Annulus_HasAHoleTheCentreRayCannotSee()
    {
        var r = Measure((x, y) => { double q = x * x + y * y; return q <= 1.0 && q >= 0.16; });
        Assert.True(Math.Abs(r.MappedArea / (Math.PI * (1 - 0.16)) - 1) < 1e-5, $"{r.MappedArea}");
    }

    [Fact]
    public void TwoSeparateDisks()
    {
        var r = Measure((x, y) =>
            (x - 0.7) * (x - 0.7) + y * y <= 0.25 || (x + 0.7) * (x + 0.7) + y * y <= 0.25);
        Assert.True(Math.Abs(r.MappedArea / (2 * Math.PI * 0.25) - 1) < 1e-5, $"{r.MappedArea}");
    }

    [Fact]
    public void DiskClippedFromBothSides()
    {
        // The lens shape two vignetting apertures cut from a pupil: a disk between two chords.
        var r = Measure((x, y) => x * x + y * y <= 1.0 && Math.Abs(y) <= 0.5);
        double segment = Math.Acos(0.5) - 0.5 * Math.Sqrt(1 - 0.25);     // one cut-off cap
        double exact = Math.PI - 2 * segment;
        Assert.True(Math.Abs(r.MappedArea / exact - 1) < 1e-5, $"{r.MappedArea} vs {exact}");
    }

    [Fact]
    public void RegionLargerThanTheWindow_IsReported()
    {
        var r = Measure((x, y) => x * x + y * y <= 4.0, half: 1.0);
        Assert.True(r.TouchesWindow);
    }

    [Fact]
    public void MappedArea_IsTheJacobianIntegral()
    {
        // A linear map that stretches x by 3 and shears: every area scales by the determinant.
        var r = new PupilIntegrator(
            (x, y) => x * x + y * y <= 1.0 ? new PupilSample(true, 3 * x + y, 0.5 * y, 1.0) : PupilSample.Blocked,
            0, 0, 1.5, 32, 5, 16).Integrate();
        Assert.True(Math.Abs(r.MappedArea / (1.5 * Math.PI) - 1) < 1e-5, $"{r.MappedArea}");
        Assert.True(Math.Abs(r.ParameterArea / Math.PI - 1) < 1e-5, $"{r.ParameterArea}");
    }
}
