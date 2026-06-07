using System.IO;
using System.Reflection;
using UnityEngine;
using ErenshorCombatParser.Core;
using Lunaris;

[assembly: AssemblyMetadata("LunarisPluginId", "perfectparse")]

namespace ErenshorCombatParser
{
    [LunarisPlugin("PerfectParse", PluginCore.PluginVersion, "Jeris",
        "Combat parser with DPS/HPS tracking and HTML reports")]
    public class LunarisEntry : LunarisPlugin
    {
        private PluginCore _core;

        private void Awake()
        {
            // Wire logging to Lunaris
            Log.Info = s => Logging.LogInfo(s);
            Log.Warning = s => Logging.LogWarning(s);
            Log.Error = s => Logging.LogError(s);

            // Register configuration via Lunaris
            var settings = Config.Register<LunarisConfig>().Get();

            var config = new PluginConfig
            {
                EncounterToggleKey = settings.EncounterToggle,
                GenerateReportKey = settings.GenerateReport,
                ToggleWindowKey = settings.ToggleWindow,
                IdleTimeout = ParseInt(settings.IdleTimeout, 5),
                EnableLogging = settings.EnableLogging,
                OutputDirectory = settings.OutputDirectory,
                OpenInOverlay = settings.OpenInOverlay,
                MaxLogSizeMB = ParseInt(settings.MaxLogSizeMB, 25),
                OpenReportOnExit = settings.OpenReportOnExit,
                LogEnvironmental = settings.LogEnvironmental,
                WindowX = ParseInt(settings.WindowX, 20),
                WindowY = ParseInt(settings.WindowY, 20),
                WindowWidth = ParseInt(settings.WindowWidth, 560),
                WindowHeight = ParseInt(settings.WindowHeight, 420),
                WindowFontSize = ParseInt(settings.FontSize, 11),
                PersistWindowRect = (x, y, w, h) =>
                {
                    settings.WindowX = ((int)x).ToString();
                    settings.WindowY = ((int)y).ToString();
                    settings.WindowWidth = ((int)w).ToString();
                    settings.WindowHeight = ((int)h).ToString();
                    Config.Save();
                }
            };

            // Lunaris plugins live in <GameDir>/plugins/
            string pluginPath = Path.Combine(Application.dataPath, "..", "plugins");
            _core = new PluginCore(config, pluginPath);
            _core.Initialize();
        }

        private static int ParseInt(string value, int fallback)
        {
            if (int.TryParse(value, out int result))
                return result;
            return fallback;
        }

        private void Update() => _core?.OnUpdate();
        private void OnGUI() => _core?.OnGUI();

        private void OnDestroy()
        {
            _core?.Shutdown();
            _core = null;
        }
    }
}
