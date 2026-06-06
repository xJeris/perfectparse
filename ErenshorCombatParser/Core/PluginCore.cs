using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ErenshorCombatParser.IO;
using ErenshorCombatParser.Models;
using ErenshorCombatParser.UI;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Shared plugin logic used by both BepInEx and Lunaris entry points.
    /// Not a MonoBehaviour — instantiated by the active entry point and
    /// driven via OnUpdate/OnGUI/Shutdown calls.
    /// </summary>
    public class PluginCore
    {
        public const string PluginGUID = "com.erenshor.perfectparse";
        public const string PluginName = "PerfectParse";
        public const string PluginVersion = "0.4.0";

        private readonly PluginConfig _config;
        private Harmony _harmony;
        private JsonLineWriter _writer;
        private string _logDir;
        private CombatWindow _combatWindow;

        public PluginCore(PluginConfig config, string pluginBasePath)
        {
            _config = config;

            // Set up output directory
            _logDir = string.IsNullOrEmpty(_config.OutputDirectory)
                ? Path.Combine(pluginBasePath, "PerfectParse", "logs")
                : _config.OutputDirectory;
        }

        public void Initialize()
        {
            Directory.CreateDirectory(_logDir);

            // Apply encounter idle timeout
            EncounterTracker.IdleTimeout = _config.IdleTimeout;

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
            _combatWindow.BaseFontSize = _config.WindowFontSize;
            _combatWindow.WindowRect = new Rect(
                _config.WindowX, _config.WindowY,
                _config.WindowWidth, _config.WindowHeight);
            CombatEventBus.OnCombatEvent += _combatWindow.OnCombatEvent;
            CombatEventBus.OnHealEvent += _combatWindow.OnHealEvent;
            CombatEventBus.OnEntityEvent += _combatWindow.OnEntityEvent;

            // Subscribe to encounter end for JSONL file rotation
            EncounterTracker.OnEncounterEnded += OnEncounterEnded;

            // Hook scene changes to clear entity cache
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Apply Harmony patches
            _harmony = new Harmony(PluginGUID);
            Patches.DamagePatches.Apply(_harmony);
            Patches.HealPatches.Apply(_harmony);
            Patches.CameraPatches.Apply(_harmony);
            Patches.BossMechanicPatches.Apply(_harmony);
            _harmony.PatchAll();

            Log.Info(PluginName + " v" + PluginVersion + " loaded.");
        }

        public void OnUpdate()
        {
            if (!IsInGameplay()) return;

            // Encounter auto-end check
            EncounterTracker.OnCombatTick();

            // Hotkeys
            if (Input.GetKeyDown(_config.EncounterToggleKey))
            {
                EncounterTracker.ToggleManual();
            }

            if (Input.GetKeyDown(_config.GenerateReportKey))
            {
                GenerateReport();
            }

            if (Input.GetKeyDown(_config.ToggleWindowKey))
            {
                _combatWindow.Visible = !_combatWindow.Visible;
            }
        }

        public void OnGUI()
        {
            if (_combatWindow == null || !IsInGameplay()) return;
            _combatWindow.Draw();

            // Persist window position and size if changed
            if (_combatWindow.Visible && _config.PersistWindowRect != null)
            {
                var r = _combatWindow.WindowRect;
                bool posChanged = Math.Abs(r.x - _config.WindowX) > 1
                    || Math.Abs(r.y - _config.WindowY) > 1;
                bool sizeChanged = Math.Abs(r.width - _config.WindowWidth) > 1
                    || Math.Abs(r.height - _config.WindowHeight) > 1;

                if (posChanged || sizeChanged)
                {
                    _config.WindowX = r.x;
                    _config.WindowY = r.y;
                    _config.WindowWidth = r.width;
                    _config.WindowHeight = r.height;
                    _config.PersistWindowRect(r.x, r.y, r.width, r.height);
                }
            }
        }

        public void Shutdown()
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

            // Generate report on exit, but only if combat events were actually logged
            try
            {
                if (_writer != null)
                {
                    _writer.FlushSync();
                    var logFile = new FileInfo(_writer.FilePath);
                    if (logFile.Exists && logFile.Length > 0)
                        GenerateReport(_config.OpenReportOnExit);
                }
            }
            catch (Exception) { }

            // Now dispose the writer (flushes remaining queue and closes file)
            _writer?.Dispose();

            // Clear all static state for clean Lunaris hot-reload
            CombatEventBus.ClearSubscribers();
            EncounterTracker.ClearSubscribers();
            EncounterTracker.Reset();
            EntityRegistry.ClearCache();
            EntityRegistry.ClearReportEntities();
            CombatContext.Reset();
            ResonanceContext.Reset();
            Patches.DamagePatches.ClearState();
            Patches.HealPatches.ClearState();
        }

        private bool IsInGameplay()
        {
            var scene = SceneManager.GetActiveScene().name;
            return scene != "Menu" && scene != "LoadScene";
        }

        private void OnCombatEvent(CombatEvent evt)
        {
            if (!_config.EnableLogging) return;

            // Filter environmental damage if disabled
            if (!_config.LogEnvironmental && evt.SourceId == "Environment")
                return;

            _writer.Enqueue(evt.ToJsonLine());
        }

        private void OnHealEvent(HealEvent evt)
        {
            if (!_config.EnableLogging) return;
            _writer.Enqueue(evt.ToJsonLine());
        }

        private void OnEntityEvent(EntitySnapshot snapshot)
        {
            if (!_config.EnableLogging) return;
            _writer.Enqueue(snapshot.ToJsonLine());
        }

        private void OnEncounterEnded()
        {
            if (_writer == null || _config.MaxLogSizeMB <= 0) return;

            try
            {
                _writer.FlushSync();
                var fileInfo = new FileInfo(_writer.FilePath);
                if (!fileInfo.Exists) return;

                long capBytes = (long)_config.MaxLogSizeMB * 1024 * 1024;
                if (fileInfo.Length >= capBytes)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string newPath = Path.Combine(_logDir, "combat_" + timestamp + ".jsonl");
                    _writer.Rotate(newPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to rotate log file: " + ex.Message);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EntityRegistry.ClearCache();

            // Leaving gameplay (menu or quit) — rotate to a new JSONL file
            // so each character session gets its own log.
            if (scene.name == "Menu" || scene.name == "LoadScene")
            {
                EncounterTracker.EndCurrentEncounter();

                try
                {
                    _writer?.FlushSync();
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string newPath = Path.Combine(_logDir, "combat_" + timestamp + ".jsonl");
                    _writer?.Rotate(newPath);
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to rotate log on scene change: " + ex.Message);
                }

                EntityRegistry.ClearReportEntities();
                EncounterTracker.Reset();
                _combatWindow?.Reset();
                Patches.DamagePatches.ClearState();
                Patches.HealPatches.ClearState();
            }
        }

        private void GenerateReport(bool allowOpen = true)
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

                if (allowOpen && _config.OpenInOverlay)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(reportPath);
                    }
                    catch (Exception openEx)
                    {
                        Log.Warning("Could not open report: " + openEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to generate report: " + ex.Message);
            }
        }
    }
}
