using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using RelativeIllumination.Core.Enums;
using RelativeIllumination.Core.Glass;
using RelativeIllumination.Core.Illumination;
using RelativeIllumination.Core.Models;
using RelativeIllumination.Core.RayTrace;
using RelativeIllumination.IO;
using RelativeIllumination.Optiland;

namespace RelativeIllumination.Cli;

/// <summary>
/// ricalc: opens a lens file from any of the common design programs and reports its relative
/// illumination across the field, by two independent methods, with a breakdown of where the
/// fall-off comes from.
/// </summary>
public static class Program
{
    public const string Usage = @"
ricalc - relative illumination of a lens, from its prescription

RI by Rimmer's method (Proc. SPIE 655, 1986); effective F/# by Siew (Proc. SPIE 5867, 2005).

usage: ricalc <lens file> [options]

Lens files: .zmx (OpticStudio), .seq (CODE V), .otx/.opt (Optalix), .len/.osl (OSLO),
            .json (Optiland), .lhlt (LensHH-LT)

Options:
  --fields N         sample N+1 fields evenly from 0 to the largest field in the file (default 10)
  --at A,B,...       sample these field values instead (degrees, or object heights)
  --method M         forward | reverse | both (default both)
  --wave K           wavelength number, 1-based (default: the file's primary)
  --source S         lambertian (default) or cos:N - object radiance ~ cos^N from its normal
  --clip-auto        treat automatic semi-diameters as hard apertures (default: only fixed
                     semi-diameters, clear apertures, obscurations and the stop clip)
  --no-rimmer        forward method: use raw ray directions, without referring them to the
                     image point through the reference sphere (Rimmer Eq. 3)
  --no-breakdown     skip Siew's distortion breakdown
  --grid N           coarse sampling cells per side (default 32)
  --depth N          edge refinement levels (default 4)
  --glass DIR        folder of .agf catalogs (default: the bundled catalogs)
  --csv FILE         also write the table as CSV
  --limits F         instead of the table, report which surfaces stop the rays at field F
  --optiland         also measure the same lens with Optiland's ray trace (needs the embedded
                     Python that tools/setup-python.ps1 installs)
  -h, --help         this text
";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return Run(args, Console.Out, Console.Error);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or InvalidOperationException
                                       or FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string? lensPath = null, glassDir = null, csvPath = null, at = null;
        double? limitsField = null;
        bool optiland = false;
        int fieldCount = 10, wave = 0;
        var opt = new IlluminationOptions();
        bool clipAuto = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");
            switch (a)
            {
                case "-h":
                case "--help":
                    output.WriteLine(Usage.Trim());
                    return 0;
                case "--fields": fieldCount = ParseInt(Next(), a, 1); break;
                case "--at": at = Next(); break;
                case "--method":
                    switch (Next().ToLowerInvariant())
                    {
                        case "forward": opt.Forward = true; opt.Reverse = false; break;
                        case "reverse": opt.Forward = false; opt.Reverse = true; break;
                        case "both": opt.Forward = opt.Reverse = true; break;
                        default: throw new ArgumentException("--method is forward, reverse or both.");
                    }
                    break;
                case "--wave": wave = ParseInt(Next(), a, 1); break;
                case "--source": opt.Source = RadianceModel.Parse(Next()); break;
                case "--clip-auto": clipAuto = true; break;
                case "--no-rimmer": opt.RimmerCorrection = false; break;
                case "--no-breakdown": opt.Breakdown = false; break;
                case "--grid": opt.BaseCells = ParseInt(Next(), a, 4); break;
                case "--depth": opt.MaxDepth = ParseInt(Next(), a, 0); break;
                case "--glass": glassDir = Next(); break;
                case "--csv": csvPath = Next(); break;
                case "--limits": limitsField = ParseDouble(Next(), a); break;
                case "--optiland": optiland = true; break;
                default:
                    if (a.StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException($"unknown option {a}");
                    if (lensPath != null) throw new ArgumentException("only one lens file, please.");
                    lensPath = a;
                    break;
            }
        }

        if (lensPath == null)
        {
            error.WriteLine(Usage.Trim());
            return 1;
        }

        var catalog = LoadCatalog(glassDir);
        var system = LensFile.Read(lensPath, catalog);

        int w = wave > 0 ? wave - 1 : system.PrimaryWavelengthIndex;
        if (w < 0 || w >= system.Wavelengths.Count)
            throw new ArgumentException($"the lens has {system.Wavelengths.Count} wavelength(s); there is no number {w + 1}.");
        double lambda = system.Wavelengths[w].Value;

        var unresolved = new List<string>();
        var indices = IndexResolver.Build(system, catalog, lambda, unresolved);
        var ts = new TraceSystem(system, indices, lambda, clipAuto);

        if (limitsField.HasValue)
        {
            WriteLimits(output, ts, limitsField.Value);
            return 0;
        }

        var fields = FieldList(system, fieldCount, at);
        var results = RelativeIlluminationCalculator.Compute(ts, fields, opt);

        WriteHeader(output, lensPath, system, ts, opt, unresolved);
        WriteTable(output, system, results, opt);
        if (optiland) WriteOptiland(output, error, ts, results);
        if (csvPath != null) WriteCsv(csvPath, system, results);
        return 0;
    }

    private static GlassCatalog LoadCatalog(string? glassDir)
    {
        if (glassDir == null) return CatalogLocator.LoadBundled();
        if (!Directory.Exists(glassDir)) throw new DirectoryNotFoundException($"no such folder: {glassDir}");
        var catalog = new GlassCatalog();
        catalog.LoadFolder(glassDir);
        return catalog;
    }

    private static List<(double X, double Y)> FieldList(OpticalSystem system, int count, string? at)
    {
        if (at != null)
            return at.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => (0.0, ParseDouble(s.Trim(), "--at")))
                     .ToList();

        double max = system.MaxFieldY();
        var list = new List<(double, double)>();
        for (int k = 0; k <= count; k++) list.Add((0.0, max * k / count));
        return list;
    }

    private static void WriteHeader(TextWriter o, string path, OpticalSystem system, TraceSystem ts,
                                    IlluminationOptions opt, List<string> unresolved)
    {
        var p = ts.Paraxial;
        o.WriteLine($"Lens        {Path.GetFileName(path)}{(string.IsNullOrWhiteSpace(system.Title) ? "" : "  -  " + system.Title)}");
        o.WriteLine($"Wavelength  {ts.WavelengthUm:0.######} um");
        o.WriteLine($"EFL {F(p.Efl)}   EPD {F(p.Epd)}   F/# {F(p.FNumber)}   stop surface {ts.StopIndex} (radius {F(ts.StopRadius)})");
        o.WriteLine($"Focus       paraxial image {F(p.ParaxialFocusDistance)} after the last surface; " +
                    $"image surface at {F(system.Surfaces[ts.LastOptical].Thickness)}");
        o.WriteLine(ts.InfiniteConjugate
            ? "Object      at infinity"
            : $"Object      at {F(Math.Abs(system.Surfaces[0].Thickness))}, radius {R(system.Surfaces[0].Radius)}" +
              (ts.Telecentric ? ", telecentric object space" : "") + $", magnification {F(p.Magnification)}");
        o.WriteLine($"Image       radius {R(system.Surfaces[ts.ImageIndex].Radius)}, exit pupil " +
                    (double.IsInfinity(ts.ExitPupilZ) ? "at infinity (telecentric)" : $"{F(p.ExitPupilPosition)} from image"));
        o.WriteLine($"Source      {opt.Source.Describe()}");
        o.WriteLine("Method      RI by Rimmer (Proc. SPIE 655, 1986); effective F/# by Siew (Proc. SPIE 5867, 2005)");

        var clipping = Enumerable.Range(1, ts.LastOptical).Where(ts.Apertures.Clips).ToList();
        o.WriteLine("Apertures   " + string.Join(", ", clipping.Select(i =>
        {
            string s = i == ts.StopIndex ? $"S{i} stop r={F(ts.Apertures.Outer(i))}" :
                       double.IsPositiveInfinity(ts.Apertures.Outer(i)) ? $"S{i}" : $"S{i} r={F(ts.Apertures.Outer(i))}";
            return ts.Apertures.Inner(i) > 0 ? s + $" obsc r={F(ts.Apertures.Inner(i))}" : s;
        })) + (ts.Apertures.ClipAutomatic ? "   (automatic semi-diameters clip)" : ""));

        foreach (var n in ts.Notes) o.WriteLine("Note        " + n);
        if (unresolved.Count > 0)
            o.WriteLine("WARNING     glass not found, treated as air: " + string.Join(", ", unresolved.Distinct()));
        o.WriteLine();
    }

    private static void WriteTable(TextWriter o, OpticalSystem system, IReadOnlyList<FieldIllumination> results,
                                   IlluminationOptions opt)
    {
        string unit = system.FieldType == FieldType.ObjectHeight ? "height" : "deg";
        var head = new StringBuilder();
        head.Append($"{"Field " + unit,11} {"Img ht",9} {"CRA",6}");
        if (opt.Reverse) head.Append($" {"RI rev",8} {"F#T",6} {"F#S",6}");
        if (opt.Forward) head.Append($" {"RI fwd",8}");
        if (opt.Forward && opt.Reverse) head.Append($" {"fwd-rev",8}");
        head.Append($" {"F/#eff",7}");
        bool siew = opt.Breakdown && opt.Forward;
        if (siew) head.Append($" | {"S/S0",6} {"cos4",6} {"D%",7} {"ydD/dy",7} {"Siew",6}");
        o.WriteLine(head.ToString());

        foreach (var r in results)
        {
            var line = new StringBuilder();
            line.Append($"{r.FieldY,11:0.####}");
            if (!r.Ok)
            {
                line.Append("   " + r.Failure);
                o.WriteLine(line.ToString());
                continue;
            }
            line.Append($" {r.ImageHeight,9:0.####} {r.ChiefAngleImageDeg,6:0.00}");
            if (opt.Reverse)
                line.Append($" {r.Reverse!.RelativeIllumination,8:0.0000} {r.Reverse.FNumberT,6:0.00} {r.Reverse.FNumberS,6:0.00}");
            if (opt.Forward) line.Append($" {r.Forward!.RelativeIllumination,8:0.0000}");
            if (opt.Forward && opt.Reverse)
                line.Append($" {r.Forward!.RelativeIllumination - r.Reverse!.RelativeIllumination,8:+0.0000;-0.0000;0.0000}");
            line.Append($" {(r.Reverse ?? r.Forward)!.EffectiveFNumber,7:0.000}");
            if (siew && r.Siew != null)
                line.Append($" | {r.Siew.PupilAreaRatio,6:0.000} {r.Siew.Cos4,6:0.000} {100 * r.Siew.Distortion,7:0.00} " +
                            $"{r.Siew.DifferentialDistortion,7:0.000} {r.Siew.Estimate,6:0.000}");
            o.WriteLine(line.ToString());
        }

        o.WriteLine();
        o.WriteLine("RI rev  illuminance / that of the brightest field, by backward trace from the image point (exact)");
        if (opt.Forward)
            o.WriteLine("RI fwd  the same from the object point" + (opt.RimmerCorrection ? ", referred to the image point (Rimmer Eq. 3)" : ", raw ray directions"));
        o.WriteLine("CRA     chief ray angle to the image-surface normal; F#T/F#S Rimmer's effective F/# 1/(m2-m1), 1/(l2-l1)");
        o.WriteLine("F/#eff  Siew's effective F/#, sqrt(pi / (4 n'^2 PSA)): the F/# of a circular pupil giving the same illuminance");
        if (siew)
            o.WriteLine("Siew    small-aperture estimate [S/S0] cos4 / ((1+D)((1+D)+y dD/dy)), Opt. Eng. 56(4) 049701");

        // RI is relative to the brightest field measured, as OpticStudio reports it. When that is
        // not the axis, say so, and how much brighter it is.
        var (brightest, peak) = Brightest(results);
        if (peak > 1.0 + 1e-9)
        {
            o.WriteLine();
            o.WriteLine($"The brightest field is {brightest!.FieldY:0.####} {unit}, not the axis, and RI is relative to it:");
            o.WriteLine($"it is {peak:0.00000} times as bright as the axis (the CSV also has RI relative to the axis).");
        }
    }

    /// <summary>The brightest field measured, by the exact reverse method when it ran, and its illuminance relative to the axis.</summary>
    private static (FieldIllumination? Field, double Peak) Brightest(IReadOnlyList<FieldIllumination> results)
    {
        FieldIllumination? best = null;
        double peak = double.NegativeInfinity;
        foreach (var r in results.Where(r => r.Ok))
        {
            double v = (r.Reverse ?? r.Forward)?.RelativeToAxis ?? double.NaN;
            if (v > peak) { peak = v; best = r; }
        }
        return (best, peak);
    }

    /// <summary>
    /// What limits the beam at one field: a grid over the entrance pupil, each ray tallied by
    /// where and why it stopped. Answers "is this vignetting, a pupil aberration, or rays missing
    /// a surface altogether".
    /// </summary>
    private static void WriteLimits(TextWriter o, TraceSystem ts, double field)
    {
        var aim = RelativeIlluminationCalculator.AimChief(ts, 0.0, field);
        if (!aim.Ok) { o.WriteLine($"The chief ray at field {field} cannot be aimed at the stop centre."); return; }

        const int n = 81;
        const double half = 2.0;
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int passed = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                double px = aim.Px + half * (2.0 * i / (n - 1) - 1.0);
                double py = aim.Py + half * (2.0 * j / (n - 1) - 1.0);
                var (s, d) = ts.Launch(0.0, field, px, py);
                var ray = ts.TraceForward(s, d);
                if (ray.Ok) { passed++; continue; }
                string key = $"S{ray.FailedAt,-3} {ray.Status}";
                tally[key] = tally.TryGetValue(key, out int c) ? c + 1 : 1;
            }

        o.WriteLine($"Field {field}: chief ray at pupil ({aim.Px:0.####}, {aim.Py:0.####}); " +
                    $"{n}x{n} rays over +/-{half} pupil radii about it");
        o.WriteLine($"  passed          {passed}");
        foreach (var (k, c) in tally) o.WriteLine($"  {k,-28} {c}");
    }

    /// <summary>
    /// The same lens measured again with Optiland's ray trace, by the forward method. An
    /// independent check of this program's tracer: same lens, same method, different rays.
    /// </summary>
    private static void WriteOptiland(TextWriter o, TextWriter error, TraceSystem ts,
                                      IReadOnlyList<FieldIllumination> mine)
    {
        if (!PythonEnvironment.IsReady)
        {
            error.WriteLine("note: " + PythonEnvironment.SetupHint);
            return;
        }

        string? no = OptilandOptic.Unsupported(ts.System);
        if (no != null)
        {
            error.WriteLine($"note: the Optiland cross-check cannot take this lens - {no}.");
            return;
        }

        OptilandOptic optic;
        IReadOnlyList<OptilandField> theirs;
        try
        {
            optic = OptilandOptic.Build(ts);
            theirs = OptilandIllumination.Compute(ts, optic, mine.Select(m => m.FieldY).ToList());
        }
        catch (Exception ex)
        {
            error.WriteLine("note: the Optiland cross-check failed: " + ex.Message);
            return;
        }

        var first = optic.Describe();
        o.WriteLine();
        o.WriteLine($"Optiland {PythonSession.OptilandVersion} cross-check: the same prescription built in Optiland " +
                    $"(EFL {F(first.Efl)}, EPD {F(first.Epd)}),");
        o.WriteLine("its rays, Rimmer's forward method with the reference-sphere correction, and Siew's F/#eff.");
        o.WriteLine();
        o.WriteLine($"{"Field",11} {"RI opt",9} {"RI fwd",9} {"opt-fwd",9} {"RI rev",9} {"opt-rev",9} {"F/# opt",8} {"F/# fwd",8}");
        for (int k = 0; k < mine.Count; k++)
        {
            var m = mine[k];
            var t = theirs[k];
            if (!t.Ok || !m.Ok)
            {
                o.WriteLine($"{m.FieldY,11:0.####}   {(t.Failure ?? m.Failure)}");
                continue;
            }
            o.Write($"{m.FieldY,11:0.####} {t.RelativeIllumination,9:0.0000}");
            o.Write(m.Forward != null ? $" {m.Forward.RelativeIllumination,9:0.0000} " +
                                        $"{t.RelativeIllumination - m.Forward.RelativeIllumination,9:+0.0000;-0.0000;0.0000}"
                                      : $" {"-",9} {"-",9}");
            o.Write(m.Reverse != null ? $" {m.Reverse.RelativeIllumination,9:0.0000} " +
                                        $"{t.RelativeIllumination - m.Reverse.RelativeIllumination,9:+0.0000;-0.0000;0.0000}"
                                      : $" {"-",9} {"-",9}");
            o.Write($" {t.EffectiveFNumber,8:0.000}");
            o.Write(m.Forward != null ? $" {m.Forward.EffectiveFNumber,8:0.000}" : $" {"-",8}");
            o.WriteLine();
        }
    }

    private static void WriteCsv(string path, OpticalSystem system, IReadOnlyList<FieldIllumination> results)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("field_x,field_y,image_x,image_y,cra_deg,ri_reverse,fnum_t,fnum_s,ri_forward,pupil_ratio,cos4,distortion,y_dD_dy,siew_estimate," +
                      "ri_reverse_to_axis,ri_forward_to_axis,fnum_eff");
        // ri_reverse and ri_forward are relative to the brightest field measured, as OpticStudio
        // reports; the last two are relative to the axis, and may exceed 1.
        foreach (var r in results.Where(r => r.Ok))
        {
            string V(double? v) => v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("R", inv) : "";
            sb.AppendLine(string.Join(",",
                V(r.FieldX), V(r.FieldY), V(r.ImagePoint.X), V(r.ImagePoint.Y), V(r.ChiefAngleImageDeg),
                V(r.Reverse?.RelativeIllumination), V(r.Reverse?.FNumberT), V(r.Reverse?.FNumberS),
                V(r.Forward?.RelativeIllumination),
                V(r.Siew?.PupilAreaRatio), V(r.Siew?.Cos4), V(r.Siew?.Distortion),
                V(r.Siew?.DifferentialDistortion), V(r.Siew?.Estimate),
                V(r.Reverse?.RelativeToAxis), V(r.Forward?.RelativeToAxis),
                V((r.Reverse ?? r.Forward)?.EffectiveFNumber)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string F(double v) =>
        double.IsInfinity(v) ? "inf" : v.ToString(Math.Abs(v) >= 1e5 || (v != 0 && Math.Abs(v) < 1e-3) ? "0.###e+0" : "0.####", CultureInfo.InvariantCulture);

    private static string R(double radius) => double.IsInfinity(radius) ? "flat" : F(radius);

    private static int ParseInt(string s, string option, int min)
    {
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < min)
            throw new ArgumentException($"{option} needs a whole number of at least {min}.");
        return v;
    }

    private static double ParseDouble(string s, string option)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            throw new ArgumentException($"{option}: '{s}' is not a number.");
        return v;
    }
}
