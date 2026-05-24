using System.Text;
using ErenshorCombatParser.IO;

namespace ErenshorCombatParser.Models
{
    public class HealEvent
    {
        public long Timestamp;
        public string Type;        // "Heal", "HoT", "Lifesteal", "HealSimple", "ManaRestore"
        public string SourceId;
        public string TargetId;
        public string SpellName;
        public int RawAmount;
        public int ActualAmount;
        public bool Critical;
        public bool IsMana;
        public int EncounterId;

        public string ToJsonLine()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"ev\":\"heal\"");
            sb.Append(",\"t\":").Append(Timestamp);
            sb.Append(",\"type\":\"").Append(Type).Append('"');
            if (SourceId != null)
                sb.Append(",\"src\":\"").Append(JsonUtil.EscapeJson(SourceId)).Append('"');
            sb.Append(",\"tgt\":\"").Append(JsonUtil.EscapeJson(TargetId)).Append('"');
            if (SpellName != null)
                sb.Append(",\"spell\":\"").Append(JsonUtil.EscapeJson(SpellName)).Append('"');
            sb.Append(",\"raw\":").Append(RawAmount);
            sb.Append(",\"actual\":").Append(ActualAmount);
            if (Critical) sb.Append(",\"crit\":true");
            if (IsMana) sb.Append(",\"mana\":true");
            if (EncounterId > 0) sb.Append(",\"enc\":").Append(EncounterId);
            sb.Append('}');
            return sb.ToString();
        }

    }
}
