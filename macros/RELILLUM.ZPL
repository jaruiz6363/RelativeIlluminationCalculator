! RELILLUM - relative illumination versus field, from real rays, by Rimmer's method,
! with Siew's effective F/#.
!
! Written by Javier Ruiz with Claude Code, September 2026.
! Companion to the ricalc program in C:\GIT\RelativeIlluminationCalculator.
!
! WHAT IT COMPUTES
!
!   The illuminance at an image point is proportional to the projected solid angle of
!   the cone of light arriving there: the AREA of the exit pupil in image-space direction
!   cosines (Rimmer, Proc. SPIE 655, 99, 1986). Relative illumination is that area at a
!   field point over the same area on axis.
!
!   The area is measured by the same algorithm as ricalc (PupilIntegrator): the normalised
!   pupil is covered by a square grid of NBASE x NBASE cells. A cell whose four corners and
!   centre all pass is taken whole, as four triangles on the mapped corners and centre. A
!   cell that is partly blocked, OR NEXT TO ONE, is split in four, NDEPTH times; at the
!   finest level the boundary is found on each cut edge by NBIS bisections, and the passing
!   part of the cell is a polygon on those crossings. A pupil with a hole, several pieces or
!   pointed tips is measured as it is, and the curve has no steps as the field changes.
!
!   Every ray is mapped to image-space direction cosines about the image-surface normal,
!   after being referred to the image point Q' through the reference sphere centred on Q'
!   and passing through the axial exit pupil: its direction is taken from where it crosses
!   that sphere TO Q'. This is Rimmer's Eq. 3, taken exactly rather than to first order. An
!   aberrated ray does not pass through Q', and its own direction would measure the cone
!   somewhere else (two points high at 35 degrees on Rimmer's Topogon); ricalc --no-rimmer
!   shows that uncorrected value, for anyone who wants it.
!
!   Compare it against OpticStudio's own Analysis > Image Quality > Relative Illumination.
!
! WHICH RAYS COUNT
!
!   A ray counts if it traces without error (RAYE), is not vignetted by a surface aperture
!   (RAYV), AND passes inside the real stop. The stop test is made here because
!   OpticStudio does not clip at the stop by itself. The stop radius is the height at the
!   stop of the real axial marginal ray, so the axial beam is exactly the one the system
!   aperture describes.
!
!   RAYTRACE REFUSES PUPIL COORDINATES OUTSIDE THE UNIT CIRCLE ("Invalid pupil range"), so
!   a point outside it is counted as blocked without being traced. That decides what this
!   macro can see:
!
!     Real ray aiming ON   the normalised pupil IS the real stop, so every ray that gets
!                          through the stop lies inside the unit circle. Nothing is lost.
!     Ray aiming OFF       the normalised pupil is the PARAXIAL entrance pupil. An off-axis
!                          pupil that grows beyond it - pupil coma, the Slyusarev effect -
!                          cannot be traced. Rows where the pupil is cut by the unit circle
!                          while still inside the stop are marked TRUNCATED: their RI is a
!                          lower bound.
!
!   Turn on real ray aiming for a complete answer.
!
!   The stop surface is found as the surface where the paraxial chief ray crosses the axis.
!   The chief ray itself is aimed, by Newton iteration, at the centre of the real stop. It
!   only fixes the image point Q'; it may itself be blocked, by an obscuration say.
!
! LIMITS
!
!   - There is no backward trace from the image point in ZPL, so the exact reverse method
!     of ricalc has no counterpart here.
!   - The image surface normal is taken from its curvature and conic; aspheric terms on
!     the IMAGE surface are ignored.
!   - Monochromatic, at the primary wavelength. Rotationally symmetric, sequential.
!   - RI is normalised to the BRIGHTEST field measured, as OpticStudio's analysis does, so
!     it never exceeds 1. That field is known only at the end, so the table is printed
!     once every field is done; a line per field shows progress meanwhile.
!   - OpticStudio caps the size of a DECLAREd array: a million elements is refused with
!     "Invalid array declaration". The traced rays are therefore kept in a hash table of
!     NHASH slots, not one slot per lattice node, and the largest array is NHASH long.
!   - To compare with OpticStudio, run its Relative Illumination analysis at a high ray
!     density (the default of 10 is about 78 rays), once with ray aiming on and once off.
!
! ZPL TRAPS OBSERVED (see AberrationCalculator macros/README.md): a block IF has no THEN;
! variable names are case-insensitive and may not be function names; FOR does not skip
! an empty range. Every loop below runs over a fixed, positive range, or is guarded. And a
! comment must be a line of its own: "x = 1  ! note" is read as code, and fails on the
! first word of the note. ZPL has no recursion, so the grid is worked one level at a time
! from a list of cells, as ricalc does it.
! ---------------------------------------------------------------------------------

! ---- settings ------------------------------------------------------------------
! Field steps from the axis to full field.
nfd = 10
! Coarse cells along each side of the pupil. ricalc uses 32.
nbase = 16
! Times a cell at the boundary may be split in four. ricalc uses 4.
ndepth = 3
! Bisections locating the boundary on a finest-level edge. ricalc uses 12.
nbis = 10
! These defaults trace about 10000 rays a field, against ricalc's 45000, and agree with
! ricalc's settings to 6E-5 on a vignetted Cooke triplet and 6E-4 on a lens with a central
! obscuration. OpticStudio aims every ray iteratively, so rays are what the time goes on.
! Longest list of cells one level may hold (a pupil with a hole needs about 17000).
maxcel = 40000
! Slots for traced rays (a prime; a field keeps about 60000 at most).
nhash = 131071
! ---------------------------------------------------------------------------------

eps = 1E-12
pival = 3.14159265358979
paraxmode = PMOD()
wl = PWAV()
ns = NSUR()
nsm = ns - 1

GETSYSTEMDATA 1
sysefl = VEC1(7)
rayaim = VEC1(19)

PRINT "RELILLUM - relative illumination from real rays"
PRINT "RI by Rimmer (Proc. SPIE 655, 1986); effective F/# by Siew (Proc. SPIE 5867, 2005)"
PRINT " "

IF (nsm < 1)
   PRINT "RELILLUM needs at least one optical surface."
   GOTO 900
ENDIF

fldmax = MAXF()
IF (ABSO(fldmax) < eps)
   PRINT "The maximum field is zero. Define at least one off-axis field."
   GOTO 900
ENDIF

fldtyp = FTYP()
fdesc$ = "deg"
IF (fldtyp > 0.5) THEN fdesc$ = "obj ht"
IF (fldtyp > 1.5) THEN fdesc$ = "par img ht"
IF (fldtyp > 2.5) THEN fdesc$ = "real img ht"

! ---- the grid -------------------------------------------------------------------
! Lattice steps per coarse cell: two per finest cell, so every cell's centre is a node.
nfine = 2
IF (ndepth > 0)
   FOR i = 1, ndepth, 1
      nfine = nfine * 2
   NEXT
ENDIF
nlat = nbase * nfine
nlat1 = nlat + 1
lstep = 2 / nlat
! How close the last passing ray of a bisection lies to the edge it found, in pupil units:
! a finest cell is two lattice steps across. Ten times that is the margin by which a ray
! must fall short of the stop to call a cut at the unit circle a truncation.
bres = 2 * lstep
FOR i = 1, nbis, 1
   bres = bres / 2
NEXT
tmarg = 10 * bres

! Traced rays, in a hash table keyed on the lattice node, with linear probing. hst holds
! this field's stamp, 2*(kf + 1), plus 1 if the ray passed; a slot from an earlier field is
! below the stamp and counts as empty, so nothing needs clearing between fields.
DECLARE hkey, DOUBLE, 1, nhash
DECLARE hst, DOUBLE, 1, nhash
DECLARE hu, DOUBLE, 1, nhash
DECLARE hv, DOUBLE, 1, nhash
! Cells of the current level and of the next.
DECLARE cli, DOUBLE, 1, maxcel
DECLARE clj, DOUBLE, 1, maxcel
DECLARE nxi, DOUBLE, 1, maxcel
DECLARE nxj, DOUBLE, 1, maxcel
! One cell's corners (1-4, counter-clockwise from the lower left) and edge crossings.
DECLARE bi, DOUBLE, 1, 4
DECLARE bj, DOUBLE, 1, 4
DECLARE bp, DOUBLE, 1, 4
DECLARE bu, DOUBLE, 1, 4
DECLARE bv, DOUBLE, 1, 4
DECLARE xh, DOUBLE, 1, 4
DECLARE xu, DOUBLE, 1, 4
DECLARE xv, DOUBLE, 1, 4
! A polygon of at most eight vertices.
DECLARE rgu, DOUBLE, 1, 8
DECLARE rgv, DOUBLE, 1, 8
! Every measured row, kept for the table at the end: RI is relative to the BRIGHTEST field,
! as OpticStudio reports it, and that is known only once every field is done.
DECLARE ofd, DOUBLE, 1, nfd + 1
DECLARE ohy, DOUBLE, 1, nfd + 1
DECLARE oht, DOUBLE, 1, nfd + 1
DECLARE ocra, DOUBLE, 1, nfd + 1
DECLARE oac, DOUBLE, 1, nfd + 1
DECLARE ofnt, DOUBLE, 1, nfd + 1
DECLARE ofns, DOUBLE, 1, nfd + 1
DECLARE otr, DOUBLE, 1, nfd + 1

! ---- the stop, and the exit pupil, from the paraxial chief ray --------------------
PARAXIAL ON
RAYTRACE 0, 1, 0, 0, wl
IF (RAYE() != 0)
   PRINT "The paraxial chief ray will not trace."
   GOTO 900
ENDIF

ks = 1
bestabs = ABSO(RAYY(1))
FOR i = 1, nsm, 1
   IF (ABSO(RAYY(i)) < bestabs)
      bestabs = ABSO(RAYY(i))
      ks = i
   ENDIF
NEXT

tele = 0
zxp = 0
uimg = RAYM(ns) / RAYN(ns)
IF (ABSO(uimg) < 1E-10)
   tele = 1
ELSE
   zxp = -RAYY(ns) / uimg
ENDIF
PARAXIAL OFF

! ---- the stop radius, from the real axial marginal ray ----------------------------
RAYTRACE 0, 0, 0, 1, wl
IF (RAYE() != 0)
   PRINT "The real axial marginal ray will not trace. Is the aperture set?"
   GOTO 900
ENDIF
srad = SQRT(RAYX(ks) * RAYX(ks) + RAYY(ks) * RAYY(ks))
srad2 = srad * srad * (1 + 1E-9)

cimg = CURV(ns)
! Image-space index, for Siew's effective F/#. ABSO: after a mirror OpticStudio may sign it.
nimg = ABSO(INDX(nsm))
kimg = CONI(ns)

FORMAT 12.6
PRINT "EFL                    ", sysefl
PRINT "Wavelength (um)        ", WAVL(wl)
FORMAT 5.0 INT
PRINT "Stop surface           ", ks
FORMAT 12.6
PRINT "Stop radius (real)     ", srad
IF (tele == 1)
   PRINT "Exit pupil             at infinity (telecentric): the correction vanishes"
ELSE
   PRINT "Exit pupil from image  ", zxp
ENDIF
PRINT "Max field              ", fldmax, "  ", fdesc$
IF (ABSO(cimg) > eps) THEN PRINT "Image surface radius   ", 1 / cimg
IF (rayaim > 0.5) THEN PRINT "Ray aiming             on"
IF (rayaim < 0.5) THEN PRINT "Ray aiming             OFF"
FORMAT 5.0 INT
PRINT "Pupil grid             ", nbase, " cells, split ", ndepth, " times, ", nbis, " bisections"
PRINT " "
IF (rayaim < 0.5)
   PRINT "With ray aiming off, OpticStudio can trace only rays inside the PARAXIAL entrance"
   PRINT "pupil. An off-axis pupil that grows beyond it is cut short, and those rows are"
   PRINT "marked TRUNCATED below: their RI is a lower bound. Turn on real ray aiming"
   PRINT "(System Explorer > Ray Aiming > Real) for a complete answer."
   PRINT "OpticStudio's own Relative Illumination analysis is cut short the same way: on the"
   PRINT "Cooke 40 degree sample it reads 0.779 at 20 degrees with aiming off, 0.876 with it on."
   PRINT " "
ENDIF

IF (bestabs > 1E-6 * (1 + srad))
   PRINT "WARNING: the paraxial chief ray does not cross the axis at any surface, so the"
   PRINT "stop surface is uncertain. Surface ", ks, " is used."
   PRINT " "
ENDIF

PRINT "Measuring each field (the table follows once all are done):"
! Elapsed time and rays traced, per field and in total, to compare with ricalc and Optiland.
TIMER
nray = 0

novfl = 0
hfull = 0
nout = 0

! Everything passtest reads, given a value before its first use.
qqx = 0
qqy = 0
qqz = 0
eax = 1
eay = 0
eaz = 0
ebx = 0
eby = 1
ebz = 0
rrx = 0
rry = 0
rrz = zxp
rref2 = zxp * zxp
tuc = 0
tvc = 0
stx = 0
sty = 0

FOR kf = 0, nfd, 1
   fhy = kf / nfd
   fgo = 1

   GOSUB aimchief
   IF (aimok == 0)
      FORMAT 8.3
      PRINT "   ", fhy * fldmax,
      PRINT "   the chief ray cannot be aimed at the stop centre; field left out"
      fgo = 0
   ENDIF

   IF (fgo == 1)
   ! The chief ray: Q', and its direction of arrival. It is traced for its geometry only;
   ! whether an aperture blocks it does not matter.
   RAYTRACE 0, fhy, 0, pyc, wl
   qqx = RAYX(ns)
   qqy = RAYY(ns)
   qqz = RAYZ(ns)
   cdl = RAYL(ns)
   cdm = RAYM(ns)
   cdn = RAYN(ns)

   ! The frame about the image-surface normal at Q'.
   nnx = -cimg * qqx
   nny = -cimg * qqy
   nnz = 1 - cimg * (1 + kimg) * qqz
   nlen = SQRT(nnx * nnx + nny * nny + nnz * nnz)
   nnx = nnx / nlen
   nny = nny / nlen
   nnz = nnz / nlen
   IF (nnx * cdl + nny * cdm + nnz * cdn < 0)
      nnx = -nnx
      nny = -nny
      nnz = -nnz
   ENDIF
   ! e1 = x - (x.N) N, normalised; e2 = N x e1
   eax = 1 - nnx * nnx
   eay = -nnx * nny
   eaz = -nnx * nnz
   elen = SQRT(eax * eax + eay * eay + eaz * eaz)
   eax = eax / elen
   eay = eay / elen
   eaz = eaz / elen
   ebx = nny * eaz - nnz * eay
   eby = nnz * eax - nnx * eaz
   ebz = nnx * eay - nny * eax

   ! Reference sphere: centre Q', through E' = (0, 0, zxp) in the image-surface frame.
   rrx = -qqx
   rry = -qqy
   rrz = zxp - qqz
   rref2 = rrx * rrx + rry * rry + rrz * rrz

   ccos = nnx * cdl + nny * cdm + nnz * cdn
   IF (ccos > 1) THEN ccos = 1
   cra = 0
   IF (ccos < 1) THEN cra = ATAN(SQRT(1 - ccos * ccos) / ccos) * 180 / pival

   ! ---- the pupil, on a grid refined toward its boundary ------------------------------
   fstamp = 2 * kf + 2
   arc = 0
   umin = 1E9
   umax = -1E9
   vmin = 1E9
   vmax = -1E9
   ntrunc = 0

   ncur = 0
   FOR cj0 = 1, nbase, 1
      FOR ci0 = 1, nbase, 1
         ncur = ncur + 1
         cli(ncur) = (ci0 - 1) * nfine
         clj(ncur) = (cj0 - 1) * nfine
      NEXT
   NEXT

   csz = nfine
   FOR dep = 0, ndepth, 1
      chh = csz / 2
      cansplit = 0
      IF (dep < ndepth) THEN cansplit = 1
      nnext = 0
      IF (ncur > 0)
         FOR kc = 1, ncur, 1
            oci = cli(kc)
            ocj = clj(kc)
            ci = oci
            cj = ocj
            GOSUB cellcut
            owncut = ccut
            ownpass = cpass0

            ! A cell is split when it, or any cell of its size beside it, is partly blocked:
            ! its own five rays cannot see a sliver of the pupil entering between them.
            dosplit = 0
            IF (cansplit == 1)
               IF (owncut == 1) THEN dosplit = 1
               FOR ndj = -1, 1, 1
                  FOR ndi = -1, 1, 1
                     IF (dosplit == 0)
                        ci = oci + ndi * csz
                        cj = ocj + ndj * csz
                        nbok = 1
                        IF ((ndi == 0) & (ndj == 0)) THEN nbok = 0
                        IF (ci < 0) THEN nbok = 0
                        IF (cj < 0) THEN nbok = 0
                        IF (ci + csz > nlat) THEN nbok = 0
                        IF (cj + csz > nlat) THEN nbok = 0
                        IF (nbok == 1)
                           GOSUB cellcut
                           IF (ccut == 1) THEN dosplit = 1
                        ENDIF
                     ENDIF
                  NEXT
               NEXT
            ENDIF

            ci = oci
            cj = ocj
            IF (dosplit == 1)
               IF (nnext + 4 > maxcel)
                  novfl = 1
               ELSE
                  kn = nnext + 1
                  nxi(kn) = ci
                  nxj(kn) = cj
                  kn = nnext + 2
                  nxi(kn) = ci + chh
                  nxj(kn) = cj
                  kn = nnext + 3
                  nxi(kn) = ci + chh
                  nxj(kn) = cj + chh
                  kn = nnext + 4
                  nxi(kn) = ci
                  nxj(kn) = cj + chh
                  nnext = nnext + 4
               ENDIF
            ELSE
               IF (owncut == 1)
                  GOSUB bcell
               ELSE
                  IF (ownpass == 1)
                     GOSUB wholecell
                  ENDIF
               ENDIF
            ENDIF
         NEXT
      ENDIF

      ncur = nnext
      IF (ncur > 0)
         FOR kc = 1, ncur, 1
            cli(kc) = nxi(kc)
            clj(kc) = nxj(kc)
         NEXT
      ENDIF
      csz = chh
   NEXT

   fnt = 0
   fns = 0
   IF (vmax > vmin) THEN fnt = 1 / (vmax - vmin)
   IF (umax > umin) THEN fns = 1 / (umax - umin)

   nout = nout + 1
   ofd(nout) = fhy * fldmax
   ohy(nout) = fhy
   oht(nout) = qqy
   ocra(nout) = cra
   oac(nout) = arc
   ofnt(nout) = fnt
   ofns(nout) = fns
   otr(nout) = ntrunc
   FORMAT 8.3
   PRINT "   ", fhy * fldmax, "  ", fdesc$, "  done at",
   FORMAT 8.1
   PRINT ETIM(), " s,",
   FORMAT 8.0 INT
   PRINT nray, " rays so far"

   ENDIF
NEXT

! ---- the table: RI relative to the brightest field measured, never above 1 -----------
! Each column is divided by its own brightest value, as OpticStudio's analysis does.
amax = 0
kmax = 1
IF (nout > 0)
   FOR ko = 1, nout, 1
      IF (oac(ko) > amax)
         amax = oac(ko)
         kmax = ko
      ENDIF
   NEXT
ENDIF
IF (amax <= 0) THEN nout = 0

IF (nout > 0)
   PRINT " "
   PRINT "   Field      hy     Img ht     CRA          RI    F#T    F#S  F/#eff"
   FOR ko = 1, nout, 1
      ric = oac(ko) / amax
      FORMAT 8.3
      PRINT ofd(ko),
      FORMAT 8.3
      PRINT ohy(ko),
      FORMAT 11.4
      PRINT oht(ko),
      FORMAT 8.2
      PRINT ocra(ko),
      FORMAT 12.4
      PRINT ric,
      FORMAT 7.2
      PRINT ofnt(ko),
      FORMAT 7.2
      PRINT ofns(ko),
      FORMAT 8.3
      PRINT SQRT(pival / (4 * nimg * nimg * oac(ko)))
      ! With real aiming the unit circle is the stop edge, so a pupil cut there is the stop
      ! doing its job. With aiming off it is the paraxial pupil, and a ray cut there while
      ! still inside the stop means the real beam extends further than RAYTRACE can follow.
      IF ((otr(ko) > 0) & (rayaim < 0.5) & (ohy(ko) > 0))
         FORMAT 6.0 INT
         PRINT "         ^ TRUNCATED: the pupil is cut by the paraxial pupil edge on ", otr(ko), " cell edges"
      ENDIF
   NEXT
   IF (ohy(kmax) > 0)
      PRINT " "
      FORMAT 8.3
      PRINT "The brightest field is ", ofd(kmax), " ", fdesc$, ", not the axis; RI is relative to it."
   ENDIF
ENDIF

IF (hfull == 1)
   PRINT " "
   PRINT "WARNING: the table of traced rays (NHASH slots) filled up, and the results are wrong."
   PRINT "Raise NHASH, or lower NBASE or NDEPTH, and run again."
ENDIF

IF (novfl == 1)
   PRINT " "
   PRINT "WARNING: a level of the grid needed more than MAXCEL cells, and some were dropped."
   PRINT "Raise MAXCEL, or lower NBASE or NDEPTH, and run again."
ENDIF

PRINT " "
PRINT "RI         illuminance relative to the brightest field; rays referred to the image"
PRINT "           point through the reference sphere (Rimmer Eq. 3)"
PRINT "CRA        chief ray angle to the image-surface normal, degrees"
PRINT "F#T, F#S   Rimmer's effective F/numbers 1/(m2-m1), 1/(l2-l1)"
PRINT "F/#eff     Siew's effective F/#, sqrt(pi / (4 n'^2 PSA)), as OpticStudio reports it"
PRINT "           (OpticStudio also folds in transmission; this macro does not)"

! ---- the same rows, tab-delimited, to paste into a plotting program ------------------
IF (nout > 0)
   tab$ = $TAB()
   PRINT " "
   PRINT "Tab-delimited, for plotting:"
   PRINT " "
   PRINT "Field (", fdesc$, ")", tab$, "RI", tab$, "F/#eff"
   FOR ko = 1, nout, 1
      FORMAT 10.4
      PRINT ofd(ko), tab$,
      FORMAT 10.6
      PRINT oac(ko) / amax, tab$,
      FORMAT 10.4
      PRINT SQRT(pival / (4 * nimg * nimg * oac(ko)))
   NEXT
ENDIF

IF (nout > 0)
   PRINT " "
   FORMAT 8.1
   PRINT "Total time ", ETIM(), " s for ",
   FORMAT 8.0 INT
   PRINT nray, " rays"
ENDIF

LABEL 900
IF (ABSO(paraxmode) > eps) THEN PARAXIAL ON
IF (ABSO(paraxmode) < eps) THEN PARAXIAL OFF

END

! =================================================================================
! SUBROUTINES
! =================================================================================

! aimchief - pyc, the normalised pupil y of the real ray through the stop centre at
! field fhy. A centred system keeps it in the meridian, so one coordinate suffices.
SUB aimchief
   pyc = 0
   aimok = 0
   adone = 0
   FOR ja = 1, 40, 1
      IF (adone == 0)
         RAYTRACE 0, fhy, 0, pyc, wl
         IF (RAYE() != 0)
            adone = 1
         ELSE
            fa = RAYY(ks)
            IF (ABSO(fa) < 1E-12 * (1 + srad))
               aimok = 1
               adone = 1
            ELSE
               ! The probe steps TOWARD the axis, so it never leaves the unit circle.
               hprobe = 1E-6
               IF (pyc > 0) THEN hprobe = -1E-6
               RAYTRACE 0, fhy, 0, pyc + hprobe, wl
               IF (RAYE() != 0)
                  adone = 1
               ELSE
                  fslope = (RAYY(ks) - fa) / hprobe
                  IF (ABSO(fslope) < eps)
                     adone = 1
                  ELSE
                     astep = -fa / fslope
                     IF (astep > 0.5) THEN astep = 0.5
                     IF (astep < -0.5) THEN astep = -0.5
                     pyc = pyc + astep
                     ! RAYTRACE refuses |py| > 1: a chief ray out there cannot be traced.
                     IF (pyc > 0.999999) THEN pyc = 0.999999
                     IF (pyc < -0.999999) THEN pyc = -0.999999
                  ENDIF
               ENDIF
            ENDIF
         ENDIF
      ENDIF
   NEXT
   IF (adone == 0)
      RAYTRACE 0, fhy, 0, pyc, wl
      IF (RAYE() == 0)
         IF (ABSO(RAYY(ks)) < 1E-7 * (1 + srad)) THEN aimok = 1
      ENDIF
   ENDIF
RETURN

! getnode - the ray at lattice node (gi, gj), traced once per field and kept. Returns
! gp = 1 if it passes, and then its mapped direction gu, gv.
SUB getnode
   gkey = gj * nlat1 + gi + 1
   gidx = gkey - INTE(gkey / nhash) * nhash + 1
   gprobe = 0
   ! Probe from the key's home slot until the key, or a slot empty for this field.
   LABEL 710
   gst = hst(gidx)
   gk = hkey(gidx)
   gmore = 0
   IF (gst >= fstamp) THEN gmore = 1
   IF (gk == gkey) THEN gmore = 0
   gprobe = gprobe + 1
   IF (gprobe >= nhash) THEN hfull = 1
   IF (gprobe >= nhash) THEN gmore = 0
   IF (gmore == 1) THEN gidx = gidx + 1
   IF (gidx > nhash) THEN gidx = 1
   IF (gmore == 1) THEN GOTO 710

   IF ((gst >= fstamp) & (gk == gkey))
      gp = gst - fstamp
   ELSE
      tpx = -1 + gi * lstep
      tpy = -1 + gj * lstep
      GOSUB passtest
      gp = tok
      hkey(gidx) = gkey
      hst(gidx) = fstamp + tok
      IF (tok == 1)
         hu(gidx) = tuc
         hv(gidx) = tvc
      ENDIF
   ENDIF
   gu = 0
   gv = 0
   IF (gp == 1)
      gu = hu(gidx)
      gv = hv(gidx)
   ENDIF
RETURN

! cellcut - ccut = 1 if the corners and centre of cell (ci, cj), side csz, disagree: the
! pupil boundary crosses it. cpass0 is whether its first corner passes.
SUB cellcut
   ccut = 0
   gi = ci
   gj = cj
   GOSUB getnode
   cpass0 = gp
   gi = ci + csz
   gj = cj
   GOSUB getnode
   IF (gp != cpass0) THEN ccut = 1
   gi = ci + csz
   gj = cj + csz
   GOSUB getnode
   IF (gp != cpass0) THEN ccut = 1
   gi = ci
   gj = cj + csz
   GOSUB getnode
   IF (gp != cpass0) THEN ccut = 1
   gi = ci + csz / 2
   gj = cj + csz / 2
   GOSUB getnode
   IF (gp != cpass0) THEN ccut = 1
RETURN

! loadcell - the corners of cell (ci, cj), side csz, into bi..bv(1-4), counter-clockwise
! from the lower left, and its centre into ccp, ccu, ccv.
SUB loadcell
   bi(1) = ci
   bj(1) = cj
   bi(2) = ci + csz
   bj(2) = cj
   bi(3) = ci + csz
   bj(3) = cj + csz
   bi(4) = ci
   bj(4) = cj + csz
   FOR kk = 1, 4, 1
      gi = bi(kk)
      gj = bj(kk)
      GOSUB getnode
      bp(kk) = gp
      bu(kk) = gu
      bv(kk) = gv
   NEXT
   gi = ci + csz / 2
   gj = cj + csz / 2
   GOSUB getnode
   ccp = gp
   ccu = gu
   ccv = gv
RETURN

! wholecell - a cell that passes entirely: four triangles on the corners and the centre.
SUB wholecell
   GOSUB loadcell
   FOR kk = 1, 4, 1
      km = kk + 1
      IF (km > 4) THEN km = 1
      tri = (bu(km) - bu(kk)) * (ccv - bv(kk)) - (ccu - bu(kk)) * (bv(km) - bv(kk))
      arc = arc + ABSO(tri) / 2
   NEXT
RETURN

! bcell - a finest-level cell the boundary crosses: each cut edge is bisected, and the
! passing part of the cell becomes a polygon on the corners that pass and the crossings.
SUB bcell
   GOSUB loadcell
   FOR ke = 1, 4, 1
      km = ke + 1
      IF (km > 4) THEN km = 1
      xh(ke) = 0
      IF (bp(ke) != bp(km))
         ! The passing end, and the failing end, of edge ke.
         kpe = ke
         kfe = km
         IF (bp(ke) == 0)
            kpe = km
            kfe = ke
         ENDIF
         apx = -1 + bi(kpe) * lstep
         apy = -1 + bj(kpe) * lstep
         afx = -1 + bi(kfe) * lstep
         afy = -1 + bj(kfe) * lstep
         ! Until a bisection ray passes, the passing corner is the best there is.
         xu(ke) = bu(kpe)
         xv(ke) = bv(kpe)
         lfcirc = 0
         IF (afx * afx + afy * afy >= 1) THEN lfcirc = 1
         bhave = 0
         bsx = 0
         bsy = 0
         FOR kb = 1, nbis, 1
            tpx = 0.5 * (apx + afx)
            tpy = 0.5 * (apy + afy)
            GOSUB passtest
            IF (tok == 1)
               apx = tpx
               apy = tpy
               xu(ke) = tuc
               xv(ke) = tvc
               bhave = 1
               bsx = stx
               bsy = sty
            ELSE
               afx = tpx
               afy = tpy
               lfcirc = tcirc
            ENDIF
         NEXT
         xh(ke) = 1
         ! Cut by the unit circle while still inside the stop: see TRUNCATED.
         IF ((lfcirc == 1) & (bhave == 1))
            IF (bsx * bsx + bsy * bsy < srad * srad * (1 - tmarg) * (1 - tmarg)) THEN ntrunc = ntrunc + 1
         ENDIF
      ENDIF
   NEXT

   nrg = 0
   issad = 0
   IF ((bp(1) == bp(3)) & (bp(2) == bp(4)) & (bp(1) != bp(2))) THEN issad = 1
   IF ((issad == 1) & (ccp == 0))
      ! Two separate corner pieces, disconnected through a blocked centre: a triangle each.
      FOR ke = 1, 4, 1
         IF (bp(ke) == 1)
            kpv = ke - 1
            IF (kpv < 1) THEN kpv = 4
            rgu(1) = xu(kpv)
            rgv(1) = xv(kpv)
            rgu(2) = bu(ke)
            rgv(2) = bv(ke)
            rgu(3) = xu(ke)
            rgv(3) = xv(ke)
            nrg = 3
            GOSUB polyarea
         ENDIF
      NEXT
   ELSE
      FOR ke = 1, 4, 1
         IF (bp(ke) == 1)
            nrg = nrg + 1
            rgu(nrg) = bu(ke)
            rgv(nrg) = bv(ke)
         ENDIF
         IF (xh(ke) == 1)
            nrg = nrg + 1
            rgu(nrg) = xu(ke)
            rgv(nrg) = xv(ke)
         ENDIF
      NEXT
      ! Fewer than three vertices: only the centre passes, an island smaller than a finest
      ! cell, below the resolution asked for. It is dropped.
      IF (nrg >= 3)
         GOSUB polyarea
      ENDIF
   ENDIF
RETURN

! polyarea - adds the shoelace area of the polygon rgu, rgv(1..nrg).
SUB polyarea
   pac = 0
   FOR kp = 1, nrg, 1
      kq = kp + 1
      IF (kq > nrg) THEN kq = 1
      pac = pac + rgu(kp) * rgv(kq) - rgu(kq) * rgv(kp)
   NEXT
   arc = arc + ABSO(pac) / 2
RETURN

! passtest - traces the ray at pupil (tpx, tpy), field fhy. tok = 1 if it lies inside the
! unit circle, traces, is not vignetted, and passes inside the stop. tcirc = 1 if it was
! refused for lying outside the unit circle. When it passes, (tuc, tvc) are its direction
! cosines referred to Q', about the image-surface normal, and (stx, sty) where it crossed
! the stop.
SUB passtest
   tok = 0
   tcirc = 0
   IF (tpx * tpx + tpy * tpy >= 1)
      tcirc = 1
   ELSE
   nray = nray + 1
   RAYTRACE 0, fhy, tpx, tpy, wl
   IF (RAYE() == 0)
      IF (RAYV() == 0)
         stx = RAYX(ks)
         sty = RAYY(ks)
         IF (stx * stx + sty * sty <= srad2)
            tok = 1
            ppx = RAYX(ns)
            ppy = RAYY(ns)
            ppz = RAYZ(ns)
            rdl = RAYL(ns)
            rdm = RAYM(ns)
            rdn = RAYN(ns)
            kkx = rdl
            kky = rdm
            kkz = rdn
            IF (tele == 0)
               wwx = ppx - qqx
               wwy = ppy - qqy
               wwz = ppz - qqz
               bdot = wwx * rdl + wwy * rdm + wwz * rdn
               cq = wwx * wwx + wwy * wwy + wwz * wwz - rref2
               dsc = bdot * bdot - cq
               IF (dsc >= 0)
                  sdq = SQRT(dsc)
                  tta = -bdot - sdq
                  ttb = -bdot + sdq
                  xax = ppx + tta * rdl - qqx
                  xay = ppy + tta * rdm - qqy
                  xaz = ppz + tta * rdn - qqz
                  xbx = ppx + ttb * rdl - qqx
                  xby = ppy + ttb * rdm - qqy
                  xbz = ppz + ttb * rdn - qqz
                  pja = xax * rrx + xay * rry + xaz * rrz
                  pjb = xbx * rrx + xby * rry + xbz * rrz
                  IF (pja >= pjb)
                     kkx = -xax
                     kky = -xay
                     kkz = -xaz
                  ELSE
                     kkx = -xbx
                     kky = -xby
                     kkz = -xbz
                  ENDIF
                  klen = SQRT(kkx * kkx + kky * kky + kkz * kkz)
                  kkx = kkx / klen
                  kky = kky / klen
                  kkz = kkz / klen
                  IF (kkx * rdl + kky * rdm + kkz * rdn <= 0)
                     kkx = rdl
                     kky = rdm
                     kkz = rdn
                  ENDIF
               ENDIF
            ENDIF
            tuc = kkx * eax + kky * eay + kkz * eaz
            tvc = kkx * ebx + kky * eby + kkz * ebz
            IF (tuc < umin) THEN umin = tuc
            IF (tuc > umax) THEN umax = tuc
            IF (tvc < vmin) THEN vmin = tvc
            IF (tvc > vmax) THEN vmax = tvc
         ENDIF
      ENDIF
   ENDIF
   ENDIF
RETURN
