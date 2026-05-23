using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Harmony patches for all damage entry points in Character.
    /// Uses manual patching for reliability.
    /// </summary>
    public static class DamagePatches
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("PerfectParse.Damage");

        private static bool _verified = false;

        public static void Apply(Harmony harmony)
        {
            var self = typeof(DamagePatches);

            // Test: patch PlayerCombat.Attack or similar to verify Harmony works at all
            try
            {
                var pcType = typeof(PlayerCombat);
                var attackMethod = pcType.GetMethod("Attack",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (attackMethod != null)
                {
                    harmony.Patch(attackMethod,
                        prefix: new HarmonyMethod(self.GetMethod("TestAttack_Prefix",
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    Log.LogInfo($"TEST: Patched PlayerCombat.Attack ({attackMethod.DeclaringType.Name})");
                }
                else
                {
                    // List all methods on PlayerCombat
                    var methods = pcType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    Log.LogInfo($"PlayerCombat methods ({methods.Length}):");
                    foreach (var m in methods)
                        Log.LogInfo($"  {m.Name}({string.Join(",", System.Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))})");
                }
            }
            catch (Exception ex) { Log.LogError("Test patch failed: " + ex); }

            // DamageMe — use BOTH prefix and postfix to diagnose
            var dmgMe = typeof(Character).GetMethod("DamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (dmgMe != null)
            {
                harmony.Patch(dmgMe,
                    prefix: new HarmonyMethod(self.GetMethod("DamageMe_Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(self.GetMethod("DamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.DamageMe (prefix + postfix)");
            }
            else Log.LogError("Could not find Character.DamageMe");

            // MagicDamageMe
            var magicDmg = typeof(Character).GetMethod("MagicDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (magicDmg != null)
            {
                harmony.Patch(magicDmg,
                    postfix: new HarmonyMethod(self.GetMethod("MagicDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.MagicDamageMe");
            }
            else Log.LogError("Could not find Character.MagicDamageMe");

            // BleedDamageMe
            var bleedDmg = typeof(Character).GetMethod("BleedDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (bleedDmg != null)
            {
                harmony.Patch(bleedDmg,
                    postfix: new HarmonyMethod(self.GetMethod("BleedDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.BleedDamageMe");
            }
            else Log.LogError("Could not find Character.BleedDamageMe");

            // SelfDamageMe
            var selfDmg = typeof(Character).GetMethod("SelfDamageMe",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(float) }, null);
            if (selfDmg != null)
            {
                harmony.Patch(selfDmg,
                    postfix: new HarmonyMethod(self.GetMethod("SelfDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.SelfDamageMe");
            }
            else Log.LogError("Could not find Character.SelfDamageMe");

            // SelfDamageMeFlat
            var selfDmgFlat = typeof(Character).GetMethod("SelfDamageMeFlat",
                BindingFlags.Public | BindingFlags.Instance);
            if (selfDmgFlat != null)
            {
                harmony.Patch(selfDmgFlat,
                    postfix: new HarmonyMethod(self.GetMethod("SelfDamageMeFlat_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.SelfDamageMeFlat");
            }
            else Log.LogError("Could not find Character.SelfDamageMeFlat");

            // DamageShieldTaken
            var dmgShield = typeof(Character).GetMethod("DamageShieldTaken",
                BindingFlags.Public | BindingFlags.Instance);
            if (dmgShield != null)
            {
                harmony.Patch(dmgShield,
                    prefix: new HarmonyMethod(self.GetMethod("DamageShieldTaken_Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.DamageShieldTaken");
            }
            else Log.LogError("Could not find Character.DamageShieldTaken");

            // EnvironmentalDamageMe
            var envDmg = typeof(Character).GetMethod("EnvironmentalDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (envDmg != null)
            {
                harmony.Patch(envDmg,
                    postfix: new HarmonyMethod(self.GetMethod("EnvironmentalDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Patched Character.EnvironmentalDamageMe");
            }
            else Log.LogError("Could not find Character.EnvironmentalDamageMe");
        }

        // ============================================================
        // Test handler — verify Harmony is intercepting calls
        // ============================================================

        static void TestAttack_Prefix()
        {
            if (!_verified)
            {
                Log.LogInfo("TEST: PlayerCombat.Attack was called! Harmony interception WORKS.");
                _verified = true;
            }
        }

        // ============================================================
        // Prefix handlers (for diagnosis)
        // ============================================================

        static void DamageMe_Prefix(Character __instance, int _incdmg)
        {
            Log.LogInfo($"DamageMe PREFIX! target={__instance?.name} incdmg={_incdmg}");
        }

        // ============================================================
        // Postfix handlers
        // ============================================================

        static void DamageMe_Postfix(
            int __result,
            Character __instance,
            int _incdmg,
            GameData.DamageType _dmgType,
            Character _attacker,
            bool _criticalHit)
        {
            try
            {
                Log.LogInfo($"DamageMe fired! result={__result} incdmg={_incdmg} attacker={_attacker?.name} target={__instance?.name}");

                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Miss"; finalAmount = 0; }
                else if (__result == -2) { eventType = "ShieldAbsorb"; finalAmount = 0; }
                else return; // -1 invuln, -3 friendly, -5 mining, -6 chest — skip

                string source = CombatContext.Get() ?? "Melee";

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = eventType,
                    SourceId = EntityRegistry.ResolveId(_attacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = _dmgType.ToString(),
                    RawAmount = _incdmg,
                    FinalAmount = finalAmount,
                    Critical = _criticalHit,
                    Source = source
                });
            }
            catch (Exception ex) { Log.LogError("DamageMe: " + ex); }
        }

        static void MagicDamageMe_Postfix(
            int __result,
            Character __instance,
            int _dmg,
            GameData.DamageType _dmgType,
            Character _attacker)
        {
            try
            {
                Log.LogInfo($"MagicDamageMe fired! result={__result} dmg={_dmg} attacker={_attacker?.name} target={__instance?.name}");

                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Resist"; finalAmount = 0; }
                else return;

                string source = CombatContext.Get() ?? "Spell:Unknown";

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = eventType,
                    SourceId = EntityRegistry.ResolveId(_attacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = _dmgType.ToString(),
                    RawAmount = _dmg,
                    FinalAmount = finalAmount,
                    Critical = false,
                    Source = source
                });
            }
            catch (Exception ex) { Log.LogError("MagicDamageMe: " + ex); }
        }

        static void BleedDamageMe_Postfix(
            int __result,
            Character __instance,
            int _incdmg,
            Character _attacker)
        {
            try
            {
                if (__result <= 0) return;

                string source = CombatContext.Get() ?? "Bleed";

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = EntityRegistry.ResolveId(_attacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Physical",
                    RawAmount = _incdmg,
                    FinalAmount = __result,
                    Critical = false,
                    Source = source
                });
            }
            catch (Exception) { }
        }

        static void SelfDamageMe_Postfix(
            int __result,
            Character __instance)
        {
            try
            {
                if (__result <= 0) return;

                var id = EntityRegistry.ResolveId(__instance);
                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = id,
                    TargetId = id,
                    DamageType = "Physical",
                    RawAmount = __result,
                    FinalAmount = __result,
                    Critical = false,
                    Source = "SelfDamage:Stance"
                });
            }
            catch (Exception) { }
        }

        static void SelfDamageMeFlat_Postfix(
            int __result,
            Character __instance,
            int _incDmg)
        {
            try
            {
                if (__result <= 0) return;

                var id = EntityRegistry.ResolveId(__instance);
                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = id,
                    TargetId = id,
                    DamageType = "Physical",
                    RawAmount = _incDmg,
                    FinalAmount = __result,
                    Critical = false,
                    Source = "SelfDamage:Flat"
                });
            }
            catch (Exception) { }
        }

        static void DamageShieldTaken_Prefix(
            Character __instance,
            int _dmg,
            Stats _giver)
        {
            try
            {
                if (_dmg <= 0) return;

                var sourceChar = _giver != null ? _giver.Myself : null;

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Reflect",
                    SourceId = EntityRegistry.ResolveId(sourceChar),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Magic",
                    RawAmount = _dmg,
                    FinalAmount = _dmg,
                    Critical = false,
                    Source = "DamageShield"
                });
            }
            catch (Exception) { }
        }

        static void EnvironmentalDamageMe_Postfix(
            int __result,
            Character __instance,
            int _dmg)
        {
            try
            {
                if (__result == -1) return;

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = "Environment",
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Physical",
                    RawAmount = _dmg,
                    FinalAmount = _dmg,
                    Critical = false,
                    Source = "Environmental"
                });
            }
            catch (Exception) { }
        }
    }
}
