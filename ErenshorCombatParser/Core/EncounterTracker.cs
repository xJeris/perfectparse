using System;
using System.Collections.Generic;
using System.Text;
using ErenshorCombatParser.IO;
using ErenshorCombatParser.Models;
using UnityEngine;

namespace ErenshorCombatParser.Core
{
    public static class EncounterTracker
    {
        public static event Action OnEncounterEnded;

        public static int CurrentEncounterId { get; private set; } = -1;
        public static List<Encounter> AllEncounters { get; } = new List<Encounter>();

        private static float _lastCombatGameTime;
        private static long _lastCombatEventMs;
        private static bool _inEncounter;
        private static int _nextId = 1;

        public static float IdleTimeout = 5f;

        /// <summary>
        /// Called every frame from Plugin.Update() to check for encounter end.
        /// </summary>
        public static void OnCombatTick()
        {
            if (!_inEncounter) return;

            float elapsed = Time.time - _lastCombatGameTime;
            if (elapsed > IdleTimeout)
            {
                EndEncounter();
            }
        }

        /// <summary>
        /// Called by CombatEventBus when a player-relevant combat event occurs.
        /// </summary>
        public static void NotifyCombatActivity()
        {
            _lastCombatGameTime = Time.time;
            _lastCombatEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!_inEncounter)
            {
                StartEncounter(manual: false);
            }
        }

        /// <summary>
        /// Called by hotkey to manually toggle encounter tracking.
        /// </summary>
        public static void ToggleManual()
        {
            if (_inEncounter)
                EndEncounter();
            else
                StartEncounter(manual: true);
        }

        private static void StartEncounter(bool manual)
        {
            _inEncounter = true;
            _lastCombatGameTime = Time.time;
            _lastCombatEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var enc = new Encounter
            {
                Id = _nextId++,
                StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Manual = manual,
                Label = "Encounter " + (_nextId - 1)
            };
            AllEncounters.Add(enc);
            CurrentEncounterId = enc.Id;
        }

        private static void EndEncounter()
        {
            if (AllEncounters.Count > 0)
            {
                // Use the last combat event timestamp, not "now" (which includes idle timeout gap)
                AllEncounters[AllEncounters.Count - 1].EndMs = _lastCombatEventMs;
            }
            _inEncounter = false;
            CurrentEncounterId = -1;
            OnEncounterEnded?.Invoke();
        }

        /// <summary>
        /// Ends the current encounter if one is active.
        /// Called before JSONL file rotation so the encounter closes cleanly.
        /// </summary>
        public static void EndCurrentEncounter()
        {
            if (_inEncounter)
                EndEncounter();
        }

        /// <summary>
        /// Resets all encounter state for a fresh session (e.g. after returning to menu).
        /// </summary>
        public static void Reset()
        {
            AllEncounters.Clear();
            CurrentEncounterId = -1;
            _nextId = 1;
            _inEncounter = false;
        }

        /// <summary>
        /// Serializes all encounters to a JSON array for the HTML report.
        /// </summary>
        public static string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('[');
            for (int i = 0; i < AllEncounters.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = AllEncounters[i];
                // If encounter is still active (EndMs == 0), use last combat event time
                long endMs = e.EndMs > 0 ? e.EndMs : _lastCombatEventMs;
                sb.Append("{\"id\":").Append(e.Id);
                sb.Append(",\"start\":").Append(e.StartMs);
                sb.Append(",\"end\":").Append(endMs);
                if (e.Manual) sb.Append(",\"manual\":true");
                sb.Append(",\"label\":\"").Append(JsonUtil.EscapeJson(e.Label)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
