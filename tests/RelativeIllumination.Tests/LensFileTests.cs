using System;
using System.IO;
using System.Linq;

using RelativeIllumination.Core.Illumination;
using Xunit;

namespace RelativeIllumination.Tests;

/// <summary>Real designs, opened from the files the common design programs write.</summary>
public class LensFileTests
{
    [Theory]
    [InlineData("KingslakeDG.zmx")]
    [InlineData("KingslakeDG.seq")]
    [InlineData("KingslakeDG.otx")]
    [InlineData("KingslakeDG.len")]
    [InlineData("KingslakeDG.json")]
    [InlineData("KingslakeDG.lhlt")]
    public void EveryFormatOpensToTheSameLens(string file)
    {
        var ts = Designs.Open(file);
        var reference = Designs.Open("KingslakeDG.zmx");
        Assert.True(Math.Abs(ts.Paraxial.Efl - reference.Paraxial.Efl) < 1e-6);
        Assert.True(Math.Abs(ts.StopRadius - reference.StopRadius) < 1e-6);

        // Inside the field where no stored aperture vignettes, every format gives the same RI.
        var ri = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 7))[1].Reverse!.RelativeIllumination;
        var riRef = RelativeIlluminationCalculator.Compute(reference, Designs.Fields(0, 7))[1].Reverse!.RelativeIllumination;
        Assert.True(Math.Abs(ri - riRef) < 1e-4, $"{file}: {ri} vs {riRef}");
    }

    [Theory]
    [InlineData("KingslakeDG.zmx")]
    [InlineData("CookeTriplet.lhlt")]
    public void ForwardAndReverseAgreeOnARealLens(string file)
    {
        var ts = Designs.Open(file);
        double max = ts.System.MaxFieldY();
        var fields = Enumerable.Range(0, 6).Select(k => (0.0, max * k / 5)).ToArray();
        var results = RelativeIlluminationCalculator.Compute(ts, fields);

        Assert.Equal(1.0, results[0].Reverse!.RelativeIllumination, 12);
        foreach (var r in results)
        {
            Assert.True(r.Ok, r.Failure);
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - r.Reverse!.RelativeIllumination) < 1e-3,
                $"{file} {r.FieldY}: forward {r.Forward.RelativeIllumination} reverse {r.Reverse.RelativeIllumination}");
        }
    }

    [Fact]
    public void CheckedAperturesVignette()
    {
        // The OSLO file marks every aperture as checked, so they clip; the Zemax file stores no
        // semi-diameters at all. Same lens: the OSLO one must lose light at the edge of the field
        // and nowhere near the axis - and the two methods must still agree when it does.
        var clipped = Designs.Open("KingslakeDG.len");
        var open = Designs.Open("KingslakeDG.zmx");
        var c = RelativeIlluminationCalculator.Compute(clipped, Designs.Fields(0, 14));
        var o = RelativeIlluminationCalculator.Compute(open, Designs.Fields(0, 14));

        Assert.True(Enumerable.Range(1, clipped.LastOptical).Count(clipped.Apertures.Clips) > 1);
        Assert.True(c[1].Reverse!.RelativeIllumination < o[1].Reverse!.RelativeIllumination - 1e-3);
        Assert.True(Math.Abs(c[1].Forward!.RelativeIllumination - c[1].Reverse!.RelativeIllumination) < 2e-3);
    }

    [Fact]
    public void RimmerCorrectionIsNeededEvenAtF8()
    {
        // Forward-traced rays do not all pass through the image point. Using their own directions
        // measures the cone somewhere else; referring them to the image point through the
        // reference sphere (Rimmer Eq. 3) recovers the backward-traced answer.
        var ts = Designs.Open("KingslakeDG.zmx");
        var corrected = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 14))[1];
        var raw = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 14),
                                                         new IlluminationOptions { RimmerCorrection = false })[1];
        double exact = corrected.Reverse!.RelativeIllumination;

        Assert.True(Math.Abs(corrected.Forward!.RelativeIllumination - exact) < 5e-4);
        Assert.True(Math.Abs(raw.Forward!.RelativeIllumination - exact) > 1e-2,
            $"raw {raw.Forward.RelativeIllumination} vs {exact}");
    }

    [Fact]
    public void SiewEstimateTracksTheMeasurementOnAModestLens()
    {
        // Siew's formula is a small-aperture approximation; at f/8 it should be close.
        var ts = Designs.Open("KingslakeDG.zmx");
        var r = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 14))[1];
        Assert.NotNull(r.Siew);
        Assert.True(Math.Abs(r.Siew!.Estimate - r.Reverse!.RelativeIllumination) < 5e-3,
            $"Siew {r.Siew.Estimate} vs {r.Reverse.RelativeIllumination}");
    }

    [Fact]
    public void CommandLinePrintsTheTable()
    {
        var output = new StringWriter();
        int code = Cli.Program.Run(new[] { Designs.Fixture("KingslakeDG.zmx"), "--fields", "2" }, output, new StringWriter());
        Assert.Equal(0, code);
        string text = output.ToString();
        Assert.Contains("RI rev", text);
        Assert.Contains("RI fwd", text);
        Assert.Contains("EFL 100.0039", text);
    }
}
