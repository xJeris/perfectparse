using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ErenshorCombatParser.Core;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Prefix patches that set CombatContext before damage methods fire.
    /// This lets DamagePatches know whether damage came from melee, bow,
    /// a skill, a spell, or a wand bolt.
    /// </summary>
    [HarmonyPatch]
    public static class ContextPatches
    {
        // ============================================================
        // Player melee attacks
        // ============================================================
        [HarmonyPatch(typeof(PlayerCombat), "HandleDamageResult")]
        [HarmonyPrefix]
        static void PlayerMelee_Prefix()
        {
            CombatContext.Set("Melee");
        }

        // ============================================================
        // NPC/Sim melee attacks (private methods)
        // ============================================================
        [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
        [HarmonyPrefix]
        static void NPCMelee_Prefix()
        {
            CombatContext.Set("Melee");
        }

        [HarmonyPatch(typeof(NPC), "PerformMeleeHitPreCalc")]
        [HarmonyPrefix]
        static void NPCMeleePreCalc_Prefix()
        {
            CombatContext.Set("Melee");
        }

        // ============================================================
        // Player/NPC skill usage
        // ============================================================
        [HarmonyPatch(typeof(UseSkill), "DoSkill")]
        [HarmonyPrefix]
        static void DoSkill_Prefix(Skill _skill)
        {
            try
            {
                if (_skill != null)
                    CombatContext.Set("Skill:" + (_skill.SkillName ?? "Unknown"));
            }
            catch (Exception) { }
        }

        [HarmonyPatch(typeof(UseSkill), "DoSkillNoChecks")]
        [HarmonyPrefix]
        static void DoSkillNoChecks_Prefix(Skill _skill)
        {
            try
            {
                if (_skill != null)
                    CombatContext.Set("Skill:" + (_skill.SkillName ?? "Unknown"));
            }
            catch (Exception) { }
        }

        // ============================================================
        // Spell resolution (private method)
        // ============================================================
        [HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]
        [HarmonyPrefix]
        static void ResolveSpell_Prefix(SpellVessel __instance)
        {
            try
            {
                if (__instance.spell != null)
                    CombatContext.Set("Spell:" + (__instance.spell.SpellName ?? "Unknown"));
            }
            catch (Exception) { }
        }

        // ============================================================
        // Wand bolt delivery (private method)
        // ============================================================
        [HarmonyPatch(typeof(WandBolt), "DeliverDamage")]
        [HarmonyPrefix]
        static void DeliverDamage_Prefix(WandBolt __instance)
        {
            try
            {
                if (__instance.DmgType == GameData.DamageType.Physical)
                    CombatContext.Set("Bow");
                else
                    CombatContext.Set("Wand");
            }
            catch (Exception) { }
        }

        // ============================================================
        // NPC Bow attacks — 5 overloads
        // Using TargetMethods to patch all at once
        // ============================================================
        [HarmonyPatch]
        static class NPCBowPatches
        {
            [HarmonyTargetMethods]
            static IEnumerable<MethodBase> TargetMethods()
            {
                var npcType = typeof(NPC);
                foreach (var method in npcType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.Name == "DoBowAttack")
                        yield return method;
                }
            }

            [HarmonyPrefix]
            static void Prefix()
            {
                CombatContext.Set("Bow");
            }
        }
    }
}
