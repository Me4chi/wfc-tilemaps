using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WfcTilemaps.Tests
{
    public class PatternModelTests
    {
        // 0 wall, 1 floor, 2 door: rooms joined by doors.
        private static readonly int[] Rooms =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 1, 1, 1, 0, 1, 1, 1, 0,
            0, 1, 1, 1, 2, 1, 1, 1, 0,
            0, 1, 1, 1, 0, 1, 1, 1, 0,
            0, 0, 2, 0, 0, 0, 0, 0, 0,
            0, 1, 1, 1, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        private const int W = 9, H = 7;

        private static HashSet<string> Windows(int[] cells, int width, int height, int n, bool periodic)
        {
            var set = new HashSet<string>();
            var maxX = periodic ? width : width - n + 1;
            var maxY = periodic ? height : height - n + 1;
            for (var y = 0; y < maxY; y++)
            for (var x = 0; x < maxX; x++)
            {
                var p = new int[n * n];
                for (var j = 0; j < n; j++)
                    for (var i = 0; i < n; i++)
                        p[i + j * n] = cells[(x + i) % width + (y + j) % height * width];
                set.Add(string.Join(",", p));
            }
            return set;
        }

        [Test]
        public void EveryWindowOfTheOutputComesFromTheSample([Values(1, 7, 21)] int seed)
        {
            var model = PatternModel.Learn(Rooms, W, H);
            var result = model.Solve(new WfcSettings { Seed = seed, Width = 30, Height = 20 });
            Assert.That(result.Success, Is.True);
            Assert.That(result.Tiles.Length, Is.EqualTo(30 * 20));
            var allowed = Windows(Rooms, W, H, 3, false);
            foreach (var window in Windows(result.Tiles, 30, 20, 3, false))
                Assert.That(allowed, Does.Contain(window));
        }

        [Test]
        public void PeriodicOutputWrapsAround()
        {
            var model = PatternModel.Learn(Rooms, W, H, periodicSample: true);
            var result = model.Solve(new WfcSettings { Seed = 4, Width = 18, Height = 14, Periodic = true });
            Assert.That(result.Success, Is.True);
            var allowed = Windows(Rooms, W, H, 3, true);
            foreach (var window in Windows(result.Tiles, 18, 14, 3, true))
                Assert.That(allowed, Does.Contain(window));
        }

        [Test]
        public void SymmetryAddsRotatedAndMirroredFragments()
        {
            var plain = PatternModel.Learn(Rooms, W, H);
            var symmetric = PatternModel.Learn(Rooms, W, H, symmetry: true);
            Assert.That(symmetric.PatternCount, Is.GreaterThan(plain.PatternCount));
            var result = symmetric.Solve(new WfcSettings { Seed = 2, Width = 24, Height = 24 });
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void SameSeedSameMap()
        {
            var model = PatternModel.Learn(Rooms, W, H, 2);
            var a = model.Solve(new WfcSettings { Seed = 8, Width = 20, Height = 12 });
            var b = model.Solve(new WfcSettings { Seed = 8, Width = 20, Height = 12 });
            Assert.That(a.Tiles, Is.EqualTo(b.Tiles));
        }

        [Test]
        public void InvalidInputThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PatternModel.Learn(Rooms, W, H, 1));
            Assert.Throws<ArgumentException>(() => PatternModel.Learn(Rooms, W, H, 8));
            Assert.Throws<ArgumentException>(() => PatternModel.Learn(new[] { 0, 1 }, 3, 1));
            var model = PatternModel.Learn(Rooms, W, H);
            Assert.Throws<ArgumentException>(() => model.Solve(new WfcSettings { Width = 2, Height = 10 }));
        }
    }
}
