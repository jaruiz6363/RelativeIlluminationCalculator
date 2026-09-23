using System;
using System.Linq;

using RelativeIllumination.Core.Illumination;
using RelativeIllumination.Core.RayTrace;
using Xunit;

namespace RelativeIllumination.Tests;

/// <summary>
/// Relative illumination against closed-form answers.
///
/// <para>An aberration-free lens sends every ray of a field through one image point, so the
/// transmitted cone there is simply the cone from that point to the aperture that limits the
/// beam. When that aperture is a flat disk, the projected solid angle of the cone is the
/// irradiance of a uniformly bright disk - Foote's formula, which Gardner uses to show that
/// even a perfect lens departs from cos⁴ at finite aperture. Both methods must reproduce it.</para>
/// </summary>
public class ExactTests
{
    private const double Tol = 2e-4;

    private static void AssertBoth(FieldIllumination r, double expected, string what)
    {
        Assert.True(r.Ok, r.Failure);
        Assert.True(Math.Abs(r.Reverse!.RelativeIllumination - expected) < Tol,
            $"{what}: reverse {r.Reverse.RelativeIllumination:F6} vs exact {expected:F6}");
        Assert.True(Math.Abs(r.Forward!.RelativeIllumination - expected) < Tol,
            $"{what}: forward {r.Forward.RelativeIllumination:F6} vs exact {expected:F6}");
    }

    [Fact]
    public void StopAtIdealLens_ObjectAtInfinity_IsTheDiskFormula()
    {
        // f/5: fast enough that the departure from cos⁴ is visible (Gardner's Table 1).
        const double f = 100, a = 10;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, double.PositiveInfinity, f));
        var fields = new[] { 0.0, 10, 20, 30, 40 };
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(fields), Designs.Fine());

        foreach (var r in results)
        {
            double x = f * Math.Tan(r.FieldY * Math.PI / 180.0);
            double exact = Designs.Disk(f, a, x) / Designs.Disk(f, a, 0);
            AssertBoth(r, exact, $"{r.FieldY} deg");
        }

        // ...and that is NOT cos⁴: at 40 deg the exact value exceeds it, as Gardner tabulates.
        var edge = results.Last();
        double cos4 = Math.Pow(Math.Cos(40 * Math.PI / 180.0), 4);
        Assert.True(edge.Reverse!.RelativeIllumination > cos4 * 1.005);
    }

    [Fact]
    public void EffectiveFNumber_IsSiewsCircularPupilEquivalent()
    {
        // Siew (Proc. SPIE 5867, 2005, Eq. 11): f/# = sqrt(pi / (4 PSA)) in air. On axis the cone
        // to the stop disk has PSA = pi sin^2(theta), so f/# = 1 / (2 sin theta) = sqrt(a^2 + f^2) / 2a -
        // 5.025 for this f/5 lens, not the paraxial 5.000. Off axis it is the axial value over
        // sqrt(RI), since both come from the same solid angle.
        const double f = 100, a = 10;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, double.PositiveInfinity, f));
        var r = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 30), Designs.Fine());

        double axial = Math.Sqrt(a * a + f * f) / (2 * a);
        foreach (var m in new[] { r[0].Reverse!, r[0].Forward! })
            Assert.True(Math.Abs(m.EffectiveFNumber / axial - 1) < 1e-5, $"axial f/# {m.EffectiveFNumber} vs {axial}");

        double offAxis = r[0].Reverse!.EffectiveFNumber / Math.Sqrt(r[1].Reverse!.RelativeToAxis);
        Assert.True(Math.Abs(r[1].Reverse!.EffectiveFNumber / offAxis - 1) < 1e-9);
    }

    [Fact]
    public void StopAtIdealLens_FiniteConjugate_IsTheDiskFormula()
    {
        // Unit magnification: object 200 before an f = 100 lens, image 200 behind it.
        const double f = 100, a = 10, d = 200;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, d, d));
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 20, 50, 100, 150), Designs.Fine());

        foreach (var r in results)
        {
            Assert.True(Math.Abs(r.ImageHeight - r.FieldY) < 1e-9, "unit magnification");
            double exact = Designs.Disk(d, a, r.ImageHeight) / Designs.Disk(d, a, 0);
            AssertBoth(r, exact, $"height {r.FieldY}");
        }
    }

    [Fact]
    public void StopBehindIdealLens_IsTheDiskFormulaAboutTheStop()
    {
        // The entrance pupil is a virtual image of the stop, so the beam is found by aiming at
        // the real stop rather than at the paraxial pupil. The cone at the image point is the cone
        // to the stop: a disk at distance f - d.
        const double f = 100, d = 40, a = 5;
        var ts = Designs.InAir(Designs.IdealLensStopBehind(f, d, a));
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 10, 20, 30), Designs.Fine());

        foreach (var r in results)
        {
            double x = f * Math.Tan(r.FieldY * Math.PI / 180.0);
            double exact = Designs.Disk(f - d, a, x) / Designs.Disk(f - d, a, 0);
            AssertBoth(r, exact, $"{r.FieldY} deg");
        }
    }

    [Fact]
    public void CentralObscuration_RemovesTheInnerDisk()
    {
        // The pupil has a hole in it. A radial search outward from the pupil centre cannot see
        // one: its first ray is blocked. The transmitted cone is the outer disk minus the inner.
        const double f = 100, a = 10, b = 4;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, double.PositiveInfinity, f, obscuration: b));
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 15, 30), Designs.Fine());

        double axial = Designs.Disk(f, a, 0) - Designs.Disk(f, b, 0);
        foreach (var r in results)
        {
            double x = f * Math.Tan(r.FieldY * Math.PI / 180.0);
            double exact = (Designs.Disk(f, a, x) - Designs.Disk(f, b, x)) / axial;
            AssertBoth(r, exact, $"{r.FieldY} deg");
        }

        // The absolute solid angle too: pi L/2 times the disk expression is the irradiance, and
        // the projected solid angle is that divided by L.
        double expectedAxial = 0.5 * Math.PI * axial;
        Assert.True(Math.Abs(results[0].Reverse!.ProjectedSolidAngle / expectedAxial - 1) < 1e-4);
    }

    [Fact]
    public void Paraboloid_AxialConeIsTheMirrorRimLessTheObscuration()
    {
        // A folded system: after the mirror the ray runs back along -z. On axis a paraboloid is
        // perfect, so the cone from the focus is bounded by the rim and the obscuration edge, each
        // sitting at its own sag.
        const double f = 200, a = 25, b = 8;
        var ts = Designs.InAir(Designs.Paraboloid(f, a, b));
        var r = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 0.5), Designs.Fine());

        double SinSq(double h)
        {
            double sag = -h * h / (4.0 * f);           // toward -z: the mirror is concave to +z light
            double dz = sag - (-f);
            return h * h / (h * h + dz * dz);
        }
        double expected = Math.PI * (SinSq(a) - SinSq(b));
        Assert.True(Math.Abs(r[0].Reverse!.ProjectedSolidAngle / expected - 1) < 1e-4,
            $"reverse {r[0].Reverse!.ProjectedSolidAngle} vs {expected}");
        Assert.True(Math.Abs(r[0].Forward!.ProjectedSolidAngle / expected - 1) < 1e-4,
            $"forward {r[0].Forward!.ProjectedSolidAngle} vs {expected}");
        Assert.True(Math.Abs(r[1].Forward!.RelativeIllumination - r[1].Reverse!.RelativeIllumination) < 1e-3);
    }

    [Fact]
    public void CurvedLambertianObject_OnlyTheImageSpaceConeMatters()
    {
        // A curved object images to a curved surface, so on the flat image plane the object points
        // are out of focus. For a Lambertian object that does not matter: radiance is conserved
        // along each ray, and the illuminance at an image point is set by the cone of directions
        // reaching it - here the cone to the lens disk, exactly as for a flat object.
        const double f = 100, a = 10, d = 200;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, d, d, objectRadius: -300));
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 30, 60), Designs.Fine());

        foreach (var r in results)
        {
            Assert.True(r.Ok, r.Failure);
            double exact = Designs.Disk(d, a, r.ImageHeight) / Designs.Disk(d, a, 0);
            Assert.True(Math.Abs(r.Reverse!.RelativeIllumination - exact) < Tol,
                $"reverse {r.Reverse.RelativeIllumination:F6} vs exact {exact:F6}");
            // Forward rays from a defocused object point do not meet at the image point; the
            // reference-sphere correction, taken exactly, refers them to it. Without it they read
            // up to 5 points high here.
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - exact) < 1e-4,
                $"forward {r.Forward.RelativeIllumination:F6} vs exact {exact:F6}");
        }
    }

    [Theory]
    [InlineData(-150.0)]      // concave toward the lens, like a Petzval-matched curved sensor
    [InlineData(200.0)]       // convex toward the lens
    public void CurvedImage_IsMeasuredAboutTheSurfaceNormal(double imageRadius)
    {
        // Illuminance ON a curved image surface: the projected solid angle is taken about the
        // surface's own normal at the image point (Rimmer Eq. 1), not about the axis. Traced
        // backward from the image point, nothing but the stop disk on the lens can block a ray,
        // so the transmitted cone is exactly the cone from the image point to that disk, and its
        // projected solid angle about any normal can be integrated over the disk directly.
        const double f = 100, a = 10;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, double.PositiveInfinity, f,
                                                           imageRadius: imageRadius));
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 15, 30), Designs.Fine());

        double c = 1.0 / imageRadius;
        double axial = DiskCone(results[0].ImagePoint, Normal(results[0].ImagePoint));
        foreach (var r in results)
        {
            Assert.True(r.Ok, r.Failure);
            var q = r.ImagePoint;
            double exact = DiskCone(q, Normal(q));

            Assert.True(Math.Abs(r.Reverse!.ProjectedSolidAngle / exact - 1) < 1e-4,
                $"{r.FieldY} deg: reverse {r.Reverse.ProjectedSolidAngle:E6} vs exact {exact:E6}");
            Assert.True(Math.Abs(r.Reverse.RelativeIllumination - exact / axial) < Tol,
                $"{r.FieldY} deg: reverse RI {r.Reverse.RelativeIllumination:F6} vs {exact / axial:F6}");

            // Forward rays converge on the focal PLANE, so on a curved surface they are out of
            // focus; the reference-sphere correction carries them to the image point to first order.
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - exact / axial) < 5e-3,
                $"{r.FieldY} deg: forward RI {r.Forward.RelativeIllumination:F6} vs {exact / axial:F6}");

            // And the normal matters: measured about the axis instead, the answer is different.
            if (r.FieldY > 20)
                Assert.True(Math.Abs(DiskCone(q, Vec3.UnitZ) / exact - 1) > 1e-2,
                    "the test cannot tell the surface normal from the axis");
        }

        // Unit normal of the image surface (a sphere, vertex at z = f) at a global point on it.
        Vec3 Normal(Vec3 p)
        {
            double z = p.Z - f;
            return new Vec3(-c * p.X, -c * p.Y, 1.0 - c * z).Normalized();
        }
    }

    /// <summary>
    /// Projected solid angle, about the unit normal <paramref name="n"/>, of the cone from point
    /// <paramref name="q"/> to the disk of radius 10 in the plane z = 0 centred on the axis:
    /// the integral over the disk of cos(at disk) cos(at q) / r².
    /// </summary>
    private static double DiskCone(Vec3 q, Vec3 n)
    {
        const double a = 10;
        const int nr = 600, nt = 600;
        double sum = 0;
        for (int i = 0; i < nr; i++)
        {
            double rr = a * (i + 0.5) / nr;
            for (int j = 0; j < nt; j++)
            {
                double t = 2 * Math.PI * (j + 0.5) / nt;
                var d = q - new Vec3(rr * Math.Cos(t), rr * Math.Sin(t), 0.0);
                double r2 = d.Dot(d), r = Math.Sqrt(r2);
                double cosDisk = d.Z / r;
                double cosImage = n.Dot(d) / r;
                sum += cosDisk * cosImage / r2 * rr;
            }
        }
        return sum * (a / nr) * (2 * Math.PI / nt);
    }

    [Fact]
    public void CurvedObjectFromAFile_IsTheDiskFormulaAtTheImagePoint()
    {
        // The same statement as CurvedLambertianObject, but read from a .zmx: an ideal f/5 lens
        // at 200/200 with an object surface of radius -300. Field points sit at the object's
        // sag, so their images are out of focus on the flat image plane - and for a Lambertian
        // object that changes nothing, because the illuminance at an image point is the cone of
        // directions reaching it, here the cone to the stop disk.
        var ts = Designs.Open("IdealLens_CurvedObject.zmx");
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 20, 40, 60), Designs.Fine());

        double axial = Designs.Disk(200, 10, 0);
        foreach (var r in results)
        {
            Assert.True(r.Ok, r.Failure);
            double exact = Designs.Disk(200, 10, r.ImageHeight) / axial;
            Assert.True(Math.Abs(r.Reverse!.RelativeIllumination - exact) < Tol,
                $"height {r.FieldY}: reverse {r.Reverse.RelativeIllumination:F6} vs exact {exact:F6}");
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - exact) < 1e-4,
                $"height {r.FieldY}: forward {r.Forward.RelativeIllumination:F6} vs exact {exact:F6}");
        }

        // The sag is what puts the image points where they are: at object height 60 the chief
        // ray lands at 58.2351, not at 60.
        Assert.True(Math.Abs(results[3].ImageHeight - 58.2351) < 1e-3, $"{results[3].ImageHeight}");
    }

    [Fact]
    public void ObscurationInFront_ShadowMovesAndTheChiefRayIsBlocked()
    {
        // An obscuration of radius 4, 20 mm in front of an ideal f/5 lens with the stop on it:
        // its shadow on the lens moves off axis by 20 tan(field), so the chief ray is blocked up
        // to about 11.3 deg and passes beyond, and the relative illumination at the edge of the
        // field is RAISED as the shadow leaves the pupil. Read from the .zmx, so the OBSC keyword
        // is exercised too. Exact: the cone from the image point to the lens disk minus the shadow.
        const double f = 100, a = 10, b = 4, gap = 20;
        var ts = Designs.Open("IdealLens_ObscurationInFront.zmx");
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 6, 15, 30), Designs.Fine());

        double Cone(double deg)
        {
            double th = deg * Math.PI / 180, qy = f * Math.Tan(th), sy = gap * Math.Tan(th), sum = 0;
            const int nr = 800, nt = 800;
            for (int i = 0; i < nr; i++)
            {
                double rr = a * (i + 0.5) / nr;
                for (int j = 0; j < nt; j++)
                {
                    double t = 2 * Math.PI * (j + 0.5) / nt;
                    double u = rr * Math.Cos(t), v = rr * Math.Sin(t);
                    if (u * u + (v - sy) * (v - sy) < b * b) continue;
                    double dy = qy - v, r2 = u * u + dy * dy + f * f;
                    sum += f * f / (r2 * r2) * rr;
                }
            }
            return sum;
        }

        double axial = Cone(0);
        foreach (var r in results)
        {
            Assert.True(r.Ok, r.Failure);
            double exact = Cone(r.FieldY) / axial;
            Assert.True(Math.Abs(r.Reverse!.RelativeIllumination - exact) < 5e-4,
                $"{r.FieldY} deg: reverse {r.Reverse.RelativeIllumination:F5} vs exact {exact:F5}");
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - exact) < 2e-3,
                $"{r.FieldY} deg: forward {r.Forward.RelativeIllumination:F5} vs exact {exact:F5}");
        }

        // The moving shadow matters: ignoring the obscuration would read 0.567 at 30 deg, not 0.647.
        Assert.True(results.Last().Reverse!.RelativeIllumination > 0.64);
    }

    [Fact]
    public void CosinePowerSource_IsWeightedRayByRay()
    {
        // Radiance cos^2 of the angle from the object normal. For the ideal lens each image-space
        // direction from the image point corresponds to one ray from the object point through one
        // point of the lens disk, so the weighted cone can be integrated over the disk directly.
        const double f = 100, a = 10, d = 200, n = 2;
        var ts = Designs.InAir(Designs.IdealLensStopAtLens(f, a, d, d));
        var opt = Designs.Fine();
        opt.Source = new CosinePowerRadiance(n);
        opt.Forward = true;
        var results = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 60, 120), opt);

        double Weighted(double h)
        {
            // Object point at (0, h, -d), image point at (0, -h, +d); lens disk at z = 0.
            double sum = 0;
            const int nr = 400, nt = 400;
            for (int i = 0; i < nr; i++)
            {
                double rr = a * (i + 0.5) / nr;
                for (int j = 0; j < nt; j++)
                {
                    double t = 2 * Math.PI * (j + 0.5) / nt;
                    double x = rr * Math.Cos(t), y = rr * Math.Sin(t);
                    double dq = Math.Sqrt(x * x + (y + h) * (y + h) + d * d);      // to the image point
                    double dp = Math.Sqrt(x * x + (y - h) * (y - h) + d * d);      // to the object point
                    double proj = d * d / (dq * dq * dq * dq);                     // cos·cos/r² at both ends
                    sum += proj * Math.Pow(d / dp, n) * rr;
                }
            }
            return sum;
        }

        double axial = Weighted(0);
        foreach (var r in results)
        {
            double exact = Weighted(r.FieldY) / axial;
            Assert.True(Math.Abs(r.Reverse!.RelativeIllumination - exact) < 5e-4,
                $"reverse {r.Reverse.RelativeIllumination:F6} vs {exact:F6}");
            Assert.True(Math.Abs(r.Forward!.RelativeIllumination - exact) < 5e-4,
                $"forward {r.Forward.RelativeIllumination:F6} vs {exact:F6}");
        }

        // A source that dims with angle must give less than the Lambertian one off axis.
        var lambert = RelativeIlluminationCalculator.Compute(ts, Designs.Fields(0, 120), Designs.Fine());
        Assert.True(results.Last().Reverse!.RelativeIllumination < lambert.Last().Reverse!.RelativeIllumination);
    }
}
