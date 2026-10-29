using System;
using System.Linq;
using NUnit.Framework;

namespace WfcTilemaps.Tests
{
    public class WfcSolverTests
    {
        // ~ water, . sand, " grass, T forest. Water never touches grass or forest directly.
        private static readonly string[] Island =
        {
            "~~~~~~~~~~~~",
            "~~~....~~~~~",
            "~~..\"\"\"..~~~",
            "~~.\"\"TT\"\".~~",
            "~.\"\"TTTT\"\".~",
            "~.\"\"TTT\"\"\".~",
            "~~.\"\"\"\"\"..~~",
            "~~~......~~~",
            "~~~~~~~~~~~~",
        };

        private static int[] Parse(string[] rows, out int width, out int height)
        {
            const string symbols = "~.\"T";
            width = rows[0].Length;
            height = rows.Length;
            var cells = new int[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    cells[x + y * width] = symbols.IndexOf(rows[y][x]);
            return cells;
        }

        private static AdjacencyRules IslandRules(bool periodic = false)
        {
            var sample = Parse(Island, out var w, out var h);
            return AdjacencyRules.Learn(sample, w, h, periodic);
        }

        private static void AssertRespectsRules(AdjacencyRules rules, WfcResult result, bool periodic)
        {
            Assert.That(result.Success, Is.True);
            for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
            for (var d = 0; d < 2; d++)
            {
                var nx = x + AdjacencyRules.Dx[d];
                var ny = y + AdjacencyRules.Dy[d];
                if (periodic) { nx %= result.Width; ny %= result.Height; }
                else if (nx >= result.Width || ny >= result.Height) continue;
                Assert.That(rules.IsAllowed(result.Get(x, y), (Direction)d, result.Get(nx, ny)), Is.True,
                    $"Tiles at ({x}, {y}) and ({nx}, {ny}) are not allowed together.");
            }
        }

        [Test]
        public void LearnRecordsNeighboursAndCounts()
        {
            var rules = IslandRules();
            Assert.That(rules.TileCount, Is.EqualTo(4));
            Assert.That(rules.IsAllowed(0, Direction.PlusX, 1), Is.True);
            Assert.That(rules.IsAllowed(1, Direction.MinusX, 0), Is.True);
            Assert.That(rules.IsAllowed(0, Direction.PlusX, 2), Is.False, "water never touches grass in the sample");
            Assert.That(rules.IsAllowed(0, Direction.PlusY, 3), Is.False);
            var sample = Parse(Island, out _, out _);
            Assert.That(rules.GetWeight(3), Is.EqualTo(sample.Count(c => c == 3)));
        }

        [Test]
        public void OutputOnlyUsesLearnedNeighbours([Values(1, 2, 3, 42, 777)] int seed)
        {
            var rules = IslandRules();
            var settings = new WfcSettings { Seed = seed, Width = 40, Height = 30 };
            AssertRespectsRules(rules, WfcSolver.Solve(rules, settings), false);
        }

        [Test]
        public void PeriodicOutputWrapsSeamlessly()
        {
            var rules = IslandRules(periodic: true);
            var settings = new WfcSettings { Seed = 9, Width = 24, Height = 24, Periodic = true };
            AssertRespectsRules(rules, WfcSolver.Solve(rules, settings), true);
        }

        [Test]
        public void SameSeedSameMapDifferentSeedDifferentMap()
        {
            var rules = IslandRules();
            var a = WfcSolver.Solve(rules, new WfcSettings { Seed = 5 });
            var b = WfcSolver.Solve(rules, new WfcSettings { Seed = 5 });
            var c = WfcSolver.Solve(rules, new WfcSettings { Seed = 6 });
            Assert.That(a.Tiles, Is.EqualTo(b.Tiles));
            Assert.That(a.Tiles, Is.Not.EqualTo(c.Tiles));
        }

        [Test]
        public void FixedTilesAreKept()
        {
            var rules = IslandRules();
            var fixedTiles = new[] { new FixedTile(0, 0, 0), new FixedTile(20, 15, 3), new FixedTile(39, 29, 0) };
            var result = WfcSolver.Solve(rules, new WfcSettings { Seed = 3, Width = 40, Height = 30 }, fixedTiles);
            AssertRespectsRules(rules, result, false);
            foreach (var f in fixedTiles) Assert.That(result.Get(f.X, f.Y), Is.EqualTo(f.Tile));
        }

        [Test]
        public void ContradictoryFixedTilesFailWithoutRetrying()
        {
            var rules = IslandRules();
            // Water directly next to forest never happens in the sample.
            var result = WfcSolver.Solve(rules, new WfcSettings { Width = 10, Height = 10 },
                new[] { new FixedTile(4, 4, 0), new FixedTile(5, 4, 3) });
            Assert.That(result.Success, Is.False);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(result.Tiles, Is.Null);
        }

        [Test]
        public void ZeroWeightRemovesTile()
        {
            var rules = IslandRules();
            rules.SetWeight(3, 0);
            var result = WfcSolver.Solve(rules, new WfcSettings { Seed = 11, Width = 30, Height = 20 });
            AssertRespectsRules(rules, result, false);
            Assert.That(result.Tiles, Has.No.Member(3));
        }

        [Test]
        public void HandWrittenRulesMakeACheckerboard()
        {
            var rules = new AdjacencyRules(2);
            rules.AllowX(0, 1);
            rules.AllowX(1, 0);
            rules.AllowY(0, 1);
            rules.AllowY(1, 0);
            var result = WfcSolver.Solve(rules, new WfcSettings { Seed = 1, Width = 9, Height = 7 });
            Assert.That(result.Success, Is.True);
            var first = result.Get(0, 0);
            for (var y = 0; y < 7; y++)
                for (var x = 0; x < 9; x++)
                    Assert.That(result.Get(x, y), Is.EqualTo((first + x + y) % 2));
        }

        [Test]
        public void ImpossibleRulesFailImmediately()
        {
            var rules = new AdjacencyRules(2);
            rules.AllowX(0, 0); // nothing may ever stand above or below anything
            var result = WfcSolver.Solve(rules, new WfcSettings { Width = 3, Height = 3, MaxAttempts = 4 });
            Assert.That(result.Success, Is.False);
            Assert.That(result.Attempts, Is.EqualTo(1));
        }

        [Test]
        public void SideWithoutNeighbourRulesIsFineOnTheMapEdge()
        {
            var rules = new AdjacencyRules(2);
            rules.AllowX(0, 0);
            rules.AllowX(0, 1); // tile 1 may only end a row: nothing is allowed at its +X
            var result = WfcSolver.Solve(rules, new WfcSettings { Seed = 2, Width = 5, Height = 1 });
            Assert.That(result.Success, Is.True);
            Assert.That(result.Tiles.Take(4), Has.No.Member(1));
        }

        [Test]
        public void InvalidInputThrows()
        {
            var rules = IslandRules();
            Assert.Throws<ArgumentException>(() => WfcSolver.Solve(rules, new WfcSettings { Width = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => WfcSolver.Solve(rules, new WfcSettings { Width = 4, Height = 4 }, new[] { new FixedTile(4, 0, 0) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => WfcSolver.Solve(rules, new WfcSettings(), new[] { new FixedTile(0, 0, 9) }));
            Assert.Throws<ArgumentException>(() => AdjacencyRules.Learn(new[] { 0, -1 }, 2, 1));
            Assert.Throws<ArgumentException>(() => AdjacencyRules.Learn(new[] { 0, 1, 2 }, 2, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.SetWeight(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.SetWeight(0, double.NaN));
        }
    }
}
