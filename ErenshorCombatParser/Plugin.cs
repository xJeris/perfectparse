using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.IO;
using ErenshorCombatParser.Models;
using ErenshorCombatParser.UI;

namespace ErenshorCombatParser
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.erenshor.perfectparse";
        public const string PluginName = "PerfectParse";
        public const string PluginVersion = "0.2.4";

        // Config entries
        private ConfigEntry<KeyCode> _encounterToggleKey;
        private ConfigEntry<KeyCode> _generateReportKey;
        private ConfigEntry<float> _idleTimeout;
        private ConfigEntry<bool> _enableLogging;
        private ConfigEntry<string> _outputDirectory;
        private ConfigEntry<bool> _logEnvironmental;
        private ConfigEntry<bool> _logNpcVsNpc;
        private ConfigEntry<bool> _openInOverlay;
        private ConfigEntry<KeyCode> _toggleWindowKey;
        private ConfigEntry<float> _windowX;
        private ConfigEntry<float> _windowY;
        private ConfigEntry<float> _windowWidth;
        private ConfigEntry<float> _windowHeight;
        private ConfigEntry<int> _maxLogSizeMB;

        private Harmony _harmony;
        private JsonLineWriter _writer;
        private string _logDir;
        private CombatWindow _combatWindow;

        private void Awake()
        {
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
            _logNpcVsNpc = Config.Bind("Filters", "LogNPCvsNPC", false,
                "Log NPC-on-NPC combat (not involving player or sims).");
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

            // Set up output directory
            _logDir = string.IsNullOrEmpty(_outputDirectory.Value)
                ? Path.Combine(Paths.PluginPath, "PerfectParse", "logs")
                : _outputDirectory.Value;
            Directory.CreateDirectory(_logDir);

            // Apply encounter idle timeout
            EncounterTracker.IdleTimeout = _idleTimeout.Value;

            // Start JSONL writer
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logFile = Path.Combine(_logDir, "combat_" + timestamp + ".jsonl");
            _writer = new JsonLineWriter(logFile);

            // Subscribe to combat events
            CombatEventBus.OnCombatEvent += OnCombatEvent;
            CombatEventBus.OnHealEvent += OnHealEvent;
            CombatEventBus.OnEntityEvent += OnEntityEvent;

            // Set up in-game combat window
            _combatWindow = new CombatWindow();
            _combatWindow.WindowRect = new Rect(
                _windowX.Value, _windowY.Value,
                _windowWidth.Value, _windowHeight.Value);
            CombatEventBus.OnCombatEvent += _combatWindow.OnCombatEvent;
            CombatEventBus.OnHealEvent += _combatWindow.OnHealEvent;
            CombatEventBus.OnEntityEvent += _combatWindow.OnEntityEvent;

            // Subscribe to encounter end for JSONL file rotation
            EncounterTracker.OnEncounterEnded += OnEncounterEnded;

            // Hook scene changes to clear entity cache
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Apply Harmony patches
            _harmony = new Harmony(PluginGUID);

            // Apply patches
            Patches.DamagePatches.Apply(_harmony);
            Patches.HealPatches.Apply(_harmony);
            Patches.CameraPatches.Apply(_harmony);
            _harmony.PatchAll();

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private bool IsInGameplay()
        {
            var scene = SceneManager.GetActiveScene().name;
            return scene != "Menu" && scene != "LoadScene";
        }

        private void Update()
        {
            if (!IsInGameplay()) return;

            // Encounter auto-end check
            EncounterTracker.OnCombatTick();

            // Hotkeys
            if (Input.GetKeyDown(_encounterToggleKey.Value))
            {
                EncounterTracker.ToggleManual();
            }

            if (Input.GetKeyDown(_generateReportKey.Value))
            {
                GenerateReport();
            }

            if (Input.GetKeyDown(_toggleWindowKey.Value))
            {
                _combatWindow.Visible = !_combatWindow.Visible;
            }
        }

        private void OnGUI()
        {
            if (_combatWindow == null || !IsInGameplay()) return;
            _combatWindow.Draw();

            // Persist window position and size if changed
            if (_combatWindow.Visible)
            {
                var r = _combatWindow.WindowRect;
                if (Math.Abs(r.x - _windowX.Value) > 1 || Math.Abs(r.y - _windowY.Value) > 1)
                {
                    _windowX.Value = r.x;
                    _windowY.Value = r.y;
                }
                if (Math.Abs(r.width - _windowWidth.Value) > 1 || Math.Abs(r.height - _windowHeight.Value) > 1)
                {
                    _windowWidth.Value = r.width;
                    _windowHeight.Value = r.height;
                }
            }
        }

        private void OnCombatEvent(CombatEvent evt)
        {
            if (!_enableLogging.Value) return;

            // Filter environmental damage if disabled
            if (!_logEnvironmental.Value && evt.SourceId == "Environment")
                return;

            // Filter NPC-vs-NPC if disabled
            if (!_logNpcVsNpc.Value && IsNpcId(evt.SourceId) && IsNpcId(evt.TargetId))
                return;

            _writer.Enqueue(evt.ToJsonLine());
        }

        private void OnHealEvent(HealEvent evt)
        {
            if (!_enableLogging.Value) return;
            _writer.Enqueue(evt.ToJsonLine());
        }

        private void OnEntityEvent(EntitySnapshot snapshot)
        {
            if (!_enableLogging.Value) return;
            _writer.Enqueue(snapshot.ToJsonLine());
        }

        private void OnEncounterEnded()
        {
            if (_writer == null || _maxLogSizeMB.Value <= 0) return;

            try
            {
                _writer.FlushSync();
                var fileInfo = new FileInfo(_writer.FilePath);
                if (!fileInfo.Exists) return;

                long capBytes = (long)_maxLogSizeMB.Value * 1024 * 1024;
                if (fileInfo.Length >= capBytes)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string newPath = Path.Combine(_logDir, "combat_" + timestamp + ".jsonl");
                    _writer.Rotate(newPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to rotate log file: " + ex.Message);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EntityRegistry.ClearCache();
        }

        private void GenerateReport()
        {
            try
            {
                // Flush all queued events to disk before reading
                _writer?.FlushSync();

                string reportPath = Path.Combine(_logDir,
                    "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");

                string entityJson = EntityRegistry.ToJson();
                string encounterJson = EncounterTracker.ToJson();

                HtmlReportGenerator.Generate(
                    _writer.FilePath, reportPath, entityJson, encounterJson);


                if (_openInOverlay.Value)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(reportPath);
                    }
                    catch (Exception openEx)
                    {
                        Logger.LogWarning("Could not open report: " + openEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to generate report: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            CombatEventBus.OnCombatEvent -= OnCombatEvent;
            CombatEventBus.OnHealEvent -= OnHealEvent;
            CombatEventBus.OnEntityEvent -= OnEntityEvent;
            if (_combatWindow != null)
            {
                CombatEventBus.OnCombatEvent -= _combatWindow.OnCombatEvent;
                CombatEventBus.OnHealEvent -= _combatWindow.OnHealEvent;
                CombatEventBus.OnEntityEvent -= _combatWindow.OnEntityEvent;
            }
            EncounterTracker.OnEncounterEnded -= OnEncounterEnded;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            _harmony?.UnpatchSelf();

            // Generate report while the writer is still alive (FlushSync needs it)
            try
            {
                GenerateReport();
            }
            catch (Exception) { }

            // Now dispose the writer (flushes remaining queue and closes file)
            _writer?.Dispose();
        }

        private static bool IsNpcId(string id)
        {
            return id != null && id.StartsWith("NPC:");
        }
    }
}
