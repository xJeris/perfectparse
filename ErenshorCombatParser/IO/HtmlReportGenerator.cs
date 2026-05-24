using System;
using System.IO;
using System.Text;

namespace ErenshorCombatParser.IO
{
    public static class HtmlReportGenerator
    {
        /// <summary>
        /// Generate from in-game with live entity/encounter data.
        /// Streams the HTML to disk — never holds the full report in memory.
        /// </summary>
        public static void Generate(string jsonlPath, string outputPath,
            string entityJson, string encounterJson)
        {
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                HtmlTemplate.WriteHeader(writer);

                // Stream events array directly: read JSONL line by line, write to HTML
                writer.Write('[');
                if (File.Exists(jsonlPath))
                {
                    bool first = true;
                    using (var inFs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(inFs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length == 0) continue;
                            if (line.Contains("\"ev\":\"entity\"")) continue;
                            if (!first) writer.Write(',');
                            first = false;
                            writer.Write(line);
                        }
                    }
                }
                writer.Write(']');

                HtmlTemplate.WriteMiddle(writer, entityJson, encounterJson);
                HtmlTemplate.WriteFooter(writer);
            }
        }

        /// <summary>
        /// Generate from a JSONL file alone (standalone / outside of game).
        /// Entity and encounter data are extracted from the JSONL lines themselves.
        /// Streams the HTML to disk — never holds the full report in memory.
        /// </summary>
        public static void GenerateStandalone(string jsonlPath, string outputPath)
        {
            // First pass: collect entity metadata and encounter timestamp ranges.
            // These are small, so keeping them in memory is fine.
            var entitySb = new StringBuilder(512);
            var encounterSb = new StringBuilder(256);
            entitySb.Append('{');

            bool firstEntity = true;
            int maxEncId = 0;
            var encStart = new System.Collections.Generic.Dictionary<int, long>();
            var encEnd = new System.Collections.Generic.Dictionary<int, long>();

            using (var fs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    if (line.Contains("\"ev\":\"entity\""))
                    {
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
                        // Track per-encounter timestamp ranges
                        string ts = ExtractJsonValue(line, "t");
                        long t = 0;
                        if (ts != null) long.TryParse(ts, out t);

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
            }

            entitySb.Append('}');

            // Build encounter array
            encounterSb.Append('[');
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

            // Second pass: stream events into the HTML output
            using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(outFs, Encoding.UTF8))
            {
                HtmlTemplate.WriteHeader(writer);

                writer.Write('[');
                bool firstEvent = true;
                using (var inFs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(inFs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        if (line.Contains("\"ev\":\"entity\"")) continue;
                        if (!firstEvent) writer.Write(',');
                        firstEvent = false;
                        writer.Write(line);
                    }
                }
                writer.Write(']');

                HtmlTemplate.WriteMiddle(writer, entitySb.ToString(), encounterSb.ToString());
                HtmlTemplate.WriteFooter(writer);
            }
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
