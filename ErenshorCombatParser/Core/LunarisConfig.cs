using Lunaris.Config;
using UnityEngine;

namespace ErenshorCombatParser.Core
{
    public class LunarisConfig
    {
        // Hotkeys
        [ConfigSection("Hotkeys")]
        [ConfigDescription("Key to manually start/stop an encounter.")]
        public KeyCode EncounterToggle = KeyCode.F9;

        [ConfigSection("Hotkeys")]
        [ConfigDescription("Key to generate the HTML combat report.")]
        public KeyCode GenerateReport = KeyCode.F10;

        [ConfigSection("Hotkeys")]
        [ConfigDescription("Key to toggle the in-game combat stats window.")]
        public KeyCode ToggleWindow = KeyCode.F11;

        // General
        [ConfigSection("General")]
        [ConfigDescription("Master toggle for combat event logging.")]
        public bool EnableLogging = true;

        [ConfigSection("General")]
        [ConfigDescription("Seconds of no combat activity before auto-ending an encounter.")]
        public string IdleTimeout = "5";

        [ConfigSection("General")]
        [ConfigDescription("Custom output directory. Leave blank to use the plugin folder.")]
        public string OutputDirectory = "";

        [ConfigSection("General")]
        [ConfigDescription("Open the HTML report in the default browser after generation.")]
        public bool OpenInOverlay = true;

        [ConfigSection("General")]
        [ConfigDescription("Maximum JSONL log file size in MB before rotating to a new file. Rotation happens after the current encounter ends.")]
        public string MaxLogSizeMB = "25";

        [ConfigSection("General")]
        [ConfigDescription("Open the HTML report in the browser when the game closes. The report is always generated on exit regardless of this setting.")]
        public bool OpenReportOnExit = false;

        [ConfigSection("General")]
        [ConfigDescription("Base font size for the in-game combat stats window. Increase for high-resolution displays.")]
        public string FontSize = "11";

        // Filters
        [ConfigSection("Filters")]
        [ConfigDescription("Log environmental damage (lava, fall damage, etc.).")]
        public bool LogEnvironmental = false;

        // Window position/size
        [ConfigSection("Window")]
        [ConfigDescription("Window X position in pixels.")]
        public string WindowX = "20";

        [ConfigSection("Window")]
        [ConfigDescription("Window Y position in pixels.")]
        public string WindowY = "20";

        [ConfigSection("Window")]
        [ConfigDescription("Window width in pixels.")]
        public string WindowWidth = "560";

        [ConfigSection("Window")]
        [ConfigDescription("Window height in pixels.")]
        public string WindowHeight = "420";
    }
}
