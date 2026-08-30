using System;
using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class DialogRngRegressionTests
    {
        private static DialogSnippetDefinition Snippet(string id, params string[] tags)
        {
            var s = new DialogSnippetDefinition { Id = id, Name = id, Text = "text " + id };
            foreach (var t in tags) s.DialogTagIds.Add(t);
            return s;
        }

        [Test]
        public void DialogSelectionDomain_IsIndependentOfSessionStream()
        {
            // The fixed dialog-domain salt must yield a stream distinct from the
            // session-selection stream for the same visible seed.
            var dialogA = SeededRandomDomains.CreateDialogSelection(7);
            var sessionA = SeededRandomDomains.CreateSessionSelection(7);
            var dialogB = SeededRandomDomains.CreateDialogSelection(7);
            var sessionB = SeededRandomDomains.CreateSessionSelection(7);

            var dialogStream = NextStream(dialogA, 50);
            var dialogStream2 = NextStream(dialogB, 50);
            var sessionStream = NextStream(sessionA, 50);
            var sessionStream2 = NextStream(sessionB, 50);

            CollectionAssert.AreEqual(dialogStream, dialogStream2, "dialog domain reproduces for the same seed");
            CollectionAssert.AreEqual(sessionStream, sessionStream2, "session domain reproduces for the same seed");
            CollectionAssert.AreNotEqual(dialogStream, sessionStream,
                "dialog and session streams must differ (independent domains)");
        }

        [Test]
        public void DialogSelectionDomain_IsIndependentOfPhaseRunCardStream()
        {
            var dialog = SeededRandomDomains.CreateDialogSelection(9);
            // Two factories with the same base seed hand the same per-run RNGs at
            // the same position; this is how per-run card streams reproduce.
            var cardA = SeededRandomDomains.CreatePhaseRunFactory(9).Create();
            var cardB = SeededRandomDomains.CreatePhaseRunFactory(9).Create();

            var dialogStream = NextStream(dialog, 40);
            var cardStream = NextStream(cardA, 40);
            var cardStream2 = NextStream(cardB, 40);

            CollectionAssert.AreEqual(cardStream, cardStream2, "card stream reproduces for the same base seed at the same position");
            CollectionAssert.AreNotEqual(dialogStream, cardStream,
                "dialog stream must not equal the PhaseRun/Card stream (no cross-domain perturbation)");
        }

        [Test]
        public void Select_AllMatchSemantics_AndStableOrderedCandidates()
        {
            var snippets = new List<DialogSnippetDefinition>
            {
                Snippet("b", "scary", "night"),
                Snippet("a", "scary"),
                Snippet("c", "night"),
                Snippet("d", "scary", "night", "wet"),
            };
            var selection = DialogSnippetSelector.Select(new[] { "scary", "night" }, snippets, SeededRandomDomains.CreateDialogSelection(1));

            // Candidates must be the ALL-match snippets, sorted by id, both tags present.
            CollectionAssert.AreEqual(
                new[] { "b", "d" },
                System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(selection.Candidates, s => s.Id)));
            Assert.That(selection.Snippet.DialogTagIds, Does.Contain("scary"));
            Assert.That(selection.Snippet.DialogTagIds, Does.Contain("night"));
        }

        [Test]
        public void Select_RejectsZeroRequiredTags_AsContentError()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DialogSnippetSelector.Select(new string[0], new List<DialogSnippetDefinition>(), SeededRandomDomains.CreateDialogSelection(1)));
        }

        [Test]
        public void Select_RejectsEmptyWhitespaceTagId_AsContentError()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DialogSnippetSelector.Select(new[] { "" }, new List<DialogSnippetDefinition>(), SeededRandomDomains.CreateDialogSelection(1)));
        }

        [Test]
        public void Select_RejectsNoMatchingSnippets_WithReadableTagNames()
        {
            var snippets = new List<DialogSnippetDefinition> { Snippet("a", "night") };
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DialogSnippetSelector.Select(new[] { "scary" }, snippets, SeededRandomDomains.CreateDialogSelection(1)));
            StringAssert.Contains("scary", ex.Message, "error must surface the tag name");
        }

        [Test]
        public void Select_SameSeed_MakesTheSameChoice()
        {
            var snippets = new List<DialogSnippetDefinition>
            {
                Snippet("p1", "t1"), Snippet("p2", "t1"), Snippet("p3", "t1"), Snippet("p4", "t1"),
            };
            var a = DialogSnippetSelector.Select(new[] { "t1" }, snippets, SeededRandomDomains.CreateDialogSelection(123));
            var b = DialogSnippetSelector.Select(new[] { "t1" }, snippets, SeededRandomDomains.CreateDialogSelection(123));
            Assert.AreEqual(a.Snippet.Id, b.Snippet.Id, "deterministic selection for a fixed seed");
        }

        [Test]
        public void Select_AllCandidatesAreReachable_AcrossTheDomain()
        {
            // Uniform over the candidate set: over enough seeds each candidate is chosen at least once.
            var snippets = new List<DialogSnippetDefinition> { Snippet("x1", "t1"), Snippet("x2", "t1"), Snippet("x3", "t1") };
            var chosen = new HashSet<string>();
            for (var seed = 0; seed < 500; seed++)
            {
                var selection = DialogSnippetSelector.Select(new[] { "t1" }, snippets, SeededRandomDomains.CreateDialogSelection(seed));
                chosen.Add(selection.Snippet.Id);
            }
            CollectionAssert.AreEquivalent(new[] { "x1", "x2", "x3" }, chosen);
        }

        private static List<int> NextStream(IRandomSource source, int count)
        {
            var result = new List<int>();
            for (var i = 0; i < count; i++) result.Add(source.NextInt(0, 1_000_000));
            return result;
        }
    }
}