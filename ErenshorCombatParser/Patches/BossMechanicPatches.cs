using System;
using System.Reflection;
using HarmonyLib;
using ErenshorCombatParser.Core;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Context patches for boss/NPC mechanic scripts that call DamageMe or
    /// MagicDamageMe directly, bypassing SpellVessel.ResolveSpell.
    /// Without these patches, their damage shows as "Spell:Unknown" or "Melee".
    ///
    /// Uses manual patching so each patch is independently try/caught —
    /// a missing boss script won't prevent the others from loading.
    /// </summary>
    public static class BossMechanicPatches
    {
        public static void Apply(Harmony harmony)
        {
            var self = typeof(BossMechanicPatches);

            TryPatch(harmony, "AEEvent", "TriggerAE", self, "AEEvent_Prefix");
            TryPatch(harmony, "AEEvent2", "Update", self, "AEEvent2_Prefix");
            TryPatch(harmony, "NPCFightEvent", "BreathAttack", self, "BreathAttack_Prefix");
            TryPatch(harmony, "SableheartEvent", "Update", self, "Sableheart_Prefix");
            TryPatch(harmony, "AstraBreathScriot", "Update", self, "AstraBreath_Prefix");
            TryPatch(harmony, "FernHighPriest", "Update", self, "FernHighPriest_Prefix");
            TryPatch(harmony, "InfernoEnergy", "Update", self, "InfernoEnergy_Prefix");
            TryPatch(harmony, "DeathTouch", "Update", self, "DeathTouch_Prefix");
        }

        private static void TryPatch(Harmony harmony, string typeName, string methodName,
            Type patchType, string prefixName)
        {
            try
            {
                var targetType = AccessTools.TypeByName(typeName);
                if (targetType == null) return;
                var method = AccessTools.Method(targetType, methodName);
                if (method == null) return;
                harmony.Patch(method, prefix: new HarmonyMethod(
                    patchType.GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception) { }
        }

        // ============================================================
        // AEEvent — generic AoE damage script
        // Uses DamageReason field for attack name (e.g. "from the poison cloud")
        // ============================================================
        static void AEEvent_Prefix(object __instance)
        {
            try
            {
                var t = Traverse.Create(__instance);
                var ch = t.Field("MyChar").GetValue<Character>();
                if (ch == null) return;
                var reason = t.Field("DamageReason").GetValue<string>();
                CombatContext.Set(ch, "Spell:" + (string.IsNullOrEmpty(reason) ? "AoE" : reason));
            }
            catch (Exception) { }
        }

        // ============================================================
        // AEEvent2 — variant AoE damage script
        // ============================================================
        static void AEEvent2_Prefix(object __instance)
        {
            try
            {
                var t = Traverse.Create(__instance);
                var ch = t.Field("MyChar").GetValue<Character>();
                if (ch == null) return;
                var reason = t.Field("DamageReason").GetValue<string>();
                CombatContext.Set(ch, "Spell:" + (string.IsNullOrEmpty(reason) ? "AoE" : reason));
            }
            catch (Exception) { }
        }

        // ============================================================
        // NPCFightEvent.BreathAttack — generic boss breath attack
        // ============================================================
        static void BreathAttack_Prefix(object __instance)
        {
            try
            {
                var stats = Traverse.Create(__instance).Field("MyStats").GetValue<Stats>();
                var ch = stats?.Myself;
                if (ch != null)
                    CombatContext.Set(ch, "Spell:Breath Attack");
            }
            catch (Exception) { }
        }

        // ============================================================
        // SableheartEvent — Sableheart's Curse (Void AoE)
        // ============================================================
        static void Sableheart_Prefix(object __instance)
        {
            try
            {
                var ch = Traverse.Create(__instance).Field("Sable").GetValue<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Spell:Sableheart's Curse");
            }
            catch (Exception) { }
        }

        // ============================================================
        // AstraBreathScriot — Dragon breath (may have OverrideBreath)
        // ============================================================
        static void AstraBreath_Prefix(object __instance)
        {
            try
            {
                var t = Traverse.Create(__instance);
                var ch = t.Field("Astra").GetValue<Character>();
                if (ch == null) return;
                var overrideName = t.Field("OverrideBreath").GetValue<string>();
                CombatContext.Set(ch, "Spell:" + (string.IsNullOrEmpty(overrideName) ? "Dragon Breath" : overrideName));
            }
            catch (Exception) { }
        }

        // ============================================================
        // FernHighPriest — Void shared pain mechanic
        // ============================================================
        static void FernHighPriest_Prefix(object __instance)
        {
            try
            {
                var ch = Traverse.Create(__instance).Field("myself").GetValue<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Spell:Shared Pain");
            }
            catch (Exception) { }
        }

        // ============================================================
        // InfernoEnergy — Twin explosion mechanic
        // ============================================================
        static void InfernoEnergy_Prefix(object __instance)
        {
            try
            {
                var ch = Traverse.Create(__instance).Field("TargetTwin").GetValue<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Spell:Twin Explosion");
            }
            catch (Exception) { }
        }

        // ============================================================
        // DeathTouch — Instant-kill boss mechanic (999999 Void damage)
        // ============================================================
        static void DeathTouch_Prefix(object __instance)
        {
            try
            {
                var ch = Traverse.Create(__instance).Field("MyChar").GetValue<Character>();
                if (ch != null)
                    CombatContext.Set(ch, "Spell:Death Touch");
            }
            catch (Exception) { }
        }
    }
}
