using System;
using System.Linq;

using RelativeIllumination.Core.Illumination;
using Xunit;

namespace RelativeIllumination.Tests;

/// <summary>Published and cross-checked relative-illumination values for real lenses.</summary>
public class BenchmarkTests
{
    /// <summary>
    /// A Cooke triplet whose off-axis beam is cut by fixed apertures on four surfaces, so its
    /// relative illumination is set by vignetting. The value at 20° was confirmed by the ZPL
    /// macro on OpticStudio's own rays (0.3331 Rimmer-corrected, 0.3362 uncorrected); a
    /// ray-counting analysis on this lens is jagged by up to 1.5 points, and LensHH-LT 1.0.156,
    /// which clips at SD·sqrt(1.01), reads 0.3430.
    /// </summary>
    [Fact]
    public void VignettedCooke_BothMethodsAgreeWithOpticStudioRays()
    {
        var ts = Designs.Open("CookeTriplet with Vignetting.zmx");
        Assert.True(Enumerable.Range(1, ts.LastOptical).Count(ts.Apertures.Clips) >= 4);

        var fields = Designs.Fields(0, 2, 4, 8, 12, 16, 20);
        var r = RelativeIlluminationCalculator.Compute(ts, fields);
        foreach (var f in r)
            Assert.True(Math.Abs(f.Forward!.RelativeIllumination - f.Reverse!.RelativeIllumination) < 1e-3,
                $"{f.FieldY} deg: forward {f.Forward.RelativeIllumination:F4} reverse {f.Reverse.RelativeIllumination:F4}");

        var edge = r.Last();
        Assert.True(Math.Abs(edge.Reverse!.RelativeIllumination - 0.3338) < 1e-3, $"reverse {edge.Reverse.RelativeIllumination:F4}");
        Assert.True(Math.Abs(edge.Forward!.RelativeIllumination - 0.3331) < 1e-3, $"forward {edge.Forward.RelativeIllumination:F4}");

        var raw = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 20),
                                                         new IlluminationOptions { RimmerCorrection = false })[1];
        Assert.True(Math.Abs(raw.Forward!.RelativeIllumination - 0.3362) < 1e-3, $"raw {raw.Forward.RelativeIllumination:F4}");
    }

    /// <summary>
    /// The same lens, where the vignetted pupil has pointed tips. With cells judged only on their
    /// own five rays, the default 32-cell grid dropped a tip lying between rays and read
    /// 5.5e-4 low from 16.7° (reverse) and 3.5e-4 low from 18.9° (forward): a step in the RI
    /// curve that a grid twice as fine did not have. Refining beside every cut cell removes it,
    /// so the answer no longer depends on the coarse grid.
    /// </summary>
    [Fact]
    public void VignettedCooke_NoStepsFromTheCoarseGrid()
    {
        var ts = Designs.Open("CookeTriplet with Vignetting.zmx");
        var fields = Designs.Fields(0, 16.6, 16.7, 16.9, 18.8, 18.9, 19.0);
        var coarse = RelativeIlluminationCalculator.Compute(ts, fields);
        var fine = RelativeIlluminationCalculator.Compute(ts, fields, new IlluminationOptions { BaseCells = 64 });

        for (int k = 1; k < fields.Length; k++)
        {
            double dr = coarse[k].Reverse!.RelativeIllumination - fine[k].Reverse!.RelativeIllumination;
            double df = coarse[k].Forward!.RelativeIllumination - fine[k].Forward!.RelativeIllumination;
            Assert.True(Math.Abs(dr) < 2e-5 && Math.Abs(df) < 2e-5,
                $"{coarse[k].FieldY} deg: reverse differs by {dr:E1}, forward by {df:E1}");
        }
    }

    /// <summary>
    /// Richter's Topogon, US 2,031,792 Fig. 1: f = 66 mm, f/6.3, at 35°. Rimmer (Proc. SPIE 655,
    /// 99, 1986, Table 1) gives 34.7 % from a grid of rays corrected for image aberration, 34.9 %
    /// from four corrected rays, 36.7 % from four UNcorrected rays, and Kingslake 35.0 %.
    ///
    /// <para>Rimmer's point about this lens is that it falls BELOW cos⁴ (45 % at 35°) without any
    /// vignetting: its positive outer elements steepen the chief ray in stop space and shrink the
    /// pupil off axis. The prescription is read from the patent drawing; Rimmer does not say where
    /// he put the image plane or at which wavelength he traced, so agreement is expected to within
    /// a few tenths of a point rather than to his last digit.</para>
    /// </summary>
    [Fact]
    public void Topogon_Rimmer1986_Table1()
    {
        var ts = Designs.Open("Topogon_US2031792_Fig1.zmx");
        Assert.True(Math.Abs(ts.Paraxial.Efl - 66.0) < 0.1, $"EFL {ts.Paraxial.Efl}: the prescription is not the patent's");

        var r = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 35))[1];
        double reverse = r.Reverse!.RelativeIllumination;
        double forward = r.Forward!.RelativeIllumination;

        // Between Rimmer's grid (34.7) and Kingslake (35.0), within half a point.
        Assert.True(reverse > 0.342 && reverse < 0.355, $"RI at 35 deg = {reverse:P2}");
        Assert.True(Math.Abs(forward - reverse) < 1e-3, $"forward {forward:P2} vs reverse {reverse:P2}");

        // Below cos⁴ with no vignetting at all: the stop alone limits the beam.
        Assert.True(reverse < 0.8 * Math.Pow(Math.Cos(35 * Math.PI / 180.0), 4));

        // Without referring the rays to the image point the forward method reads about two points
        // high - the same error Rimmer's own uncorrected calculation shows (36.7 against 34.7).
        var raw = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 35),
                                                         new IlluminationOptions { RimmerCorrection = false })[1];
        double excess = raw.Forward!.RelativeIllumination - reverse;
        Assert.True(excess > 0.01 && excess < 0.03, $"uncorrected excess {excess:P2}");
    }
}
