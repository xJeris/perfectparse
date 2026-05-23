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

namespace ErenshorCombatParser
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.erenshor.perfectparse";
        public const string PluginName = "PerfectParse";
        public const string PluginVersion = "0.1.0";

        // Config entries
        private ConfigEntry<KeyCode> _encounterToggleKey;
        private ConfigEntry<KeyCode> _generateReportKey;
        private ConfigEntry<float> _idleTimeout;
        private ConfigEntry<bool> _enableLogging;
        private ConfigEntry<string> _outputDirectory;
        private ConfigEntry<bool> _logEnvironmental;
        private ConfigEntry<bool> _logNpcVsNpc;

        private Harmony _harmony;
        private JsonLineWriter _writer;
        private string _logDir;

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

            // Hook scene changes to clear entity cache
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Apply Harmony patches
            _harmony = new Harmony(PluginGUID);

            // Diagnostic: log Character type info
            var charType = typeof(Character);
            Logger.LogInfo($"Character type: {charType.FullName} from {charType.Assembly.GetName().Name}");
            var dmgMethod = charType.GetMethod("DamageMe",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (dmgMethod != null)
            {
                Logger.LogInfo($"DamageMe: {dmgMethod}, DeclaringType={dmgMethod.DeclaringType.FullName}");
                var parms = dmgMethod.GetParameters();
                Logger.LogInfo($"DamageMe params ({parms.Length}): {string.Join(", ", System.Array.ConvertAll(parms, p => p.ParameterType.Name + " " + p.Name))}");
            }
            else
            {
                Logger.LogInfo("DamageMe method NOT FOUND via reflection!");
                // List all public methods on Character
                var methods = charType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                    Logger.LogInfo($"  Character method: {m.Name}");
            }

            // Manual patches for reliability
            Patches.DamagePatches.Apply(_harmony);
            Patches.HealPatches.Apply(_harmony);

            // Attribute-based patches for everything else (ContextPatches, FinalePatches, etc.)
            _harmony.PatchAll();

            // Log all patched methods
            var patchedMethods = _harmony.GetPatchedMethods();
            foreach (var pm in patchedMethods)
                Logger.LogInfo($"Harmony patched: {pm.DeclaringType?.Name}.{pm.Name}");

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded. Logging to: {logFile}");
        }

        private void Update()
        {
            // Encounter auto-end check
            EncounterTracker.OnCombatTick();

            // Hotkeys
            if (Input.GetKeyDown(_encounterToggleKey.Value))
            {
                EncounterTracker.ToggleManual();
                Logger.LogInfo(EncounterTracker.CurrentEncounterId > 0
                    ? "Encounter started (manual)."
                    : "Encounter ended (manual).");
            }

            if (Input.GetKeyDown(_generateReportKey.Value))
            {
                GenerateReport();
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EntityRegistry.ClearCache();
            Logger.LogInfo("Scene loaded: " + scene.name + " — entity cache cleared.");
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

                Logger.LogInfo("Report generated: " + reportPath);
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
            SceneManager.sceneLoaded -= OnSceneLoaded;

            _harmony?.UnpatchSelf();

            // Dispose writer first so the JSONL file is closed and fully flushed
            _writer?.Dispose();

            // Then generate report from the closed file
            try
            {
                GenerateReport();
            }
            catch (Exception) { }
        }

        private static bool IsNpcId(string id)
        {
            return id != null && id.StartsWith("NPC:");
        }
    }
}
