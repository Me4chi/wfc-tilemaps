using System;

namespace WfcTilemaps
{
    /// <summary>Output size and solver options. Serializable, so it shows up in the Unity inspector.</summary>
    [Serializable]
    public class WfcSettings
    {
        public int Seed = 12345;
        public int Width = 48;
        public int Height = 32;
        /// <summary>Make the output tile seamlessly: the right edge must fit the left edge, the top the bottom.</summary>
        public bool Periodic;
        /// <summary>How many fresh starts to try when the solver paints itself into a corner.</summary>
        public int MaxAttempts = 20;

        public WfcSettings Clone() => (WfcSettings)MemberwiseClone();

        public void Validate()
        {
            if (Width < 1 || Height < 1) throw new ArgumentException("Width and Height must be at least 1.");
            if (Width * (long)Height > 4_000_000) throw new ArgumentException("Width * Height must not exceed 4,000,000 cells.");
            if (MaxAttempts < 1) throw new ArgumentException("MaxAttempts must be at least 1.");
        }
    }

    /// <summary>A tile the output must contain at a given cell, e.g. a door on the map edge.</summary>
    public readonly struct FixedTile
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Tile;

        public FixedTile(int x, int y, int tile)
        {
            X = x;
            Y = y;
            Tile = tile;
        }
    }

    public sealed class WfcResult
    {
        public bool Success { get; }
        /// <summary>Row-major tile ids, index = x + y * Width. Null when <see cref="Success"/> is false.</summary>
        public int[] Tiles { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Attempts used, including the successful one.</summary>
        public int Attempts { get; }

        internal WfcResult(bool success, int[] tiles, int width, int height, int attempts)
        {
            Success = success;
            Tiles = tiles;
            Width = width;
            Height = height;
            Attempts = attempts;
        }

        public int Get(int x, int y) => Tiles[x + y * Width];
    }
}
