using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Harmony patches for healing methods on Stats.
    /// Uses manual patching for reliability (same as DamagePatches).
    /// </summary>
    public static class HealPatches
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("PerfectParse.Heal");

        public static void Apply(Harmony harmony)
        {
            var self = typeof(HealPatches);
            var statsType = typeof(Stats);

            // ============================================================
            // Stats.HealMe(Spell, int, bool, bool, Character) — full spell heal
            // ============================================================
            try
            {
                var healFull = statsType.GetMethod("HealMe", BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Spell), typeof(int), typeof(bool), typeof(bool), typeof(Character) },
                    null);
                if (healFull != null)
                {
                    harmony.Patch(healFull,
                        postfix: new HarmonyMethod(self.GetMethod(nameof(HealMe_Full_Postfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    Log.LogInfo("Patched Stats.HealMe(Spell,int,bool,bool,Character)");
                }
                else
                {
                    Log.LogWarning("Stats.HealMe(Spell,int,bool,bool,Character) NOT FOUND");
                    // List all HealMe overloads for diagnosis
                    var methods = statsType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var m in methods)
                    {
                        if (m.Name == "HealMe")
                        {
                            var ps = m.GetParameters();
                            Log.LogInfo($"  Found HealMe({string.Join(",", Array.ConvertAll(ps, p => p.ParameterType.Name))})");
                        }
                    }
                }
            }
            catch (Exception ex) { Log.LogError("HealMe(full) patch failed: " + ex); }

            // ============================================================
            // Stats.HealMe(int) — simple heal
            // ============================================================
            try
            {
                var healSimple = statsType.GetMethod("HealMe", BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);
                if (healSimple != null)
                {
                    harmony.Patch(healSimple,
                        prefix: new HarmonyMethod(self.GetMethod(nameof(HealMe_Simple_Prefix),
                            BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(self.GetMethod(nameof(HealMe_Simple_Postfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    Log.LogInfo("Patched Stats.HealMe(int)");
                }
                else
                {
                    Log.LogWarning("Stats.HealMe(int) NOT FOUND");
                }
            }
            catch (Exception ex) { Log.LogError("HealMe(int) patch failed: " + ex); }

            // ============================================================
            // Stats.TickEffects — HoT tracking (private method)
            // ============================================================
            try
            {
                var tickEffects = statsType.GetMethod("TickEffects",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (tickEffects != null)
                {
                    harmony.Patch(tickEffects,
                        prefix: new HarmonyMethod(self.GetMethod(nameof(TickEffects_Prefix),
                            BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(self.GetMethod(nameof(TickEffects_Postfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    Log.LogInfo("Patched Stats.TickEffects (private)");
                }
                else
                {
                    Log.LogWarning("Stats.TickEffects NOT FOUND");
                }
            }
            catch (Exception ex) { Log.LogError("TickEffects patch failed: " + ex); }
        }

        // ============================================================
        // Postfix implementations
        // ============================================================

        private static bool _loggedFirstHeal = false;

        static void HealMe_Full_Postfix(
            int __result,
            Stats __instance,
            Spell _spell,
            int _amt,
            bool _isCrit,
            bool _isMana,
            Character _source)
        {
            try
            {
                if (!_loggedFirstHeal)
                {
                    Log.LogInfo($"HealMe_Full fired! spell={_spell?.SpellName}, amt={_amt}, result={__result}, isMana={_isMana}");
                    _loggedFirstHeal = true;
                }

                CombatEventBus.EmitHeal(new HealEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = _isMana ? "ManaRestore" : "Heal",
                    SourceId = EntityRegistry.ResolveId(_source),
                    TargetId = __instance.Myself != null
                        ? EntityRegistry.ResolveId(__instance.Myself)
                        : null,
                    SpellName = _spell != null ? _spell.SpellName : null,
                    RawAmount = _amt,
                    ActualAmount = __result,
                    Critical = _isCrit,
                    IsMana = _isMana
                });
            }
            catch (Exception ex) { Log.LogError("HealMe_Full error: " + ex); }
        }

        // ============================================================
        // Stats.HealMe(int) — simple heal prefix/postfix
        // ============================================================
        private static readonly Dictionary<int, int> _preHealHP = new Dictionary<int, int>();
        private static bool _loggedFirstSimpleHeal = false;

        static void HealMe_Simple_Prefix(Stats __instance)
        {
            try
            {
                _preHealHP[__instance.GetInstanceID()] = __instance.CurrentHP;
            }
            catch (Exception) { }
        }

        static void HealMe_Simple_Postfix(Stats __instance, int _amt)
        {
            try
            {
                int preHP;
                if (!_preHealHP.TryGetValue(__instance.GetInstanceID(), out preHP))
                    return;

                _preHealHP.Remove(__instance.GetInstanceID());

                int actualHealed = __instance.CurrentHP - preHP;
                if (actualHealed <= 0) return; // no healing occurred

                if (!_loggedFirstSimpleHeal)
                {
                    Log.LogInfo($"HealMe_Simple fired! amt={_amt}, actual={actualHealed}");
                    _loggedFirstSimpleHeal = true;
                }

                CombatEventBus.EmitHeal(new HealEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "HealSimple",
                    SourceId = null,
                    TargetId = __instance.Myself != null
                        ? EntityRegistry.ResolveId(__instance.Myself)
                        : null,
                    SpellName = null,
                    RawAmount = _amt,
                    ActualAmount = actualHealed,
                    Critical = false,
                    IsMana = false
                });
            }
            catch (Exception ex) { Log.LogError("HealMe_Simple error: " + ex); }
        }

        // ============================================================
        // Stats.TickEffects — HoT tracking
        // ============================================================
        private static readonly Dictionary<int, int> _preTickHP = new Dictionary<int, int>();
        private static bool _loggedFirstTick = false;

        static void TickEffects_Prefix(Stats __instance)
        {
            try
            {
                _preTickHP[__instance.GetInstanceID()] = __instance.CurrentHP;
            }
            catch (Exception) { }
        }

        static void TickEffects_Postfix(Stats __instance)
        {
            try
            {
                int preHP;
                if (!_preTickHP.TryGetValue(__instance.GetInstanceID(), out preHP))
                    return;

                _preTickHP.Remove(__instance.GetInstanceID());

                int delta = __instance.CurrentHP - preHP;

                if (delta > 0)
                {
                    if (!_loggedFirstTick)
                    {
                        Log.LogInfo($"TickEffects HoT fired! delta={delta}");
                        _loggedFirstTick = true;
                    }

                    CombatEventBus.EmitHeal(new HealEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Type = "HoT",
                        SourceId = null,
                        TargetId = __instance.Myself != null
                            ? EntityRegistry.ResolveId(__instance.Myself)
                            : null,
                        SpellName = null,
                        RawAmount = delta,
                        ActualAmount = delta,
                        Critical = false,
                        IsMana = false
                    });
                }
            }
            catch (Exception ex) { Log.LogError("TickEffects error: " + ex); }
        }
    }
}
