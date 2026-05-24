namespace ErenshorCombatParser.IO
{
    public static class JsonUtil
    {
        public static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
