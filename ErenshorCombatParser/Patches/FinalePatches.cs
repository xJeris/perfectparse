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
        private static int _preDeliverHP;
        private static int _preDeliverMaxHP;
        private static Character _targetChar;
        private static Character _sourceChar;
        private static GameData.DamageType _dmgType;

        [HarmonyPrefix]
        static void Prefix(WandBolt __instance)
        {
            try
            {
                _targetChar = __instance.TargetChar;
                _sourceChar = __instance.SourceChar;
                _dmgType = __instance.DmgType;
                if (_targetChar != null && _targetChar.MyStats != null)
                {
                    _preDeliverHP = _targetChar.MyStats.CurrentHP;
                    _preDeliverMaxHP = _targetChar.MyStats.CurrentMaxHP;
                }
                else
                {
                    _preDeliverHP = 0;
                    _preDeliverMaxHP = 0;
                }
            }
            catch (Exception) { }
        }

        [HarmonyPostfix]
        static void Postfix(WandBolt __instance)
        {
            try
            {
                if (_targetChar == null || _targetChar.MyStats == null) return;
                if (_sourceChar == null) return;

                // Finale condition: HP went to 0, was > 0 before, was <= 15% max
                if (_targetChar.MyStats.CurrentHP == 0 &&
                    _preDeliverHP > 0 &&
                    _preDeliverMaxHP > 0 &&
                    (float)_preDeliverHP / _preDeliverMaxHP <= 0.15f)
                {
                    string dmgTypeName = _dmgType == GameData.DamageType.Physical ? "Physical" : "Magic";
                    CombatEventBus.EmitDamage(new CombatEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Type = "Finale",
                        SourceId = EntityRegistry.ResolveId(_sourceChar),
                        TargetId = EntityRegistry.ResolveId(_targetChar),
                        DamageType = dmgTypeName,
                        RawAmount = _preDeliverHP,
                        FinalAmount = _preDeliverHP,
                        Critical = false,
                        Source = "Wand (Finale)"
                    }, _sourceChar, _targetChar);
                }
            }
            catch (Exception) { }
        }
    }
}
