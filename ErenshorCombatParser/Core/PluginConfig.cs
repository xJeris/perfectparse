using System;
using UnityEngine;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Configuration container populated by LunarisEntry from the registered
    /// LunarisConfig and passed to PluginCore.
    /// </summary>
    public class PluginConfig
    {
        // Hotkeys
        public KeyCode EncounterToggleKey = KeyCode.F9;
        public KeyCode GenerateReportKey = KeyCode.F10;
        public KeyCode ToggleWindowKey = KeyCode.F11;

        // Encounters
        public float IdleTimeout = 5f;

        // General
        public bool EnableLogging = true;
        public string OutputDirectory = "";
        public bool OpenInOverlay = true;
        public int MaxLogSizeMB = 25;
        public bool OpenReportOnExit = false;

        // Filters
        public bool LogEnvironmental = false;

        // Window
        public float WindowX = 20f;
        public float WindowY = 20f;
        public float WindowWidth = 560f;
        public float WindowHeight = 420f;
        public int WindowFontSize = 11;

        /// <summary>
        /// Callback for the entry point to persist window position/size
        /// changes back to its config system. Parameters: x, y, width, height.
        /// </summary>
        public Action<float, float, float, float> PersistWindowRect;
    }
}
