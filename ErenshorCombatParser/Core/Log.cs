using System;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Logging abstraction. LunarisEntry wires these delegates to the Lunaris
    /// logging API during Awake(). Shared code uses these throughout.
    /// </summary>
    public static class Log
    {
        public static Action<string> Info = _ => { };
        public static Action<string> Warning = _ => { };
        public static Action<string> Error = _ => { };
    }
}
