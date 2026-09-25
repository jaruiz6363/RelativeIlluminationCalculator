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
    public void AnOsloFilesPrimaryWavelengthIsTheFirst()
    {
        // OSLO has no primary-wavelength keyword: wavelength 1 is the primary. The same lens
        // written d, F, C and F, d, C is therefore a d-line lens and an F-line lens, which is how
        // OSLO itself traces them.
        string dFirst = Designs.Fixture("KingslakeDG.len");
        string fFirst = Path.Combine(Path.GetTempPath(), $"KingslakeDG_F_first_{Guid.NewGuid():N}.len");
        File.WriteAllText(fFirst, File.ReadAllText(dFirst).Replace("WV  0.58756 0.48613 0.65627", "WV  0.48613 0.58756 0.65627"));
        try
        {
            var d = IO.LensFile.Read(dFirst);
            var f = IO.LensFile.Read(fFirst);
            Assert.Equal(0.58756, d.Wavelengths[d.PrimaryWavelengthIndex].Value, 5);
            Assert.Equal(0.48613, f.Wavelengths[f.PrimaryWavelengthIndex].Value, 5);
        }
        finally { File.Delete(fFirst); }
    }

    [Theory]
    [InlineData("Topogon_US2031792_Fig1", 35.0)]           // model glasses; F/6.3 as EBR
    [InlineData("CookeTriplet with Vignetting", 20.0)]     // checked and unchecked apertures
    [InlineData("Paraboloid_Mirror", 0.5)]                  // stop on the mirror
    [InlineData("IdealLens_CurvedImage_R200", 30.0)]        // perfect lens, curved image
    [InlineData("IdealLens_CurvedImage_R-150", 30.0)]
    [InlineData("IdealLens_CurvedObject", 60.0)]            // NAO, OBH, RD on the object, PFM
    public void AnOsloExportReadsAsTheLensItCameFrom(string name, double field)
    {
        // LensHH-LT 1.0.158 exports of the .zmx lenses, written as OSLO writes (EBR/ANG, PFL,
        // GLA MOD with an index per wavelength, AP CHK only where the source clips, AP otherwise).
        var zmx = Designs.Open(name + ".zmx");
        var len = Designs.Open(name + ".len");
        Assert.True(Math.Abs(len.Paraxial.Efl - zmx.Paraxial.Efl) < 1e-3 * Math.Abs(zmx.Paraxial.Efl),
            $"EFL {len.Paraxial.Efl} vs {zmx.Paraxial.Efl}");
        // A model glass arrives as its index at the file's wavelength (1.62010066 for the
        // Topogon's nd 1.6201), so EFL and stop radius may differ in the fifth digit.
        Assert.True(Math.Abs(len.StopRadius - zmx.StopRadius) < 1e-5 * zmx.StopRadius, $"stop {len.StopRadius} vs {zmx.StopRadius}");

        var a = RelativeIlluminationCalculator.Compute(len, Designs.Fields(0, field))[1].Forward!;
        var b = RelativeIlluminationCalculator.Compute(zmx, Designs.Fields(0, field))[1].Forward!;
        Assert.True(Math.Abs(a.RelativeIllumination - b.RelativeIllumination) < 1e-4,
            $"{name}: RI {a.RelativeIllumination} vs {b.RelativeIllumination}");
    }

    [Fact]
    public void AnObjectNaFillsThePupilItsMarginalRayReaches()
    {
        // NA = n sin(theta). At NA 0.5 the marginal ray leaves the object at 30 degrees and meets
        // the entrance pupil, at the lens 200 away, at 200 tan 30. Taking NA/n itself as the slope
        // (sin for tan) gave 100, a pupil 13 % small; OSLO and LensHH-LT take tan, and OpticStudio
        // reports an entrance pupil diameter of 230.9401 for this lens (2026-09-25).
        var sys = Designs.IdealLensStopAtLens(100, 120, 200, 200);
        sys.Aperture = new Core.Models.Aperture(Core.Enums.ApertureType.ObjectSpaceNA, 0.5);
        Assert.Equal(2.0 * 200.0 * Math.Tan(Math.PI / 6.0), Designs.InAir(sys).Paraxial.Epd, 9);
    }

    // Written by OSLO 6.6 EDU itself: a finite object with a curved surface, a perfect lens, a
    // catalog glass, a model glass (an index per wavelength), one checked and one unchecked aperture.
    private const string OslosOwnFile = @"// OSLO 6.6 58447     0     0
LEN NEW ""No name"" 100 5
NAO  0.05
OBH  10.0
DES  ""OSLO""
UNI  1.0
// SRF 0
AIR
RD   -300.0
TH   200.0
AP  10.0
NXT  // SRF 1
AIR
TH   100.0
PFL    100.0
NXT  // SRF 2
GLA BAF4
RD   200.0
TH   10.0
AP CHK 0.0
NXT  // SRF 3
WV 0.58756 0.48613 0.65627
GLA MOD MODEL1      1.6201 1.6272360458395 1.6169694895481
TCE  236.0
TH   20.0
NXT  // SRF 4
AIR
TH   30.0
AP  0.4595340390767
NXT  // SRF 5
AIR
WV 0.58756 0.48613 0.65627
WW 1.0 1.0 1.0
END  5
";

    [Fact]
    public void ReadsWhatOsloWrites()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oslo_own_{Guid.NewGuid():N}.len");
        File.WriteAllText(path, OslosOwnFile);
        try
        {
            var sys = IO.LensFile.Read(path);
            Assert.Equal(Core.Enums.ApertureType.ObjectSpaceNA, sys.Aperture.Type);
            Assert.Equal(0.05, sys.Aperture.Value, 12);
            Assert.Equal(Core.Enums.FieldType.ObjectHeight, sys.FieldType);
            Assert.Equal(10.0, sys.Fields.Max(f => f.Y), 12);
            Assert.Equal(-300.0, sys.Surfaces[0].Radius, 12);
            Assert.Equal(Core.Enums.SurfaceType.Paraxial, sys.Surfaces[1].Type);
            Assert.Equal(100.0, sys.Surfaces[1].FocalLength, 12);
            Assert.True(sys.Surfaces[3].ModelIndexEnabled);
            Assert.Equal(1.6201, sys.Surfaces[3].ModelNd, 9);
            Assert.Equal(60.4, sys.Surfaces[3].ModelVd, 3);
            Assert.Equal(Core.Enums.SemiDiameterMode.Auto, sys.Surfaces[4].SemiDiameterMode);   // AP, not AP CHK
            Assert.Equal(3, sys.Wavelengths.Count);          // the repeated WV line replaced, not added
        }
        finally { File.Delete(path); }
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
