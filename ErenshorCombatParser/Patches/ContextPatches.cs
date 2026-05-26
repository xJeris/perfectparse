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
        /// <summary>
        /// Returns true if the skill applies a bleed effect through any path:
        /// direct EffectToApply, CastOnTarget that IS a bleed, or CastOnTarget
        /// with a StatusEffectToApply that is a bleed.
        /// </summary>
        private static bool SkillAppliesBleed(Skill skill)
        {
            if (skill.EffectToApply != null && skill.EffectToApply.BleedDamagePercent > 0)
                return true;
            var cast = skill.CastOnTarget;
            if (cast != null)
            {
                if (cast.BleedDamagePercent > 0)
                    return true;
                if (cast.StatusEffectToApply != null && cast.StatusEffectToApply.BleedDamagePercent > 0)
                    return true;
            }
            return false;
        }

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
                    {
                        CombatContext.Set(ch, "Skill:" + (_skill.SkillName ?? "Unknown"));
                        // If this skill applies a bleed (directly or via CastOnTarget),
                        // stash the skill name before ResolveSpell overwrites CombatContext
                        if (SkillAppliesBleed(_skill))
                            DamagePatches.SetPendingBleedSkill(ch, _skill.SkillName ?? "Unknown");
                    }
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
                    {
                        CombatContext.Set(ch, "Skill:" + (_skill.SkillName ?? "Unknown"));
                        if (SkillAppliesBleed(_skill))
                            DamagePatches.SetPendingBleedSkill(ch, _skill.SkillName ?? "Unknown");
                    }
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
                    {
                        var spell = __instance.spell;
                        CombatContext.Set(ch, "Spell:" + (spell.SpellName ?? "Unknown"));
                        // If this spell applies a bleed via StatusEffectToApply,
                        // stash the parent spell name for bleed attribution.
                        // Don't overwrite if DoSkill already set the skill name.
                        var se = spell.StatusEffectToApply;
                        if (se != null && se.BleedDamagePercent > 0
                            && !DamagePatches.HasPendingBleedSkill(ch))
                            DamagePatches.SetPendingBleedSkill(ch, spell.SpellName ?? "Unknown");
                    }
                }
            }
            catch (Exception) { }
        }

        // ============================================================
        // Spell-from-proc — captures bleed context before ResolveSpell
        // overwrites CombatContext. When a bleed spell is cast via proc
        // (weapon proc, skill proc, AddProc), CombatContext still holds
        // the Skill:/Melee context from the triggering attack.
        // ============================================================
        [HarmonyPatch(typeof(CastSpell), "StartSpellFromProc")]
        [HarmonyPrefix]
        static void StartSpellFromProc_Prefix(CastSpell __instance, Spell _spell)
        {
            try
            {
                if (_spell != null)
                {
                    bool isBleed = _spell.BleedDamagePercent > 0
                        || (_spell.StatusEffectToApply != null
                            && _spell.StatusEffectToApply.BleedDamagePercent > 0);
                    if (isBleed)
                    {
                        var ch = __instance.MyChar;
                        if (ch != null)
                        {
                            // If DoSkill already set a pending bleed skill name
                            // (e.g. "Arterial Razor"), don't overwrite it with the
                            // generic CombatContext (which may be "Bow" by now).
                            // Only set from CombatContext if there's no pending entry.
                            if (!DamagePatches.HasPendingBleedSkill(ch))
                            {
                                string ctx = CombatContext.Get(ch);
                                if (ctx != null)
                                    DamagePatches.SetPendingBleedSkill(ch, ctx);
                            }
                        }
                    }
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
