using System;
using System.IO;
using System.Text;

namespace ErenshorCombatParser.IO
{
    public static class HtmlReportGenerator
    {
        /// <summary>
        /// Generate from in-game with live entity/encounter data.
        /// </summary>
        public static void Generate(string jsonlPath, string outputPath,
            string entityJson, string encounterJson)
        {
            string eventsJson = BuildEventsArray(jsonlPath);

            string html = HtmlTemplate.Build(eventsJson, entityJson, encounterJson);
            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        /// <summary>
        /// Generate from a JSONL file alone (standalone / outside of game).
        /// Entity and encounter data are extracted from the JSONL lines themselves.
        /// </summary>
        public static void GenerateStandalone(string jsonlPath, string outputPath)
        {
            string[] lines = ReadAllLinesShared(jsonlPath);

            // Separate entity lines from combat/heal lines, and build encounter data
            var eventsSb = new StringBuilder(lines.Length * 200);
            var entitySb = new StringBuilder(512);
            var encounterSb = new StringBuilder(256);

            eventsSb.Append('[');
            entitySb.Append('{');
            encounterSb.Append('[');

            bool firstEvent = true;
            bool firstEntity = true;
            // Track encounter IDs and per-encounter timestamp ranges
            int maxEncId = 0;
            long firstTimestamp = 0;
            long lastTimestamp = 0;
            var encStart = new System.Collections.Generic.Dictionary<int, long>();
            var encEnd = new System.Collections.Generic.Dictionary<int, long>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                // Quick check for event type by looking for "ev":" prefix
                if (line.Contains("\"ev\":\"entity\""))
                {
                    // Entity line — parse and add to entity map
                    // Extract id, name, class, level, type, master from the JSON
                    string id = ExtractJsonString(line, "id");
                    if (id != null)
                    {
                        if (!firstEntity) entitySb.Append(',');
                        firstEntity = false;

                        entitySb.Append('"').Append(JsonUtil.EscapeJson(id)).Append("\":{");
                        string name = ExtractJsonString(line, "name");
                        entitySb.Append("\"name\":\"").Append(JsonUtil.EscapeJson(name ?? id)).Append('"');

                        string cls = ExtractJsonString(line, "class");
                        if (cls != null)
                            entitySb.Append(",\"class\":\"").Append(JsonUtil.EscapeJson(cls)).Append('"');

                        string level = ExtractJsonValue(line, "level");
                        entitySb.Append(",\"level\":").Append(level ?? "0");

                        string type = ExtractJsonString(line, "type");
                        entitySb.Append(",\"type\":\"").Append(JsonUtil.EscapeJson(type ?? "NPC")).Append('"');

                        string master = ExtractJsonString(line, "master");
                        if (master != null)
                            entitySb.Append(",\"master\":\"").Append(JsonUtil.EscapeJson(master)).Append('"');

                        entitySb.Append('}');
                    }
                }
                else
                {
                    // Combat or heal event
                    if (!firstEvent) eventsSb.Append(',');
                    firstEvent = false;
                    eventsSb.Append(line);

                    // Track timestamps for encounter estimation
                    string ts = ExtractJsonValue(line, "t");
                    long t = 0;
                    if (ts != null && long.TryParse(ts, out t))
                    {
                        if (firstTimestamp == 0) firstTimestamp = t;
                        lastTimestamp = t;
                    }

                    // Track per-encounter timestamp ranges
                    string enc = ExtractJsonValue(line, "enc");
                    if (enc != null && int.TryParse(enc, out int encId) && encId > 0)
                    {
                        if (encId > maxEncId) maxEncId = encId;
                        if (t > 0)
                        {
                            if (!encStart.ContainsKey(encId) || t < encStart[encId])
                                encStart[encId] = t;
                            if (!encEnd.ContainsKey(encId) || t > encEnd[encId])
                                encEnd[encId] = t;
                        }
                    }
                }
            }

            eventsSb.Append(']');
            entitySb.Append('}');

            // Build encounter array — derive start/end from event timestamps
            bool firstEnc = true;
            for (int id = 1; id <= maxEncId; id++)
            {
                if (!firstEnc) encounterSb.Append(',');
                firstEnc = false;
                long eStart = encStart.ContainsKey(id) ? encStart[id] : 0;
                long eEnd = encEnd.ContainsKey(id) ? encEnd[id] : 0;
                encounterSb.Append("{\"id\":").Append(id);
                encounterSb.Append(",\"start\":").Append(eStart);
                encounterSb.Append(",\"end\":").Append(eEnd);
                encounterSb.Append(",\"label\":\"Encounter ").Append(id).Append('"');
                encounterSb.Append('}');
            }
            encounterSb.Append(']');

            string html = HtmlTemplate.Build(
                eventsSb.ToString(), entitySb.ToString(), encounterSb.ToString());

            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        private static string[] ReadAllLinesShared(string path)
        {
            if (!File.Exists(path)) return Array.Empty<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                var lines = new System.Collections.Generic.List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
                return lines.ToArray();
            }
        }

        private static string BuildEventsArray(string jsonlPath)
        {
            string[] lines = ReadAllLinesShared(jsonlPath);

            var sb = new StringBuilder(lines.Length * 200);
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                // Skip entity lines — they're metadata, not events
                if (line.Contains("\"ev\":\"entity\"")) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(line);
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Extracts a string value for a given key from a JSON line.
        /// Simple parser — does not handle escaped quotes in values.
        /// </summary>
        private static string ExtractJsonString(string json, string key)
        {
            string pattern = "\"" + key + "\":\"";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0) return null;
            start += pattern.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        /// <summary>
        /// Extracts a raw value (number, bool) for a given key from a JSON line.
        /// </summary>
        private static string ExtractJsonValue(string json, string key)
        {
            string pattern = "\"" + key + "\":";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0) return null;
            start += pattern.Length;
            int end = start;
            while (end < json.Length && json[end] != ',' && json[end] != '}')
                end++;
            return json.Substring(start, end - start).Trim();
        }

    }
}
