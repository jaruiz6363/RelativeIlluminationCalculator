using System;
using System.Collections.Generic;

namespace RelativeIllumination.Core.Illumination;

/// <summary>
/// What one sampled ray reports: whether it got through, where it lands in the mapped space
/// (image-space direction cosines), and the radiance weight it carries.
/// </summary>
public readonly record struct PupilSample(bool Pass, double U, double V, double Weight)
{
    public static readonly PupilSample Blocked = new(false, 0, 0, 0);
}

/// <summary>The measured region.</summary>
public sealed class PupilIntegral
{
    /// <summary>Area of the passing region in the mapped (U, V) space, each piece weighted by radiance.</summary>
    public double WeightedArea { get; init; }

    /// <summary>The same area without the radiance weight.</summary>
    public double MappedArea { get; init; }

    /// <summary>Area of the passing region in the sampled parameter space.</summary>
    public double ParameterArea { get; init; }

    /// <summary>Extent of the passing samples in the mapped space.</summary>
    public double UMin { get; init; }
    public double UMax { get; init; }
    public double VMin { get; init; }
    public double VMax { get; init; }

    /// <summary>
    /// A passing ray was found on the edge of the searched window, so the region may extend
    /// beyond it. The caller widens the window and measures again.
    /// </summary>
    public bool TouchesWindow { get; init; }

    /// <summary>Rays traced.</summary>
    public int Evaluations { get; init; }

    public bool Empty => !(MappedArea > 0.0);
}

/// <summary>
/// Measures the area of the region of a 2-D parameter space whose rays get through, as that
/// region appears after mapping each ray to image-space direction cosines.
///
/// <para>This replaces the usual boundary search - one binary search outward from the pupil
/// centre per azimuth, then the area of the resulting polygon - which silently assumes the
/// region is star-shaped about its centre and that the centre passes. It does not survive a
/// central obscuration (the pupil has a hole), a vignetted chief ray, or a pupil clipped from two
/// sides into a shape the radial search crosses twice.</para>
///
/// <para>Here the window is covered by a square grid. A cell whose corners and centre all pass is
/// taken whole: its image is four triangles on the mapped corners and centre, which follows the
/// curvature of the mapping to second order. A cell that is partly blocked, or next to one, is
/// split into four, down to a set depth; at the finest level the boundary is located on each cut
/// edge by bisection and the passing part of the cell becomes a polygon on those crossings -
/// marching squares with exact crossings. A region with a hole, several pieces or re-entrant
/// edges is measured as it is.</para>
///
/// <para>Because each piece's area is taken in the MAPPED space, the parameter space only has to
/// cover the region, not sample it uniformly: Rimmer's magnification matrix, needed to space
/// rays evenly in exit direction cosines when counting them, is not needed at all.</para>
///
/// <para>The grid is worked one level at a time, and every ray a level needs is asked for in one
/// batch; so are the rays of each bisection step, across every cut edge at once. A tracer that
/// is expensive to call - Optiland, through Python - then costs a few dozen calls per field
/// instead of one per ray. <c>macros/RELILLUM.ZPL</c> carries the same algorithm, level by level
/// in the same order, for OpticStudio's rays.</para>
/// </summary>
public sealed class PupilIntegrator
{
    private readonly Func<IReadOnlyList<(double X, double Y)>, IReadOnlyList<PupilSample>> _sample;
    private readonly double _x0, _y0, _halfWidth;
    private readonly int _baseCells, _maxDepth, _bisections;
    private readonly int _fine;                    // lattice steps per base cell
    private readonly int _n;                       // lattice steps across the window
    private readonly double _step;                 // parameter length of one lattice step
    // Traced lattice nodes, stored in blocks of 8 x 8 slots; a block exists only once a node in
    // it is asked for, so memory follows the refined band around the boundary rather than the
    // whole window. A slot holds 0 for "not traced", -1 for "asked for in the batch being
    // gathered", or 1 + the node's index in _samples.
    private const int BlockShift = 3;
    private const int BlockSide = 1 << BlockShift;
    private readonly int _blocksPerSide;
    private readonly int[]?[] _blocks;
    private readonly List<PupilSample> _samples = new();

    private double _weighted, _mapped, _param;
    private double _uMin = double.PositiveInfinity, _uMax = double.NegativeInfinity;
    private double _vMin = double.PositiveInfinity, _vMax = double.NegativeInfinity;
    private int _evaluations;

    /// <param name="sample">Traces the ray at parameter (x, y).</param>
    /// <param name="centreX">Centre of the square window.</param>
    /// <param name="halfWidth">Half the side of the window.</param>
    /// <param name="baseCells">Cells along each side of the coarse grid.</param>
    /// <param name="maxDepth">How many times a partly blocked cell may be split in four.</param>
    /// <param name="bisections">Bisection steps locating the boundary on a finest-level edge.</param>
    public PupilIntegrator(Func<double, double, PupilSample> sample,
                           double centreX, double centreY, double halfWidth,
                           int baseCells = 32, int maxDepth = 4, int bisections = 12)
        : this(OneByOne(sample ?? throw new ArgumentNullException(nameof(sample))),
               centreX, centreY, halfWidth, baseCells, maxDepth, bisections)
    {
    }

    /// <param name="sampleBatch">Traces the rays at the given parameters, returning one sample per point, in order.</param>
    public PupilIntegrator(Func<IReadOnlyList<(double X, double Y)>, IReadOnlyList<PupilSample>> sampleBatch,
                           double centreX, double centreY, double halfWidth,
                           int baseCells = 32, int maxDepth = 4, int bisections = 12)
    {
        _sample = sampleBatch ?? throw new ArgumentNullException(nameof(sampleBatch));
        if (baseCells < 2) throw new ArgumentOutOfRangeException(nameof(baseCells));
        if (maxDepth < 0 || maxDepth > 12) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        _x0 = centreX;
        _y0 = centreY;
        _halfWidth = halfWidth;
        _baseCells = baseCells;
        _maxDepth = maxDepth;
        _bisections = bisections;
        // Two lattice steps per finest cell, so every cell's centre is a lattice node too.
        _fine = 2 << maxDepth;
        _n = baseCells * _fine;
        _step = 2.0 * halfWidth / _n;
        _blocksPerSide = (_n >> BlockShift) + 1;
        _blocks = new int[]?[_blocksPerSide * _blocksPerSide];
    }

    private static Func<IReadOnlyList<(double X, double Y)>, IReadOnlyList<PupilSample>> OneByOne(
        Func<double, double, PupilSample> sample) =>
        points =>
        {
            var r = new PupilSample[points.Count];
            for (int k = 0; k < r.Length; k++) r[k] = sample(points[k].X, points[k].Y);
            return r;
        };

    public PupilIntegral Integrate()
    {
        // The window's outline, on the coarse lattice: a passing ray there means the region may
        // run beyond the window.
        var rim = new List<(int, int)>();
        for (int k = 0; k <= _baseCells; k++)
        {
            int e = k * _fine;
            rim.Add((e, 0)); rim.Add((e, _n)); rim.Add((0, e)); rim.Add((_n, e));
        }
        Fetch(rim);
        bool touches = false;
        foreach (var (i, j) in rim) touches |= Node(i, j).Pass;

        var cells = new List<(int I, int J)>();
        for (int j = 0; j < _baseCells; j++)
            for (int i = 0; i < _baseCells; i++)
                cells.Add((i * _fine, j * _fine));

        var boundary = new List<(int I, int J, int Size)>();
        int size = _fine;
        for (int depth = 0; cells.Count > 0; depth++, size /= 2)
        {
            int h = size / 2;
            bool canSplit = depth < _maxDepth && h >= 2;

            var own = new List<(int, int)>(5 * cells.Count);
            foreach (var (i, j) in cells) AddCellNodes(own, i, j, size);
            Fetch(own);

            // A cell is split when it, or any cell of its size beside it, is partly blocked. Its
            // own five rays cannot see a sliver of the region that enters between them - the
            // pointed tip of a vignetted pupil, say - so judged alone it would be taken whole or
            // dropped, and the sliver would appear in the total only once the field moved it onto
            // a ray: a step in RI. Next to the boundary such a cell is refined instead, and what
            // is still missed shrinks fourfold with each level rather than staying at the size of
            // a coarse cell.
            // Neighbours already traced settle most cells; only the rest trace theirs.
            var split = new bool[cells.Count];
            if (canSplit)
            {
                var near = new List<(int, int)>();
                for (int c = 0; c < cells.Count; c++)
                {
                    var (i, j) = cells[c];
                    split[c] = IsCut(i, j, size) || NeighbourCut(i, j, size, tracedOnly: true);
                    if (!split[c])
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int a = i + di * size, b = j + dj * size;
                                if (Beside(di, dj, a, b, size) && !Traced(a, b, size)) AddCellNodes(near, a, b, size);
                            }
                }
                Fetch(near);
                for (int c = 0; c < cells.Count; c++)
                    if (!split[c]) split[c] = NeighbourCut(cells[c].I, cells[c].J, size, tracedOnly: false);
            }

            var next = new List<(int I, int J)>();
            for (int c = 0; c < cells.Count; c++)
            {
                var (i, j) = cells[c];
                bool cut = IsCut(i, j, size);
                if (split[c])
                {
                    next.Add((i, j)); next.Add((i + h, j)); next.Add((i + h, j + h)); next.Add((i, j + h));
                }
                else if (cut) boundary.Add((i, j, size));
                else if (Node(i, j).Pass) AddWhole(i, j, size);
            }
            cells = next;
        }

        MeasureBoundary(boundary);

        bool any = _uMax >= _uMin;
        return new PupilIntegral
        {
            WeightedArea = _weighted,
            MappedArea = _mapped,
            ParameterArea = _param,
            UMin = any ? _uMin : 0, UMax = any ? _uMax : 0,
            VMin = any ? _vMin : 0, VMax = any ? _vMax : 0,
            TouchesWindow = touches,
            Evaluations = _evaluations,
        };
    }

    // ── The lattice ──────────────────────────────────────────────────────────────────

    private (double X, double Y) Param(double i, double j) =>
        (_x0 - _halfWidth + i * _step, _y0 - _halfWidth + j * _step);

    private static long Key(int i, int j) => ((long)i << 32) | (uint)j;

    private ref int Slot(int i, int j)
    {
        ref int[]? block = ref _blocks[(j >> BlockShift) * _blocksPerSide + (i >> BlockShift)];
        block ??= new int[BlockSide * BlockSide];
        return ref block[((j & (BlockSide - 1)) << BlockShift) + (i & (BlockSide - 1))];
    }

    private bool IsTraced(int i, int j) => Slot(i, j) > 0;

    private PupilSample Node(int i, int j) => _samples[Slot(i, j) - 1];

    /// <summary>Traces, in one batch, every lattice node in the list not already traced.</summary>
    private void Fetch(List<(int I, int J)> nodes)
    {
        var wanted = new List<(int I, int J)>();
        var points = new List<(double X, double Y)>();
        foreach (var (i, j) in nodes)
        {
            ref int slot = ref Slot(i, j);
            if (slot != 0) continue;
            slot = -1;
            wanted.Add((i, j));
            points.Add(Param(i, j));
        }
        if (points.Count == 0) return;
        var s = Evaluate(points);
        for (int k = 0; k < wanted.Count; k++)
        {
            _samples.Add(s[k]);
            Slot(wanted[k].I, wanted[k].J) = _samples.Count;
        }
    }

    private IReadOnlyList<PupilSample> Evaluate(List<(double X, double Y)> points)
    {
        var s = _sample(points);
        if (s.Count != points.Count)
            throw new InvalidOperationException($"asked for {points.Count} rays, got {s.Count}");
        _evaluations += points.Count;
        foreach (var r in s)
        {
            if (!r.Pass) continue;
            if (r.U < _uMin) _uMin = r.U;
            if (r.U > _uMax) _uMax = r.U;
            if (r.V < _vMin) _vMin = r.V;
            if (r.V > _vMax) _vMax = r.V;
        }
        return s;
    }

    private static void AddCellNodes(List<(int, int)> list, int i, int j, int size)
    {
        list.Add((i, j)); list.Add((i + size, j)); list.Add((i + size, j + size)); list.Add((i, j + size));
        list.Add((i + size / 2, j + size / 2));
    }

    /// <summary>Whether the cell at (a, b), offset (di, dj) cells from another, is a neighbour inside the window.</summary>
    private bool Beside(int di, int dj, int a, int b, int size) =>
        (di != 0 || dj != 0) && a >= 0 && b >= 0 && a + size <= _n && b + size <= _n;

    private bool NeighbourCut(int i, int j, int size, bool tracedOnly)
    {
        for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int a = i + di * size, b = j + dj * size;
                if (Beside(di, dj, a, b, size) && (!tracedOnly || Traced(a, b, size)) && IsCut(a, b, size))
                    return true;
            }
        return false;
    }

    private bool Traced(int i, int j, int size) =>
        IsTraced(i, j) && IsTraced(i + size, j) && IsTraced(i + size, j + size) && IsTraced(i, j + size)
        && IsTraced(i + size / 2, j + size / 2);

    /// <summary>Whether the cell's corners and centre disagree: the boundary crosses it.</summary>
    private bool IsCut(int i, int j, int size)
    {
        bool p = Node(i, j).Pass;
        return Node(i + size, j).Pass != p || Node(i + size, j + size).Pass != p
               || Node(i, j + size).Pass != p || Node(i + size / 2, j + size / 2).Pass != p;
    }

    // ── Areas ────────────────────────────────────────────────────────────────────────

    /// <summary>A cell that passes entirely: four triangles on the corners and the centre.</summary>
    private void AddWhole(int i, int j, int size)
    {
        var c0 = Node(i, j);
        var c1 = Node(i + size, j);
        var c2 = Node(i + size, j + size);
        var c3 = Node(i, j + size);
        var cc = Node(i + size / 2, j + size / 2);
        double a = Tri(c0, c1, cc) + Tri(c1, c2, cc) + Tri(c2, c3, cc) + Tri(c3, c0, cc);
        double w = (c0.Weight + c1.Weight + c2.Weight + c3.Weight + 4.0 * cc.Weight) / 8.0;
        _mapped += a;
        _weighted += a * w;
        double side = size * _step;
        _param += side * side;

        static double Tri(PupilSample p, PupilSample q, PupilSample r) =>
            0.5 * Math.Abs((q.U - p.U) * (r.V - p.V) - (r.U - p.U) * (q.V - p.V));
    }

    /// <summary>
    /// The finest cells the boundary crosses: marching squares, with each crossing located by
    /// bisection along the cut edge and represented by the last ray that still passed. Every cut
    /// edge is bisected at once, one batch per step; an edge two cells share is bisected once.
    /// </summary>
    private void MeasureBoundary(List<(int I, int J, int Size)> cells)
    {
        var edges = new Dictionary<(long, long), int>();
        var passPt = new List<(double X, double Y)>();
        var failPt = new List<(double X, double Y)>();
        var best = new List<(PupilSample S, double X, double Y)>();

        int EdgeOf((int I, int J) a, (int I, int J) b)
        {
            bool aPasses = Node(a.I, a.J).Pass;
            var (p, f) = aPasses ? (a, b) : (b, a);
            var key = (Key(p.I, p.J), Key(f.I, f.J));
            if (edges.TryGetValue(key, out int k)) return k;
            k = passPt.Count;
            edges[key] = k;
            var pp = Param(p.I, p.J);
            passPt.Add(pp);
            failPt.Add(Param(f.I, f.J));
            // Until a bisection ray passes, the passing corner itself is the best there is: the
            // boundary is then within one bisection step of it.
            best.Add((Node(p.I, p.J), pp.X, pp.Y));
            return k;
        }

        var cellEdges = new int[cells.Count][];
        for (int c = 0; c < cells.Count; c++)
        {
            var (i, j, size) = cells[c];
            var corner = Corners(i, j, size);
            cellEdges[c] = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int m = (k + 1) & 3;
                cellEdges[c][k] = Node(corner[k].I, corner[k].J).Pass != Node(corner[m].I, corner[m].J).Pass
                    ? EdgeOf(corner[k], corner[m]) : -1;
            }
        }

        for (int it = 0; it < _bisections && passPt.Count > 0; it++)
        {
            var mid = new List<(double X, double Y)>(passPt.Count);
            for (int k = 0; k < passPt.Count; k++)
                mid.Add((0.5 * (passPt[k].X + failPt[k].X), 0.5 * (passPt[k].Y + failPt[k].Y)));
            var s = Evaluate(mid);
            for (int k = 0; k < mid.Count; k++)
            {
                if (s[k].Pass) { passPt[k] = mid[k]; best[k] = (s[k], mid[k].X, mid[k].Y); }
                else failPt[k] = mid[k];
            }
        }

        for (int c = 0; c < cells.Count; c++)
        {
            var (i, j, size) = cells[c];
            var cn = Corners(i, j, size);
            var corner = new PupilSample[4];
            var cornerParam = new (double X, double Y)[4];
            for (int k = 0; k < 4; k++)
            {
                corner[k] = Node(cn[k].I, cn[k].J);
                cornerParam[k] = Param(cn[k].I, cn[k].J);
            }
            var cc = Node(i + size / 2, j + size / 2);
            var cross = new (PupilSample S, double X, double Y)?[4];
            for (int k = 0; k < 4; k++)
                if (cellEdges[c][k] >= 0) cross[k] = best[cellEdges[c][k]];

            bool saddle = corner[0].Pass == corner[2].Pass && corner[1].Pass == corner[3].Pass
                          && corner[0].Pass != corner[1].Pass;

            if (saddle && !cc.Pass)
            {
                // Two separate corner pieces, disconnected through a blocked centre.
                for (int k = 0; k < 4; k++)
                {
                    if (!corner[k].Pass) continue;
                    int prev = (k + 3) & 3;
                    AddPolygon(new List<(PupilSample S, double X, double Y)>
                    {
                        cross[prev]!.Value, (corner[k], cornerParam[k].X, cornerParam[k].Y), cross[k]!.Value,
                    });
                }
                continue;
            }

            var ring = new List<(PupilSample S, double X, double Y)>(8);
            for (int k = 0; k < 4; k++)
            {
                if (corner[k].Pass) ring.Add((corner[k], cornerParam[k].X, cornerParam[k].Y));
                if (cross[k].HasValue) ring.Add(cross[k]!.Value);
            }

            // Fewer than three vertices means every corner is blocked and only the centre passes:
            // an island smaller than a finest cell, below the resolution asked for. It is dropped.
            if (ring.Count >= 3) AddPolygon(ring);
        }
    }

    private static (int I, int J)[] Corners(int i, int j, int size) =>
        new[] { (i, j), (i + size, j), (i + size, j + size), (i, j + size) };

    private void AddPolygon(List<(PupilSample S, double X, double Y)> poly)
    {
        double a = 0.0, ap = 0.0, w = 0.0;
        for (int k = 0; k < poly.Count; k++)
        {
            var p = poly[k];
            var q = poly[(k + 1) % poly.Count];
            a += p.S.U * q.S.V - q.S.U * p.S.V;
            ap += p.X * q.Y - q.X * p.Y;
            w += p.S.Weight;
        }
        a = 0.5 * Math.Abs(a);
        _mapped += a;
        _weighted += a * (w / poly.Count);
        _param += 0.5 * Math.Abs(ap);
    }
}
