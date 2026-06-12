using System;
using HarmonyLib;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Detects the Finale ascension instant-kill, which sets CurrentHP = 0
    /// directly without calling DamageMe. We compare HP before and after
    /// WandBolt.DeliverDamage to catch this.
    /// </summary>
    [HarmonyPatch(typeof(WandBolt), "DeliverDamage")]
    public static class FinalePatches
    {
        public struct FinaleState
        {
            public int PreHP;
            public int PreMaxHP;
            public Character Target;
            public Character Source;
            public GameData.DamageType DmgType;
        }

        [HarmonyPrefix]
        static void Prefix(WandBolt __instance, ref FinaleState __state)
        {
            try
            {
                __state.Target = __instance.TargetChar;
                __state.Source = __instance.SourceChar;
                __state.DmgType = __instance.DmgType;
                if (__state.Target != null && __state.Target.MyStats != null)
                {
                    __state.PreHP = __state.Target.MyStats.CurrentHP;
                    __state.PreMaxHP = __state.Target.MyStats.CurrentMaxHP;
                }
                else
                {
                    __state.PreHP = 0;
                    __state.PreMaxHP = 0;
                }
            }
            catch (Exception) { }
        }

        [HarmonyPostfix]
        static void Postfix(WandBolt __instance, FinaleState __state)
        {
            try
            {
                if (__state.Target == null || __state.Target.MyStats == null) return;
                if (__state.Source == null) return;

                // Finale condition: HP went to 0 and was > 0 before.
                // The game itself enforces the <=15% post-hit threshold, so we
                // don't re-check it here — if HP is 0 after DeliverDamage and
                // the normal DamageMe didn't kill the target, Finale fired.
                if (__state.Target.MyStats.CurrentHP == 0 &&
                    __state.PreHP > 0)
                {
                    // Subtract the wand hit that preceded the Finale within
                    // this same DeliverDamage call to avoid double-counting.
                    int wandDamage = DamagePatches.ConsumeLastDamage(__state.Target);
                    int finaleAmount = __state.PreHP - wandDamage;

                    // If the wand hit itself killed the target, there's no
                    // Finale damage to attribute.
                    if (finaleAmount <= 0) return;

                    string dmgTypeName = __state.DmgType == GameData.DamageType.Physical ? "Physical" : "Magic";
                    CombatEventBus.EmitDamage(new CombatEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Type = "Finale",
                        SourceId = EntityRegistry.ResolveId(__state.Source),
                        TargetId = EntityRegistry.ResolveId(__state.Target),
                        DamageType = dmgTypeName,
                        RawAmount = finaleAmount,
                        FinalAmount = finaleAmount,
                        Critical = false,
                        Source = "Wand (Finale)"
                    }, __state.Source, __state.Target);
                }
            }
            catch (Exception) { }
        }
    }
}
