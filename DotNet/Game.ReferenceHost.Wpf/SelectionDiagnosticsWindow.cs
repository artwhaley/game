using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 15: per-Phase selection diagnostics. For the selected Phase,
    /// profile, and a Happiness slider (0–100, test override — never
    /// persisted), shows every Card with eligibility, typed rejection
    /// reasons, and the computed weight; search/filter included.
    /// </summary>
    public partial class SelectionDiagnosticsWindow : Window
    {
        private readonly GameContentDefinition _content;
        private readonly PhaseDefinition _phase;
        private readonly CardSelectionProfile _profile;
        private readonly SessionCardWeightingDefinition _weighting;

        public SelectionDiagnosticsWindow(GameContentDefinition content, PhaseDefinition phase,
            UserProfileSnapshot profileSnapshot, SessionCardWeightingDefinition weighting)
        {
            InitializeComponent();
            _content = content;
            _phase = phase;
            _profile = CardSelectionProfile.FromProfile(profileSnapshot);
            _weighting = weighting ?? new SessionCardWeightingDefinition();

            Title = "Selection Diagnostics — " + phase.Title;
            var allTitles = phase.MustHaveAllCardTags.Select(id => TitleOf(content.CardTagDefinitions, id)).ToList();
            var anyTitles = phase.MustHaveAnyCardTags.Select(id => TitleOf(content.CardTagDefinitions, id)).ToList();
            PhaseText.Text = $"Phase '{phase.Title}' · ALL [{string.Join(", ", allTitles)}] · ANY [{string.Join(", ", anyTitles)}]";
            HappinessSlider.Value = 50;
            HappinessSlider.ValueChanged += (_, _) => Refresh();
            SearchBox.TextChanged += (_, _) => Refresh();
            Refresh();
        }

        private static string TitleOf(List<TruthCardGame.Content.CardTagDefinition> definitions, string id)
        {
            var match = definitions.FirstOrDefault(t => t.Id == id);
            return match?.Title ?? id;
        }

        private void Refresh()
        {
            var happiness = (float)HappinessSlider.Value;
            HappinessText.Text = happiness.ToString("0");
            var query = (SearchBox.Text ?? "").Trim();

            var weighting = _weighting;
            var rows = new List<DiagnosticRow>();
            foreach (var card in _content.Cards)
            {
                if (!string.IsNullOrEmpty(query) &&
                    (card.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var eligibility = CardEligibilityEngine.EvaluateOne(card, _phase, _profile);
                var weight = eligibility.IsEligible
                    ? CardWeightCalculator.ComputeWeight(card, _profile, weighting, happiness)
                    : (float?)null;
                rows.Add(new DiagnosticRow
                {
                    Title = card.Title,
                    Status = eligibility.IsEligible ? "Eligible" : "Rejected",
                    Weight = weight,
                    Reasons = string.Join("; ", eligibility.Reasons.Select(r => r.Describe())),
                });
            }

            var eligibleCount = rows.Count(r => r.Weight != null);
            var tuningLabel = "default 1.0 tuning";
            if (_weighting != null && (_weighting.LoveBase != 1f || _weighting.LikeBase != 1f || _weighting.TortureBase != 1f ||
                _weighting.LoveHappinessGain != 1f || _weighting.LikeHappinessGain != 1f || _weighting.TortureUnhappinessGain != 1f))
            {
                tuningLabel = "this session's tuning";
            }
            SummaryText.Text = $"{eligibleCount} of {rows.Count} shown cards eligible · weights use {tuningLabel}";
            DiagnosticsList.ItemsSource = rows;
        }

        private sealed class DiagnosticRow
        {
            public string Title { get; set; }
            public string Status { get; set; }
            public float? Weight { get; set; }
            public string Reasons { get; set; }

            public string WeightText => Weight == null ? "—" : Weight.Value.ToString("0.###");
        }
    }
}
