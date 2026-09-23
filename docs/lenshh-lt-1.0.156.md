# Relative illumination in LensHH-LT 1.0.156: issues found

Found on 2026-09-22 while validating this repository against exact answers, OpticStudio and
LensHH-LT. The author of LensHH-LT has been told and **will correct them in a later release**.
Nothing in LensHH-LT was modified here; this is a report.

The code concerned is `LensHH-LT-Engine/Core/Analysis/RelativeIllumination.cs` (the analysis);
the `ILL` merit operand in `LensHH-LT-Engine/Core/MeritFunction/MeritFunctionEvaluator.cs` (C#)
and `LensHH-LT-NativeCore/src/merit_function.cpp` (native); and the ray intersection in
`LensHH-LT-Engine/Core/RayTrace/SurfaceIntersection.cs`.

How each item was established is stated with it: **measured** (LensHH-LT itself was run),
**from the code**, or **reproduced** (the same computation run in `ricalc`).

## Summary

| # | Issue | Effect | Established |
|---|---|---|---|
| 1 | Direction cosines taken about the optical axis, not the image-surface normal | Wrong on any curved image: 59 % high at 30° on the test lens | measured |
| 2 | Raw ray directions, no reference-sphere correction (Rimmer Eq. 3) | 0.5 to 3 points on ordinary lenses; image curvature drops out entirely | measured (curved image), reproduced (flat) |
| 3 | Pupil search confined to the paraxial entrance pupil, no ray aiming in the analysis | Off-axis pupil growth missed; 11 % low at 20° on a Cooke triplet in the equivalent OpticStudio case | from the code, reproduced |
| 4 | Conic intersection picks the root nearest the vertex plane, with no branch check | Wrong hemisphere hit silently near a deep meniscus's centre of curvature | from the code |
| 5 | Radial edge search from the chief ray | Obscured pupils mismeasured; a blocked chief ray gives RI = 0 | from the code |
| 6 | Analysis never aims rays; the native `ILL` operand does when the system has aiming on | The two can differ on the same lens | from the code |
| 7 | Apertures clip at r² > SD² × 1.01, i.e. 0.5 % larger than stated | Vignetted fields too bright: +0.7 points of the +0.9 error at 20° on a vignetted Cooke triplet | measured, reproduced |

## 1. Curved image surface: measured about the axis (measured)

The polygon area is formed from `FinalRay.L - chiefL` and `FinalRay.M - chiefM`, the direction
cosines about z. Illuminance on a curved image is the projected solid angle about the surface's
own normal at the image point (Rimmer 1986, Eq. 1). Nothing rotates into that frame.

Test lens `tests/fixtures/lenses/IdealLens_CurvedImage_R200.zmx`: an ideal f = 100 lens, stop on
the lens, f/5, image surface of radius +200 at the focus. Traced back from the image point, only
the stop disk limits the cone, so the exact answer is that cone integrated about the normal.

| Field | Exact | ricalc | OpticStudio | **LensHH-LT 1.0.156** |
|---|---|---|---|---|
| 15° | 0.8028 | 0.8028 | 0.8024 | **0.8727** |
| 30° | 0.3558 | 0.3558 | 0.3572 | **0.5670** |

LensHH-LT's values are, to every printed digit, the cone at the point on the **flat focal plane**
- 0.8727 and 0.5670 are Foote's disk formula there - which is what issues 1 and 2 together
predict: the image surface's shape drops out. The concave R −150 lens
(`IdealLens_CurvedImage_R-150.zmx`, exact 0.7852 at 30°) is therefore expected to give 0.5670
too, 28 % low.

The ZPL macro in this repository separates the two faults on this lens, because it can measure
about the surface normal with either set of directions:

| At 30° | Frame | Directions | Value |
|---|---|---|---|
| Exact, `ricalc`, macro corrected | image-surface normal | corrected | 0.3558 |
| Macro raw | image-surface normal | raw | 0.4339 |
| LensHH-LT 1.0.156 | optical axis | raw | 0.5670 |

So issue 2 costs 0.078 here and issue 1 a further 0.133.

**Fix:** form (l, m) in a frame about the image-surface normal at the chief ray's hit:
e₁ = normalise(x − (x·N)N), e₂ = N × e₁, and use d·e₁, d·e₂. About ten lines.

## 2. No reference-sphere correction (measured, reproduced)

Rays traced forward from the object point do not all pass through the image point; their own
directions describe the cone somewhere else. Rimmer's Eq. 3 refers each to the image point
through the reference sphere centred there and passing through the axial exit pupil. LensHH-LT
uses the raw directions.

On the curved image above this is half of the error (measured). On flat images, the same
uncorrected computation run in `ricalc --no-rimmer` gives:

| Lens | Field | Exact | Uncorrected |
|---|---|---|---|
| Kingslake double Gauss, f/8 | 14° | 0.9007 | 0.8721 (−2.9 points) |
| Cooke triplet, f/5 | 20° | 0.8760 | 0.8711 (−0.5) |
| Topogon US 2,031,792, f/6.3 | 35° | 0.3516 | 0.3730 (+2.1) |

The Topogon figure repeats Rimmer's own Table 1, where his uncorrected calculation reads 36.7 %
against 34.7 %.

**Fix:** for each boundary ray, intersect it with the reference sphere and take the direction
from that point to the image point - or trace backward from the image point on a grid of
direction cosines, which needs no correction (OpticStudio does the latter).

## 3. Pupil confined to the paraxial entrance pupil (from the code, reproduced)

`RelativeIlluminationCalculator.Compute` calls `ArbitraryRay.Trace` without ray aiming, and the
boundary search runs over ρ ∈ [0, 1] of the paraxial entrance pupil. An off-axis pupil that grows
beyond it - pupil coma, the effect that lets wide-angle lenses beat cos⁴ - is cut off. OpticStudio
with ray aiming off has exactly this limit, and on its Cooke 40° sample it reads 0.7794 at 20°
against 0.8760 (11 % low); the ZPL macro in this repository, confined the same way, reproduces
that to 1e-4. The native `ILL` operand does pass ray aiming.

**Fix:** trace every ray through the real stop and let the search extend past ρ = 1, or always
aim at the stop.

## 4. Wrong-hemisphere conic intersection (from the code)

`SurfaceIntersection.IntersectConic` takes t = e / (g ± √disc), the root nearest the vertex plane,
and does not check that the hit lies on the branch of the conic through the vertex. For a ray
that starts near the centre of curvature - one leaving a stop that sits inside a deep meniscus -
that root is on the far hemisphere. The same choice in `ricalc` sent the Topogon's rays to the
far side of its inner menisci and read 21.8 % where the answer is 35.2 %; there the bad hits were
rejected as misses, whereas LensHH-LT accepts the point and refracts there.

**Fix:** compute both roots, keep those with 1 − (1+k) c z ≥ 0, and take the first ahead of the
ray. **Check:** `tests/fixtures/lenses/Topogon_US2031792_Fig1.zmx`, expected RI 0.352 at 35°.

## 5. Radial edge search (from the code)

One binary search outward per azimuth assumes the pupil is star-shaped about a chief ray that
passes. A central obscuration is measured as a full disk; a pupil clipped from two sides can be
crossed twice; a vignetted chief ray makes `ComputeEffectiveFNumber` return −1 and the field
reads RI = 0.

## 6. Ray aiming differs between the analysis and the ILL operand (from the code)

| | Analysis (C#) | `ILL`, C# path | `ILL`, native path |
|---|---|---|---|
| Ray aiming | never | never | when the system has it on |
| Pupil directions | `numPupilRays`, default 36 | Arms column, default 36 | Arms column, default 36 |
| Normalisation | to the brightest field | to the axis | to the axis |

Only the ray-aiming row is an issue. On a file with aiming on, the analysis traces the paraxial
pupil (issue 3) while the native operand aims at the stop, so an optimisation targeting `ILL` and
the analysis plot of the same design can disagree. The normalisation difference is deliberate and
matters only for a lens brighter off axis than on it.

*Correction to the first version of this report,* which said the native operand clamps at 1.0
and always uses 24 directions. That describes `compute_relative_illumination` /
`compute_effective_fnum_at_field` in `relative_illumination.cpp`, which are exported
(`lenshh_compute_relative_illumination`) but not called from anywhere in the C# code - the live
`ILL` operand is evaluated in `merit_function.cpp` and honours the Arms column. That old code
could be removed.

## 7. Apertures clip 0.5 % large (measured, reproduced)

`RayTracer.Trace` fails a ray at a fixed semi-diameter, a CLAP or a FLAP only when
r² > SD² × 1.01, so every clipping aperture acts as if its radius were SD × 1.005. Where
vignetting sets the off-axis beam, that lets through light the lens does not.

Test lens `tests/fixtures/lenses/CookeTriplet with Vignetting.zmx`: f/4, fixed semi-diameters on
S1, S3, S5 and S6 that vignet from about 1.6° outward. Every column but LensHH-LT's comes from a
calculation that clips at the stated radius:

| Field | ricalc reverse (exact) | ricalc forward | RELILLUM on OpticStudio's rays | OpticStudio analysis | **LensHH-LT 1.0.156** |
|---|---|---|---|---|---|
| 4° | 0.9572 | 0.9575 | 0.9575 | 0.9721 | **0.9605** |
| 8° | 0.8612 | 0.8611 | 0.8611 | 0.8670 | **0.8628** |
| 12° | 0.7053 | 0.7048 | 0.7047 | 0.7190 | **0.7066** |
| 16° | 0.5233 | 0.5227 | 0.5227 | 0.5237 | **0.5269** |
| 20° | 0.3338 | 0.3333 | 0.3331 | 0.3441 | **0.3430** |

(The OpticStudio analysis counts rays on a grid, and on a vignetted pupil that is jagged by up
to 1.5 points from field to field; its errors change sign. LensHH-LT's are all of one sign.)

Applying LensHH-LT's rules in `ricalc`'s tracer - the 1.01 slack, raw ray directions, rays
confined to the paraxial entrance pupil, the stop clipped at its fixed semi-diameter -
reproduces LensHH-LT's curve to within 0.0015 at every field (0.3431 at 20°). The slack alone
accounts for +0.0068 of the +0.0092 at 20°; issues 2 and 3 for the rest. (That the stop is
clipped at its fixed semi-diameter is my reading of the code, not confirmed; the slack is.)

**Fix:** clip at SD exactly. If the slack exists to keep the ray that DEFINES a semi-diameter
from failing on round-off, a relative tolerance of 1e-9 does that without changing the aperture.

## Reproducing

All the lenses are in `tests/fixtures/lenses`; `ricalc <file>` prints the exact (reverse) and
corrected forward values, and `ricalc <file> --no-rimmer` the uncorrected ones. The exact curved-
image values come from `ExactTests.CurvedImage_IsMeasuredAboutTheSurfaceNormal`.
