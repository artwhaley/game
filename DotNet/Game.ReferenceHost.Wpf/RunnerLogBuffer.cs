using System;
using System.Collections.Generic;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Shared line-bounded log storage/export behavior for the runner surfaces.</summary>
    public static class RunnerLogBuffer
    {
        public const int MaxLines = 10000;

        public static void Append(IList<string> lines, string message)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var split = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in split) lines.Add(line);
            while (lines.Count > MaxLines) lines.RemoveAt(0);
        }

        public static void Clear(IList<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            lines.Clear();
        }

        public static string Export(IEnumerable<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            return string.Join(Environment.NewLine, lines);
        }
    }
}
