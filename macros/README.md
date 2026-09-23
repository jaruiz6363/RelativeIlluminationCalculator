# RELILLUM.ZPL - relative illumination in OpticStudio

`RELILLUM.ZPL` computes relative illumination (RI) versus field inside Zemax OpticStudio, from
OpticStudio's own real rays: RI by Rimmer's method (1986), and the effective F/# by Siew's
definition (2005), which is the one OpticStudio reports. It was written for one job the built-in
analysis does badly: **a lens whose pupil is vignetted, obscured or clipped.** OpticStudio's Relative
Illumination analysis counts rays on a fixed grid, so a vignetting edge crossing the pupil is
measured in steps, and its curve is jagged - by up to 1.5 points, changing sign from field to
field, on a vignetted Cooke triplet. The macro finds the edge of the transmitted pupil by
bisection on a grid refined toward it, so its curve is smooth and its values are right.

It runs the same algorithm as `ricalc`'s forward method (see [../docs/method.md](../docs/method.md)),
on OpticStudio's rays instead of `ricalc`'s.

## Running it

1. Copy `RELILLUM.ZPL` to the OpticStudio macro folder (`Documents\Zemax\Macros`).
2. Open the lens. **Turn on real ray aiming** (System Explorer > Ray Aiming > Real) - see below.
3. Run it from **Programming > Edit/Run ZPL Macros**.

A line is printed as each field finishes, with the elapsed time and the rays traced so far. The
table follows once every field is done, because RI is normalised to the brightest field, which is
known only then.

## Settings

At the top of the file:

| Setting | Default | Meaning |
|---|---|---|
| `nfd` | 10 | field steps from the axis to full field (11 fields) |
| `nbase` | 16 | coarse cells along each side of the pupil |
| `ndepth` | 3 | times a cell at the pupil's boundary may be split in four |
| `nbis` | 10 | bisections locating the boundary on a finest-level cell edge |
| `maxcel` | 40000 | longest list of cells one level may hold |
| `nhash` | 131071 | slots for the rays traced at one field |
| `rmvig` | 1 | remove the lens's vignetting factors for the run, and restore them at the end |

**Speed.** OpticStudio traces one ray per `RAYTRACE` call and, with real ray aiming, aims each one
iteratively; about 1 ms per ray. The default grid traces 12,000-19,000 rays a field, so a field
takes 15-21 s. `ricalc` uses a finer grid (32/4/12); the defaults here trace about 4.5 times
fewer rays, and differ from 32/4/12 by at most 5e-6 on a Kingslake double Gauss, 6e-5 on the
vignetted Cooke triplet and 6e-4 on the obscuration lens below, with the curve still smooth. For
a quicker look, 12/2/8 traces about 3 times fewer again (errors up to 5e-4 on the Cooke, 5e-3 on
the obscuration lens); for the finest numbers, set 32/4/12 and allow several minutes a field.
The macro warns if `maxcel` or `nhash` is ever too small; at 32/4/12 neither is.

## Output

| Column | Meaning |
|---|---|
| Field | field value, in the lens's field units |
| hy | normalised field |
| Img ht | chief ray height on the image surface |
| CRA | chief ray angle to the image-surface normal, degrees |
| RI | relative illumination: illuminance relative to the brightest field measured |
| F#T, F#S | Rimmer's effective F/numbers in the tangential and sagittal directions, 1/(m₂−m₁) and 1/(l₂−l₁) |
| F/#eff | Siew's effective F/#, √(π / (4 n'² PSA)): the F/# of a circular pupil giving the same illuminance - the Effective F/# OpticStudio reports, without its transmission weighting |

**RI never exceeds 1.** As in OpticStudio, it is relative to the brightest field measured - the
axis in nearly every lens. When the brightest field is not the axis, a line under the table says
which it is.

The report ends with the same rows as a **tab-delimited table** - Field, RI and F/#eff, one header
line - to paste straight into Excel or a plotting program.

**TRUNCATED** marks a row whose pupil was cut by the edge of OpticStudio's normalised pupil while
still inside the stop; it appears only with ray aiming off.

## Why real ray aiming

`RAYTRACE` refuses pupil coordinates outside the unit circle. With real ray aiming the unit circle
is the real stop, so every ray the lens transmits can be traced. With aiming off it is the
PARAXIAL entrance pupil, and an off-axis pupil that grows beyond it - pupil coma - cannot be
reached. On a Cooke triplet that loses 11 % of the light at 20°. OpticStudio's own analysis loses
the same light with aiming off, and says nothing:

| Cooke triplet, 20° | RI |
|---|---|
| `ricalc` (exact) | 0.8760 |
| This macro, real aiming | 0.8761 |
| OpticStudio analysis, real aiming | 0.8764 |
| OpticStudio analysis, **aiming off** | **0.7794** |
| This macro, aiming off | 0.7795, marked TRUNCATED |

## Vignetting factors

OpticStudio's vignetting factors (VDX, VDY, VCX, VCY, VAN) reshape the normalised pupil: the unit
circle `RAYTRACE` accepts no longer maps to the real stop but to an ellipse that approximates the
vignetted beam. The real vignetted pupil is not an ellipse - it is usually a cat's eye cut by
two apertures - so light outside the ellipse could not be traced, and RI would come out low
without warning.

So, as OpticStudio's own analysis does with **Remove Vignetting Factors** checked, the macro
removes them: with `rmvig = 1` (the default) it records each field's factors, prints them, sets
them to zero for the run and restores them at the end. The surface apertures then do the
vignetting, as they do in `ricalc`, which never reads vignetting factors. The values are printed
first so that they can be re-entered if the run is stopped before it restores them.

**A lens whose vignetting lives only in its factors**, with automatic semi-diameters on every
surface, has no vignetting left once they are removed, and the macro will report the unvignetted RI.
To model the vignetting, give the surfaces that clip the beam fixed semi-diameters (in the Lens
Data Editor, set the semi-diameter solve to Fixed).

## How it works

For each field:

1. The chief ray is aimed, by Newton iteration, at the centre of the real stop. It fixes the image
   point; it may itself be blocked, by an obscuration say.
2. The normalised pupil is covered by a grid of `nbase` × `nbase` cells. Each grid ray is traced
   and kept if it traces, is not vignetted by any surface aperture, and passes inside the real
   stop (whose radius is set by the real axial marginal ray). Points outside the unit circle are
   counted as blocked without being traced.
3. A cell whose four corners and centre all pass is taken whole. A cell that is partly blocked,
   **or next to one**, is split in four, `ndepth` times. The second condition matters: five rays
   cannot see a thin sliver of the pupil entering a cell between them, and without it such a
   sliver would be counted only once the field moved it onto a ray - a step in the RI curve.
4. At the finest level the boundary is found on each cut cell edge by `nbis` bisections, and the
   passing part of the cell becomes a polygon on those crossings.
5. Each ray's direction is referred to the image point through the reference sphere (Rimmer
   Eq. 3, taken exactly), and expressed as direction cosines about the image-surface normal
   (Rimmer Eq. 1). The area of the pupil in those coordinates - the projected solid angle of the
   cone - is proportional to the illuminance.

A pupil with a hole, several pieces or pointed tips is measured as it is. ZPL has no recursion,
so the grid is worked one level at a time from a list of cells, and traced rays are kept in a
hash table.

## Verification

**In OpticStudio**, on `tests/fixtures/lenses/IdealLens_ObscurationInFront.zmx` - an obscuration
20 mm ahead of an ideal f/5 lens, whose shadow crosses the pupil as the field grows, blocking the
chief ray below 11° - with real ray aiming and the default settings (2026-09-23):

| Field | Exact | This macro | OpticStudio analysis |
|---|---|---|---|
| 0° | 1.0000 | 1.0000 | |
| 3° | 0.9943 | 0.9943 | |
| 6° | 0.9773 | 0.9773 | |
| 9° | 0.9494 | 0.9494 | |
| 12° | 0.9116 | 0.9116 | 0.9062 |
| 15° | 0.8647 | 0.8647 | 0.8591 |
| 18° | 0.8158 | 0.8156 | 0.8145 |
| 21° | 0.7791 | 0.7791 | 0.7789 |
| 24° | 0.7411 | 0.7411 | 0.7446 |
| 27° | 0.6978 | 0.6978 | 0.6996 |
| 30° | 0.6472 | 0.6472 | 0.6524 |

The whole run took 207.6 s for 179,106 rays. For comparison, on the same lens and grid:

| | Time | Rays | Per ray |
|---|---|---|---|
| `ricalc`, forward, one core | 0.03 s | 139,000 | 0.2 µs |
| Optiland's rays, same algorithm | 1.1 s | 191,000 | 6 µs |
| This macro in OpticStudio | 207.6 s | 179,106 | 1.16 ms |

**With vignetting factors**, on `tests/fixtures/lenses/CookeTriplet with Vignetting.zmx` - fixed
apertures on four surfaces - with OpticStudio's vignetting factors also set on the 20° field (VDY
0.091, VCX 0.136, VCY 0.545: an ellipse about half the pupil's height). The macro removed them,
measured, and restored them, and matched `ricalc`'s forward method at every field, to 1e-4 in RI
and 0.001 in F/#eff; 11 fields in 191 s:

| Field | `ricalc` RI | This macro | `ricalc` F/#eff | This macro |
|---|---|---|---|---|
| 0° | 1.0000 | 1.0000 | 3.992 | 3.992 |
| 4° | 0.9575 | 0.9575 | 4.080 | 4.080 |
| 8° | 0.8611 | 0.8611 | 4.302 | 4.302 |
| 12° | 0.7048 | 0.7048 | 4.755 | 4.755 |
| 16° | 0.5227 | 0.5227 | 5.521 | 5.522 |
| 20° | 0.3333 | 0.3333 | 6.915 | 6.915 |

OpticStudio's own analysis reads 0.9721, 0.7190 and 0.3441 at 4°, 12° and 20° on this lens.

**Against the C# code.** The `.ZPL` text was also run through a small ZPL interpreter whose
`RAYTRACE` is answered by Optiland, and compared with `ricalc`'s integrator on the same Optiland
rays: the projected solid angles agree to 4e-11 on the vignetted Cooke triplet and 3e-15 on an
ideal lens with an obscuration on the stop.

**Earlier version.** Until 2026-09-23 the macro found the pupil edge by a radial search along 120
azimuths, which cannot measure a pupil with a hole or a blocked chief ray. For pupils that
search could handle it used the same rays and the same correction, and in OpticStudio it gave:
0.8761 on the Cooke triplet at 20° (exact 0.8760); 0.3331 on the vignetted Cooke triplet at 20°
(`ricalc` forward 0.3333, OpticStudio's analysis 0.3441); 0.8504 on an ideal lens imaging a curved
object (exact 0.8504); and 0.3558 on an ideal lens with a curved image (exact 0.3558).

## Expected output

From `ricalc`'s forward method on the lenses in `tests/fixtures/lenses`, at the primary
wavelength. With real ray aiming the macro should reproduce these to about 1e-4.

**KingslakeDG.zmx** (f/8, 14°)

| Field | Img ht | RI |
|---|---|---|
| 0° | 0 | 1.0000 |
| 2.8° | 4.891 | 0.9959 |
| 5.6° | 9.806 | 0.9836 |
| 8.4° | 14.770 | 0.9634 |
| 11.2° | 19.809 | 0.9356 |
| 14° | 24.950 | 0.9009 |

**Topogon_US2031792_Fig1.zmx** (f/6.3, 35°; Rimmer 1986 gives 34.7 % at 35°)

| Field | Img ht | RI |
|---|---|---|
| 0° | 0 | 1.0000 |
| 7° | 8.112 | 0.9635 |
| 14° | 16.495 | 0.8601 |
| 21° | 25.457 | 0.7066 |
| 28° | 35.382 | 0.5279 |
| 35° | 46.805 | 0.3518 |

The Topogon file was written by hand from the patent; if OpticStudio will not open it, enter the
prescription from [../docs/references.md](../docs/references.md).

## Limits

- Forward rays only: ZPL cannot trace backward from the image point, so `ricalc`'s reverse method
  has no counterpart here.
- One wavelength (the primary); no transmission weighting (coatings, absorption); a uniform
  Lambertian object.
- Rotationally symmetric, sequential systems. The image surface's normal is taken from its
  curvature and conic; aspheric terms on the image surface are ignored.
- A field whose chief ray cannot be aimed at the stop centre is left out, and says so.

## Notes for maintainers: ZPL traps met while writing it

- A comment must be a line of its own. `nfd = 10  ! field steps` is read as code and fails with
  "Syntax error: Unknown symbol FIELD".
- `RAYTRACE` stops the macro with "ERROR in RAYTRACE: Invalid pupil range" for a pupil coordinate
  outside the unit circle, so every point is tested before it is traced.
- `DECLARE` refuses large arrays: 1,050,625 elements failed with "Invalid array declaration";
  131,071 works. The exact cap is not known.
- There is no recursion; `FOR` runs its body at least once even for an empty range; a block `IF`
  has no `THEN`; variable names are case-insensitive and may not be function names.
- These all work (OpticStudio, 2026-09-23): `TIMER` / `ETIM()`, `$TAB()`, `INDX()`, `NFLD()`,
  `FVDX()`..`FVAN()`, and `SETSYSTEMPROPERTY` codes 105-109 (a field's VDX, VDY, VCX, VCY, VAN)
  followed by `UPDATE`.
