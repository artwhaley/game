using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class SeedTests
    {
        [Test]
        public void Core_CanReferenceContent()
        {
            Assert.AreEqual("core:Game.Content", CoreSeed.Describe());
            Assert.AreEqual("Game.Content", ContentSeed.Marker);
        }
    }
}
