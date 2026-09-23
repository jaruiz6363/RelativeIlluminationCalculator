# Optiland 0.6.2: two issues found while cross-checking

Found on 2026-09-22 while adding an Optiland cross-check to this program (`ricalc --optiland`).
Both are in the Zemax `.zmx` import; Optiland's ray trace itself agrees with this program's to
6e-6 (see below). Nothing in Optiland was modified. Worth reporting upstream to
[HarrisonKramer/optiland](https://github.com/HarrisonKramer/optiland).

Reproduced with optiland 0.6.2 (its `__version__` string says 0.6.1), numpy 2.5.3, Python 3.12.8.

## 1. Glass names are resolved without the catalog the file names

`tests/fixtures/lenses/CookeTriplet with Vignetting.zmx` declares

    GCAT SCHOTT
    ...
      GLAS SK16 0 0 0 0 0
      GLAS F4 0 0 0 0 0

These are Schott's legacy SK16 and F4. Optiland resolves them out of its
`database/data-nk/glass` tree by name alone:

| Glass | File Optiland picks | n at 0.58756 µm | Schott's value | Error |
|---|---|---|---|---|
| SK16 | `hikari/SK16.yml` | 1.6204090 | 1.6204101 | 1e-6, harmless |
| F4 | `cdgm/F4.yml` | **1.6200475** | **1.6165920** | 0.35 % |

CDGM's F4 is a different glass that happens to share the name; Schott's F4 is not in the
database at all (`schott/` has only the N-glasses). The flint therefore arrives with almost the
crown's index, and the lens is not the lens the file describes:

| | EFL |
|---|---|
| This program, and OpticStudio | 50.0005 mm |
| Optiland's `load_zemax_file` | 50.4784 mm |

The same happens to `KingslakeDG.zmx` (SK4 and F4): EFL 101.31 mm against 100.0039 mm.

**Suggested fix:** honour the `GCAT` line when resolving a glass name, and say so when a name
can only be found in a different maker's catalog rather than silently taking it.

## 2. Surface apertures are dropped on import

The same file carries fixed semi-diameters that vignette the beam from about 1.6° outward:

    SURF 1 ... DIAM 6.5 1 0 0 1 ""
    SURF 3 ... DIAM 5   1 0 0 1 ""

After `load_zemax_file`, every surface has `aperture = None`, and a ray at full field and full
pupil - one this program and OpticStudio both block - traces through. Any analysis that depends
on vignetting is then computed on an unvignetted lens.

**Suggested fix:** import a user-defined `DIAM` (the flag field = 1) as a `RadialAperture`, and
`OBSC`/`CLAP` likewise.

## What is NOT wrong: the ray trace

Handing Optiland the prescription directly - radii, thicknesses, conics, indices at one
wavelength, the stop, and the clipping apertures as `RadialAperture`s, which is what
`src/RelativeIllumination.Optiland/python/ricalc_optiland.py` does - gives EFL 50.00054 mm,
matching this program and OpticStudio. Relative illumination measured from Optiland's rays by
this program's own pupil integrator then agrees with this program's own tracer:

| Field | ricalc forward | Optiland's rays, same integrator | Difference |
|---|---|---|---|
| 8° | 0.861055 | 0.861056 | 1.8e-6 |
| 16° | 0.522746 | 0.522751 | 4.4e-6 |
| 20° | 0.333281 | 0.333287 | 6.1e-6 |

(Until 2026-09-23 the Optiland rays were measured by a radial edge search instead, and agreed to
2e-4. The remaining difference is the parameter space: Optiland's normalised stop against this
program's entrance pupil, so the two grids fall differently.)

That is a third independent tracer - after this program's own and OpticStudio's, through
`macros/RELILLUM.ZPL` - agreeing on a vignetted lens.

The bridge also carries **ideal lenses** (Optiland's `surface_type="paraxial"` with `f`) and
**mirrors** (`material="mirror"`, thicknesses negative after the reflection, image at z = −200 on
the test paraboloid, the same convention this program uses). Both agree:

| Lens | Field | Exact or ricalc | Optiland's rays |
|---|---|---|---|
| Ideal f/5 lens, convex R +200 image | 15° | 0.8028 exact | 0.8027 |
| | 30° | 0.3558 exact | 0.3558 |
| Paraboloidal mirror, f/4 | 0.5° | 0.9999 | 0.9999 |

A curved OBJECT surface is carried too - Optiland takes a radius on the object surface like any
other, so a field point sits at its sag. On `IdealLens_CurvedObject.zmx` (ideal f/5 lens at
200/200, object radius -300) Optiland gives 0.9805, 0.9267 and 0.8504 at object heights 20, 40
and 60, against the exact 0.9805, 0.9268 and 0.8504.

A central obscuration on the stop of an ideal lens blocks the chief ray and puts a hole in the
pupil. Optiland's rays give 0.873056 and 0.567784 at 15° and 30°, against the exact 0.873055 and
0.567782.

What the bridge still refuses, rather than building something different: even-asphere surfaces.

## Notes, not bugs

- `trace_generic` refuses a normalised pupil coordinate outside [−1, 1] - each of Px and Py
  separately, so the square, not the unit circle that OpticStudio's ZPL `RAYTRACE` enforces.
  With `ray_tracer.set_aiming("iterative")` the unit circle is the real stop, and the bridge
  counts every point outside it as blocked without tracing it; without aiming the circle is the
  paraxial pupil, and an off-axis pupil that grows beyond it cannot be reached.
- Optiland has no relative-illumination analysis. It does have `IncoherentIrradiance`, which
  bins traced rays on the detector - a different route to the same physics, and worth comparing
  against some day.
