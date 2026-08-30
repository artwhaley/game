using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Result of a DialogFromTags selection: the chosen snippet plus the
    /// candidate set it was drawn from (for diagnostics). Pure data — no host
    /// services, no logging side effects.
    /// </summary>
    public sealed class DialogSnippetSelection
    {
        public DialogSnippetDefinition Snippet { get; }
        public IReadOnlyList<DialogSnippetDefinition> Candidates { get; }

        public DialogSnippetSelection(DialogSnippetDefinition snippet, IReadOnlyList<DialogSnippetDefinition> candidates)
        {
            Snippet = snippet;
            Candidates = candidates;
        }
    }

    /// <summary>
    /// Pure Core selector for Dialog From Tags (Docs/ToyPatternDialog/
    /// DIALOG-CATALOG-SPEC.md). Semantics:
    ///
    /// - Candidate = snippet whose DialogTagIds contain ALL required tag IDs.
    /// - Zero required tags: content error (loud).
    /// - Zero candidates: content error listing the required tag IDs (loud).
    /// - Selection: uniform random among candidates in stable deterministic
    ///   ordering (sorted by snippet Id). No weighting, no anti-repeat.
    ///
    /// The selector consumes the dialog RNG exactly once per invocation and is
    /// independent from Session/PhaseRun/Card streams by construction (the
    /// caller supplies the session-scoped dialog RNG).
    /// </summary>
    public static class DialogSnippetSelector
    {
        public static DialogSnippetSelection Select(
            IReadOnlyList<string> requiredDialogTagIds,
            IReadOnlyList<DialogSnippetDefinition> snippetCatalog,
            IRandomSource dialogRng)
        {
            if (requiredDialogTagIds == null || requiredDialogTagIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "DialogFromTags: no required tags configured. At least one required Dialog Tag is needed " +
                    "(content error — set tags in the editor).");
            }
            if (snippetCatalog == null)
            {
                throw new InvalidOperationException("DialogFromTags: snippet catalog is missing (null).");
            }
            if (dialogRng == null)
            {
                throw new InvalidOperationException("DialogFromTags: dialog RNG is not available.");
            }

            // Deduplicate while preserving authored order.
            var distinctRequired = new List<string>();
            foreach (var tagId in requiredDialogTagIds)
            {
                if (string.IsNullOrWhiteSpace(tagId))
                {
                    throw new InvalidOperationException(
                        "DialogFromTags: an empty Dialog Tag id appears in the required tags.");
                }
                if (!distinctRequired.Contains(tagId)) distinctRequired.Add(tagId);
            }

            // Stable deterministic candidate ordering: sort by snippet Id.
            var candidates = new List<DialogSnippetDefinition>();
            foreach (var snippet in snippetCatalog)
            {
                if (snippet == null) continue;
                var tags = snippet.DialogTagIds ?? new List<string>();
                var matchesAll = true;
                foreach (var required in distinctRequired)
                {
                    if (!tags.Contains(required))
                    {
                        matchesAll = false;
                        break;
                    }
                }
                if (matchesAll) candidates.Add(snippet);
            }

            if (candidates.Count == 0)
            {
                var readable = string.Join(", ", distinctRequired);
                throw new InvalidOperationException(
                    $"DialogFromTags: no Dialog Snippet matches the required tags ({readable}). " +
                    "Content error — create snippets carrying these tags.");
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var index = dialogRng.NextInt(0, candidates.Count);
            return new DialogSnippetSelection(candidates[index], candidates);
        }
    }
}
