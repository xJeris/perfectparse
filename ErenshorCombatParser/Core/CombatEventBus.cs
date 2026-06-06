using System;
using System.Reflection;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Central event dispatcher. All Harmony patches emit events through here.
    /// The JsonLineWriter and EncounterTracker subscribe to these events.
    /// </summary>
    public static class CombatEventBus
    {
        public static event Action<CombatEvent> OnCombatEvent;
        public static event Action<HealEvent> OnHealEvent;
        public static event Action<EntitySnapshot> OnEntityEvent;

        // Reflection accessor for SimPlayer.InRaid (may not exist in playtest builds)
        private static FieldInfo _inRaidField;
        private static bool _inRaidChecked;

        /// <summary>
        /// Clears all event subscribers. Called during shutdown to prevent
        /// stale delegates persisting across Lunaris hot-reloads.
        /// </summary>
        public static void ClearSubscribers()
        {
            OnCombatEvent = null;
            OnHealEvent = null;
            OnEntityEvent = null;
        }

        public static void EmitDamage(CombatEvent evt, Character source = null, Character target = null)
        {
            if (!IsRelevantEntity(source) && !IsRelevantEntity(target))
                return;

            EncounterTracker.NotifyCombatActivity();
            evt.EncounterId = EncounterTracker.CurrentEncounterId;
            OnCombatEvent?.Invoke(evt);
        }

        public static void EmitHeal(HealEvent evt, Character source = null, Character target = null)
        {
            if (!IsRelevantEntity(source) && !IsRelevantEntity(target))
                return;

            evt.EncounterId = EncounterTracker.CurrentEncounterId;
            OnHealEvent?.Invoke(evt);
        }

        public static void EmitEntity(EntitySnapshot snapshot)
        {
            OnEntityEvent?.Invoke(snapshot);
        }

        /// <summary>
        /// Returns true if the character is the player, a pet, or a sim
        /// currently in the player's group or raid.
        /// </summary>
        private static bool IsRelevantEntity(Character c)
        {
            if (c == null) return false;

            // Player character is always relevant
            if (!c.isNPC) return true;

            var npc = c.MyNPC;
            if (npc == null) return false;

            // Pets are always relevant (charmed or summoned by player)
            if ((c.MyStats != null && c.MyStats.Charmed) ||
                npc.SummonedByPlayer)
                return true;

            // Sim players: only relevant if in the player's group or raid
            if (npc.SimPlayer && npc.ThisSim != null)
            {
                if (npc.ThisSim.InGroup) return true;
                if (GetInRaid(npc.ThisSim)) return true;
            }

            return false;
        }

        /// <summary>
        /// Safely reads SimPlayer.InRaid via reflection so we don't crash
        /// if the field doesn't exist in the playtest build.
        /// </summary>
        private static bool GetInRaid(SimPlayer sim)
        {
            if (!_inRaidChecked)
            {
                _inRaidChecked = true;
                try
                {
                    _inRaidField = typeof(SimPlayer).GetField("InRaid",
                        BindingFlags.Public | BindingFlags.Instance);
                }
                catch (Exception) { }
            }

            if (_inRaidField == null) return false;

            try
            {
                return (bool)_inRaidField.GetValue(sim);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
