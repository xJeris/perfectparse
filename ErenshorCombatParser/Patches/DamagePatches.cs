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

        public static void Apply(Harmony harmony)
        {
            var self = typeof(DamagePatches);

            // DamageMe
            var dmgMe = typeof(Character).GetMethod("DamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (dmgMe != null)
            {
                harmony.Patch(dmgMe,
                    postfix: new HarmonyMethod(self.GetMethod("DamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
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
            }
            else Log.LogError("Could not find Character.EnvironmentalDamageMe");
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
                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Miss"; finalAmount = 0; }
                else if (__result == -2) { eventType = "ShieldAbsorb"; finalAmount = 0; }
                else return; // -1 invuln, -3 friendly, -5 mining, -6 chest — skip

                string source = CombatContext.Get(_attacker) ?? "Melee";

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
                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Resist"; finalAmount = 0; }
                else return;

                string source = CombatContext.Get(_attacker) ?? "Spell:Unknown";

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

                string source = CombatContext.Get(_attacker) ?? "Bleed";

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
