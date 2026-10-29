using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace WfcTilemaps
{
    /// <summary>
    /// Learns from a hand-painted sample Tilemap and fills the target Tilemap with a new map in the same style.
    /// Empty cells in the sample count as a tile too, so holes and open space are reproduced.
    /// Use the component's context menu to generate in edit mode.
    /// </summary>
    [AddComponentMenu("WFC Tilemaps/WFC Tilemap Builder")]
    public class WfcTilemapBuilder : MonoBehaviour
    {
        [Tooltip("A small example map painted by hand. It can live on a disabled GameObject.")]
        public Tilemap Sample;
        [Tooltip("Where the generated map is written. It is cleared first.")]
        public Tilemap Target;

        public enum ModelType
        {
            /// <summary>Copies N×N fragments of the sample: reproduces shapes. The usual choice.</summary>
            Overlapping,
            /// <summary>Only pairs of neighbouring tiles: looser, for tile sets designed to snap together.</summary>
            Adjacent,
        }

        public WfcSettings Settings = new WfcSettings();

        public ModelType Model = ModelType.Overlapping;

        [Tooltip("Overlapping model: fragment size. 2 is loose, 3 is the usual choice, 4+ copies the sample more literally.")]
        [Min(2)] public int PatternSize = 3;

        [Tooltip("Overlapping model: also learn rotated and mirrored fragments. Use only when your tiles look the same rotated.")]
        public bool Symmetry;

        [Tooltip("Treat the sample as wrapping, so its right edge also touches its left edge.")]
        public bool PeriodicSample;

        [Tooltip("Generate automatically when play mode starts.")]
        public bool GenerateOnStart = true;

        [Tooltip("Pick a new random seed every time Generate() is called.")]
        public bool RandomizeSeed;

        /// <summary>The last solver result, or null before the first run.</summary>
        public WfcResult Result { get; private set; }

        /// <summary>Raised after a successful generation.</summary>
        public event Action<WfcResult> Generated;

        private void Start()
        {
            if (GenerateOnStart) Generate();
        }

        /// <summary>Generates and writes the map. Returns false (and leaves Target untouched) if no map was found.</summary>
        [ContextMenu("Generate")]
        public bool Generate()
        {
            if (Sample == null || Target == null)
            {
                Debug.LogError("WFC Tilemap Builder: assign both Sample and Target.", this);
                return false;
            }
            if (RandomizeSeed) Settings.Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            var palette = new List<TileBase>();
            var sample = ReadTilemap(Sample, palette, out var width, out var height);
            if (sample == null)
            {
                Debug.LogError("WFC Tilemap Builder: the sample Tilemap is empty.", this);
                return false;
            }

            Result = Model == ModelType.Overlapping
                ? PatternModel.Learn(sample, width, height, PatternSize, PeriodicSample, Symmetry).Solve(Settings)
                : WfcSolver.Solve(AdjacencyRules.Learn(sample, width, height, PeriodicSample), Settings);
            if (!Result.Success)
            {
                Debug.LogWarning($"WFC Tilemap Builder: no map found in {Result.Attempts} attempts. Try another seed, more attempts or a richer sample.", this);
                return false;
            }

            WriteTilemap(Target, Result, palette);
            Generated?.Invoke(Result);
            return true;
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (Target != null) Target.ClearAllTiles();
        }

        /// <summary>
        /// Converts a Tilemap into tile ids. <paramref name="palette"/> receives the TileBase for each id;
        /// empty cells become a null entry. Returns null for an empty Tilemap.
        /// </summary>
        public static int[] ReadTilemap(Tilemap map, List<TileBase> palette, out int width, out int height)
        {
            map.CompressBounds();
            var bounds = map.cellBounds;
            width = bounds.size.x;
            height = bounds.size.y;
            if (width <= 0 || height <= 0) return null;

            var ids = new Dictionary<TileBase, int>();
            var emptyId = -1;
            var cells = new int[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var tile = map.GetTile(new Vector3Int(bounds.xMin + x, bounds.yMin + y, bounds.zMin));
                int id;
                if (tile == null)
                {
                    if (emptyId < 0) { emptyId = palette.Count; palette.Add(null); }
                    id = emptyId;
                }
                else if (!ids.TryGetValue(tile, out id))
                {
                    id = palette.Count;
                    ids.Add(tile, id);
                    palette.Add(tile);
                }
                cells[x + y * width] = id;
            }
            return cells;
        }

        /// <summary>Writes a result into a Tilemap starting at cell (0, 0), after clearing it.</summary>
        public static void WriteTilemap(Tilemap map, WfcResult result, IReadOnlyList<TileBase> palette)
        {
            map.ClearAllTiles();
            var positions = new Vector3Int[result.Tiles.Length];
            var tiles = new TileBase[result.Tiles.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = new Vector3Int(i % result.Width, i / result.Width, 0);
                tiles[i] = palette[result.Tiles[i]];
            }
            map.SetTiles(positions, tiles);
        }
    }
}
