using System;
using System.Linq;

using RelativeIllumination.Core.Illumination;
using RelativeIllumination.Optiland;
using Xunit;
using Xunit.Abstractions;

namespace RelativeIllumination.Tests;

/// <summary>
/// The Optiland cross-check: the same lens built inside Optiland from this program's parsed
/// prescription, traced with Optiland's rays, measured by the forward method.
///
/// <para>These tests need the embedded Python that <c>tools/setup-python.ps1</c> installs. A
/// fresh clone does not have it, so they report that they did nothing rather than failing -
/// and say so in the test output, so a green run cannot quietly mean "never ran".</para>
/// </summary>
public class OptilandTests
{
    private readonly ITestOutputHelper _output;

    public OptilandTests(ITestOutputHelper output) { _output = output; }

    private bool Ready()
    {
        if (PythonEnvironment.IsReady) return true;
        _output.WriteLine("NOT RUN: " + PythonEnvironment.SetupHint);
        return false;
    }

    [Fact]
    public void OptilandRaysReproduceTheForwardMethodOnAVignettedLens()
    {
        if (!Ready()) return;

        var ts = Designs.Open("CookeTriplet with Vignetting.zmx");
        var optic = OptilandOptic.Build(ts);

        // The lens arrived intact: Optiland's own paraxial trace agrees with ours and with
        // OpticStudio (50.0005). Reading the .zmx with Optiland's importer gives 50.478 instead
        // - see docs/optiland-0.6.2.md - which is why the prescription is handed over directly.
        var first = optic.Describe();
        Assert.True(Math.Abs(first.Efl - ts.Paraxial.Efl) < 5e-3, $"Optiland EFL {first.Efl} vs {ts.Paraxial.Efl}");
        Assert.True(Math.Abs(first.Epd - ts.Paraxial.Epd) < 1e-9);

        double[] fields = { 0, 8, 16, 20 };
        var theirs = OptilandIllumination.Compute(ts, optic, fields);
        var mine = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(fields));

        for (int k = 0; k < fields.Length; k++)
        {
            Assert.True(theirs[k].Ok, theirs[k].Failure);
            double diff = theirs[k].RelativeIllumination - mine[k].Forward!.RelativeIllumination;
            _output.WriteLine($"{fields[k],5} deg: optiland {theirs[k].RelativeIllumination:F6} " +
                              $"ricalc {mine[k].Forward!.RelativeIllumination:F6} diff {diff:E1}");
            // The same integrator on the same lens: only the tracer, and the pupil parameterisation
            // (Optiland's normalised stop against this program's entrance pupil), differ.
            Assert.True(Math.Abs(diff) < 5e-5, $"{fields[k]} deg: differs by {diff:E1}");

            // Siew's effective F/#, from the same solid angle by the same formula.
            double fr = theirs[k].EffectiveFNumber / mine[k].Forward!.EffectiveFNumber - 1;
            Assert.True(Math.Abs(fr) < 5e-5, $"{fields[k]} deg: F/#eff {theirs[k].EffectiveFNumber:F4} vs {mine[k].Forward!.EffectiveFNumber:F4}");
        }
    }

    [Fact]
    public void IdealLensOnACurvedImage()
    {
        if (!Ready()) return;

        // Optiland's "paraxial" surface is an ideal thin lens of focal length f, and its image
        // surface can be curved, so this lens - whose exact relative illumination is the cone to
        // the stop disk about the surface normal - can be measured with its rays too.
        var ts = Designs.Open("IdealLens_CurvedImage_R200.zmx");
        var optic = OptilandOptic.Build(ts);
        Assert.True(Math.Abs(optic.Describe().Efl - 100.0) < 1e-6);

        double[] fields = { 0, 15, 30 };
        double[] exact = { 1.0, 0.8028, 0.3558 };
        var theirs = OptilandIllumination.Compute(ts, optic, fields);
        for (int k = 0; k < fields.Length; k++)
        {
            Assert.True(theirs[k].Ok, theirs[k].Failure);
            _output.WriteLine($"{fields[k],5} deg: optiland {theirs[k].RelativeIllumination:F6} exact {exact[k]:F6}");
            Assert.True(Math.Abs(theirs[k].RelativeIllumination - exact[k]) < 2e-4,
                $"{fields[k]} deg: {theirs[k].RelativeIllumination:F4} vs exact {exact[k]:F4}");
        }
    }

    [Fact]
    public void ParaboloidalMirror()
    {
        if (!Ready()) return;

        // A folded system: the thickness after the mirror is negative and the image sits at
        // z = -200. Optiland reflects on material "mirror" and keeps the same convention.
        var ts = Designs.Open("Paraboloid_Mirror.zmx");
        var optic = OptilandOptic.Build(ts);
        Assert.True(Math.Abs(Math.Abs(optic.Describe().Efl) - 200.0) < 1e-6,
            $"Optiland EFL {optic.Describe().Efl} (its sign convention for a folded system differs)");

        double[] fields = { 0, 0.5 };
        var theirs = OptilandIllumination.Compute(ts, optic, fields);
        var mine = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(fields));
        for (int k = 0; k < fields.Length; k++)
        {
            Assert.True(theirs[k].Ok, theirs[k].Failure);
            Assert.True(Math.Abs(theirs[k].RelativeIllumination - mine[k].Reverse!.RelativeIllumination) < 1e-3,
                $"{fields[k]} deg: optiland {theirs[k].RelativeIllumination:F4} vs {mine[k].Reverse.RelativeIllumination:F4}");
        }
    }

    [Fact]
    public void CurvedObjectSurface()
    {
        if (!Ready()) return;

        // Optiland's object surface takes a radius like any other, so a field point sits at its
        // sag there too. The exact answer is the cone to the stop disk at the chief ray's image
        // height, which the defocus caused by the curved object does not change.
        var ts = Designs.Open("IdealLens_CurvedObject.zmx");
        var optic = OptilandOptic.Build(ts);
        double[] fields = { 0, 20, 40, 60 };
        var theirs = OptilandIllumination.Compute(ts, optic, fields);
        var mine = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(fields));

        for (int k = 0; k < fields.Length; k++)
        {
            Assert.True(theirs[k].Ok, theirs[k].Failure);
            double exact = Designs.Disk(200, 10, mine[k].ImageHeight) / Designs.Disk(200, 10, 0);
            _output.WriteLine($"h={fields[k],3}: optiland {theirs[k].RelativeIllumination:F6} exact {exact:F6}");
            Assert.True(Math.Abs(theirs[k].RelativeIllumination - exact) < 1e-4,
                $"height {fields[k]}: {theirs[k].RelativeIllumination:F4} vs exact {exact:F4}");
        }
    }

    [Fact]
    public void CentralObscuration()
    {
        if (!Ready()) return;

        // The chief ray is blocked and the pupil has a hole: the radial edge search this bridge
        // once used could not measure it, and said so. The shared integrator measures it like any
        // other pupil, from Optiland's rays, and must reach the exact outer-disk-minus-inner-disk.
        const double f = 100, a = 10, b = 4;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, double.PositiveInfinity, f, obscuration: b));
        var optic = OptilandOptic.Build(ts, maxField: 30);          // the design defines no fields of its own
        double[] fields = { 0, 15, 30 };
        var theirs = OptilandIllumination.Compute(ts, optic, fields);

        double axial = Designs.Disk(f, a, 0) - Designs.Disk(f, b, 0);
        for (int k = 0; k < fields.Length; k++)
        {
            Assert.True(theirs[k].Ok, theirs[k].Failure);
            double x = f * Math.Tan(fields[k] * Math.PI / 180.0);
            double exact = (Designs.Disk(f, a, x) - Designs.Disk(f, b, x)) / axial;
            _output.WriteLine($"{fields[k],5} deg: optiland {theirs[k].RelativeIllumination:F6} exact {exact:F6}");
            Assert.True(Math.Abs(theirs[k].RelativeIllumination - exact) < 2e-4,
                $"{fields[k]} deg: {theirs[k].RelativeIllumination:F4} vs exact {exact:F4}");
        }

        // Beyond the field the optic was built for there is nothing to trace. Built without a
        // maximum, this lens once traced every field on axis and read 0.934 at 15 degrees.
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Trace(31, new[] { 0.0 }, new[] { 0.0 }));
    }

    [Fact]
    public void WhatTheBridgeCannotBuildIsRefused()
    {
        // Ideal lenses, mirrors and a curved object surface are carried; an even asphere is not,
        // and must be refused rather than quietly built as something else.
        Assert.Null(OptilandOptic.Unsupported(Designs.IdealLensStopAtLens(100, 10, double.PositiveInfinity, 100)));
        Assert.Null(OptilandOptic.Unsupported(Designs.Paraboloid(200, 25, 8)));
        Assert.Null(OptilandOptic.Unsupported(Designs.Open("CookeTriplet with Vignetting.zmx").System));

        Assert.Null(OptilandOptic.Unsupported(
            Designs.IdealLensStopAtLens(100, 10, 200, 200, objectRadius: -300)));

        var aspheric = Designs.IdealLensStopAtLens(100, 10, double.PositiveInfinity, 100);
        aspheric.Surfaces[2].AsphericCoefficients[1] = 1e-8;
        Assert.NotNull(OptilandOptic.Unsupported(aspheric));
    }
}
