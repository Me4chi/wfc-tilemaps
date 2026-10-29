using System;
using System.Collections.Generic;

namespace WfcTilemaps
{
    /// <summary>
    /// Overlapping model: learns every N×N fragment of the sample and builds maps in which every N×N
    /// window is one of those fragments. Unlike <see cref="AdjacencyRules.Learn"/>, which only knows
    /// pairs of neighbours, this reproduces shapes: coastlines, rooms, corridors, roads.
    /// </summary>
    public sealed class PatternModel
    {
        private readonly List<int[]> patterns;

        /// <summary>Fragment size N.</summary>
        public int N { get; }

        /// <summary>One "tile" per distinct fragment; neighbours are fragments that overlap consistently.</summary>
        public AdjacencyRules Rules { get; }

        public int PatternCount => patterns.Count;

        private PatternModel(int n, List<int[]> patterns, double[] counts)
        {
            N = n;
            this.patterns = patterns;
            Rules = new AdjacencyRules(patterns.Count);
            for (var p = 0; p < patterns.Count; p++)
            {
                Rules.SetWeight(p, counts[p]);
                for (var d = 0; d < 2; d++) // +X and +Y; Allow() records the opposite pair too
                    for (var q = 0; q < patterns.Count; q++)
                        if (Overlaps(patterns[p], patterns[q], AdjacencyRules.Dx[d], AdjacencyRules.Dy[d]))
                            Rules.Allow(p, (Direction)d, q);
            }
        }

        /// <param name="sample">Row-major tile ids, index = x + y * width; ids must be 0 or greater.</param>
        /// <param name="n">Fragment size. 2 is loose, 3 is the usual choice, 4+ copies the sample more literally.</param>
        /// <param name="periodicSample">Treat the sample as wrapping, so fragments may cross its edges.</param>
        /// <param name="symmetry">Also learn every rotation and mirror image of each fragment. Use only when your tiles look the same rotated.</param>
        public static PatternModel Learn(int[] sample, int width, int height, int n = 3, bool periodicSample = false, bool symmetry = false)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (width < 1 || height < 1 || sample.Length != width * height)
                throw new ArgumentException("Sample length must equal width * height.", nameof(sample));
            if (n < 2) throw new ArgumentOutOfRangeException(nameof(n), "Fragment size must be at least 2.");
            if (!periodicSample && (n > width || n > height))
                throw new ArgumentException("The sample must be at least N×N, or set periodicSample.", nameof(sample));
            foreach (var t in sample)
                if (t < 0) throw new ArgumentException("Tile ids must be 0 or greater.", nameof(sample));

            var index = new Dictionary<string, int>();
            var patterns = new List<int[]>();
            var counts = new List<double>();
            var maxX = periodicSample ? width : width - n + 1;
            var maxY = periodicSample ? height : height - n + 1;
            for (var y = 0; y < maxY; y++)
            for (var x = 0; x < maxX; x++)
            {
                var p = new int[n * n];
                for (var j = 0; j < n; j++)
                    for (var i = 0; i < n; i++)
                        p[i + j * n] = sample[(x + i) % width + (y + j) % height * width];

                var variant = p;
                for (var v = 0; v < (symmetry ? 8 : 1); v++)
                {
                    if (v == 4) variant = Mirror(p, n);
                    else if (v > 0) variant = Rotate(variant, n);
                    var key = string.Join(",", variant);
                    if (index.TryGetValue(key, out var id)) counts[id] += 1;
                    else
                    {
                        index.Add(key, patterns.Count);
                        patterns.Add(variant);
                        counts.Add(1);
                    }
                }
            }
            return new PatternModel(n, patterns, counts.ToArray());
        }

        /// <summary>Generates a map of settings.Width × settings.Height sample tile ids.</summary>
        public WfcResult Solve(WfcSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            if (!settings.Periodic && (settings.Width < N || settings.Height < N))
                throw new ArgumentException($"Width and Height must be at least N ({N}) unless Periodic is set.");

            // A non-periodic map of W×H tiles holds (W-N+1)×(H-N+1) whole fragments.
            var grid = settings.Clone();
            if (!settings.Periodic)
            {
                grid.Width = settings.Width - N + 1;
                grid.Height = settings.Height - N + 1;
            }
            var solved = WfcSolver.Solve(Rules, grid);
            if (!solved.Success) return new WfcResult(false, null, settings.Width, settings.Height, solved.Attempts);

            var tiles = new int[settings.Width * settings.Height];
            for (var y = 0; y < settings.Height; y++)
            for (var x = 0; x < settings.Width; x++)
            {
                // Each cell takes the top-left tile of its fragment; the last row and column read further into the edge fragments.
                var px = Math.Min(x, grid.Width - 1);
                var py = Math.Min(y, grid.Height - 1);
                tiles[x + y * settings.Width] = patterns[solved.Get(px, py)][(x - px) + (y - py) * N];
            }
            return new WfcResult(true, tiles, settings.Width, settings.Height, solved.Attempts);
        }

        /// <summary>True if q, shifted by (dx, dy) from p, agrees with p on every overlapping cell.</summary>
        private bool Overlaps(int[] p, int[] q, int dx, int dy)
        {
            for (var y = Math.Max(0, dy); y < Math.Min(N, N + dy); y++)
                for (var x = Math.Max(0, dx); x < Math.Min(N, N + dx); x++)
                    if (p[x + y * N] != q[(x - dx) + (y - dy) * N]) return false;
            return true;
        }

        private static int[] Rotate(int[] p, int n)
        {
            var r = new int[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    r[x + y * n] = p[(n - 1 - y) + x * n];
            return r;
        }

        private static int[] Mirror(int[] p, int n)
        {
            var r = new int[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    r[x + y * n] = p[(n - 1 - x) + y * n];
            return r;
        }
    }
}
