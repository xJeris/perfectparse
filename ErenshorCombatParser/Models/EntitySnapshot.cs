using System;
using System.Text;
using ErenshorCombatParser.IO;

namespace ErenshorCombatParser.Core
{
    public class EntitySnapshot
    {
        public string Id;
        public string DisplayName;
        public string ClassName;
        public int Level;
        public EntityType Type;
        public string MasterEntityId;

        public enum EntityType { Player, SimPlayer, NPC, Pet }

        public string ToJsonLine()
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"ev\":\"entity\"");
            sb.Append(",\"id\":\"").Append(JsonUtil.EscapeJson(Id)).Append('"');
            sb.Append(",\"name\":\"").Append(JsonUtil.EscapeJson(DisplayName)).Append('"');
            if (ClassName != null)
                sb.Append(",\"class\":\"").Append(JsonUtil.EscapeJson(ClassName)).Append('"');
            sb.Append(",\"level\":").Append(Level);
            sb.Append(",\"type\":\"").Append(Type.ToString()).Append('"');
            if (MasterEntityId != null)
                sb.Append(",\"master\":\"").Append(JsonUtil.EscapeJson(MasterEntityId)).Append('"');
            sb.Append(",\"t\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            sb.Append('}');
            return sb.ToString();
        }
    }
}
