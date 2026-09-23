# References

The papers are kept outside the repository, because they are copyrighted, in
`C:\Research\RelativeIllumination`.

## The method and where each part comes from

### Rimmer (1986) - the method itself

M. P. Rimmer, "Relative illumination calculations," *Proc. SPIE* **655**, 99-104 (1986).
File: `rimmer1986.pdf`.

The foundation of everything here, and of OpticStudio's own analysis. Taken from it:

| Rimmer | What it gives | Where it is used |
|---|---|---|
| Fig. 2 and text | Illuminance at an image point is proportional to the area of the exit pupil in image-space direction cosines: the projected solid angle of the cone arriving there | The quantity `ricalc`, the macro and the Optiland cross-check all measure |
| Eq. 1 | Direction cosines are taken about the image-surface normal at the image point, not about the optical axis | Curved image surfaces |
| Eq. 3 | A forward-traced ray does not pass through the image point; its direction must be referred to the image point before its direction cosines are used | The forward method, in all three implementations |
| "The straightforward way" | Trace backward from the image point on a grid of directions and keep those that get out | The reverse method (exact; no correction needed) |
| 1/(m₂−m₁) | Effective F/number in each direction from the extent of the pupil in direction cosines | F#T and F#S columns (the single F/#eff follows Siew 2005, below) |
| Discussion | Obstructions and vignetting must be included in the pupil | The pupil is measured as it is, holes and all |
| Table 1 | The Topogon at 35°: 34.7 % by ray grid, 36.7 % from uncorrected rays | Benchmark (`ricalc`: 35.2 %, and 37.3 % without the correction) |

**Where this program goes beyond the paper:**

- **Eq. 3 is taken exactly.** Rimmer gives a first-order correction. Here each ray is carried to
  the reference sphere - centred on the image point, through the axial exit pupil - and its
  direction is taken from that crossing to the image point. On an unaberrated lens at focus the
  correction is exactly zero, as it should be.
- **The pupil is measured by an adaptive grid with located boundaries,** not by counting rays.
  Rimmer spaces rays evenly in exit direction cosines, using a magnification matrix (his Eq. 4),
  and counts them. Here each piece of the pupil is measured as an area in direction-cosine
  space, so the rays need not be evenly spaced there, and Eq. 4 is not needed. The edge of the
  pupil is found by bisection, and cells next to the boundary are refined too, so the result
  changes smoothly with field instead of in steps.
- **Both directions of trace are done and compared,** forward from the object with the Eq. 3
  correction and backward from the image point without it. Their agreement is a check on each.

### Gardner (1947) - exact answers for the tests

I. C. Gardner, "Validity of the cosine-fourth-power law of illumination," *J. Res. Natl. Bur.
Stand.* **39**, 213-219 (1947). File: `jresv39n3p213_A1b.pdf`.

- Radiometric transfer between pupils (Eqs. 3-6).
- The exact irradiance from a uniform disk (Eq. 7), used as the exact answer in the test suite:
  an ideal lens with the stop at the lens, at infinite and finite conjugates, with the stop behind
  the lens, with a central obscuration, and on curved image surfaces.
- When cos⁴ is exact (stop in front, object at infinity, no distortion), and that transmission
  losses belong inside the integral - not yet included here (see the roadmap in the README).

### Reshidko and Sasián (2016) - the reverse trace

D. Reshidko and J. Sasián, "The role of aberrations in the relative illumination of a lens
system," *Proc. SPIE* **9948**, 994806 (2016). File: `994806.pdf`.

- Tracing backward from the image point as the rigorous method (Sec. 4.3): the reverse method.
- Fourth-order RI coefficients in terms of wave-aberration coefficients (Table 1) - planned, to
  show which aberration costs the light.
- Benchmark designs US 6,441,971 and US 2,516,724 - planned.

### Siew (2017) - the distortion breakdown

R. Siew, "Relative illumination and image distortion," *Opt. Eng.* **56**(4), 049701 (2017).
File: `siew2017.pdf`.

- The small-aperture approximation RI ≈ [S(y)/S(0)] cos⁴θ / {(1+D)[(1+D) + y dD/dy]} (Eq. 12),
  printed beside the measured RI as a breakdown of where the fall-off comes from: pupil size,
  obliquity, distortion and differential distortion. It is a diagnostic; the reported RI is
  always the measured one.
- Two projection-lens prescriptions (Tables 1-2) - planned as benchmarks.

### Siew (2005) - the effective F/#

R. H. Siew, "f/No. and the radiometry of image forming optical systems with non-circular aperture
stops," *Proc. SPIE* **5867**, 586701 (2005). File: `siew2005.pdf`.

- Eq. 6 and 8: an effective numerical aperture and f/No. for an exit pupil of any shape,
  f/No. = 1/(2 NA), defined so that it gives the right image irradiance rather than the right
  aperture area.
- Eq. 10-11: the same in terms of the projected solid angle, f/No. = √(π / (4 n'² PSA)), with n'
  the image-space index - the formula `ricalc` and the macro use for the F/#eff column. Siew
  credits the suggestion to K. Moore of Zemax, and notes that OpticStudio reports it versus field,
  with transmission folded into the PSA, so that its value is strictly a T/#.
- Eq. 12: RI = PSA(x, y) / PSA(0, 0) - the quantity measured here.
- Not used: the "apparent f/No." of Sec. 4, which adds diffraction and aberration of the image
  point, and matters only when the image is comparable in size to the point spread function.

## Other programs

**Zemax OpticStudio**, *User Manual*, Analysis > Image Quality > Relative Illumination. Relevant
statements, which the comparisons in the README rely on:

- The method "is based upon one described in M. Rimmer, 'Relative illumination calculations',
  Proc. SPIE Vol. 655, p99 (1986)", extended to apodization, transmission, polarization and
  non-planar image surfaces.
- RI is "normalized to the illumination at the point in the field that has maximum
  illumination (which may not be on axis)". `ricalc` and the macro normalise the same way.
- The integration "is carried out in direction cosine space using a uniform grid in image cosine
  space" - the reverse method, on a fixed grid. The Ray Density setting is the number of rays
  across that grid (10 gives about 78 rays). A fixed grid is what makes its curve jagged when a
  vignetting edge crosses the pupil.
- The Effective F/# is "the F/# required for a perfect optical system with 100 % transmission and
  a circular exit pupil to have the same image illumination", computed from the projected solid
  angle weighted for transmission, following Siew (2005), above.

**Optiland**, H. Kramer and contributors, an open-source Python optical design package,
[github.com/HarrisonKramer/optiland](https://github.com/HarrisonKramer/optiland), version 0.6.2.
Used as an independent ray tracer for the cross-check (`ricalc --optiland`). Two issues found in
its Zemax file import are written up in [optiland-0.6.2.md](optiland-0.6.2.md).

**LensHH-LT**, an optical design program by Javier Ruiz. Its relative illumination was checked
against this program, and the issues found were addressed in version 1.0.157.

## Benchmark lenses

**The Topogon.** R. Richter, "Anastigmatic objective for photography and projection," US Patent
2,031,792 (1936), Fig. 1 (drawing sheet 1), as used by Rimmer (Table 1). The prescription in
`tests/fixtures/lenses/Topogon_US2031792_Fig1.zmx`:

- r = 11.25, 16.55, 9.094, 7.35 | 7.35, 9.094, 16.55, 11.25 (concave sides toward the stop)
- d₁ = 4.44, l₁ = 0.02, d₂ = 0.5, l₂ = 12.98, d₃ = 0.5, l₃ = 0.02, d₄ = 4.44
- L₁ = L₄: n_D 1.6201, ν 60.4; L₂ = L₃: n_D 1.7172, ν 29.5
- f = 66.0 mm, f/6.3

The patent's OCR text reads 1.6111 for the crown; the drawing and the printed table say 1.6201,
and that value reproduces f = 66.0.

Kingslake's value for this lens, 35.0 % at 35°, is quoted by Rimmer from R. Kingslake, in
*Applied Optics and Optical Engineering*, Vol. II, p. 29 (Academic Press, 1965).

The other test lenses - ideal lenses with curved image or object surfaces and obscurations, a
paraboloidal mirror, Cooke triplets and a double Gauss - have exact answers computed from
Gardner's disk formula, or are checked by agreement between the forward and reverse methods and
between independent ray tracers.

## Further reading

Cited by the papers above:

- M. Reiss, *J. Opt. Soc. Am.* **35**, 283 (1945), and **38**, 980 (1948).
- G. Slyusarev, *J. Phys. USSR* **4**, 537 (1941) - the growth of the pupil off axis that lets a
  wide-angle lens exceed cos⁴.
- H. H. Hopkins, in *Applied Optics and Optical Engineering*, Vol. IX, p. 307 (Academic Press,
  1983), and his Reading lecture notes on canonical coordinates, cited by Rimmer.
