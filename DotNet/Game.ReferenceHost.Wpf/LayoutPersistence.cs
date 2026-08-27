using System;
using System.IO;
using System.Text.Json;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// WPF-only persistence for the WorkbenchLayout ratios (ticket 12). Lives
    /// in the Windows host where System.Text.Json is available; the portable
    /// layout model itself stays JSON-free. Path defaults to %LocalAppData%.
    /// </summary>
    public static class LayoutPersistence
    {
        private const string SettingsFileName = "workbench-layout.json";

        private sealed class Persisted
        {
            public double Library { get; set; } = WorkbenchLayout.DefaultLibraryRatio;
            public double Session { get; set; } = WorkbenchLayout.DefaultSessionRatio;
            public double Phase { get; set; } = WorkbenchLayout.DefaultPhaseRatio;
            public double Inspector { get; set; } = WorkbenchLayout.DefaultInspectorRatio;
        }

        /// <summary>Default settings path inside %LocalAppData%\TruthCardGame.</summary>
        public static string DefaultSettingsPath()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "TruthCardGame", SettingsFileName);
        }

        public static WorkbenchLayout Load(string path)
        {
            var layout = new WorkbenchLayout();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return layout;
            try
            {
                var persisted = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(path));
                if (persisted != null)
                {
                    layout.SetRatios(persisted.Library, persisted.Session, persisted.Phase, persisted.Inspector);
                }
            }
            catch (Exception)
            {
                // Corrupt/foreign settings fall back to defaults; never crash the shell.
            }
            return layout;
        }

        public static void Save(string path, WorkbenchLayout layout)
        {
            if (string.IsNullOrEmpty(path) || layout == null) return;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var persisted = new Persisted
            {
                Library = layout.LibraryRatio,
                Session = layout.SessionRatio,
                Phase = layout.PhaseRatio,
                Inspector = layout.InspectorRatio,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}