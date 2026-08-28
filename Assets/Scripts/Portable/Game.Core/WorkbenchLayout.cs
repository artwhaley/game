using System;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Pure four-pane layout model (ticket 12): the four logical pane widths
    /// as fractions of the window, with Session and Phase stacked in the WPF
    /// center column, plus the minimum pixel widths. Ratios are
    /// always normalized to sum to 1.0; widths are derived from a window width
    /// with per-pane minimums enforced. No WPF or JSON dependency — portable,
    /// so the geometry is unit-testable from the .NET suite and the Unity thin
    /// host; persistence lives in the WPF host layer (LayoutPersistence).
    /// </summary>
    public sealed class WorkbenchLayout
    {
        /// <summary>The four panes in fixed order: Library, Session, Phase, Inspector.</summary>
        public enum Pane
        {
            Library = 0,
            Session = 1,
            Phase = 2,
            Inspector = 3,
        }

        public const double DefaultLibraryRatio = 0.18;
        public const double DefaultSessionRatio = 0.32;
        public const double DefaultPhaseRatio = 0.32;
        public const double DefaultInspectorRatio = 0.18;

        /// <summary>Default minimum per-pane width in pixels (nonzero so aggressive dragging cannot lose a pane).</summary>
        public const double MinPaneWidth = 120.0;

        private double[] _ratios = { DefaultLibraryRatio, DefaultSessionRatio, DefaultPhaseRatio, DefaultInspectorRatio };

        public double LibraryRatio => _ratios[0];
        public double SessionRatio => _ratios[1];
        public double PhaseRatio => _ratios[2];
        public double InspectorRatio => _ratios[3];

        /// <summary>The four ratios, in Pane order.</summary>
        public double[] AsArray() => (double[])_ratios.Clone();

        /// <summary>Replaces all four ratios and re-normalizes to a 1.0 sum.</summary>
        public void SetRatios(double library, double session, double phase, double inspector)
        {
            var values = new[] { library, session, phase, inspector };
            var sum = 0.0;
            for (var i = 0; i < values.Length; i++)
            {
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || values[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(values), $"pane {i} ratio must be a finite non-negative value.");
                }
                sum += values[i];
            }
            if (sum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(values), "at least one pane must have a positive ratio.");
            }
            for (var i = 0; i < values.Length; i++)
            {
                _ratios[i] = values[i] / sum;
            }
        }

        /// <summary>
        /// Pixel widths for a given window width: ratio-scaled, then the total
        /// width is re-distributed if any pane would fall below its minimum
        /// (taking the shortfall from the other panes proportionally).
        /// </summary>
        public double[] ComputeWidths(double windowWidth)
        {
            if (windowWidth < MinPaneWidth * 4)
            {
                windowWidth = MinPaneWidth * 4;
            }

            var widths = new double[4];
            for (var i = 0; i < 4; i++)
            {
                widths[i] = _ratios[i] * windowWidth;
            }

            // Enforce minimums: repeatedly take the shortfall from the largest pane.
            for (var pass = 0; pass < 4; pass++)
            {
                var underflow = 0.0;
                var adjust = false;
                for (var i = 0; i < 4; i++)
                {
                    if (widths[i] < MinPaneWidth)
                    {
                        underflow += MinPaneWidth - widths[i];
                        widths[i] = MinPaneWidth;
                        adjust = true;
                    }
                }
                if (!adjust) break;

                // Take the needed total from the panes above their minimum,
                // proportionally to how much headroom they have.
                var donors = 0.0;
                for (var i = 0; i < 4; i++)
                {
                    if (widths[i] > MinPaneWidth) donors += widths[i] - MinPaneWidth;
                }
                if (donors <= 0) break; // window too narrow; minimums win
                for (var i = 0; i < 4; i++)
                {
                    if (widths[i] > MinPaneWidth)
                    {
                        var headroom = widths[i] - MinPaneWidth;
                        widths[i] -= underflow * (headroom / donors);
                    }
                }
            }
            return widths;
        }
    }
}
