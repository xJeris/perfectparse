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
    /// a skill, a spell, or a wand bolt. Context is keyed by attacker
    /// Character to prevent cross-entity contamination in the same frame.
    /// </summary>
    [HarmonyPatch]
    public static class ContextPatches
    {
        // ============================================================
        // Player melee attacks
        // ============================================================
        [HarmonyPatch(typeof(PlayerCombat), "HandleDamageResult")]
        [HarmonyPrefix]
        static void PlayerMelee_Prefix(PlayerCombat __instance)
        {
            try
            {
                var ch = __instance.GetComponent<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Melee");
            }
            catch (Exception) { }
        }

        // ============================================================
        // NPC/Sim melee attacks (private methods)
        // ============================================================
        [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
        [HarmonyPrefix]
        static void NPCMelee_Prefix(NPC __instance)
        {
            try
            {
                var ch = __instance.GetComponent<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Melee");
            }
            catch (Exception) { }
        }

        [HarmonyPatch(typeof(NPC), "PerformMeleeHitPreCalc")]
        [HarmonyPrefix]
        static void NPCMeleePreCalc_Prefix(NPC __instance)
        {
            try
            {
                var ch = __instance.GetComponent<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Melee");
            }
            catch (Exception) { }
        }

        // ============================================================
        // Player/NPC skill usage
        // ============================================================
        [HarmonyPatch(typeof(UseSkill), "DoSkill")]
        [HarmonyPrefix]
        static void DoSkill_Prefix(UseSkill __instance, Skill _skill)
        {
            try
            {
                if (_skill != null)
                {
                    var ch = __instance.GetComponent<Character>();
                    if (ch != null)
                        CombatContext.Set(ch, "Skill:" + (_skill.SkillName ?? "Unknown"));
                }
            }
            catch (Exception) { }
        }

        [HarmonyPatch(typeof(UseSkill), "DoSkillNoChecks")]
        [HarmonyPrefix]
        static void DoSkillNoChecks_Prefix(UseSkill __instance, Skill _skill)
        {
            try
            {
                if (_skill != null)
                {
                    var ch = __instance.GetComponent<Character>();
                    if (ch != null)
                        CombatContext.Set(ch, "Skill:" + (_skill.SkillName ?? "Unknown"));
                }
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
                {
                    // SpellSource is a private CastSpell field; CastSpell.MyChar is the caster
                    var spellSource = Traverse.Create(__instance).Field("SpellSource").GetValue<CastSpell>();
                    var ch = spellSource?.MyChar;
                    if (ch != null)
                        CombatContext.Set(ch, "Spell:" + (__instance.spell.SpellName ?? "Unknown"));
                }
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
                var ch = __instance.SourceChar;
                if (ch != null)
                {
                    if (__instance.DmgType == GameData.DamageType.Physical)
                        CombatContext.Set(ch, "Bow");
                    else
                        CombatContext.Set(ch, "Wand");
                }
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
            static void Prefix(NPC __instance)
            {
                try
                {
                    var ch = __instance.GetComponent<Character>();
                    if (ch != null)
                        CombatContext.Set(ch, "Bow");
                }
                catch (Exception) { }
            }
        }
    }
}
