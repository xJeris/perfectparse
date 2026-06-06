using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using ErenshorCombatParser.Core;

namespace ErenshorCombatParser
{
    [BepInPlugin(PluginCore.PluginGUID, PluginCore.PluginName, PluginCore.PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        // BepInEx config entries (for live read/write via ConfigEntry<T>)
        private ConfigEntry<KeyCode> _encounterToggleKey;
        private ConfigEntry<KeyCode> _generateReportKey;
        private ConfigEntry<float> _idleTimeout;
        private ConfigEntry<bool> _enableLogging;
        private ConfigEntry<string> _outputDirectory;
        private ConfigEntry<bool> _logEnvironmental;
        private ConfigEntry<bool> _openInOverlay;
        private ConfigEntry<KeyCode> _toggleWindowKey;
        private ConfigEntry<float> _windowX;
        private ConfigEntry<float> _windowY;
        private ConfigEntry<float> _windowWidth;
        private ConfigEntry<float> _windowHeight;
        private ConfigEntry<int> _maxLogSizeMB;
        private ConfigEntry<bool> _openReportOnExit;
        private ConfigEntry<int> _windowFontSize;

        private PluginCore _core;

        private void Awake()
        {
            // Wire logging to BepInEx
            Log.Info = s => Logger.LogInfo(s);
            Log.Warning = s => Logger.LogWarning(s);
            Log.Error = s => Logger.LogError(s);

            // Bind config
            _encounterToggleKey = Config.Bind("Hotkeys", "EncounterToggle", KeyCode.F9,
                "Key to manually start/stop an encounter.");
            _generateReportKey = Config.Bind("Hotkeys", "GenerateReport", KeyCode.F10,
                "Key to generate the HTML combat report.");
            _idleTimeout = Config.Bind("Encounters", "IdleTimeout", 5f,
                "Seconds of no combat activity before auto-ending an encounter.");
            _enableLogging = Config.Bind("General", "EnableLogging", true,
                "Master toggle for combat event logging.");
            _outputDirectory = Config.Bind("General", "OutputDirectory", "",
                "Custom output directory. Leave blank to use the plugin folder.");
            _logEnvironmental = Config.Bind("Filters", "LogEnvironmental", false,
                "Log environmental damage (lava, fall damage, etc.).");
            _openInOverlay = Config.Bind("General", "OpenInOverlay", true,
                "Open the HTML report in the default browser after generation.");
            _toggleWindowKey = Config.Bind("Hotkeys", "ToggleWindow", KeyCode.F11,
                "Key to toggle the in-game combat stats window.");
            _windowX = Config.Bind("Window", "X", 20f,
                "Window X position in pixels.");
            _windowY = Config.Bind("Window", "Y", 20f,
                "Window Y position in pixels.");
            _windowWidth = Config.Bind("Window", "Width", 560f,
                "Window width in pixels.");
            _windowHeight = Config.Bind("Window", "Height", 420f,
                "Window height in pixels.");
            _maxLogSizeMB = Config.Bind("General", "MaxLogSizeMB", 25,
                "Maximum JSONL log file size in MB before rotating to a new file. Rotation happens after the current encounter ends.");
            _openReportOnExit = Config.Bind("General", "OpenReportOnExit", false,
                "Open the HTML report in the browser when the game closes. The report is always generated on exit regardless of this setting.");
            _windowFontSize = Config.Bind("Window", "FontSize", 11,
                "Base font size for the in-game combat stats window. Increase for high-resolution displays.");

            // Populate loader-agnostic config
            var config = new PluginConfig
            {
                EncounterToggleKey = _encounterToggleKey.Value,
                GenerateReportKey = _generateReportKey.Value,
                ToggleWindowKey = _toggleWindowKey.Value,
                IdleTimeout = _idleTimeout.Value,
                EnableLogging = _enableLogging.Value,
                OutputDirectory = _outputDirectory.Value,
                OpenInOverlay = _openInOverlay.Value,
                MaxLogSizeMB = _maxLogSizeMB.Value,
                OpenReportOnExit = _openReportOnExit.Value,
                LogEnvironmental = _logEnvironmental.Value,
                WindowX = _windowX.Value,
                WindowY = _windowY.Value,
                WindowWidth = _windowWidth.Value,
                WindowHeight = _windowHeight.Value,
                WindowFontSize = _windowFontSize.Value,
                PersistWindowRect = (x, y, w, h) =>
                {
                    _windowX.Value = x;
                    _windowY.Value = y;
                    _windowWidth.Value = w;
                    _windowHeight.Value = h;
                }
            };

            _core = new PluginCore(config, Paths.PluginPath);
            _core.Initialize();
        }

        private void Update() => _core?.OnUpdate();
        private void OnGUI() => _core?.OnGUI();
        private void OnDestroy() => _core?.Shutdown();
    }
}
