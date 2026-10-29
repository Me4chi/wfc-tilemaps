using System;
using System.Collections.Generic;

namespace WfcTilemaps
{
    /// <summary>
    /// Wave Function Collapse, simple tiled model.
    /// Every cell starts as "any tile". The solver repeatedly picks the most constrained cell
    /// (lowest entropy), collapses it to one tile chosen by weight, and propagates what that rules out
    /// to the neighbours. If a cell runs out of options it restarts, up to MaxAttempts times.
    /// The same rules, settings and fixed tiles always produce the same map.
    /// </summary>
    public static class WfcSolver
    {
        public static WfcResult Solve(AdjacencyRules rules, WfcSettings settings, IEnumerable<FixedTile> fixedTiles = null)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            var fixedList = fixedTiles == null ? new List<FixedTile>() : new List<FixedTile>(fixedTiles);
            foreach (var f in fixedList)
            {
                if (f.X < 0 || f.Y < 0 || f.X >= settings.Width || f.Y >= settings.Height)
                    throw new ArgumentOutOfRangeException(nameof(fixedTiles), $"Fixed tile at ({f.X}, {f.Y}) is outside the map.");
                if (f.Tile < 0 || f.Tile >= rules.TileCount)
                    throw new ArgumentOutOfRangeException(nameof(fixedTiles), $"Fixed tile id {f.Tile} is outside 0..{rules.TileCount - 1}.");
            }

            var state = new State(rules, settings, new Random(settings.Seed));
            for (var attempt = 1; attempt <= settings.MaxAttempts; attempt++)
            {
                if (!state.Reset() || !state.ApplyFixed(fixedList))
                    return new WfcResult(false, null, settings.Width, settings.Height, attempt); // the rules or fixed tiles contradict: retrying cannot help
                if (state.Run())
                    return new WfcResult(true, state.Collapse(), settings.Width, settings.Height, attempt);
            }
            return new WfcResult(false, null, settings.Width, settings.Height, settings.MaxAttempts);
        }

        private sealed class State
        {
            private readonly int w, h, n, t;
            private readonly bool periodic;
            private readonly double[] weights, weightLogWeights;
            // propagator[d][a]: tiles that may stand one step in direction d from tile a.
            private readonly int[][][] propagator;

            private readonly bool[] wave;          // [cell * t + tile]
            private readonly int[] supports;       // [(cell * t + tile) * 4 + d]: tiles in the neighbour opposite d that still allow this tile
            private readonly int[] count;          // remaining options per cell
            private readonly double[] sumW, sumWLogW;
            private readonly Stack<(int cell, int tile)> banned = new Stack<(int, int)>();
            private readonly Random random;
            private bool contradiction;

            // Min-heap of (entropy, cell, count at push). Stale entries are skipped on pop,
            // so finding the most constrained cell costs O(log n) instead of a full scan.
            private double[] heapKey = new double[16];
            private int[] heapCell = new int[16];
            private int[] heapCount = new int[16];
            private int heapSize;

            public State(AdjacencyRules rules, WfcSettings s, Random random)
            {
                this.random = random;
                w = s.Width;
                h = s.Height;
                n = w * h;
                t = rules.TileCount;
                periodic = s.Periodic;

                weights = new double[t];
                weightLogWeights = new double[t];
                for (var i = 0; i < t; i++)
                {
                    weights[i] = rules.GetWeight(i);
                    weightLogWeights[i] = weights[i] > 0 ? weights[i] * Math.Log(weights[i]) : 0;
                }

                propagator = new int[4][][];
                var list = new List<int>();
                for (var d = 0; d < 4; d++)
                {
                    propagator[d] = new int[t][];
                    for (var a = 0; a < t; a++)
                    {
                        list.Clear();
                        for (var b = 0; b < t; b++)
                            if (rules.IsAllowed(a, (Direction)d, b)) list.Add(b);
                        propagator[d][a] = list.ToArray();
                    }
                }

                wave = new bool[n * t];
                supports = new int[n * t * 4];
                count = new int[n];
                sumW = new double[n];
                sumWLogW = new double[n];
            }

            /// <summary>Returns false if the rules alone already leave some cell without options.</summary>
            public bool Reset()
            {
                contradiction = false;
                banned.Clear();
                heapSize = 0;
                double startW = 0, startWLogW = 0;
                for (var a = 0; a < t; a++) { startW += weights[a]; startWLogW += weightLogWeights[a]; }

                for (var cell = 0; cell < n; cell++)
                {
                    count[cell] = t;
                    sumW[cell] = startW;
                    sumWLogW[cell] = startWLogW;
                    for (var a = 0; a < t; a++)
                    {
                        wave[cell * t + a] = true;
                        for (var d = 0; d < 4; d++)
                            supports[(cell * t + a) * 4 + d] = propagator[AdjacencyRules.Opposite(d)][a].Length;
                    }
                    if (t > 1) Push(cell);
                }

                for (var cell = 0; cell < n; cell++)
                for (var a = 0; a < t; a++)
                {
                    // Weight 0 means "never".
                    if (weights[a] <= 0) { Ban(cell, a); continue; }
                    // A tile with no allowed neighbour on a side cannot stand where that side has a cell.
                    for (var d = 0; d < 4; d++)
                        if (propagator[AdjacencyRules.Opposite(d)][a].Length == 0 && HasNeighbour(cell, AdjacencyRules.Opposite(d)))
                        {
                            Ban(cell, a);
                            break;
                        }
                }
                Propagate();
                return !contradiction;
            }

            private bool HasNeighbour(int cell, int d)
            {
                if (periodic) return true;
                var nx = cell % w + AdjacencyRules.Dx[d];
                var ny = cell / w + AdjacencyRules.Dy[d];
                return nx >= 0 && ny >= 0 && nx < w && ny < h;
            }

            public bool ApplyFixed(List<FixedTile> fixedTiles)
            {
                foreach (var f in fixedTiles)
                {
                    var cell = f.X + f.Y * w;
                    if (!wave[cell * t + f.Tile]) return false;
                    for (var a = 0; a < t; a++)
                        if (a != f.Tile && wave[cell * t + a]) Ban(cell, a);
                    Propagate();
                    if (contradiction) return false;
                }
                return !contradiction;
            }

            public bool Run()
            {
                while (!contradiction)
                {
                    var cell = LowestEntropyCell();
                    if (cell < 0) return true; // everything collapsed
                    Observe(cell);
                    Propagate();
                }
                return false;
            }

            public int[] Collapse()
            {
                var result = new int[n];
                for (var cell = 0; cell < n; cell++)
                    for (var a = 0; a < t; a++)
                        if (wave[cell * t + a]) { result[cell] = a; break; }
                return result;
            }

            private int LowestEntropyCell()
            {
                while (heapSize > 0)
                {
                    var cell = heapCell[0];
                    var pushedCount = heapCount[0];
                    PopRoot();
                    if (pushedCount == count[cell] && count[cell] > 1) return cell;
                }
                return -1;
            }

            private void Push(int cell)
            {
                if (heapSize == heapKey.Length)
                {
                    Array.Resize(ref heapKey, heapSize * 2);
                    Array.Resize(ref heapCell, heapSize * 2);
                    Array.Resize(ref heapCount, heapSize * 2);
                }
                // Tiny noise breaks ties without a directional bias.
                var key = Math.Log(sumW[cell]) - sumWLogW[cell] / sumW[cell] + 1e-6 * random.NextDouble();
                var i = heapSize++;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (heapKey[parent] <= key) break;
                    heapKey[i] = heapKey[parent];
                    heapCell[i] = heapCell[parent];
                    heapCount[i] = heapCount[parent];
                    i = parent;
                }
                heapKey[i] = key;
                heapCell[i] = cell;
                heapCount[i] = count[cell];
            }

            private void PopRoot()
            {
                var last = --heapSize;
                if (last == 0) return;
                var key = heapKey[last];
                var i = 0;
                while (true)
                {
                    var child = i * 2 + 1;
                    if (child >= last) break;
                    if (child + 1 < last && heapKey[child + 1] < heapKey[child]) child++;
                    if (key <= heapKey[child]) break;
                    heapKey[i] = heapKey[child];
                    heapCell[i] = heapCell[child];
                    heapCount[i] = heapCount[child];
                    i = child;
                }
                heapKey[i] = key;
                heapCell[i] = heapCell[last];
                heapCount[i] = heapCount[last];
            }

            private void Observe(int cell)
            {
                var roll = random.NextDouble() * sumW[cell];
                var chosen = -1;
                for (var a = 0; a < t; a++)
                {
                    if (!wave[cell * t + a]) continue;
                    chosen = a;
                    roll -= weights[a];
                    if (roll < 0) break;
                }
                for (var a = 0; a < t; a++)
                    if (a != chosen && wave[cell * t + a]) Ban(cell, a);
            }

            private void Ban(int cell, int tile)
            {
                var i = cell * t + tile;
                if (!wave[i]) return;
                wave[i] = false;
                for (var d = 0; d < 4; d++) supports[i * 4 + d] = 0;
                banned.Push((cell, tile));

                count[cell]--;
                sumW[cell] -= weights[tile];
                sumWLogW[cell] -= weightLogWeights[tile];
                if (count[cell] == 0) contradiction = true;
                else if (count[cell] > 1) Push(cell);
            }

            private void Propagate()
            {
                while (banned.Count > 0)
                {
                    var (cell, tile) = banned.Pop();
                    var x = cell % w;
                    var y = cell / w;
                    for (var d = 0; d < 4; d++)
                    {
                        var nx = x + AdjacencyRules.Dx[d];
                        var ny = y + AdjacencyRules.Dy[d];
                        if (periodic) { nx = (nx + w) % w; ny = (ny + h) % h; }
                        else if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;

                        var neighbour = nx + ny * w;
                        foreach (var b in propagator[d][tile])
                        {
                            var s = (neighbour * t + b) * 4 + d;
                            if (supports[s] == 0) continue; // already banned
                            if (--supports[s] == 0) Ban(neighbour, b);
                        }
                    }
                }
            }
        }
    }
}
