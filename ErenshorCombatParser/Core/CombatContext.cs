using System.Collections.Generic;
using UnityEngine;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Frame-scoped context that captures what attack/spell/skill is currently
    /// being executed, keyed by attacker Character instance. Set by ContextPatches
    /// prefixes, consumed by DamagePatches postfixes. Automatically expires after
    /// one frame. Per-attacker scoping prevents cross-entity contamination when
    /// multiple entities act in the same frame.
    /// </summary>
    public static class CombatContext
    {
        private struct Entry
        {
            public string Source;
            public int Frame;
        }

        private static readonly Dictionary<Character, Entry> _entries = new Dictionary<Character, Entry>();

        // Frame-scoped crit override. The game's DoSkill/DoSkillNoChecks methods
        // call isCriticalAttack() but store the result in a local variable and
        // never pass it through to DamageMe's _criticalHit parameter. We capture
        // the crit result in a postfix on isCriticalAttack() and consume it in
        // DamageMe_Postfix to correct the flag.
        private static readonly Dictionary<Character, int> _critOverrides = new Dictionary<Character, int>();

        // Reusable list for cleanup to avoid allocations
        private static readonly List<Character> _staleKeys = new List<Character>();
        private static int _lastCleanupFrame = -1;

        public static void Set(Character attacker, string source)
        {
            if (attacker == null) return;

            _entries[attacker] = new Entry
            {
                Source = source,
                Frame = Time.frameCount
            };

            // Periodically clean up stale entries (at most once per frame)
            int currentFrame = Time.frameCount;
            if (currentFrame != _lastCleanupFrame && _entries.Count > 16)
            {
                _lastCleanupFrame = currentFrame;
                _staleKeys.Clear();
                foreach (var kvp in _entries)
                {
                    // Use ReferenceEquals for destroyed-object check since Unity's
                    // == override doesn't help with Dictionary key removal
                    if (kvp.Value.Frame < currentFrame - 1
                        || ReferenceEquals(kvp.Key, null)
                        || !kvp.Key)
                        _staleKeys.Add(kvp.Key);
                }
                for (int i = 0; i < _staleKeys.Count; i++)
                    _entries.Remove(_staleKeys[i]);
                // Also prune stale crit overrides
                _staleKeys.Clear();
                foreach (var kvp in _critOverrides)
                {
                    if (kvp.Value < currentFrame - 1
                        || ReferenceEquals(kvp.Key, null)
                        || !kvp.Key)
                        _staleKeys.Add(kvp.Key);
                }
                for (int i = 0; i < _staleKeys.Count; i++)
                    _critOverrides.Remove(_staleKeys[i]);
            }
        }

        public static string Get(Character attacker)
        {
            if (attacker != null && _entries.TryGetValue(attacker, out Entry entry))
            {
                if (entry.Frame == Time.frameCount)
                    return entry.Source;
            }
            return null;
        }

        /// <summary>
        /// Record that the given entity scored a critical hit this frame.
        /// Called from isCriticalAttack() postfix when it returns true.
        /// </summary>
        public static void SetCrit(Character attacker)
        {
            if (attacker == null) return;
            _critOverrides[attacker] = Time.frameCount;
        }

        /// <summary>
        /// Check and consume the crit override for this entity/frame.
        /// Returns true if isCriticalAttack() returned true for this
        /// entity in the current frame. Consuming prevents double-counting
        /// if DamageMe is called multiple times per frame.
        /// </summary>
        public static bool ConsumeCrit(Character attacker)
        {
            if (attacker != null && _critOverrides.TryGetValue(attacker, out int frame))
            {
                if (frame == Time.frameCount)
                {
                    _critOverrides.Remove(attacker);
                    return true;
                }
            }
            return false;
        }
    }
}
