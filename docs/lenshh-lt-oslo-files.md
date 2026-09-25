# LensHH-LT's OSLO files: what was wrong, and what 1.0.158 fixed

A record, not a work order: every item below is fixed in LensHH-LT 1.0.158 (not yet released).
They came to light while writing and testing `ccl/relillum.ccl` in OSLO EDU 6.6 against this
repository's lenses, and each fix was checked against a lens file OSLO itself saved.

**How OSLO writes a lens file**, from two files OSLO 6.6 saved (2026-09-24):

| What | OSLO writes |
|---|---|
| Aperture, object at infinity | `EBR <entrance beam radius>` |
| Aperture, finite object | `NAO <object-space NA>` |
| Field, object at infinity | `ANG <maximum field angle>` |
| Field, finite object | `OBH <object height>` |
| Curved object surface | `RD` on surface 0 |
| Ideal lens | the perfect lens, `PFL <f>` (and `PFM <m>` at a finite conjugate) |
| Model glass | a `WV` line, then `GLA MOD <name> <index at each wavelength>` |
| Aperture that blocks rays | `AP CHK <r>` |
| Aperture that sizes the surface only | `AP <r>` |
| Aperture OSLO solves (paraxial heights) | no `AP` line |
| Primary wavelength | the first on the `WV` line; there is no keyword for it |
| Lens name | `LEN NEW "<name>" 100 <surfaces>`; a number standing as a word in the name is read as one of those arguments |

## The exporter

| Was | Found by | Now |
|---|---|---|
| Wavelengths in stored order: an F, d, C lens with d primary opened in OSLO as an F-line lens | The Kingslake chief ray at 14° in OSLO: 24.945679, the F-line value (d: 24.9495) | Primary first, the rest short to long |
| Any aperture not an EPD written `EBR 5` | The Topogon, F/6.3, arrived as F/6.6 | F-number converted to EBR; finite object as `NAO` |
| Finite object's field written as `ANG` | The curved-object lens | `OBH` |
| Object surface's radius dropped | The curved-object lens | `RD` on surface 0 |
| Ideal lens dropped (a flat surface with no power) | Every ideal-lens file | `PFL`, `PFM` |
| Model glass not written (the surface became air) | The Topogon's patent glasses | `GLA MOD` with LensHH-LT's own indices |
| Every semi-diameter `AP CHK`: an exported lens vignetted where its source did not | The vignetted Cooke triplet's automatic second surface | `AP CHK` only where it clips; `AP` otherwise; an automatic stop left to OSLO |
| A number in the name, e.g. "R 200", refused with "Maximum number of surfaces is 10" | The R 200 curved-image lens | Such words left out of `LEN NEW`; `SNO1` keeps the name |

## The importers

LensHH-LT's own reader, this repository's `ricalc` and AberrationCalculator now read all of the
above, take the first wavelength as primary (`ricalc` and AberrationCalculator used to take the
middle one, which only suited the old export), and read an `AP` that is not checked as an
automatic semi-diameter rather than a clipping one.

LensHH-LT's Zemax reader also took a stop on a mirror for a stock lens's glass surface and moved it
to a flat dummy at the mirror's vertex; a stop on a mirror now stays on it.

## What is not a defect

OSLO's perfect lens obeys the sine condition (h = f sin u'); Zemax's paraxial surface, and the
closed-form answers in this repository, the tangent law (h = f tan u'). At an object at infinity
the two cones differ: F/#eff 5.000 against 5.025 on the f/5 ideal lens, and relative illumination
by up to 1e-4. At a finite conjugate they agree.
