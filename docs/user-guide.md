# User Guide

`ricalc` reads a lens prescription and reports its **relative illumination** (RI) and
**effective F/#** across the field. This guide covers installing it, running it, reading its
output and plotting the results. How the numbers are computed is in [method.md](method.md);
running the same calculation inside OpticStudio is in
[the macro guide](../macros/README.md).

## 1. What you need

- **Windows.** `ricalc` is a .NET program and may well build elsewhere, but it has been used and
  tested on Windows.
- **The .NET 8 SDK**, from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).
  It includes the .NET 8 runtime that `ricalc` runs on. A later SDK also builds it, but the
  .NET 8 runtime must still be installed to run it.
- *Optional:* an internet connection once, to set up Python and Optiland for the Optiland
  cross-check (section 10).
- *Optional:* Zemax OpticStudio, for the macro that runs the same calculation on OpticStudio's
  own rays.

To check what .NET is installed - look for an SDK, and for `Microsoft.NETCore.App 8.x` among the
runtimes:

    dotnet --list-sdks
    dotnet --list-runtimes

## 2. Getting and building it

    git clone https://github.com/jaruiz6363/RelativeIlluminationCalculator.git
    cd RelativeIlluminationCalculator
    dotnet build -c Release

The program is then

    src\RelativeIllumination.Cli\bin\Release\net8.0\ricalc.exe

with the glass catalogs copied beside it. Run it by that path, or from the repository folder
with `dotnet run --project src\RelativeIllumination.Cli -c Release -- <lens file> [options]`.

## 3. Installing it (optional)

To have `ricalc` in its own folder, independent of the source:

    dotnet publish src\RelativeIllumination.Cli -c Release -o C:\Tools\ricalc

and add `C:\Tools\ricalc` to your PATH. The folder is self-contained: the program, its libraries
and the glass catalogs (`catalogs\Glass`). The Python environment for the Optiland cross-check is
not copied; see section 10.

## 4. A first run

    ricalc "tests\fixtures\lenses\CookeTriplet with Vignetting.zmx" --fields 4

(Quotes, because the file name has spaces.)

```
Lens        CookeTriplet with Vignetting.zmx  -  Cooke Triplet with Vignetting
Wavelength  0.58756 um
EFL 50.0005   EPD 12.5   F/# 4   stop surface 4 (radius 4.8746)
Focus       paraxial image 43.0806 after the last surface; image surface at 42.95
Object      at infinity
Image       radius flat, exit pupil -53.0202 from image
Source      Lambertian, uniform radiance
Method      RI by Rimmer (Proc. SPIE 655, 1986); effective F/# by Siew (Proc. SPIE 5867, 2005)
Apertures   S1 r=6.5, S3 r=5, S4 stop r=4.8746, S5 r=6.5, S6 r=6.5

  Field deg    Img ht    CRA   RI rev    F#T    F#S   RI fwd  fwd-rev  F/#eff |   S/S0   cos4      D%  ydD/dy   Siew
          0         0   0.00   1.0000   3.99   3.99   1.0000   0.0000   3.992 |  1.000  1.000    0.00   0.000  1.000
          5    4.3646   4.70   0.9361   4.24   4.00   0.9364  +0.0003   4.126 |  0.952  0.985    0.02   0.000  0.936
         10    8.8025   9.39   0.7881   4.93   4.03   0.7876  -0.0005   4.496 |  0.841  0.941    0.09   0.002  0.788
         15   13.3976  14.05   0.5703   6.37   4.13   0.5698  -0.0005   5.286 |  0.662  0.871    0.25   0.007  0.570
         20   18.2643  18.65   0.3338   9.42   4.58   0.3333  -0.0005   6.909 |  0.440  0.780    0.61   0.020  0.332
```

A run takes well under a second for a typical lens.

## 5. Reading the output

**The header** says how the file was understood - check it first:

| Line | What to check |
|---|---|
| Lens, Wavelength | the file and the wavelength traced (the file's primary, unless `--wave`) |
| EFL, EPD, F/#, stop | these should match your design program's |
| Focus | where the paraxial image lies against the image surface in the file (a defocused image surface is honoured) |
| Object, Image | object at infinity or its distance; image surface radius; exit pupil position |
| Source | Lambertian, unless `--source` |
| Apertures | the apertures that clip rays - see section 9. If a surface you expect to vignette is missing, its semi-diameter is automatic |
| Note, WARNING | anything unusual about how the file was read; a glass that could not be found is treated as air, and says so |

**The table:**

| Column | Meaning |
|---|---|
| Field | field value, in the file's units: degrees for field angle, lens units for object height |
| Img ht | where the chief ray meets the image surface |
| CRA | chief ray angle to the image-surface normal, degrees |
| **RI rev** | **relative illumination, by the reverse method** - traced backward from the image point. This is the exact value: use it |
| F#T, F#S | Rimmer's directional F/numbers: the beam's extent tangentially and sagittally |
| RI fwd | relative illumination by the forward method - traced from the object point, corrected to the image point |
| fwd-rev | the difference between the two methods: a check (below) |
| **F/#eff** | **Siew's effective F/#**: the F/# of a circular pupil giving the same illuminance - the Effective F/# OpticStudio reports |
| S/S0 ... Siew | Siew's breakdown of where the fall-off comes from (below) |

**RI never exceeds 1**: it is relative to the brightest field computed, which is the axis in
nearly every lens. When it is not, a line under the table says which field is brightest, and by
how much it outshines the axis.

**fwd-rev.** The two methods are independent, and on a well-behaved lens they agree to a few
parts in 10⁴. A difference of 10⁻³ or more says the lens is doing something the forward method
finds hard - strong aberration, a severely defocused image surface - and the reverse value is the
one to trust.

**F/#eff against F#T and F#S.** F/#eff answers *how bright*: it falls straight out of RI, as
F/#eff(axis) / √RI. F#T and F#S answer *what shape*: on a round pupil both equal F/#eff, and
on a vignetted one they part - the lens above is F/9.4 by F/4.6 at 20°, with F/#eff 6.9.

**The breakdown** (Siew 2017) splits RI into factors, as a diagnostic - it is an approximation,
and the reported RI is always the measured one:

| Column | Meaning |
|---|---|
| S/S0 | area of the transmitted beam in the entrance pupil, relative to the axis: vignetting and pupil growth |
| cos4 | cos⁴ of the chief ray angle in object space: obliquity |
| D% | distortion, percent |
| ydD/dy | differential distortion: how fast distortion changes across the field |
| Siew | the product of these, Siew's estimate of RI |

In the example, S/S0 falls to 0.44 at 20° while cos4 is 0.78: vignetting, not obliquity, is what
costs the light. Leave the breakdown out with `--no-breakdown`.

## 6. Choosing fields and wavelength

| Option | Effect |
|---|---|
| `--fields N` | N+1 fields evenly from the axis to the file's largest field (default 10) |
| `--at 0,5,10.5,14` | exactly these fields instead |
| `--wave K` | the K-th wavelength in the file (1-based); default the primary |

Fields are in the file's own units - degrees for a field-angle lens, lens units of object height
for a finite-conjugate one. RI is monochromatic; run once per wavelength to compare colours.

## 7. All the options

| Option | Effect |
|---|---|
| `--fields N`, `--at A,B,...` | which fields (section 6) |
| `--wave K` | which wavelength (section 6) |
| `--method M` | `forward`, `reverse` or `both` (default both) |
| `--source S` | `lambertian` (default), or `cos:N` for an object whose radiance falls as cosᴺ of the angle from its surface normal |
| `--clip-auto` | treat automatic semi-diameters as apertures too (section 9) |
| `--no-rimmer` | forward method without the reference-sphere correction - to see what the correction is worth; never needed for results |
| `--no-breakdown` | omit Siew's breakdown columns |
| `--grid N`, `--depth N` | sampling of the pupil: coarse cells per side (default 32) and refinement levels (default 4). The defaults are accurate to about 10⁻⁵; raise them only for an unusual pupil, such as a thin crescent around a large obscuration |
| `--glass DIR` | a folder of `.agf` glass catalogs to use instead of the bundled ones |
| `--csv FILE` | also write the results as CSV (section 8) |
| `--limits F` | instead of the table, report which surfaces stop the rays at field F (section 9) |
| `--optiland` | also measure the lens with Optiland's ray trace (section 10) |
| `-h`, `--help` | the list of options |

**Lens files:** OpticStudio `.zmx`, CODE V `.seq`, Optalix `.otx`/`.opt`, OSLO `.len`/`.osl`,
Optiland `.json`, LensHH-LT `.lhlt`. Rotationally symmetric sequential systems; decentred or tilted
systems (coordinate breaks) are refused, not approximated.

## 8. Saving and plotting

    ricalc mylens.zmx --fields 40 --csv mylens.csv

writes one row per field, with a header line. The columns:

| Column | Meaning |
|---|---|
| `field_x`, `field_y` | the field |
| `image_x`, `image_y` | chief ray on the image surface |
| `cra_deg` | chief ray angle to the image-surface normal |
| `ri_reverse`, `ri_forward` | RI by each method, relative to the brightest field |
| `fnum_t`, `fnum_s` | Rimmer's directional F/numbers |
| `pupil_ratio`, `cos4`, `distortion`, `y_dD_dy`, `siew_estimate` | the breakdown (`distortion` as a fraction, not percent) |
| `ri_reverse_to_axis`, `ri_forward_to_axis` | RI relative to the axis instead - above 1 wherever a field outshines the axis |
| `fnum_eff` | Siew's effective F/# |

**To plot RI in Excel:** open the CSV, select the `field_y` and `ri_reverse` columns, and insert
a Scatter chart with smooth lines. `--fields 40` or more gives a smooth curve.

## 9. Apertures, vignetting, and what stops the rays

A ray counts if it passes every aperture that clips. Which do:

| Aperture | Clips? |
|---|---|
| the stop | always |
| a fixed semi-diameter | yes |
| an automatic semi-diameter | no - it is solved to pass the file's own fields - unless `--clip-auto` |
| a clear aperture, a floating aperture | yes, outside its radius |
| an obscuration, an annulus's inner radius | yes, inside its radius |
| a mechanical semi-diameter | no |

So **vignetting comes from fixed apertures.** A design whose vignetting is described only by
automatic semi-diameters, or only by OpticStudio's vignetting factors, reports no vignetting.
`ricalc` does not read vignetting factors: they approximate a vignetted pupil by an ellipse,
and `ricalc` measures the real one. Give the surfaces that should clip the beam fixed
semi-diameters, or try `--clip-auto`.

Every ray goes through the real stop, so ray aiming is built in: there is no ray-aiming setting
to get wrong, and a pupil that grows off axis is measured in full.

**`--limits F`** shows what limits the beam at field F: a grid of 81 × 81 rays over twice the
pupil, each tallied by the surface that stopped it.

```
Field 20: chief ray at pupil (0, -0.0587); 81x81 rays over +/-2 pupil radii about it
  passed          557
  S1   Vignetted               5202
  S3   Vignetted               354
  S4   Vignetted               119
  S5   Vignetted               305
  S6   Vignetted               24
```

Here the front surface, S1, does most of the vignetting at 20°. Besides `Vignetted`, a ray can be
reported as missing a surface or failing to refract (total internal reflection).

## 10. Checking against Optiland (optional)

The same calculation can be run on the rays of Optiland, an independent open-source ray tracer,
as a cross-check on `ricalc`'s own tracer. Set it up once, from the repository folder in
PowerShell:

    .\tools\setup-python.ps1

This downloads an embeddable Python into `python-embed\` and installs Optiland (and `markdown`,
for the documentation tool). Then:

    ricalc mylens.zmx --optiland

adds a section with RI and F/#eff from Optiland's rays beside `ricalc`'s. They normally agree to
a few parts in 10⁵. `ricalc` finds `python-embed` beside itself or in a folder above it; a
published copy (section 3) needs the environment variable `RICALC_PYTHON_HOME` pointing at the
`python-embed` folder. The cross-check cannot yet take even-asphere surfaces, and says so.

## 11. In OpticStudio

`macros\RELILLUM.ZPL` runs the same calculation inside OpticStudio, on OpticStudio's own rays,
and prints a table ready to plot. See [the macro guide](../macros/README.md).

## 12. When something goes wrong

| Message or symptom | What it means |
|---|---|
| `error: Lens file not found.` | check the path; put quotes around a name with spaces |
| `WARNING  glass not found, treated as air: ...` | a glass is in none of the catalogs. Add its catalog with `--glass DIR`, or change the glass in the file. The results are wrong until it is fixed |
| `No glass catalogs found` | the `catalogs\Glass` folder is not beside the program; rebuild, or use `--glass` |
| `the chief ray cannot be aimed at the stop centre` | at that field no ray reaches the centre of the stop - usually a field beyond what the lens can image. That field is left out |
| A surface type or coordinate break is refused | the lens has something `ricalc` does not model; it refuses rather than approximating |
| `fwd-rev` of 10⁻³ or more | see section 5: trust RI rev |
| No vignetting where you expect it | the clipping surfaces have automatic semi-diameters: see section 9 |
| `Optiland is not available` | run `tools\setup-python.ps1`, or set `RICALC_PYTHON_HOME` (section 10) |

## 13. Running the tests

    dotnet test

runs about 40 tests against exact answers (Gardner's disk formula for ideal lenses, with and
without obscurations, at finite and infinite conjugates, on curved object and image surfaces) and
published values (Rimmer's Topogon). The Optiland tests report `NOT RUN` rather than failing when
the Python environment has not been set up.
