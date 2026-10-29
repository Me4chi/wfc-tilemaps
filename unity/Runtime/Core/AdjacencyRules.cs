using System;

namespace WfcTilemaps
{
    /// <summary>Grid directions. The core never assumes which way Y points: that is up to the engine.</summary>
    public enum Direction
    {
        PlusX = 0,
        PlusY = 1,
        MinusX = 2,
        MinusY = 3,
    }

    /// <summary>
    /// Which tiles may stand next to which, and how often each tile should appear.
    /// Tiles are plain integers 0..TileCount-1; the engine layer maps them to real tiles.
    /// Build rules by hand with <see cref="Allow"/> or learn them from an example map with <see cref="Learn"/>.
    /// </summary>
    public sealed class AdjacencyRules
    {
        public static readonly int[] Dx = { 1, 0, -1, 0 };
        public static readonly int[] Dy = { 0, 1, 0, -1 };

        public static int Opposite(int direction) => (direction + 2) % 4;

        private readonly double[] weights;
        // allowed[(d * T + a) * T + b]: tile b may sit one step in direction d from tile a.
        private readonly bool[] allowed;

        public int TileCount { get; }

        public AdjacencyRules(int tileCount)
        {
            if (tileCount < 1) throw new ArgumentOutOfRangeException(nameof(tileCount), "At least one tile is required.");
            TileCount = tileCount;
            weights = new double[tileCount];
            for (var i = 0; i < tileCount; i++) weights[i] = 1;
            allowed = new bool[4 * tileCount * tileCount];
        }

        /// <summary>Relative frequency of a tile. 0 removes the tile from the output entirely.</summary>
        public double GetWeight(int tile) => weights[Check(tile)];

        public void SetWeight(int tile, double weight)
        {
            if (!(weight >= 0) || double.IsInfinity(weight)) throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be a finite number >= 0.");
            weights[Check(tile)] = weight;
        }

        /// <summary>Lets <paramref name="b"/> stand one step in <paramref name="direction"/> from <paramref name="a"/> (and a next to b the other way).</summary>
        public void Allow(int a, Direction direction, int b)
        {
            var d = (int)direction;
            allowed[Index(d, Check(a), Check(b))] = true;
            allowed[Index(Opposite(d), b, a)] = true;
        }

        /// <summary>b may stand at x + 1 of a.</summary>
        public void AllowX(int a, int b) => Allow(a, Direction.PlusX, b);

        /// <summary>b may stand at y + 1 of a.</summary>
        public void AllowY(int a, int b) => Allow(a, Direction.PlusY, b);

        public bool IsAllowed(int a, Direction direction, int b) => allowed[Index((int)direction, Check(a), Check(b))];

        /// <summary>
        /// Learns rules from an example map: every pair of neighbours seen in the sample becomes allowed,
        /// and each tile's weight is how many times it appears. Tile ids must be 0 or greater.
        /// </summary>
        /// <param name="sample">Row-major tile ids, index = x + y * width.</param>
        /// <param name="periodic">Treat the sample as wrapping, so its right edge also touches its left edge.</param>
        public static AdjacencyRules Learn(int[] sample, int width, int height, bool periodic = false)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (width < 1 || height < 1 || sample.Length != width * height)
                throw new ArgumentException("Sample length must equal width * height.", nameof(sample));

            var max = 0;
            foreach (var t in sample)
            {
                if (t < 0) throw new ArgumentException("Tile ids must be 0 or greater.", nameof(sample));
                max = Math.Max(max, t);
            }

            var rules = new AdjacencyRules(max + 1);
            Array.Clear(rules.weights, 0, rules.weights.Length);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var a = sample[x + y * width];
                rules.weights[a] += 1;
                for (var d = 0; d < 2; d++) // +X and +Y; Allow() records the opposite pair too
                {
                    var nx = x + Dx[d];
                    var ny = y + Dy[d];
                    if (periodic) { nx = (nx + width) % width; ny = (ny + height) % height; }
                    else if (nx >= width || ny >= height) continue;
                    rules.Allow(a, (Direction)d, sample[nx + ny * width]);
                }
            }
            return rules;
        }

        private int Index(int d, int a, int b) => (d * TileCount + a) * TileCount + b;

        private int Check(int tile)
        {
            if ((uint)tile >= (uint)TileCount) throw new ArgumentOutOfRangeException(nameof(tile), $"Tile {tile} is outside 0..{TileCount - 1}.");
            return tile;
        }
    }
}
