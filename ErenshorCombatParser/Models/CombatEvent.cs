using System.Text;

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
            sb.Append(",\"src\":\"").Append(EscapeJson(SourceId)).Append('"');
            sb.Append(",\"tgt\":\"").Append(EscapeJson(TargetId)).Append('"');
            sb.Append(",\"dmgType\":\"").Append(DamageType).Append('"');
            sb.Append(",\"raw\":").Append(RawAmount);
            sb.Append(",\"final\":").Append(FinalAmount);
            if (Critical) sb.Append(",\"crit\":true");
            sb.Append(",\"source\":\"").Append(EscapeJson(Source)).Append('"');
            if (EncounterId > 0) sb.Append(",\"enc\":").Append(EncounterId);
            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
