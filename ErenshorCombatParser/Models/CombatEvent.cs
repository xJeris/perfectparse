using System.Text;
using ErenshorCombatParser.IO;

namespace ErenshorCombatParser.Models
{
    public class CombatEvent
    {
        public long Timestamp;
        public string Type;        // "Damage", "Miss", "Resist", "ShieldAbsorb", "Finale", "Reflect"
        public string SourceId;
        public string TargetId;
        public string DamageType;  // "Physical", "Magic", "Elemental", "Void", "Poison"
        public int RawAmount;
        public int FinalAmount;
        public bool Critical;
        public string Source;      // "Melee", "Bow", "Skill:Backstab", "Spell:Fire Bolt", etc.
        public int EncounterId;

        public string ToJsonLine()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"ev\":\"combat\"");
            sb.Append(",\"t\":").Append(Timestamp);
            sb.Append(",\"type\":\"").Append(Type).Append('"');
            sb.Append(",\"src\":\"").Append(JsonUtil.EscapeJson(SourceId)).Append('"');
            sb.Append(",\"tgt\":\"").Append(JsonUtil.EscapeJson(TargetId)).Append('"');
            sb.Append(",\"dmgType\":\"").Append(DamageType).Append('"');
            sb.Append(",\"raw\":").Append(RawAmount);
            sb.Append(",\"final\":").Append(FinalAmount);
            if (Critical) sb.Append(",\"crit\":true");
            sb.Append(",\"source\":\"").Append(JsonUtil.EscapeJson(Source)).Append('"');
            if (EncounterId > 0) sb.Append(",\"enc\":").Append(EncounterId);
            sb.Append('}');
            return sb.ToString();
        }

    }
}
