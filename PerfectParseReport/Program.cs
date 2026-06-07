using System;
using System.IO;
using System.Linq;
using ErenshorCombatParser.IO;
using Microsoft.Win32;

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
            // Check paths relative to the exe location and discovered Steam library folders
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchDirs = new System.Collections.Generic.List<string>
            {
                // If exe is in the plugins folder alongside the mod
                Path.Combine(exeDir, "logs"),
                Path.Combine(exeDir, "PerfectParse", "logs"),
                // Lunaris plugin paths
                Path.Combine(exeDir, "..", "PerfectParse", "logs"),
            };

            // Discover Steam library folders and add game log paths
            foreach (string lib in FindSteamLibraryFolders())
            {
                searchDirs.Add(Path.Combine(lib, "steamapps", "common",
                    "Erenshor Playtest", "plugins", "PerfectParse", "logs"));
                searchDirs.Add(Path.Combine(lib, "steamapps", "common",
                    "Erenshor", "plugins", "PerfectParse", "logs"));
            }

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
        /// Discovers all Steam library folders by reading the Steam install path from
        /// the Windows registry and parsing libraryfolders.vdf.
        /// </summary>
        private static string[] FindSteamLibraryFolders()
        {
            try
            {
                // Find Steam install directory from registry
                string steamPath = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath", null) as string;
                if (steamPath == null)
                {
                    steamPath = Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                        "InstallPath", null) as string;
                }
                if (steamPath == null) return Array.Empty<string>();

                var folders = new System.Collections.Generic.List<string> { steamPath };

                // Parse libraryfolders.vdf for additional library paths
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    foreach (string line in File.ReadAllLines(vdfPath))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("\"path\""))
                        {
                            // Format: "path"		"C:\SteamLibrary"
                            int first = trimmed.IndexOf('"', 5);
                            if (first >= 0)
                            {
                                int start = trimmed.IndexOf('"', first + 1);
                                if (start >= 0)
                                {
                                    int end = trimmed.IndexOf('"', start + 1);
                                    if (end > start)
                                    {
                                        string path = trimmed.Substring(start + 1, end - start - 1)
                                            .Replace("\\\\", "\\");
                                        if (Directory.Exists(path) && !folders.Contains(path))
                                            folders.Add(path);
                                    }
                                }
                            }
                        }
                    }
                }

                return folders.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
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
