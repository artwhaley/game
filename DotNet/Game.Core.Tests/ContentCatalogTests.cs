using System;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    public class ContentCatalogTests
    {
        private static GameContentDefinition MakeContent()
        {
            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck" }
            };
            content.Sessions.Add(new SessionDefinition { Id = "s1", Title = "S" });
            content.Phases.Add(new PhaseDefinition { Id = "p1", Title = "P" });
            content.Cards.Add(new CardDefinition { Id = "c1", Title = "C" });
            content.Actions.Add(new DebugActionDefinition { Id = "a1" });
            content.Resources.Add(new ResourceDefinition { Id = "r1", Kind = "cutscene" });
            return content;
        }

        [Test]
        public void Indexes_AllEntityTypes_ById()
        {
            var catalog = new ContentCatalog(MakeContent());

            Assert.AreEqual("S", catalog.SessionById("s1").Title);
            Assert.AreEqual("P", catalog.PhaseById("p1").Title);
            Assert.AreEqual("C", catalog.CardById("c1").Title);
            Assert.AreEqual("a1", catalog.ActionById("a1").Id);
            Assert.AreEqual("r1", catalog.ResourceById("r1").Id);
        }

        [Test]
        public void MissingSession_ThrowsWithContext()
        {
            var catalog = new ContentCatalog(MakeContent());

            var ex = Assert.Throws<InvalidOperationException>(() => catalog.SessionById("nope"));
            StringAssert.Contains("no Session", ex.Message);
            StringAssert.Contains("nope", ex.Message);
        }

        [Test]
        public void MissingAction_ThrowsWithContext()
        {
            var catalog = new ContentCatalog(MakeContent());

            var ex = Assert.Throws<InvalidOperationException>(() => catalog.ActionById("nope"));
            StringAssert.Contains("no Action", ex.Message);
        }

        [Test]
        public void MissingResource_ThrowsWithContext()
        {
            var catalog = new ContentCatalog(MakeContent());

            var ex = Assert.Throws<InvalidOperationException>(() => catalog.ResourceById("nope"));
            StringAssert.Contains("no Resource", ex.Message);
        }

        [Test]
        public void DuplicateId_Throws()
        {
            var content = MakeContent();
            content.Phases.Add(new PhaseDefinition { Id = "p1" });

            var ex = Assert.Throws<InvalidOperationException>(() => new ContentCatalog(content));
            StringAssert.Contains("duplicate Phase id 'p1'", ex.Message);
        }

        [Test]
        public void MissingId_Throws()
        {
            var content = MakeContent();
            content.Cards.Add(new CardDefinition { Title = "no id" });

            var ex = Assert.Throws<InvalidOperationException>(() => new ContentCatalog(content));
            StringAssert.Contains("missing/empty id", ex.Message);
        }

        [Test]
        public void NullDeck_Throws()
        {
            var content = MakeContent();
            content.Deck = null;

            Assert.Throws<InvalidOperationException>(() => new ContentCatalog(content));
        }

        [Test]
        public void NullContent_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ContentCatalog(null));
        }
    }
}
