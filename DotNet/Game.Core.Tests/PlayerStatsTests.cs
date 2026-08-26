using NUnit.Framework;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class PlayerStatsTests
    {
        [Test]
        public void UnknownKey_ReadsAsZero()
        {
            var stats = new PlayerStats();
            Assert.AreEqual(0, stats.Get("missing"));
        }

        [Test]
        public void Add_Accumulates_OnSameKey()
        {
            var stats = new PlayerStats();
            stats.Add("courage", 2);
            stats.Add("courage", 3);
            Assert.AreEqual(5, stats.Get("courage"));
        }

        [Test]
        public void Add_NegativeAmount_Subtracts()
        {
            var stats = new PlayerStats();
            stats.Add("fear", 5);
            stats.Add("fear", -3);
            Assert.AreEqual(2, stats.Get("fear"));
        }

        [Test]
        public void Add_UnknownKey_StartsFromZero()
        {
            var stats = new PlayerStats();
            stats.Add("new", 4);
            Assert.AreEqual(4, stats.Get("new"));
        }
    }
}
