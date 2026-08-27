using System;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// View-model tests for the four-pane workbench (ticket 12): ratio
    /// normalization/enforcement and pixel width distribution under minimums.
    /// Pure — no WPF dependency, so these run in the cross-platform .NET CI
    /// worker. (JSON persistence lives WPF-side in LayoutPersistence and is
    /// exercised via the running shell, since the test project targets net10.0
    /// and cannot reference the WinExe host.)
    /// </summary>
    [TestFixture]
    public class WorkbenchLayoutTests
    {
        [Test]
        public void DefaultRatios_SumToOne()
        {
            var layout = new WorkbenchLayout();
            var sum = layout.LibraryRatio + layout.SessionRatio
                    + layout.PhaseRatio + layout.InspectorRatio;
            Assert.That(sum, Is.EqualTo(1.0).Within(1e-6));
        }

        [Test]
        public void DefaultRatios_Are182218()
        {
            var layout = new WorkbenchLayout();
            Assert.That(layout.LibraryRatio, Is.EqualTo(0.18).Within(1e-6));
            Assert.That(layout.SessionRatio, Is.EqualTo(0.32).Within(1e-6));
            Assert.That(layout.PhaseRatio, Is.EqualTo(0.32).Within(1e-6));
            Assert.That(layout.InspectorRatio, Is.EqualTo(0.18).Within(1e-6));
        }

        [Test]
        public void SetRatios_NormalizesUnguardedInputs()
        {
            var layout = new WorkbenchLayout();
            layout.SetRatios(18, 32, 32, 18); // user-authored absolute weights
            Assert.That(layout.LibraryRatio, Is.EqualTo(0.18).Within(1e-6));
            Assert.That(layout.SessionRatio, Is.EqualTo(0.32).Within(1e-6));
            Assert.That(layout.PhaseRatio, Is.EqualTo(0.32).Within(1e-6));
            Assert.That(layout.InspectorRatio, Is.EqualTo(0.18).Within(1e-6));
        }

        [Test]
        public void SetRatios_AlwaysRenormalizesToOne()
        {
            var layout = new WorkbenchLayout();
            layout.SetRatios(0.1, 0.2, 0.3, 0.4);
            var sum = layout.AsArray().Sum();
            Assert.That(sum, Is.EqualTo(1.0).Within(1e-6));
        }

        [Test]
        public void SetRatios_RejectsNegative()
        {
            var layout = new WorkbenchLayout();
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.SetRatios(-1, 1, 1, 1));
        }

        [Test]
        public void SetRatios_RejectsNaN()
        {
            var layout = new WorkbenchLayout();
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.SetRatios(double.NaN, 1, 1, 1));
        }

        [Test]
        public void SetRatios_RejectsAllZero()
        {
            var layout = new WorkbenchLayout();
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.SetRatios(0, 0, 0, 0));
        }

        [Test]
        public void ComputeWidths_SumsToWindowWidth()
        {
            var layout = new WorkbenchLayout(); // defaults 18/32/32/18
            var widths = layout.ComputeWidths(1600);
            Assert.That(widths.Sum(), Is.EqualTo(1600).Within(1e-6));
            // Default proportions honored at ultrawide.
            Assert.That(widths[0], Is.EqualTo(288).Within(1e-6)); // 18% of 1600
            Assert.That(widths[1], Is.EqualTo(512).Within(1e-6)); // 32%
            Assert.That(widths[2], Is.EqualTo(512).Within(1e-6)); // 32%
            Assert.That(widths[3], Is.EqualTo(288).Within(1e-6)); // 18%
        }

        [Test]
        public void ComputeWidths_EnforcesMinimums_NoPaneLost()
        {
            var layout = new WorkbenchLayout();
            // A window too narrow for four panes at any positive ratio.
            var widths = layout.ComputeWidths(200);
            // Every pane stays at or above the minimum, and total width is the
            // clamped floor (4 * MinPaneWidth) when the window is too narrow.
            foreach (var width in widths)
            {
                Assert.That(width, Is.GreaterThanOrEqualTo(WorkbenchLayout.MinPaneWidth - 1e-6));
            }
            Assert.That(widths.Sum(), Is.EqualTo(WorkbenchLayout.MinPaneWidth * 4).Within(1e-6));
        }

        [Test]
        public void ComputeWidths_TakesShortfallFromOtherPanes()
        {
            // A tiny library pane forces the others to give up width to keep it
            // above the minimum, and the total still sums to the window width.
            var layout = new WorkbenchLayout();
            layout.SetRatios(0.02, 0.40, 0.38, 0.20);
            var widths = layout.ComputeWidths(1000);
            Assert.That(widths[0], Is.GreaterThanOrEqualTo(WorkbenchLayout.MinPaneWidth - 1e-6));
            Assert.That(widths.Sum(), Is.EqualTo(1000).Within(1e-6));
        }
    }
}