using System;
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

        public static void EmitDamage(CombatEvent evt)
        {
            // Notify encounter tracker first so auto-detection starts the encounter
            // before we assign the ID — ensures the first event gets the correct ID
            if (evt.SourceId != null &&
                (evt.SourceId == "Player" ||
                 evt.SourceId.StartsWith("Sim:") ||
                 evt.SourceId.StartsWith("Pet:")) ||
                evt.TargetId == "Player" ||
                (evt.TargetId != null && evt.TargetId.StartsWith("Sim:")))
            {
                EncounterTracker.NotifyCombatActivity();
            }

            evt.EncounterId = EncounterTracker.CurrentEncounterId;
            OnCombatEvent?.Invoke(evt);
        }

        public static void EmitHeal(HealEvent evt)
        {
            evt.EncounterId = EncounterTracker.CurrentEncounterId;
            OnHealEvent?.Invoke(evt);
        }

        public static void EmitEntity(EntitySnapshot snapshot)
        {
            OnEntityEvent?.Invoke(snapshot);
        }
    }
}
