using System;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Loader-agnostic logging abstraction. Each entry point (BepInEx Plugin
    /// or Lunaris LunarisEntry) wires these delegates to its own logging API
    /// during Awake(). Patch files and shared code use these instead of
    /// referencing BepInEx.Logging directly.
    /// </summary>
    public static class Log
    {
        public static Action<string> Info = _ => { };
        public static Action<string> Warning = _ => { };
        public static Action<string> Error = _ => { };
    }
}
