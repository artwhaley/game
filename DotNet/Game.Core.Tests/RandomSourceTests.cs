using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class RandomSourceTests
    {
        [Test]
        public void NextInt_RespectsBounds()
        {
            var source = new SystemRandomSource(12345);
            for (var i = 0; i < 10000; i++)
            {
                var value = source.NextInt(2, 5);
                Assert.That(value, Is.InRange(2, 4));
            }
        }

        [Test]
        public void NextInt_SingleValueRange_AlwaysReturnsMin()
        {
            var source = new SystemRandomSource(1);
            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(3, source.NextInt(3, 4));
            }
        }

        [Test]
        public void NextInt_CoversFullRange_IncludingMaxExclusiveMinusOne()
        {
            var source = new SystemRandomSource(7);
            var seen = new HashSet<int>();
            for (var i = 0; i < 5000; i++)
            {
                seen.Add(source.NextInt(0, 3));
            }
            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SeededSources_ProduceIdenticalSequences()
        {
            var a = new SystemRandomSource(42);
            var b = new SystemRandomSource(42);
            for (var i = 0; i < 50; i++)
            {
                Assert.AreEqual(a.NextInt(0, 100), b.NextInt(0, 100));
            }
        }
    }
}
