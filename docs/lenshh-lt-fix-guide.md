# Fixing relative illumination in LensHH-LT

A work order for the issues in [lenshh-lt-1.0.156.md](lenshh-lt-1.0.156.md), written so the work
can be done on another machine with this repository beside it: every fix has a lens file here and
a number to hit.

**The files to change**

| What | Where |
|---|---|
| The analysis | `LensHH-LT-Engine/Core/Analysis/RelativeIllumination.cs` |
| The `ILL` operand, C# | `LensHH-LT-Engine/Core/MeritFunction/MeritFunctionEvaluator.cs`, `EvaluateRelativeIllumination` / `ComputeEffectiveFNumber` |
| The `ILL` operand, native | `LensHH-LT-NativeCore/src/merit_function.cpp`, the `MeritOpType::ILL` branch |
| Ray-surface intersection | `LensHH-LT-Engine/Core/RayTrace/SurfaceIntersection.cs`, `IntersectConic` |
| Aperture clipping | `LensHH-LT-Engine/Core/RayTrace/RayTracer.cs` |

**The one thing to hold on to.** Illuminance at an image point is the projected solid angle of
the cone arriving there: the area of the transmitted pupil in image-space direction cosines,
measured about the IMAGE SURFACE'S NORMAL at that point, with each ray's direction taken as
seen FROM the image point. Everything below follows from that sentence.

**Checking your work.** Each fix names a lens in `tests/fixtures/lenses/` and the value it must
produce. `ricalc <lens> --at <fields>` prints the exact (backward-traced) value beside the
corrected forward one; `ricalc <lens> --no-rimmer` prints what an uncorrected calculation gives,
which is what LensHH-LT prints today. Where a value is called exact it is a closed-form answer,
not another program's opinion.

---

## 1. Measure about the image-surface normal (Rimmer Eq. 1)

**Now:** `RelativeIllumination.cs` builds the polygon from `FinalRay.L - chiefL` and
`FinalRay.M - chiefM`, which are direction cosines about the z axis.

**Change:** build a frame about the image surface's normal at the chief ray's intercept and use
the components in that frame:

```csharp
// N: unit normal of the image surface at the chief ray's hit, turned toward the incoming light.
// For a surface of curvature c and conic k, at local (x, y, z):  N ~ (-c x, -c y, 1 - c (1+k) z)
var n = ImageSurfaceNormal(chiefHit);
if (Dot(n, chiefDir) < 0) n = -n;

var e1 = Normalize(UnitX - Dot(n, UnitX) * n);   // UnitY if that degenerates
var e2 = Normalize(Cross(n, e1));

// per boundary ray, in place of (dl, dm):
double u = Dot(d, e1);
double v = Dot(d, e2);
```

On a flat image this is the identity, so nothing that works today changes.

**Check:** `IdealLens_CurvedImage_R200.zmx` → 0.8028 at 15°, **0.3558** at 30° (exact).
LensHH-LT 1.0.156 gives 0.5670. `IdealLens_CurvedImage_R-150.zmx` → 0.7852 at 30°.

## 2. Refer each ray to the image point (Rimmer Eq. 3)

**Now:** the boundary ray's own direction is used. An aberrated ray does not pass through the
image point, so that direction describes the cone somewhere else.

**Change:** intersect the ray with the reference sphere - centred on the image point Q', passing
through the axial exit pupil E' - and use the direction from that crossing to Q':

```csharp
// R = |Q' - E'|, E' = (0, 0, exitPupilZ). Skip when the exit pupil is at infinity (telecentric).
var w = hit - q;  double b = Dot(w, d);
double disc = b * b - (Dot(w, w) - R2);
if (disc >= 0) {
    double s = Math.Sqrt(disc);
    var p1 = hit + (-b - s) * d;
    var p2 = hit + (-b + s) * d;
    var p  = Dot(p1 - q, e - q) >= Dot(p2 - q, e - q) ? p1 : p2;   // the one on the pupil side
    var k  = Normalize(q - p);
    if (Dot(k, d) > 0) d = k;
}
```

Rimmer's printed Eq. 3 is the first-order form of this; the exact form above costs nothing extra.

**Check:** `IdealLens_CurvedObject.zmx` → **0.8504** at object height 60 (exact); uncorrected
gives 0.9011. `KingslakeDG.zmx` → 0.9007 at 14° (exact), uncorrected 0.8721.
`Topogon_US2031792_Fig1.zmx` → 0.3516 at 35°, uncorrected 0.3730. On a flat image at focus the
correction changes nothing (1e-17), so those designs keep their present answers.

## 3. Clip apertures where they are, not 0.5 % out

**Now:** `RayTracer.cs` fails a ray only when `r² > SD² * 1.01`, so every clipping aperture acts
as if its radius were `SD * 1.005`.

**Change:** compare against `SD²` (a relative tolerance of 1e-9 if one is wanted for the ray that
defines a solved semi-diameter). Both the fixed-semi-diameter branch and the CLAP/FLAP branch.

**Check:** `CookeTriplet with Vignetting.zmx` → **0.3338** at 20° (exact; OpticStudio's own rays
through the ZPL macro give 0.3331 by the forward method). LensHH-LT 1.0.156 gives 0.3430, and
about 0.007 of that 0.009 excess is this tolerance.

## 4. Take the conic root on the branch through the vertex

**Now:** `IntersectConic` takes `t = e / (g ± √disc)`, the root nearest the vertex plane, with no
check of which sheet the hit is on. A ray leaving a stop that sits near a deep meniscus's centre
of curvature meets the far hemisphere first by that measure, and is then refracted there.

**Change:** compute both roots, keep those satisfying `1 - (1+k) c z >= 0` (the branch through the
vertex), and take the first one ahead of the ray:

```csharp
double q0 = -0.5 * (b + Math.Sign(b) * Math.Sqrt(disc));   // b >= 0 ? +sqrt : -sqrt
double t1 = q0 != 0 ? c / q0 : double.NaN;                 // c = the constant term
double t2 = a != 0 ? q0 / a : double.NaN;
// keep those on the vertex branch; of those, the smallest t >= -1e-9, else the nearest behind
```

**Check:** `Topogon_US2031792_Fig1.zmx` → **0.352** at 35° (this program read 21.8 % before the
same fix). Rimmer 1986 Table 1 gives 34.7 % by ray grid and Kingslake 35.0 %.

## 5. Trace through the real stop, and past ρ = 1

**Now:** the analysis calls `ArbitraryRay.Trace` without ray aiming and searches ρ ∈ [0, 1] of the
PARAXIAL entrance pupil, so an off-axis pupil that grows beyond it is cut off.

**Change:** size the stop once from the real axial marginal ray at the edge of the paraxial pupil,
then keep a ray only if it passes inside that radius and every other clipping aperture, and let
the search run past ρ = 1 until the transmitted region is inside the window. That is ray aiming by
construction and makes the answer independent of the file's ray-aiming setting. (The native `ILL`
path already aims; this brings the analysis and the C# operand into line with it.)

**Check:** the Cooke 40° sample, or any lens with pupil coma: OpticStudio with ray aiming off
reads 0.7794 at 20° where the answer is 0.8760 - an 11 % loss that comes from exactly this.

## 6. Say so when the radial search cannot answer

The outward binary search per azimuth assumes the transmitted pupil is one blob containing the
chief ray. Three cases break it, and today all three fail quietly:

| Case | What happens now | What to do |
|---|---|---|
| Obscuration inside the pupil | the scan steps over the hole and reports the pupil as solid | flag the field, or measure it on a grid |
| Chief ray blocked | `ComputeEffectiveFNumber` returns −1 and the field reads RI = 0 | take the image point from a chief trace that ignores apertures, then flag |
| Pupil clipped from two sides | the outermost edge is kept, the near one lost | flag |

The cheap detection: while scanning outward, if a ray passes AFTER one that failed, this azimuth
leaves the region and re-enters it. Count those azimuths and report them.

**Check:** `IdealLens_ObscurationInFront.zmx`, where the shadow crosses the pupil as the field
grows: exact is 0.9116 at 12° and 0.6472 at 30°. A radial search reads 1.093 of the axial cone at
12° - not a possible relative illumination - and is right at 27° and 30°, where the shadow
overlaps the rim. The permanent fix is a grid with located boundaries (`ricalc` uses an adaptive
quadtree with exact crossings, which also handles the annulus of a Cassegrain).

## 7. Keep the three paths agreeing

The analysis, the C# `ILL` path and the native `ILL` path must give the same number for the same
lens. After the fixes above, check one vignetted lens through all three. Ray aiming is where they
differ today (item 5). `compute_relative_illumination` / `compute_effective_fnum_at_field` in
`relative_illumination.cpp` are exported but called from nowhere in C#, and carry an old clamp at
1.0 and a fixed 24 directions; they should be deleted rather than fixed.

---

## The whole check, in one table

Run after each fix; every value is exact unless noted.

| Lens | Field | Expected | LensHH-LT 1.0.156 |
|---|---|---|---|
| `IdealLens_CurvedImage_R200.zmx` | 30° | 0.3558 | 0.5670 |
| `IdealLens_CurvedObject.zmx` | 60 mm | 0.8504 | ~0.901 (predicted) |
| `KingslakeDG.zmx` | 14° | 0.9007 | ~0.872 (predicted) |
| `Topogon_US2031792_Fig1.zmx` | 35° | 0.3516 (Rimmer 34.7 %, Kingslake 35.0 %) | not run |
| `CookeTriplet with Vignetting.zmx` | 20° | 0.3338 | 0.3430 |
| `IdealLens_ObscurationInFront.zmx` | 30° | 0.6472 | not run |
| `Paraboloid_Mirror.zmx` | 0.5° | 0.9999 | not run |
| `IdealLens_CurvedImage_R200.zmx` | 15° | 0.8028 | 0.8727 |

A lens with a flat image at focus, no obscuration and no pupil growth - a Cooke triplet at 20°,
say - should come out where it is now to within a few tenths of a point. If such a lens moves a
long way, something in the fixes has gone further than intended.

---

# Follow-up after 1.0.157 (2026-09-23)

LensHH-LT 1.0.157 was run on the seven lenses of the table above, 51 fields each, and compared
field by field with `ricalc` (reverse method, exact; the forward method where noted). The large
1.0.156 errors are gone: the image-surface frame (fix 1), the reference-sphere correction (fix 2),
aperture clipping (fix 3) and pupils with a hole at mid-field (fix 6) all check. RI never exceeds 1.

| Lens | Headline | 1.0.157 | Expected | Verdict |
|---|---|---|---|---|
| `IdealLens_CurvedImage_R200.zmx` | 30° | 0.3558 | 0.3558 | pass (1.0.156: 0.5670); all fields within 1.4e-4 |
| `IdealLens_CurvedObject.zmx` | 60 mm | 0.8504 | 0.8504 | pass; all fields within 8e-5 (rounding) |
| `KingslakeDG.zmx` | 14° | 0.9009 | 0.9007 | pass, but see item 10 |
| `CookeTriplet with Vignetting.zmx` | 20° | 0.3333 | 0.3333 fwd, 0.3338 rev | pass; all fields within 5e-5 of the forward method, smooth (1.0.156: 0.3430) |
| `IdealLens_ObscurationInFront.zmx` | 12°, 15°, 30° | 0.9116, 0.8647, 0.6472 | the same | pass there; see item 9 |
| `Topogon_US2031792_Fig1.zmx` | 35° | 0.3517 | 0.3516 | pass there; jagged at mid-field, item 8 |
| `Paraboloid_Mirror.zmx` | 0.5° | 0.9997 | 0.99985 | 1.5e-4 low, item 11 |

Four items remain, the first two of which make the curve visibly uneven.

## 8. Topogon: jagged and low at mid-field

From 12.6° to 26° the Topogon reads low by 0.3 to 3.2e-3, and the error jumps between neighbouring
fields, so the curve zig-zags. The largest second difference over the 51 fields is 3.2e-3, against
7e-4 for `ricalc`. The lens has no vignetting - `ricalc --clip-auto` changes nothing - so the
stop alone bounds a smooth pupil, and `ricalc`'s forward and reverse methods agree to 2e-4 at
every field:

| Field | 1.0.157 | Exact | Difference |
|---|---|---|---|
| 14.0° | 0.8588 | 0.85998 | −1.2e-3 |
| 14.7° | 0.8460 | 0.84651 | −5.1e-4 |
| 15.4° | 0.8312 | 0.83256 | −1.4e-3 |
| 16.1° | 0.8177 | 0.81815 | −4.5e-4 |
| 16.8° | 0.8010 | 0.80330 | −2.3e-3 |
| 19.6° | 0.7368 | 0.74003 | −3.2e-3 |
| 21.0° | 0.7034 | 0.70643 | −3.0e-3 |
| 35.0° | 0.3517 | 0.35155 | +1.5e-4 |

Always low, never high: rays that pass are being counted as blocked, and which ones changes from
field to field. **Suspect fix 4 is incomplete** - the conic root on the branch through the vertex.
This is the lens that exposed that bug in `ricalc`: its deep inner menisci put the stop near their
centres of curvature, and steep mid-field rays meet them where the nearest root lies on the far
hemisphere. Check every conic intersection in the analysis's trace path, including any copy in the
native core, not only `IntersectConic`.

**Check:** trace single rays at 19.6° around the pupil edge and look for rays that fail, or land far
from their neighbours, where `ricalc` passes them. Then the whole curve: within 2e-4 of `ricalc`,
second differences below 1e-3.

## 9. Crescent-shaped pupils: thin slivers missed

`IdealLens_ObscurationInFront.zmx` is exact at 12° and 15°, where the shadow sits inside the pupil,
and at 30°. Between them it reads low where the shadow straddles the pupil's rim and the
transmitted pupil becomes a crescent with thin pointed horns:

| Field | 1.0.157 | Exact | Difference |
|---|---|---|---|
| 16.2° | 0.8418 | 0.84370 | −1.9e-3 |
| 16.8° | 0.8324 | 0.83286 | −4.6e-4 |
| 17.4° | 0.8232 | 0.82383 | −6.3e-4 |
| 18.0° | 0.8152 | 0.81574 | −5.4e-4 |
| 19.2° | 0.8000 | 0.80076 | −7.6e-4 |
| 29.4° | 0.6577 | 0.65798 | −2.8e-4 |

(`ricalc` holds these to four decimals at a grid twice as fine, 64 cells and depth 5.)

A horn narrower than the sampling slips between rays and is not counted, and whether it is caught
changes with field. `ricalc` had the same defect until 2026-09-23 (it stepped RI by 5.5e-4 on the
vignetted Cooke triplet). **If the pupil is measured on a grid:** split every cell that is partly
blocked OR NEXT TO ONE, at every level of refinement, not only cells whose own sample rays
disagree. A sliver is then caught where it enters a cell, and what is still missed shrinks
fourfold per level. See `PupilIntegrator.cs`, `NeighbourCut`. **If it is still a radial search:** a
crescent is exactly the shape a radial search cannot follow, and the grid is the fix.

**Check:** 16.2° → **0.8437**; every field within 2e-4 of `ricalc`.

## 10. Kingslake: one field traced at the wrong angle

`KingslakeDG.zmx` agrees with `ricalc` to 2e-4 at 50 of 51 fields. At the row labelled 7.00° it reads
0.9737 with F/# 8.103, where 7.00° gives 0.97443 and 8.099. Both numbers are what 7.10° gives
(0.97370, F/# 8.102). So that row appears to trace a different field from the one it prints; 7.00°
is not one of the file's defined fields (0, 10, 14), so it is not a defined field point being
treated differently. Look at how the field list is generated or snapped - 7.00 = 25 × 0.28, and the
error is the only one in the run.

**Check:** single fields at 7.00° and 7.10° must differ (0.97443 and 0.97370). If the chief ray's
image height is available, 7.00° is 12.280 mm and 7.10° is 12.458 mm.

## 11. Paraboloidal mirror: fall-off about twice too large

`Paraboloid_Mirror.zmx`, the only folded system in the set, reads a little low, and the error grows
with field:

| Field | 1.0.157 | Exact | Difference |
|---|---|---|---|
| 0.13° | 0.9999 | 0.99999 | −9e-5 |
| 0.30° | 0.9998 | 0.99995 | −1.5e-4 |
| 0.43° | 0.9997 | 0.99989 | −1.9e-4 |
| 0.50° | 0.9997 | 0.99985 | −1.5e-4 |

Negligible in size, but systematic: the fall-off at 0.5° is about 3e-4 where it should be 1.5e-4
(cos⁴ 0.5° = 0.99985). Suspect a sign in the folded geometry after the reflection - the image
surface normal, or the exit pupil position used for the reference sphere (the image is at z = −200,
the exit pupil +200 from it).

**Check:** 0.5° → **0.9999**. A wider field on a mirror would show it more plainly.

## A note on the Effective F/# column

1.0.157 prints one Effective F/#: the F/# of a circular cone with the same projected solid angle,
F/#(axis) / √RI (the vignetted Cooke at 20°: 3.992 / √0.3333 = 6.915). On a nearly round pupil it
equals √(F#T · F#S) of Rimmer's two directional F/numbers - within 0.001 on the curved-image and
curved-object lenses - but on a vignetted pupil the two part company: that Cooke is F/9.4 by
F/4.6 at 20°, and √(F#T · F#S) = 6.57. Both are legitimate. It is worth one line in LensHH-LT's
documentation saying which it prints.
