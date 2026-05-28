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

        // Cached reflection accessor for SpellVessel.resonating (private field)
        private static FieldInfo _resonatingField;

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
                }
                else
                {
                    Log.LogWarning("Stats.HealMe(Spell,int,bool,bool,Character) NOT FOUND");
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
                }
                else
                {
                    Log.LogWarning("Stats.TickEffects NOT FOUND");
                }
            }
            catch (Exception ex) { Log.LogError("TickEffects patch failed: " + ex); }

            // ============================================================
            // SpellVessel.ResolveSpell — resonance context tracking
            // ============================================================
            try
            {
                var svType = typeof(SpellVessel);
                _resonatingField = svType.GetField("resonating",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (_resonatingField != null)
                {
                    var resolveSpell = svType.GetMethod("ResolveSpell",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (resolveSpell != null)
                    {
                        harmony.Patch(resolveSpell,
                            prefix: new HarmonyMethod(self.GetMethod(nameof(ResolveSpell_Prefix),
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(self.GetMethod(nameof(ResolveSpell_Postfix),
                                BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                    else
                    {
                        Log.LogWarning("SpellVessel.ResolveSpell NOT FOUND — resonance tracking disabled");
                    }
                }
                else
                {
                    Log.LogWarning("SpellVessel.resonating field NOT FOUND — resonance tracking disabled");
                }
            }
            catch (Exception ex) { Log.LogWarning("ResolveSpell resonance patch failed: " + ex.Message); }
        }

        // ============================================================
        // SpellVessel.ResolveSpell — resonance context
        // ============================================================

        static void ResolveSpell_Prefix(SpellVessel __instance)
        {
            try
            {
                if (_resonatingField != null)
                    ResonanceContext.IsResonance = (bool)_resonatingField.GetValue(__instance);
            }
            catch (Exception) { }
        }

        static void ResolveSpell_Postfix()
        {
            ResonanceContext.IsResonance = false;
        }

        // ============================================================
        // Postfix implementations
        // ============================================================

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
                // Skip 0-amount heals — these are HoT-only spells (e.g. Group Regrowth)
                // where the direct heal is 0 and actual healing comes via TickEffects.
                if (__result <= 0 && !_isMana) return;

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
                    IsMana = _isMana,
                    IsResonance = ResonanceContext.IsResonance
                });
            }
            catch (Exception ex) { Log.LogError("HealMe_Full error: " + ex); }
        }

        // ============================================================
        // Stats.HealMe(int) — simple heal prefix/postfix
        // ============================================================
        private static readonly Dictionary<int, int> _preHealHP = new Dictionary<int, int>();
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

                // HealMe(int) is a self-heal (lifesteal, lifetap, regen, etc.)
                // so source = target
                string entityId = __instance.Myself != null
                    ? EntityRegistry.ResolveId(__instance.Myself)
                    : null;

                CombatEventBus.EmitHeal(new HealEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "HealSimple",
                    SourceId = entityId,
                    TargetId = entityId,
                    SpellName = "Self Heal",
                    RawAmount = _amt,
                    ActualAmount = actualHealed,
                    Critical = false,
                    IsMana = false
                });
            }
            catch (Exception ex) { Log.LogError("HealMe_Simple error: " + ex); }
        }

        // ============================================================
        // Stats.TickEffects — per-slot HoT tracking
        // ============================================================

        private struct HoTInfo
        {
            public string SpellName;
            public Character Owner; // deferred — only resolved if healing occurs
            public int ExpectedAmount;
        }

        private static readonly Dictionary<int, int> _preTickHP = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<HoTInfo>> _activeHoTs = new Dictionary<int, List<HoTInfo>>();

        static void TickEffects_Prefix(Stats __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                _preTickHP[id] = __instance.CurrentHP;

                // Scan all 30 status effect slots for active HoTs, mirroring the
                // game's TickEffects condition (Stats.cs line 1540):
                //   TargetHealing > 0 && Duration > 0 && DamageType == Physical
                //   && (CombatStance == null || !CombatStance.StopRegen)
                bool regenBlocked = __instance.CombatStance != null
                    && __instance.CombatStance.StopRegen;

                List<HoTInfo> hots = null;

                if (!regenBlocked)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var slot = __instance.StatusEffects[i];
                        if (slot == null || slot.Effect == null) continue;

                        var effect = slot.Effect;
                        if (effect.TargetHealing <= 0) continue;
                        if (slot.Duration <= 0f) continue;
                        if (effect.MyDamageType != GameData.DamageType.Physical) continue;

                        // Skip equipment passive regen (WornEffect) — not a cast spell
                        if (effect.WornEffect) continue;

                        // Check for destroyed Unity objects before accessing properties
                        var owner = slot.Owner;
                        bool ownerAlive = !ReferenceEquals(owner, null) && owner;

                        // Replicate the game's tick amount calculation (Stats.cs lines 1542-1549)
                        int expected = effect.TargetHealing;
                        if (ownerAlive && owner.MyStats != null)
                        {
                            expected += UnityEngine.Mathf.RoundToInt(
                                (float)owner.MyStats.WisScaleMod / 100f
                                * (float)owner.MyStats.GetCurrentWis() * 10f);
                            if (owner.MyStats.CharacterClass == GameData.ClassDB.Druid)
                            {
                                expected += owner.MyStats.GetCurrentWis();
                            }
                        }

                        if (hots == null) hots = new List<HoTInfo>();
                        hots.Add(new HoTInfo
                        {
                            SpellName = effect.SpellName,
                            Owner = ownerAlive ? owner : null,
                            ExpectedAmount = expected
                        });
                    }
                }

                if (hots != null)
                    _activeHoTs[id] = hots;
                else
                    _activeHoTs.Remove(id);
            }
            catch (Exception) { }
        }

        static void TickEffects_Postfix(Stats __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                int preHP;
                if (!_preTickHP.TryGetValue(id, out preHP))
                    return;

                _preTickHP.Remove(id);

                int delta = __instance.CurrentHP - preHP;
                if (delta <= 0)
                {
                    _activeHoTs.Remove(id);
                    return;
                }

                string targetId = __instance.Myself != null
                    ? EntityRegistry.ResolveId(__instance.Myself)
                    : null;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                List<HoTInfo> hots;
                if (!_activeHoTs.TryGetValue(id, out hots) || hots.Count == 0)
                {
                    // Fallback: no HoT slots found but HP increased — generic event
                    _activeHoTs.Remove(id);
                    CombatEventBus.EmitHeal(new HealEvent
                    {
                        Timestamp = now,
                        Type = "HoT",
                        SourceId = targetId,
                        TargetId = targetId,
                        SpellName = "HoT:Unknown",
                        RawAmount = delta,
                        ActualAmount = delta,
                        Critical = false,
                        IsMana = false
                    });
                    return;
                }

                _activeHoTs.Remove(id);

                // Distribute actual delta proportionally across active HoTs
                // based on their expected tick amounts
                int totalExpected = 0;
                for (int i = 0; i < hots.Count; i++)
                    totalExpected += hots[i].ExpectedAmount;

                if (totalExpected <= 0) totalExpected = 1;

                int distributed = 0;
                for (int i = 0; i < hots.Count; i++)
                {
                    var hot = hots[i];
                    int actual;
                    if (i == hots.Count - 1)
                    {
                        // Last HoT gets the remainder to avoid rounding drift
                        actual = delta - distributed;
                    }
                    else
                    {
                        actual = (int)((long)delta * hot.ExpectedAmount / totalExpected);
                    }

                    if (actual <= 0) continue;
                    distributed += actual;

                    // Resolve entity ID only now — avoids registering entities
                    // for HoTs that never actually healed anything
                    string sourceId = hot.Owner != null
                        ? EntityRegistry.ResolveId(hot.Owner)
                        : null;

                    CombatEventBus.EmitHeal(new HealEvent
                    {
                        Timestamp = now,
                        Type = "HoT",
                        SourceId = sourceId ?? targetId,
                        TargetId = targetId,
                        SpellName = "HoT:" + (hot.SpellName ?? "Unknown"),
                        RawAmount = hot.ExpectedAmount,
                        ActualAmount = actual,
                        Critical = false,
                        IsMana = false
                    });
                }
            }
            catch (Exception ex) { Log.LogError("TickEffects error: " + ex); }
        }
    }
}
