using System.Collections.Generic;
using UnityEngine;
using ErenshorCombatParser.IO;

namespace ErenshorCombatParser.Core
{
    public static class EntityRegistry
    {
        private static readonly Dictionary<int, EntitySnapshot> _cache
            = new Dictionary<int, EntitySnapshot>();

        // Keyed by entity ID string — never cleared. Used for report generation
        // so entities from previous zones are still available.
        private static readonly Dictionary<string, EntitySnapshot> _reportEntities
            = new Dictionary<string, EntitySnapshot>();

        // Class name remapping: code name -> display name
        // Only applied to Player and SimPlayer entities
        private static readonly Dictionary<string, string> _classRemap
            = new Dictionary<string, string>
            {
                { "Duelist", "Windblade" }
            };

        public static EntitySnapshot Resolve(Character character)
        {
            if (character == null) return null;

            int key = character.GetInstanceID();
            if (_cache.TryGetValue(key, out var existing))
                return existing;

            var snapshot = BuildSnapshot(character);
            _cache[key] = snapshot;
            _reportEntities[snapshot.Id] = snapshot;
            CombatEventBus.EmitEntity(snapshot);
            return snapshot;
        }

        public static string ResolveId(Character character)
        {
            return Resolve(character)?.Id;
        }

        private static EntitySnapshot BuildSnapshot(Character c)
        {
            if (!c.isNPC)
            {
                string className = GetClassName(c);
                if (className != null && _classRemap.TryGetValue(className, out var remapped))
                    className = remapped;

                string playerName = c.MyStats != null && !string.IsNullOrEmpty(c.MyStats.MyName)
                    ? c.MyStats.MyName
                    : c.transform.name;

                return new EntitySnapshot
                {
                    Id = "Player",
                    DisplayName = playerName + " [P]",
                    ClassName = className,
                    Level = c.MyStats != null ? c.MyStats.Level : 0,
                    Type = EntitySnapshot.EntityType.Player
                };
            }

            var npc = c.MyNPC;
            bool isSim = npc != null && npc.SimPlayer;
            bool isPet = (c.MyStats != null && c.MyStats.Charmed) ||
                         (npc != null && npc.SummonedByPlayer);

            string entityName = npc != null ? npc.NPCName : c.transform.name;
            string id;
            EntitySnapshot.EntityType type;
            string masterEntityId = null;

            if (isSim)
            {
                id = "Sim:" + entityName;
                type = EntitySnapshot.EntityType.SimPlayer;
            }
            else if (isPet)
            {
                // Key by owner name + pet name instead of instance ID so the
                // same pet doesn't create duplicate entries after zone changes
                // or cache clears. Each owner can only have one pet at a time.
                string ownerName = c.Master != null ? c.Master.transform.name : "Unknown";
                id = "Pet:" + ownerName + ":" + entityName;
                type = EntitySnapshot.EntityType.Pet;
                if (c.Master != null)
                    masterEntityId = Resolve(c.Master)?.Id;
            }
            else
            {
                id = "NPC:" + c.GetInstanceID() + ":" + entityName;
                type = EntitySnapshot.EntityType.NPC;
            }

            string clsName = GetClassName(c);
            // Only remap class names for player-side entities
            if ((isSim || isPet) && clsName != null && _classRemap.TryGetValue(clsName, out var remappedCls))
                clsName = remappedCls;

            return new EntitySnapshot
            {
                Id = id,
                DisplayName = entityName,
                ClassName = clsName,
                Level = c.MyStats != null ? c.MyStats.Level : 0,
                Type = type,
                MasterEntityId = masterEntityId
            };
        }

        private static string GetClassName(Character c)
        {
            if (c.MyStats == null) return null;
            var cls = c.MyStats.CharacterClass;
            if (cls == null) return null;
            // Use ClassName field from Class ScriptableObject
            return cls.ClassName;
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }

        public static void ClearReportEntities()
        {
            _reportEntities.Clear();
        }

        /// <summary>
        /// Serializes all known entities to a JSON object string for the HTML report.
        /// Uses _reportEntities which persists across scene changes.
        /// </summary>
        public static string ToJson()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append('{');
            bool first = true;
            foreach (var kvp in _reportEntities)
            {
                var e = kvp.Value;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsonUtil.EscapeJson(e.Id)).Append("\":{");
                sb.Append("\"name\":\"").Append(JsonUtil.EscapeJson(e.DisplayName)).Append('"');
                if (e.ClassName != null)
                    sb.Append(",\"class\":\"").Append(JsonUtil.EscapeJson(e.ClassName)).Append('"');
                sb.Append(",\"level\":").Append(e.Level);
                sb.Append(",\"type\":\"").Append(e.Type.ToString()).Append('"');
                if (e.MasterEntityId != null)
                    sb.Append(",\"master\":\"").Append(JsonUtil.EscapeJson(e.MasterEntityId)).Append('"');
                sb.Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
