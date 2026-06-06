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
    public class LunarisEntry : Lunaris.LunarisPlugin
    {
        private PluginCore _core;

        private void Awake()
        {
            // Wire logging to Lunaris ILog
            Log.Info = s => Logging.LogInfo(s);
            Log.Warning = s => Logging.LogWarning(s);
            Log.Error = s => Logging.LogError(s);

            // Read config via Lunaris low-level API
            var config = new PluginConfig
            {
                EncounterToggleKey = Config.Read("EncounterToggle", KeyCode.F9),
                GenerateReportKey = Config.Read("GenerateReport", KeyCode.F10),
                ToggleWindowKey = Config.Read("ToggleWindow", KeyCode.F11),
                IdleTimeout = Config.Read("IdleTimeout", 5f),
                EnableLogging = Config.Read("EnableLogging", true),
                OutputDirectory = Config.Read("OutputDirectory", ""),
                OpenInOverlay = Config.Read("OpenInOverlay", true),
                MaxLogSizeMB = Config.Read("MaxLogSizeMB", 25),
                OpenReportOnExit = Config.Read("OpenReportOnExit", false),
                LogEnvironmental = Config.Read("LogEnvironmental", false),
                WindowX = Config.Read("WindowX", 20f),
                WindowY = Config.Read("WindowY", 20f),
                WindowWidth = Config.Read("WindowWidth", 560f),
                WindowHeight = Config.Read("WindowHeight", 420f),
                WindowFontSize = Config.Read("FontSize", 11),
                PersistWindowRect = (x, y, w, h) =>
                {
                    Config.Write("WindowX", x);
                    Config.Write("WindowY", y);
                    Config.Write("WindowWidth", w);
                    Config.Write("WindowHeight", h);
                    Config.Save();
                }
            };

            // Lunaris plugins live in <GameDir>/plugins/
            string pluginPath = Path.Combine(Application.dataPath, "..", "plugins");
            _core = new PluginCore(config, pluginPath);
            _core.Initialize();
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
