using UnityEngine;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Frame-scoped context that captures what attack/spell/skill is currently
    /// being executed. Set by ContextPatches prefixes, consumed by DamagePatches
    /// postfixes. Automatically expires after one frame.
    /// </summary>
    public static class CombatContext
    {
        private static string _source;
        private static int _frame = -1;

        public static void Set(string source)
        {
            _source = source;
            _frame = Time.frameCount;
        }

        public static string Get()
        {
            if (_frame == Time.frameCount && _source != null)
                return _source;
            return null;
        }
    }
}
