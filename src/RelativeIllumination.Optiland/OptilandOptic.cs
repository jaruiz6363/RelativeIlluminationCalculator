using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Python.Runtime;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Models;
using RelativeIllumination.Core.RayTrace;

namespace RelativeIllumination.Optiland;

/// <summary>One batch of rays as Optiland returned them, at the image surface.</summary>
public sealed class RayBatch
{
    public double[] X = Array.Empty<double>();
    public double[] Y = Array.Empty<double>();
    public double[] Z = Array.Empty<double>();
    public double[] L = Array.Empty<double>();
    public double[] M = Array.Empty<double>();
    public double[] N = Array.Empty<double>();

    /// <summary>Optiland's ray intensity: zero where an aperture blocked the ray.</summary>
    public double[] Intensity = Array.Empty<double>();

    public int Count => Y.Length;

    public bool Passed(int k) => Intensity[k] > 0.0 && !double.IsNaN(Y[k]);
}

/// <summary>
/// The same lens inside Optiland, built from the prescription this program parsed rather than
/// from Optiland's own file import.
///
/// <para>That is deliberate. Optiland 0.6.2 mis-resolves glasses when it reads a .zmx - F4
/// arrives as n = 1.620047 instead of 1.616592, moving a Cooke triplet's focal length by 1 % -
/// and drops the surface apertures entirely, so a lens it imported is not the lens this program
/// measured. Handing the prescription over explicitly makes both programs trace the same lens,
/// and any difference is then in the tracing and the illumination calculation. See
/// docs/optiland-0.6.2.md.</para>
///
/// <para>The Optiland calls live in <c>python/ricalc_optiland.py</c>; this class passes JSON to
/// it and reads JSON back, so only a few Python.NET calls are needed.</para>
/// </summary>
public sealed class OptilandOptic
{
    private readonly PyObject _module;
    private readonly PyObject _optic;

    public double MaxField { get; }

    /// <summary>First-order data from Optiland's own paraxial trace, for a sanity check.</summary>
    public (double Efl, double Epd, int Surfaces) Describe()
    {
        string json = PythonSession.WithGil(() => _module.InvokeMethod("describe", _optic).ToString() ?? "{}");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (r.GetProperty("efl").GetDouble(), r.GetProperty("epd").GetDouble(),
                r.GetProperty("surfaces").GetInt32());
    }

    private OptilandOptic(PyObject module, PyObject optic, double maxField)
    {
        _module = module;
        _optic = optic;
        MaxField = maxField;
    }

    /// <summary>What this bridge cannot hand to Optiland. Null when the lens can be built.</summary>
    public static string? Unsupported(OpticalSystem system)
    {
        // A curved object surface is carried: Optiland's object surface takes a radius like any
        // other, and a field point then sits at its sag, as it does here.
        if (system.Surfaces[0].HasPolynomial) return "the object surface carries even-asphere terms";
        for (int i = 1; i < system.Surfaces.Count; i++)
        {
            var s = system.Surfaces[i];
            if (s.Type != SurfaceType.Standard && s.Type != SurfaceType.Paraxial)
                return $"surface {i} is of type {s.Type}";
            if (s.HasPolynomial) return $"surface {i} carries even-asphere terms";
        }
        return null;
    }

    /// <summary>Builds the lens that <paramref name="ts"/> describes inside Optiland.</summary>
    /// <param name="maxField">
    /// The largest field that will be traced, in the file's units. Optiland takes fields
    /// normalised to it; by default it is the file's own largest field.
    /// </param>
    public static OptilandOptic Build(TraceSystem ts, double? maxField = null)
    {
        if (ts == null) throw new ArgumentNullException(nameof(ts));
        string? no = Unsupported(ts.System);
        if (no != null) throw new NotSupportedException($"Optiland cross-check: {no}.");

        var system = ts.System;
        double fieldMax = maxField ?? system.MaxFieldY();
        string spec = Prescription(ts, fieldMax);

        return PythonSession.WithGil(() =>
        {
            var module = ImportHelper();
            var optic = module.InvokeMethod("build", new PyString(spec));
            return new OptilandOptic(module, optic, fieldMax);
        });
    }

    /// <summary>The prescription as the Python helper wants it.</summary>
    private static string Prescription(TraceSystem ts, double maxField)
    {
        var system = ts.System;
        var surfaces = new List<Dictionary<string, object?>>();
        for (int i = 0; i < system.Surfaces.Count; i++)
        {
            var s = system.Surfaces[i];
            double outer = i > 0 && i < system.Surfaces.Count - 1 ? ts.Apertures.Outer(i) : double.PositiveInfinity;
            double inner = i > 0 && i < system.Surfaces.Count - 1 ? ts.Apertures.Inner(i) : 0.0;

            surfaces.Add(new Dictionary<string, object?>
            {
                ["surface_type"] = s.Type == SurfaceType.Paraxial ? "paraxial" : "standard",
                ["focal_length"] = s.Type == SurfaceType.Paraxial ? s.FocalLength : (double?)null,
                ["mirror"] = s.IsMirror,
                ["radius"] = double.IsInfinity(s.Radius) ? null : s.Radius,
                ["thickness"] = i == 0 && ts.InfiniteConjugate ? null
                                 : double.IsInfinity(s.Thickness) ? 0.0 : Math.Abs(s.Thickness),
                ["conic"] = s.Conic,
                ["is_stop"] = i == ts.StopIndex,
                ["index_after"] = ts.Indices[i],
                ["aperture_outer"] = double.IsPositiveInfinity(outer) ? null : outer,
                ["aperture_inner"] = inner,
            });
        }

        var spec = new Dictionary<string, object?>
        {
            ["surfaces"] = surfaces,
            ["epd"] = 2.0 * ts.PupilUnit,
            ["field_type"] = system.FieldType == FieldType.ObjectAngle ? "angle" : "object_height",
            ["max_field"] = maxField,
            ["wavelength_um"] = ts.WavelengthUm,
            ["ray_aiming"] = "iterative",
        };

        return JsonSerializer.Serialize(spec);
    }

    /// <summary>Imports the helper module, adding its folder to sys.path on first use.</summary>
    private static PyObject ImportHelper()
    {
        string dir = Path.Combine(Path.GetDirectoryName(typeof(OptilandOptic).Assembly.Location) ?? ".", "python");
        if (!File.Exists(Path.Combine(dir, "ricalc_optiland.py")))
            throw new FileNotFoundException("The Optiland helper module was not copied next to the assembly.",
                                            Path.Combine(dir, "ricalc_optiland.py"));

        dynamic sys = Py.Import("sys");
        sys.path.insert(0, dir);
        return Py.Import("ricalc_optiland");
    }

    /// <summary>
    /// Traces one field's rays at the given normalised pupil coordinates, in a single call.
    /// <paramref name="fieldY"/> is in the file's field units.
    /// </summary>
    public RayBatch Trace(double fieldY, IReadOnlyList<double> px, IReadOnlyList<double> py)
    {
        if (px == null) throw new ArgumentNullException(nameof(px));
        if (py == null) throw new ArgumentNullException(nameof(py));
        if (px.Count != py.Count) throw new ArgumentException("px and py must be the same length.");

        // Beyond the largest field there is no normalised field to ask for. Tracing the axis
        // instead - as a lens with no fields once did here - measures the wrong beam, silently.
        if (Math.Abs(fieldY) > MaxField * (1.0 + 1e-12))
            throw new ArgumentOutOfRangeException(nameof(fieldY),
                $"field {fieldY} is beyond the largest field ({MaxField}) this optic was built for");
        double hy = MaxField > 0 ? fieldY / MaxField : 0.0;
        string json = PythonSession.WithGil(() =>
        {
            var pxList = new PyList();
            var pyList = new PyList();
            for (int k = 0; k < px.Count; k++)
            {
                pxList.Append(new PyFloat(px[k]));
                pyList.Append(new PyFloat(py[k]));
            }
            return _module.InvokeMethod("trace", _optic, new PyFloat(hy), pxList, pyList).ToString() ?? "{}";
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RayBatch
        {
            X = Column(root, "x"), Y = Column(root, "y"), Z = Column(root, "z"),
            L = Column(root, "L"), M = Column(root, "M"), N = Column(root, "N"),
            Intensity = Column(root, "i"),
        };
    }

    private static double[] Column(JsonElement root, string name)
    {
        var array = root.GetProperty(name);
        var result = new double[array.GetArrayLength()];
        int k = 0;
        foreach (var v in array.EnumerateArray())
            result[k++] = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
        return result;
    }
}
