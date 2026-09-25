# relillum.ccl - relative illumination in OSLO

`relillum.ccl` computes relative illumination (RI) versus field inside OSLO, from OSLO's own real
rays: RI by Rimmer's method (1986) and the effective F/# by Siew's definition (2005). It runs the
same algorithm as `ricalc`'s forward method and `macros/RELILLUM.ZPL`, on OSLO's rays instead (see
[../docs/method.md](../docs/method.md)). OSLO EDU has no relative illumination of its own - its
Image Illumination is locked to Standard, and its ray-grid method is Premium only - so this is also
the only way to get it there.

It was written and verified in OSLO EDU 6.6, which limits a lens to 10 surfaces and has no special
apertures (obscurations); it uses no feature EDU lacks.

## Installing and running it

1. Copy `relillum.ccl` into OSLO's CCL folder, `public\ccl` or `private\ccl` under the OSLO data
   folder (for OSLO EDU, `C:\Users\Public\Documents\OSLO66 EDU\`).
2. Compile it: **Tools > Compile CCL**, or restart OSLO, which compiles the folders at startup.
   *OSLO keeps running the version it last compiled*; after replacing the file, compile again, and
   check that the window shows `No errors detected`.
3. Open a lens and type `relillum`.

```
relillum                        10 field steps, 16/3/10 grid, current wavelength
relillum 10 32 3 12             a finer grid, slower (32/3 is the most that fits)
relillum 10 16 3 10 2           at wavelength 2
relillum 10 16 3 10 0 1         and also write the report file
```

| Argument | Default | Meaning |
|---|---|---|
| 1 | 10 | field steps from the axis to full field (11 fields) |
| 2 | 16 | coarse cells along each side of the pupil |
| 3 | 3 | times a cell at the pupil's boundary may be split in four |
| 4 | 10 | bisections locating the boundary on a finest cell edge |
| 5 | 0 | wavelength number; 0 is the current one |
| 6 | 0 | 1 also writes a fuller report to `RL_REPORT` |

Field steps are equal in field **angle** for an object at infinity, as `ricalc`'s are, and equal in
object height for a finite object. The text window shows the setup (wavelength, stop, real stop
radius, exit pupil, grid), a line per field, and the table; the graphics window plots RI against
field. The report file adds the rays and time per field, notes on the columns, and the table
tab-delimited with six decimals. Set `RL_REPORT` at the top of the file to a folder that exists; if
it cannot be written, `relillum` says so and carries on without it.

**Speed.** OSLO's trace is fast from CCL: about 28,000 rays and 0.3-0.4 s a field at the default
grid, against 15-21 s a field for the OpticStudio macro, whose every ray OpticStudio aims
iteratively.

## Output

| Column | Meaning |
|---|---|
| Field | field angle (deg), or object height |
| hy | OSLO's fractional field, FBY |
| Img ht | chief ray height on the image surface |
| CRA | chief ray angle to the image-surface normal, degrees |
| RI | relative illumination: illuminance relative to the brightest field measured |
| F#T, F#S | Rimmer's effective F/numbers, 1/(m₂−m₁) and 1/(l₂−l₁) |
| F/#eff | Siew's effective F/#, √(π / (4 n′² PSA)) |

## How it works

For each field, OSLO aims the reference ray at the centre of the stop (`trr`), which fixes the image
point Q′. The pupil is covered by a grid in OSLO's fractional pupil coordinates FX, FY; cells on or
next to the pupil's edge are split, and the edge is found by bisection, exactly as in the macro.
Each ray is traced with `trace_ray(std, …)`, and it counts if it reaches the image with aperture
checking on and crosses the stop inside the real stop radius. Its direction is referred to Q′
through the reference sphere (Rimmer Eq. 3) and expressed about the image-surface normal (Eq. 1).

Two things differ from the OpticStudio macro:

- **OSLO traces pupil coordinates beyond 1**, where OpticStudio refuses them. The grid starts at
  ±1.1 and, if a ray that passes lands on its edge, the field is measured again on a grid 1.5 times
  wider. With OSLO's default aiming the pupil stayed inside ±1.1 on every lens below, the 35°
  Topogon included.
- **The reference ray is OSLO's**, aimed at the stop centre by OSLO itself; the macro aims its own
  by Newton iteration.

## Verification

On `ricalc`'s test lenses, exported to OSLO by LensHH-LT 1.0.158, at the default grid:

| Lens | Field | relillum (OSLO's rays) | `ricalc` forward / exact |
|---|---|---|---|
| Kingslake double Gauss, apertures checked | 14° | 0.8954 | 0.8954 |
| Vignetted Cooke triplet, every aperture checked | 20° | 0.3330 | 0.3330 |
| Topogon, model glasses, F/6.3 | 35° | 0.3518, F/#eff 10.614 | 0.3518, 10.614 |
| Paraboloidal mirror, stop on the mirror | 0.5° | 0.9999, F/#eff 4.016 | 0.9999, 4.016 |
| Ideal lens, curved image R 200 | 15°, 30° | 0.8027, 0.3559 | exact 0.8028, 0.3558 |
| Ideal lens, curved image R −150 | 30° | 0.7853 | exact 0.7852 |
| Ideal lens, curved object R −300 | 60 mm | 0.8504, F/#eff 10.858 | exact 0.8504, 10.858 |

Every field of every lens agrees to the digits printed, but for the ideal lenses at infinity:

**OSLO's perfect lens obeys the sine condition**, h = f sin u′, where Zemax's paraxial surface - and
the closed-form answers - obey the tangent law, h = f tan u′. At an object at infinity their cones
differ: `EBR 10` on the f = 100 lens is an image-space sin u′ of 0.1 in OSLO (F/#eff 5.000, the edge
ray crossing the lens at 10.050) and a tan u′ of 0.1 in Zemax (F/#eff 5.025). The relative
illumination differs by up to 1e-4. At a finite conjugate the two agree.

The obscuration lens cannot be run: an obscuration needs OSLO's special apertures, which EDU lacks.

## CCL, as met while writing this

- **An array dimension is at most 32,000.** A lattice of 513 × 513 nodes is a 2-D array.
- **`a/b` is real even for integers**, and an `int` parameter refuses a real ("Expecting argument
  to be an integer"): halve into an `int` variable first.
- **An error handler that sets `errno = 0` and returns** lets execution continue; if the error
  recurs - the one above did, on every cell - the stack overflows. `relillum` installs its handler
  only around `trr`, the one call that can fail with an error.
- **A ray blocked by a checked aperture, and a ray that misses a surface, come back the same**:
  nothing written to the spreadsheet buffer, and no error. Fill the buffer with a sentinel first.
- **The buffer layout.** `trr`: row 3 is YC, XC, YFS, XFS, OPL, reference sphere radius.
  `trace_ray(std, loc, all, FY, FX, yes)`: a row per surface, Y, X, Z, YANG, XANG, D, and the
  surface number in column 7; the direction cosines follow from YANG and XANG, whose tangents are
  L/M and K/M - M's sign is not in them, and is negative after an odd number of reflections.
  `sag(srf, y, x)`: y, x, sag, then the normal's direction cosines.
- **`printf("%g", 0.0)` prints `--`.** Use `%.10g`.
- **`apck(on)` and `apck(off)` print a line each** while text output is on.
- **Paths in strings**: forward slashes. A backslash starts an escape.
- **`fopen(path, "at")`** appends; closing and reopening after each field keeps what a run wrote if
  it stops part way.

`ri_probe.ccl` is the probe that established the buffer layout, how blocked and failed rays come
back, and the trace speed, before `relillum` was written. Its reports go to this folder.

## OSLO lens files

What OSLO itself writes - `EBR`/`ANG` or `NAO`/`OBH`, `PFL`, `GLA MOD` with an index per
wavelength, `AP CHK` against `AP`, the primary wavelength first, and a lens name without numbers in
it - is recorded in [../docs/lenshh-lt-oslo-files.md](../docs/lenshh-lt-oslo-files.md), with what it
took to make LensHH-LT's exporter and this repository's reader match it.
