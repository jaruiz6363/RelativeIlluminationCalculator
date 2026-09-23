"""Build a lens inside Optiland from this program's own parsed prescription, and trace rays.

Why build it here rather than let Optiland read the .zmx: optiland 0.6.2 mis-resolves glass
names on import (F4 arrives as n = 1.620047 instead of 1.616592) and drops the surface
apertures, so a lens it imported is not the lens this program measured. Everything below is
handed over explicitly: radii, thicknesses, conics, indices at one wavelength, the stop, and
the clipping apertures. Then both programs trace the same lens.

The C# side (OptilandOptic.cs) calls `build` once per lens and `trace` once per batch of rays,
passing JSON and getting JSON back, so the Python.NET surface stays small.
"""

from __future__ import annotations

import json

import numpy as np
from optiland.materials import IdealMaterial
from optiland.optic import Optic
from optiland.physical_apertures import RadialAperture


def build(spec_json: str):
    """Build an Optic from a JSON prescription. Returns the Optic."""
    spec = json.loads(spec_json)
    optic = Optic()

    for i, s in enumerate(spec["surfaces"]):
        kwargs = {
            "index": i,
            "thickness": np.inf if s["thickness"] is None else s["thickness"],
            "is_stop": bool(s.get("is_stop", False)),
        }

        if s.get("surface_type") == "paraxial":
            # An ideal thin lens: Optiland calls it a paraxial surface and takes its focal
            # length as f. It has no radius or conic.
            kwargs["surface_type"] = "paraxial"
            kwargs["f"] = s["focal_length"]
        else:
            kwargs["radius"] = np.inf if s["radius"] is None else s["radius"]
            kwargs["conic"] = s.get("conic", 0.0)

        if s.get("mirror", False):
            # Optiland reflects when the material is the string "mirror"; the thickness after
            # such a surface is negative, as it is in the file, so the ray runs back along -z
            # and every vertex after it keeps its place on the axis.
            kwargs["material"] = "mirror"
        else:
            n = s.get("index_after", 1.0)
            kwargs["material"] = "air" if abs(n - 1.0) < 1e-12 else IdealMaterial(n=n)

        outer, inner = s.get("aperture_outer"), s.get("aperture_inner", 0.0) or 0.0
        if outer is not None or inner > 0.0:
            # RadialAperture blocks outside r_max and inside r_min: the clipping rules this
            # program decided, handed over unchanged.
            kwargs["aperture"] = RadialAperture(r_max=outer if outer is not None else 1e12,
                                                r_min=inner)
        optic.surfaces.add(**kwargs)

    optic.set_aperture(aperture_type="EPD", value=spec["epd"])
    optic.fields.set_type(spec["field_type"])
    optic.fields.add(y=0.0)
    if spec["max_field"] > 0:
        optic.fields.add(y=spec["max_field"])
    optic.wavelengths.add(value=spec["wavelength_um"], is_primary=True)

    # Iterative aiming makes a normalised pupil coordinate mean a point on the REAL stop, which
    # is what this program's own tracing does: the unit disk is then exactly the stop.
    optic.ray_tracer.set_aiming(spec.get("ray_aiming", "iterative"))
    return optic


def trace(optic, hy: float, px: list[float], py: list[float]) -> str:
    """Trace one field's rays at the given normalised pupil coordinates.

    Returns JSON with the image-surface intercept (x, y, z), the direction cosines (L, M, N)
    and Optiland's intensity, which is zero where an aperture blocked the ray.
    """
    px_arr = np.asarray(px, dtype=float)
    py_arr = np.asarray(py, dtype=float)
    rays = optic.ray_tracer.trace_generic(
        Hx=0.0, Hy=float(hy), Px=px_arr, Py=py_arr,
        wavelength=optic.primary_wavelength,
    )

    def col(name):
        return np.asarray(getattr(rays, name), dtype=float).ravel().tolist()

    return json.dumps({
        "x": col("x"), "y": col("y"), "z": col("z"),
        "L": col("L"), "M": col("M"), "N": col("N"),
        "i": col("i"),
    })


def describe(optic) -> str:
    """First-order data, for checking that the lens arrived intact."""
    return json.dumps({
        "efl": float(optic.paraxial.f2()),
        "epd": float(optic.paraxial.EPD()),
        "surfaces": len(optic.surfaces.surfaces),
    })
