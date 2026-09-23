# How relative illumination is computed

Relative illumination is computed by Rimmer's method (Proc. SPIE 655, 1986), and the effective
F/# by Siew's definition (Proc. SPIE 5867, 2005). The same method runs in `ricalc`, in the
Optiland cross-check and in the OpticStudio macro; what each paper contributes is in
[references.md](references.md).

## The quantity

For a Lambertian object of uniform radiance *L*, radiance is conserved along every ray through
a lossless system, so the illuminance at an image point is

    E' = L (n'/n)² ∬ dl dm

where (l, m) are the direction cosines of the rays arriving at the point, taken about the
**image-surface normal** there, and the integral runs over the rays that actually get through.
∬ dl dm is the *projected solid angle* of the transmitted cone. Relative illumination is its
ratio to the value at the brightest field measured - the axis, in nearly every lens - so it never
exceeds 1, as OpticStudio reports it. The ratio to the axis itself, which Rimmer and Gardner use
and which exceeds 1 wherever a field is brighter than the axis, is kept too (`RelativeToAxis`,
and the `_to_axis` columns of the CSV).

This one quantity carries everything at once: the cos⁴ law, the obliquity of the pupil, pupil
aberrations (the Slyusarev effect), distortion, vignetting, obscurations, a curved image
surface. It holds at any conjugate and for any object shape, because nothing in it refers to
the object side except which rays pass. Rimmer (1986) states it; Gardner (1947) and Reshidko &
Sasián (2016) build on the same radiometric transfer.

An object that is not Lambertian breaks the last statement: its radiance depends on the angle
between the ray and the object surface's normal, so the integral must be weighted ray by ray
(`--source cos:N`), and the object's shape now enters through its normal.

## Curved objects

OpticStudio's documentation lists, among the assumptions of its Relative Illumination analysis,
that "the object scene is plane, uniform, and Lambertian". For relative illumination, the
**plane** is not needed; **uniform** and **Lambertian** are.

**Why the shape of a uniform Lambertian object does not matter.** A Lambertian surface has the
same radiance in every direction, and a uniform one the same radiance everywhere. Radiance is
conserved along each ray through a lossless system, so every ray arriving at an image point
carries the same radiance, whatever the shape of the surface it left. The illuminance is then
that radiance times the projected solid angle of the cone arriving there, which is set entirely
in image space. The object's shape changes which image point each field maps to, and where the
rays that reach it started, but not the illuminance there.

**What a curved object does change** is focus. A curved object images onto a curved surface, so
on a flat image surface its points are out of focus: the rays from one object point do not meet
at one image point. That breaks a different assumption in OpticStudio's list - that the image
surface is "a reasonably good conjugate" of the object - and it matters to the forward method.
A forward-traced ray's own direction then describes the cone at some other point, and must be
referred to the image point through the reference sphere (Rimmer Eq. 3). The reverse method
starts at the image point and needs no correction.

**Tested** on `tests/fixtures/lenses/IdealLens_CurvedObject.zmx` - an ideal f/5 lens at 200/200
with an object surface of radius −300, whose field points sit at the object's sag - where the
exact answer is the cone from the image point to the stop disk, exactly as for a flat object:

| Object height | Exact | Reverse | Forward, corrected | Forward, uncorrected | OpticStudio's analysis |
|---|---|---|---|---|---|
| 20 | 0.98052 | 0.98052 | 0.98051 | 0.98704 | |
| 40 | 0.92676 | 0.92676 | 0.92674 | 0.95137 | |
| 60 | 0.85042 | 0.85042 | 0.85038 | 0.90109 | 0.85036 |

The macro gave 0.8504 at 60 from OpticStudio's rays, and Optiland's rays 0.8504 by the same
method. OpticStudio's own analysis is right as well - its "plane" requirement is conservative for
this case. The test suite also checks a curved Lambertian object built in code, and holds the
corrected forward method to 1e-4 of exact on both.

**Where the object's shape does matter:**

- A **non-Lambertian** object: each ray's radiance depends on its angle to the object surface's
  normal where it starts, so the surface's shape enters. `ricalc --source cos:N` weights each ray
  by that angle, taken about the curved surface's own normal.
- A **non-uniform** object: if the scene's radiance varies across it, relative illumination no
  longer describes the image's brightness, flat object or curved.

**Not yet tested:** a strongly curved object behind a real, aberrated lens. There is no exact
answer for such a lens, but the forward and reverse methods would have to agree.

## Two independent measurements

**Reverse (exact).** Rays leave the image point on a grid of direction cosines and are traced
backward through the system into object space. The area of the directions that get out is the
projected solid angle. This is what Rimmer calls "the straightforward way" and what Gardner and
Reshidko & Sasián call rigorous; nothing is approximated but the sampling.

**Forward (Rimmer's practical method).** Rays leave the object point and are mapped to their
image-space directions. An aberrated ray does not pass through the image point, so its own
direction describes the cone somewhere else. Each is referred to the image point through the
reference sphere - centred on the image point, through the axial exit pupil - by taking the
direction from where the ray crosses the sphere to the image point. Rimmer's Eq. 3 is the
first-order form of this; it is applied here exactly.

The two agree when one object point images to one image point, and their difference measures
how far that fails. On the Kingslake double Gauss at 14° they agree to 1e-4; with the
correction switched off (`--no-rimmer`) the forward result is 2.9 % low.

## The stop decides what passes

The stop is sized once, from the real on-axis ray through the edge of the paraxial entrance
pupil, so the axial beam is exactly the one the system aperture describes. From then on every
ray is traced through the real stop and kept only if it passes every clipping aperture. The
sampling window in the entrance pupil is not limited to the paraxial pupil and grows until the
transmitted region lies inside it.

This is ray aiming by construction. A calculation that launches off-axis rays into the
paraxial entrance pupil (ρ ≤ 1) cannot see the real pupil grow with field - which is how a
Roossinov-type wide-angle lens reaches cos³ rather than cos⁴ - and reports less light than
there is.

Which apertures clip (see `RayTrace/ApertureModel.cs`):

| Aperture | Clips? |
|---|---|
| stop | always, at the radius above |
| fixed semi-diameter | yes |
| automatic semi-diameter | no (it is solved to pass the file's own fields), unless `--clip-auto` or a clear-aperture % below 100 |
| clear aperture (CLAP), floating aperture (FLAP) | yes, outside the outer radius |
| obscuration, annular inner radius | yes, inside the radius |
| mechanical semi-diameter (MEMA) | no |
| image surface | no |

## Measuring the area

The sampled parameter space (entrance-pupil coordinates forward, direction cosines backward) is
covered by a square grid. A cell whose four corners and centre all pass is taken whole, as four
triangles on the mapped corners and centre. A cell that is partly blocked, or that lies next to
one, is split into four, down to a set depth; at the finest level the boundary is located on each cut edge by bisection, and the
passing part becomes a polygon on those crossings (marching squares with exact crossings).
Every piece's area is taken in the mapped (l, m) space.

Splitting the neighbours too is what keeps the curve smooth. Five rays cannot see a sliver of the
region entering a cell between them - the pointed tip of a vignetted pupil, typically - so a cell
judged alone drops the sliver until the field moves it onto a ray, and RI steps. On the vignetted
Cooke triplet that was a step of 5.5e-4 at 16.7° (reverse) and 3.5e-4 at 18.9° (forward), gone
at a grid twice as fine. Refining beside every cut cell makes what is still missed shrink fourfold
per level: over 201 fields from 0° to 20° the third difference of the curve, away from the two
real kinks (vignetting begins at 1.5°, a further aperture at 7.1°), fell from 1.2e-3 to 7e-6.
It costs about half as many rays again.

The grid is worked one level at a time, and the rays each level needs - then the rays of each
bisection step, across every cut edge at once - are asked for in one batch. The same integrator
therefore serves this program's tracer, Optiland's (one call into Python per batch, a few dozen per
field) and, transcribed level by level, `macros/RELILLUM.ZPL` on OpticStudio's rays. Run through
an interpreter on Optiland's rays, the macro reproduces the C# integrator to 1e-10.

This replaces the common alternative - one binary search outward from the pupil centre per
azimuth, then the area of the polygon through the edge points - which assumes the region is
star-shaped about a centre that passes. A central obscuration, a vignetted chief ray, or a pupil
clipped from two sides breaks that assumption silently.

Because each piece is measured in mapped space, rays need not be spaced evenly in exit direction
cosines, so Rimmer's magnification matrix (his Eq. 4) is not needed.

## Siew's breakdown

Alongside the forward method, the report gives Siew's small-aperture reading (2017, Eq. 12):

    RI ≈ [S(y)/S(0)] cos⁴θ / {(1+D)[(1+D) + y dD/dy]}

with S the transmitted beam area in the entrance pupil, θ the real chief ray's object-space
angle, D the distortion and y dD/dy the "differential distortion". It says where the fall-off
comes from (pupil size, obliquity, image stretching). It is a diagnostic, not the result.

## Effective F/number

Two kinds are reported, answering different questions.

**Siew's effective F/# (F/#eff)** - how bright. Siew (2005, Eq. 11) defines it as the F/# of a
perfect system with a circular exit pupil that gives the same illuminance:

    F/#eff = sqrt( pi / (4 n'² PSA) )

with PSA the projected solid angle measured above and n' the image-space index. On axis, for a
circular cone of half-angle θ, PSA = π sin²θ and this is the working F/# 1/(2 n' sin θ); for any
other pupil - vignetted, obscured, elongated - it is the circular pupil of equal brightness. Off
axis it is the axial value divided by √RI. It is the Effective F/# OpticStudio reports, except
that OpticStudio folds transmission into the PSA (strictly a T/#); transmission is not modelled
here yet. It is taken from the reverse trace when that runs.

**Rimmer's directional F/numbers (F#T, F#S)** - what shape. 1/(m₂ − m₁) meridionally and
1/(l₂ − l₁) sagittally: the extent of the transmitted cone in direction cosines, from the reverse
trace. On a round pupil both equal F/#eff; on a vignetted one they part company - the vignetted
Cooke triplet at 20° is F/9.4 by F/4.6, with F/#eff 6.9.

## Not modelled yet

- Transmission: coatings, bulk absorption, polarization. Gardner notes the losses depend on
  angle and belong inside the integral; the integrand already carries a per-ray weight for this.
- Several wavelengths combined.
- Decentred or tilted systems (coordinate breaks are refused).
- Afocal systems.
- Reshidko & Sasián's aberration-coefficient decomposition.
