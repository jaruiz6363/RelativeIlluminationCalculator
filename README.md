# Relative Illumination Calculator

Opens a lens design written by any of the common optical design programs and reports its
**relative illumination** across the field, measured two independent ways by Rimmer's method
(1986), with the **effective F/#** by Siew's definition (2005) and a breakdown of where the
fall-off comes from.

    ricalc lens.zmx

```
  Field deg    Img ht    CRA   RI rev    F#T    F#S   RI fwd  fwd-rev  F/#eff |   S/S0   cos4      D%  ydD/dy   Siew
          0         0   0.00   1.0000   8.00   8.00   1.0000   0.0000   7.995 |  1.000  1.000    0.00   0.000  1.000
          7   12.2804   6.91   0.9744   8.15   8.05   0.9744   0.0000   8.099 |  1.005  0.971    0.01   0.000  0.974
         14   24.9495  13.81   0.9007   8.65   8.21   0.9009  +0.0001   8.424 |  1.019  0.886    0.07   0.001  0.901
```

RI is relative to the brightest field (the axis here). F#T and F#S are Rimmer's directional
F/numbers, F/#eff is Siew's effective F/# - the one OpticStudio reports - and the columns after
the bar are Siew's breakdown of the fall-off; see [docs/method.md](docs/method.md).

## Why

Relative illumination is the projected solid angle of the transmitted cone at the image point,
measured in image-space direction cosines about the image-surface normal (Rimmer 1986). That
one quantity carries the cos⁴ law, pupil aberrations, distortion, vignetting and obscurations
together, at any conjugate and for any object shape. What goes wrong in practice is how it is
measured:

- **Forward-traced rays used as they are.** Rays from the object point do not pass through the
  image point, and their raw directions describe the cone somewhere else. On the Kingslake
  double Gauss at f/8 that alone puts the full-field value **2.9 % low**. Here each ray is
  referred to the image point through the reference sphere (Rimmer's Eq. 3). A backward trace
  from the image point, which needs no such correction, is done as well, and the two must agree.
- **Rays launched into the paraxial entrance pupil.** The real pupil grows or shrinks with field.
  That growth is what lets a wide-angle lens beat cos⁴. Here every ray goes through the real stop,
  which is ray aiming by construction, and the search window grows until it contains the beam.
- **A radial edge search from the pupil centre.** It cannot see a central obscuration, a vignetted
  chief ray, or a pupil clipped from two sides. Here the pupil is covered by an adaptive grid
  with exact boundary crossings, so a region with a hole or several pieces is measured as it is.
- **Flat image, Lambertian object.** Curved image surfaces are handled by measuring about the
  local normal (Rimmer's Eq. 1). Curved object surfaces and finite conjugates are handled too.
  So are non-Lambertian sources (`--source cos:N`), weighted ray by ray.

**To install and use it, see the [User Guide](docs/user-guide.md).** The method and the choices
behind it are in [docs/method.md](docs/method.md); the papers are in
[docs/references.md](docs/references.md).

## Lens files

| Program | Extensions |
|---|---|
| Zemax OpticStudio | `.zmx` |
| CODE V | `.seq` |
| Optalix | `.otx`, `.opt` |
| OSLO | `.len`, `.osl` |
| Optiland | `.json` |
| LensHH-LT | `.lhlt` |

The readers and glass catalogs come from AberrationCalculator. Glass is resolved from the
bundled AGF catalogs in `catalogs/Glass`, or from `--glass DIR`.

## Options

    --fields N         N+1 fields from 0 to the file's largest (default 10)
    --at A,B,...       these field values instead
    --method M         forward | reverse | both (default both)
    --wave K           wavelength number, 1-based (default: primary)
    --source S         lambertian (default) or cos:N
    --clip-auto        automatic semi-diameters clip too
    --no-rimmer        forward method without the reference-sphere correction
    --no-breakdown     skip Siew's breakdown
    --grid N, --depth N   sampling density (default 32, 4)
    --csv FILE         also write CSV (it adds RI relative to the axis)
    --limits F         report which surfaces stop the rays at field F, and why
    --optiland         also measure the lens with Optiland's ray trace (see below)

## Building and testing

    dotnet build -c Release
    dotnet test

Requirements, installation, and how to read the output are in the
[User Guide](docs/user-guide.md). The tests compare against exact answers, not against another
program:

- Foote's disk irradiance (Gardner Eq. 7) for an ideal lens with the stop on the lens, both at
  infinite and at finite conjugate; with the stop behind the lens; and with a central obscuration.
- The cone of a paraboloidal mirror with an obscuration (a folded system).
- A curved Lambertian object, whose image-plane illuminance must depend only on the image-space
  cone - built in code, and again read from `IdealLens_CurvedObject.zmx`, where the sag puts the
  chief ray at 58.2351 for an object height of 60. On that lens the ZPL macro reproduces the
  exact 0.8504 from OpticStudio's rays, while uncorrected ray directions read 0.9011.
- A curved image surface, concave and convex: the cone to the stop disk integrated about the
  surface normal at the image point (Rimmer Eq. 1), and shown to differ from the same cone taken
  about the axis.
- An obscuration 20 mm in front of the lens, whose shadow moves across the pupil with field and
  blocks the chief ray below 11°, against the cone to the disk minus that shadow.
- A cos² source integrated independently over the lens disk.
- The area integrator on a disk, an annulus, two disks, a doubly clipped disk and a sheared map.
- On real lenses, forward and reverse agree (including a vignetted Cooke triplet whose 20°
  value the ZPL macro confirms on OpticStudio's own rays), every file format gives the same
  answer, and checked apertures vignette.
- With the embedded Python set up, Optiland's rays, measured by the same integrator, reproduce
  the forward method on that same vignetted lens to 6e-6, and the exact answer on an ideal lens
  with a central obscuration, whose chief ray is blocked.

And against published values:

| Lens | Field | Published | This program |
|---|---|---|---|
| Topogon, US 2,031,792 Fig. 1, f/6.3 (Rimmer 1986, Table 1) | 35° | 34.7 % ray grid, 35.0 % Kingslake; 36.7 % uncorrected | 35.2 % both methods; 37.3 % with `--no-rimmer` |

The Topogon is the case Rimmer uses to show a lens falling below cos⁴ (45 %) with no
vignetting at all. Its prescription (`tests/fixtures/lenses/Topogon_US2031792_Fig1.zmx`) is read
from the patent's drawing sheet. The deep inner menisci put the stop close to their centres of
curvature, and that is what exposed a ray-surface intersection bug here: the root nearest the
vertex plane is not always on the right hemisphere.

## Against OpticStudio

`macros/RELILLUM.ZPL` runs the forward method inside OpticStudio on its own real rays, with the
same pupil integrator as `ricalc`, and prints a smooth RI curve ready to plot (see
[macros/README.md](macros/README.md)). On the obscuration lens below it matches the exact answer
at all 11 fields, to 2e-4, where OpticStudio's analysis is off by up to 0.6 points.

OpticStudio's own analysis is based on Rimmer's method and integrates over a uniform grid in
image-space direction cosines - the reverse method - normalising to the brightest field. On the
Cooke 40° sample lens at 20°:

| | RI at 20° |
|---|---|
| ricalc, reverse (exact) | 0.8760 |
| RELILLUM (the macro), real ray aiming | 0.8761 |
| OpticStudio analysis, real ray aiming (ray density 25) | 0.8764 |
| OpticStudio analysis, **ray aiming off** | **0.7794** |
| RELILLUM, ray aiming off (confined to the paraxial pupil) | 0.7795 |

With real ray aiming OpticStudio is right. With aiming off its RI silently drops 11 %, because
it samples only the paraxial entrance pupil and cannot see the real pupil grow off axis. The
macro confined the same way reproduces that to 1e-4. `ricalc` traces through the real stop and
has no such setting to get wrong.

On a **curved image** OpticStudio is right as well. For the ideal f/5 lens with a convex R +200
image surface (`tests/fixtures/lenses/IdealLens_CurvedImage_R200.zmx`), where the exact answer
is the cone to the stop disk taken about the surface normal:

| Field | Exact | ricalc reverse | RELILLUM | OpticStudio (ray density 25) | Measured about the axis instead |
|---|---|---|---|---|---|
| 15° | 0.8028 | 0.8028 | 0.8027 | 0.8024 | 0.8412 |
| 30° | 0.3558 | 0.3558 | 0.3558 | 0.3572 | 0.4651 |

OpticStudio uses the surface normal. Its residual, smooth and at most 0.0015 at 30°, looks like
sampling at that ray density.

**Curved object.** On `IdealLens_CurvedObject.zmx` - an ideal f/5 lens at 200/200 whose object
surface has radius -300, so every field point images out of focus on the flat image plane -
OpticStudio is within 5.4e-5 of exact at every field (0.850364 at object height 60, against
0.850418). Its best agreement of any case here, which fits: no vignetting and no obscuration, so
its ray grid has no hard edge to quantise. Five calculations agree on that lens - the closed-form
integral, ricalc's two methods, Optiland's rays, the ZPL macro on OpticStudio's rays, and
OpticStudio's own analysis - while uncorrected ray directions read 0.9011.

**Vignetting and obscurations.** OpticStudio gets both right in principle - it handles a pupil
with a hole, and fields whose chief ray is blocked - but it counts rays on a fixed grid, and a
hard edge crossing the pupil is then measured in steps:

| Lens | Field | Exact | ricalc | OpticStudio analysis |
|---|---|---|---|---|
| Cooke triplet with vignetting, f/4 | 4° | - | 0.9572 | 0.9721 (+1.5 pts) |
| | 12° | - | 0.7053 | 0.7190 (+1.4) |
| | 18° | - | 0.4281 | 0.4227 (−0.5) |
| Obscuration 20 mm in front, f/5 | 15° | 0.8647 | 0.8647 | 0.8591 (−0.6) |
| | 30° | 0.6472 | 0.6472 | 0.6524 (+0.5) |

Its errors change sign from field to field, which is sampling noise rather than bias, and raising
the ray density reduces it only slowly. On the vignetted Cooke triplet the ZPL macro - the same
OpticStudio rays, but with the pupil edge found by bisection instead of counted - landed within
2e-4 of `ricalc` (its earlier radial-search version, 2026-09-22).

## Against Optiland

This cross-check is optional; everything else in `ricalc` runs without Python. To use it, run
the setup script once from the repository folder in PowerShell. It needs an internet connection:
it downloads Python's embeddable package from python.org into `python-embed\` and installs
Optiland into it with pip. Nothing is installed system-wide, and a fresh clone does not include
it, because `python-embed\` is not kept in git.

    .\tools\setup-python.ps1          # once: Python + optiland, into python-embed\
    ricalc lens.zmx --optiland        # adds a column measured from Optiland's rays

Without that setup, `--optiland` prints a note saying how to set it up, and the Optiland tests
report `NOT RUN`. See section 10 of the [User Guide](docs/user-guide.md).

The lens is built inside Optiland from the prescription this program parsed - radii,
thicknesses, conics, indices, stop and clipping apertures - not from Optiland's own file import,
and the forward method is run on Optiland's rays - Rimmer's RI and Siew's F/#eff, by the same code.
On the vignetted Cooke triplet:

| Field | ricalc forward | Optiland's rays, same integrator |
|---|---|---|
| 8° | 0.861055 | 0.861056 |
| 16° | 0.522746 | 0.522751 |
| 20° | 0.333281 | 0.333287 |

Optiland's parameter space is its normalised pupil - with iterative aiming, the real stop -
and this program's is the entrance pupil, so the two grids differ and the last digit may too.

Three independent ray tracers now agree on that lens: this program's, OpticStudio's (through
`macros/RELILLUM.ZPL`) and Optiland's. The bridge also carries ideal lenses and mirrors - on the
ideal lens with a curved image Optiland gives the exact 0.3558 at 30 degrees - and refuses what it
cannot build faithfully (even aspheres) rather than building something else. On a curved OBJECT
surface all three - exact, ricalc and Optiland - agree to 1e-4. On an ideal lens with a central
obscuration on the stop, which blocks the chief ray, Optiland's rays give 0.873056 and 0.567784
at 15° and 30° against the exact 0.873055 and 0.567782.

Two issues in **Optiland 0.6.2** came out of this, worth reporting upstream and written up in
[docs/optiland-0.6.2.md](docs/optiland-0.6.2.md): reading a `.zmx` resolves glass names without
the catalog the file names (Schott F4 arrives as CDGM's F4, 0.35 % off in index and 1 % in focal
length), and surface apertures are dropped, so a vignetted lens imports unvignetted.

## Against LensHH-LT

Checking LensHH-LT against the exact answers here found issues in its relative illumination:
it measured the cone about the optical axis rather than the image-surface normal, used forward
ray directions without referring them to the image point, clipped apertures slightly large, and
could not measure a pupil with a hole. These were addressed in **LensHH-LT 1.0.157**, which now
agrees with `ricalc` on the curved-image, curved-object, Kingslake and vignetted Cooke lenses to
within 2e-4. A few smaller items found in that release are being followed up.
The engineering record is in [docs/lenshh-lt-fix-guide.md](docs/lenshh-lt-fix-guide.md).

## Roadmap

- More benchmarks: Siew's two projection lenses, US 6441971 and US 2516724.
- Transmission inside the integral (coatings, absorption), and several wavelengths combined.
- Reshidko & Sasián's aberration-coefficient decomposition, showing which aberration costs the light.
- Decentred and tilted systems.

## License and authors

MIT, © 2026 Javier Ruiz. See [LICENSE](LICENSE) and [AUTHORS](AUTHORS): Javier Ruiz, Claude Code.

The lens-file readers, glass catalogs and paraxial trace were copied from
[AberrationCalculator](https://github.com/jaruiz6363/AberrationCalculator), which is not a
dependency. The only build dependency outside the .NET SDK is the `pythonnet` package. Python
and the `optiland` package are needed only for `--optiland`, and `tools\setup-python.ps1`
installs them into the repository folder.
