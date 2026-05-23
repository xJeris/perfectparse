using System;
using System.IO;
using System.Linq;
using ErenshorCombatParser.IO;

namespace PerfectParseReport
{
    class Program
    {
        static int Main(string[] args)
        {
            string jsonlPath;
            string outputPath = null;

            if (args.Length >= 1)
            {
                jsonlPath = args[0];
                if (args.Length >= 2)
                    outputPath = args[1];
            }
            else
            {
                // No args — try to find the latest JSONL in the default log directory
                Console.WriteLine("PerfectParse Report Generator");
                Console.WriteLine();

                jsonlPath = FindLatestJsonl();
                if (jsonlPath == null)
                {
                    Console.WriteLine("No JSONL file specified and none found in the default log directory.");
                    Console.WriteLine();
                    Console.WriteLine("Usage: PerfectParseReport <path-to-combat.jsonl> [output.html]");
                    Console.WriteLine("  Or drag and drop a .jsonl file onto this exe.");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey(true);
                    return 1;
                }

                Console.WriteLine("Found latest log: " + jsonlPath);
            }

            if (!File.Exists(jsonlPath))
            {
                Console.Error.WriteLine("Error: File not found: " + jsonlPath);
                WaitIfNoArgs(args);
                return 1;
            }

            if (outputPath == null)
            {
                string dir = Path.GetDirectoryName(jsonlPath);
                string name = Path.GetFileNameWithoutExtension(jsonlPath);
                outputPath = Path.Combine(dir ?? ".", name + "_report.html");
            }

            try
            {
                HtmlReportGenerator.GenerateStandalone(jsonlPath, outputPath);
                Console.WriteLine("Report generated: " + outputPath);
                WaitIfNoArgs(args);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error generating report: " + ex.Message);
                WaitIfNoArgs(args);
                return 1;
            }
        }

        /// <summary>
        /// Searches common locations for the most recent combat_*.jsonl file.
        /// </summary>
        private static string FindLatestJsonl()
        {
            // Check paths relative to the exe location and common Steam paths
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchDirs = new[]
            {
                // If exe is in the plugins folder alongside the mod
                Path.Combine(exeDir, "logs"),
                Path.Combine(exeDir, "PerfectParse", "logs"),
                // Common BepInEx plugin paths
                Path.Combine(exeDir, "..", "PerfectParse", "logs"),
                Path.Combine(exeDir, "..", "..", "BepInEx", "plugins", "PerfectParse", "logs"),
                // Steam default path
                @"C:\Program Files (x86)\Steam\steamapps\common\Erenshor Playtest\BepInEx\plugins\PerfectParse\logs",
            };

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                var latest = Directory.GetFiles(dir, "combat_*.jsonl")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (latest != null)
                    return latest;
            }

            return null;
        }

        /// <summary>
        /// If launched without args (e.g. double-click), pause so the user can read output.
        /// </summary>
        private static void WaitIfNoArgs(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
        }
    }
}
