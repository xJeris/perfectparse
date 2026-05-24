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
    }
}
