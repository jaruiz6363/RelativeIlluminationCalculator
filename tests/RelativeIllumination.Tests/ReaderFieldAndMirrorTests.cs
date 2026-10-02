using System;
using System.IO;
using System.Linq;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Glass;
using RelativeIllumination.Core.Models;
using RelativeIllumination.Core.RayTrace;
using RelativeIllumination.IO;
using Xunit;

namespace RelativeIllumination.Tests;

/// <summary>
/// Reader fixes carried over from AberrationCalculator: a mirror inside glass in an Optalix file,
/// and a negative field angle in an OSLO file.
/// </summary>
public class ReaderFieldAndMirrorTests
{
    // A Mangin mirror as Optalix writes one (Telescopes/43-84_Mangin-mirror.otx): the mirror is the
    // back of the glass, and its GLA names the medium the reflected light goes on in - the glass.
    private const string Mangin = @"VERS 11.82
RAIM  2
EPD  20.0000
WL   0.5875618
WTW  100
REF    1
FTYP    1
NFLD    1
FLD    1   0.000000000       0.000000000      100  1        0
SUR   0
  SUT S
  CUY 0.0000000000000
  THI  0.1000000000E+21
SUR   1
  SUT S
  CUY -0.2000000000000E-02
  THI   10.00000000
  GLA N-BK7
  STO
SUR   2
  SUT SM
  CUY -0.4000000000000E-02
  THI  -10.00000000
  GLA N-BK7
SUR   3
  SUT S
  CUY -0.2000000000000E-02
  THI  -100.0000000
SUR   4
  SUT S
  CUY 0.0000000000000
  THI 0.000000000
";

    private static readonly Lazy<GlassCatalog> Catalog = new(CatalogLocator.LoadBundled);

    private static string Temp(string text, string ext)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ric_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, text);
        return path;
    }

    private static double Efl(OpticalSystem system)
    {
        double lambda = system.Wavelengths[system.PrimaryWavelengthIndex].Value;
        return new TraceSystem(system, IndexResolver.Build(system, Catalog.Value, lambda), lambda).Paraxial.Efl;
    }

    /// <summary>The same Mangin mirror, built here: the reflection a plain mirror, inside the glass.</summary>
    private static OpticalSystem ManginByHand()
    {
        var s = new OpticalSystem { Title = "Mangin", Aperture = new Aperture(ApertureType.EPD, 20.0) };
        s.Wavelengths.Add(new Wavelength(0.5875618, 1.0, isPrimary: true));
        s.Fields.Add(new Field(0.0));
        s.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        s.Surfaces.Add(new Surface { Index = 1, Radius = -500.0, Thickness = 10.0, Material = "N-BK7", IsStop = true });
        s.Surfaces.Add(new Surface { Index = 2, Radius = -250.0, Thickness = -10.0, Material = "MIRROR" });
        s.Surfaces.Add(new Surface { Index = 3, Radius = -500.0, Thickness = -100.0 });
        s.Surfaces.Add(new Surface { Index = 4 });
        return s;
    }

    /// <summary>
    /// A mirror with a glass named on it is still a mirror: in Optalix the glass is the medium the
    /// light goes on in. Read as the surface's own glass, the Mangin mirror became a refracting
    /// surface and the lens another lens.
    /// </summary>
    [Fact]
    public void AnOptalixManginMirrorIsAMirrorInGlass()
    {
        string path = Temp(Mangin, ".otx");
        try
        {
            var sys = LensFile.Read(path, Catalog.Value);
            Assert.True(sys.Surfaces[2].IsMirror);
            Assert.Equal("N-BK7", sys.Surfaces[1].Material);
            Assert.True(string.IsNullOrEmpty(sys.Surfaces[3].Material));
            double efl = Efl(sys);
            Assert.True(double.IsFinite(efl) && efl != 0.0, $"EFL {efl}");
            Assert.Equal(Efl(ManginByHand()), efl, 9);
        }
        finally { File.Delete(path); }
    }

    // A Maksutov's two hiding places, as its .zmx writes them: a flat air dummy with DIAM 1e-6
    // fixed and no aperture record (surface 1), and the secondary's spot with DIAM 0.001 fixed and
    // an obscuration of 2.5 (surface 4). Each blocked every ray.
    private const string MaksutovLike = @"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 10
FTYP 0 0 1 1 0 0 0
YFLN 0
WAVM 1 0.55 1
PWAV 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  CURV 0.0 0 0 0 0 """"
  DISZ 5
  DIAM 9.9999999700000005e-07 1 0 0 1 """"
  MEMA 9.9999999700000005e-07 0 0 0 1 """"
SURF 2
  STOP
  CURV 0
  DISZ 10
SURF 3
  CURV 0
  DISZ 10
  DIAM 0.00100000005 1 0 0 1 """"
  MEMA 2.5 0 0 0 1 """"
  OBSC 0 2.5 0
SURF 4
  TYPE PARAXIAL
  PARM 1 100
  DISZ 100
SURF 5
  CURV 0
  DISZ 0
";

    /// <summary>
    /// A flat air dummy without an aperture record does not clip (a .zmx semi-diameter blocks
    /// light only through an aperture record), and an obscured surface's fixed semi-diameter is
    /// the obscuration's drawn size, not an outer aperture.
    /// </summary>
    [Fact]
    public void AMaksutovsHiddenSurfacesDoNotBlockTheBeam()
    {
        string path = Temp(MaksutovLike, ".zmx");
        try
        {
            var sys = LensFile.Read(path, Catalog.Value);
            Assert.Equal(SemiDiameterMode.Auto, sys.Surfaces[1].SemiDiameterMode);
            var apertures = new ApertureModel(sys, 5.0, clipAutomatic: false);
            Assert.True(double.IsPositiveInfinity(apertures.Outer(1)), "the dummy does not clip");
            Assert.True(double.IsPositiveInfinity(apertures.Outer(3)), "the obscured surface has no outer aperture");
            Assert.Equal(2.5, apertures.Inner(3), 9);                   // its obscuration still blocks
        }
        finally { File.Delete(path); }
    }

    private static string StopWithClap(double enpd, double clapOuter) => $@"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD {enpd.ToString(System.Globalization.CultureInfo.InvariantCulture)}
FTYP 0 0 1 1 0 0 0
YFLN 0
WAVM 1 0.55 1
PWAV 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  STOP
  CURV 0
  DISZ 10
  CLAP 0 {clapOuter.ToString(System.Globalization.CultureInfo.InvariantCulture)} 0
SURF 2
  TYPE PARAXIAL
  PARM 1 100
  DISZ 100
SURF 3
  CURV 0
  DISZ 0
";

    /// <summary>
    /// The stop's CLAP sets the entrance pupil only when it is smaller than ENPD (a stock lens,
    /// whose ENPD is the part's full diameter). A larger one - a telescope primary's, CLAP 80
    /// under ENPD 150 - does not limit the beam, and used to widen the pupil to 160.
    /// </summary>
    [Theory]
    [InlineData(150.0, 80.0, 150.0)]
    [InlineData(25.4, 11.43, 22.86)]
    public void TheStopClapOnlyNarrowsThePupil(double enpd, double clapOuter, double expected)
    {
        string path = Temp(StopWithClap(enpd, clapOuter), ".zmx");
        try
        {
            var sys = LensFile.Read(path, Catalog.Value);
            Assert.Equal(expected, sys.Aperture.Value, 9);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A negative OSLO field angle is read by its size; it used to be dropped altogether.</summary>
    [Fact]
    public void ANegativeOsloFieldAngleIsKept()
    {
        string text = File.ReadAllText(Designs.Fixture("KingslakeDG.len"));
        string angLine = text.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("ANG "));
        string path = Temp(text.Replace(angLine, "ANG -14"), ".len");
        try
        {
            var sys = LensFile.Read(path, Catalog.Value);
            Assert.Equal(FieldType.ObjectAngle, sys.FieldType);
            Assert.Equal(new[] { 0.0, 14.0 }, sys.Fields.Select(f => f.Y).ToArray());
        }
        finally { File.Delete(path); }
    }
}
