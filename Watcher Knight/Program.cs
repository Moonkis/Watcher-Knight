using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Configuration;
using System.Linq;
using static System.Environment;

namespace Watcher_Knight
{
    class Program
    {
        private static readonly uint WM_KEYDOWN = 0x0100;
        private static readonly uint WM_KEYUP = 0x0101;
        private static HashSet<string> CinematicItems = new HashSet<string> 
        {
            "Mothwing_Cloak",
            "Mantis_Claw",
            "Crystal_Heart",
            "Monarch_Wings",
            "Shade_Cloak",
            "Isma's_Tear",
            "Dream_Nail",
            "Dream_Gate",
            "Awoken_Dream_Nail",
            "Vengeful_Spirit",
            "Shade_Soul",
            "Desolate_Dive",
            "Descending_Dark",
            "Howling_Wraiths",
            "Abyss_Shriek",

            "Cyclone_Slash",
            "Dash_Slash",
            "Great_Slash",

            "King_Fragment",
            "Queen_Fragment",
            "Void_Heart",
            "King's_Brand",


            "World_Sense",
            "Godtuner",
            "Dreamer",
            "Collector's_Map",

            "Lurien",
            "Monomon",
            "Herrah",
        };

        private static string[] IgnoredPickups = new[]
        {
            "_Geo-", // Ignore chests containing geo
            "Geo_Rock", // Ignore any randomized geo rocks 
            "Lifeblood_Cocoon", // Ignore any lifeblood cocoon
            "Soul_Totem", // Ignore soul refills
        };

        private static string[] Shopkeepers = new[] { "Iselda", "Sly", "Sly_(Key)","Salubra", "Leg_Eater" };

        private static string TrackerFileName = "TrackerLog.txt";
        private static string HollowKnightDirectory = @"\AppData\LocalLow\Team Cherry\Hollow Knight\Randomizer 4\Recent\";
        private static string FullPath;

        private static string pattern = @"{(.*?)}";

        private static long lastOffset = 0;
        private static Int32 screenshotKeyCode = 0x7B;
        private static int normalScreenshotDelay = 800;
        private static int cinematicScreenshotDelay = 1500;

        private static bool showObtainedItems = false;
        private static bool captureExtras = false;
        private static bool captureShopkeepers = false;

        private static HashSet<string> ObtainedItems = new HashSet<string>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        static void Main(string[] args)
        {
            Console.Title = "Watcher Knight";
            Print(@"__        ___  _____ ____ _   _ _____ ____    _  ___   _ ___ ____ _   _ _____ ");
            Print(@"\ \      / / \|_   _/ ___| | | | ____|  _ \  | |/ | \ | |_ _/ ___| | | |_   _|");
            Print(@" \ \ /\ / / _ \ | || |   | |_| |  _| | |_) | | ' /|  \| || | |  _| |_| | | |  ");
            Print(@"  \ V  V / ___ \| || |___|  _  | |___|  _ <  | . \| |\  || | |_| |  _  | | |  ");
            Print(@"   \_/\_/_/   \_|_| \____|_| |_|_____|_| \_\ |_|\_|_| \_|___\____|_| |_| |_|  ");
            Print(string.Empty);

            // Read settings
            try
            {
                showObtainedItems = bool.Parse(ConfigurationManager.AppSettings["showObtainedItems"]);
                captureExtras = bool.Parse(ConfigurationManager.AppSettings["captureExtras"]);
                captureShopkeepers = bool.Parse(ConfigurationManager.AppSettings["captureShopkeepers"]);
                screenshotKeyCode = (Int32)new System.ComponentModel.Int32Converter().ConvertFromString(ConfigurationManager.AppSettings["screenshotKeyCode"]);
                cinematicScreenshotDelay = int.Parse(ConfigurationManager.AppSettings["cinematicScreenshotDelay"]);
                normalScreenshotDelay = int.Parse(ConfigurationManager.AppSettings["normalScreenshotDelay"]);
            }
            catch(Exception e)
            {
                Print($"Failed to parse settings. {e}", ConsoleColor.Red);
                Console.ReadLine();
                return;
            }


            // Path to tracker log.
            var TrackerBaseDirectory = $@"{GetFolderPath(SpecialFolder.UserProfile)}{HollowKnightDirectory}";
            FullPath = $@"{TrackerBaseDirectory}{TrackerFileName}";

            if (File.Exists(FullPath))
            {
                Print($"{TrackerFileName} already exists. Recording already obtained items.", ConsoleColor.Green);
                UpdateTracker();
                Print("Done.", ConsoleColor.Green);
            }

            using(FileSystemWatcher watcher = new FileSystemWatcher())
            {
                watcher.Path = TrackerBaseDirectory;
                watcher.Filter = $"{TrackerFileName}";

                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;

                watcher.Changed += OnChanged;
                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;


                watcher.EnableRaisingEvents = true;

                Print("Press q to quit the Watcher Knight.", ConsoleColor.Yellow);
                while (Console.Read() != 'q') 
                { 
                }
            }
        }

        private static void OnChanged(object source, FileSystemEventArgs e)
        {
            Print($"{e.FullPath} changed.");
            UpdateTracker(true);
        }

        private static void OnCreated(object source, FileSystemEventArgs e)
        {
            ObtainedItems.Clear();
            Print($"{e.FullPath} created. Resetting the list of picked up items.");
        }

        private static void OnDeleted(object source, FileSystemEventArgs e)
        {
            ObtainedItems.Clear();
            Print($"{e.FullPath} deleted. Resetting the list of picked up items.");
        }

        private static void UpdateTracker(bool takeScreenshot = false)
        {
            // TODO: Don't read the entire file. Seek using the lastOffset.
            string content = string.Empty;

            using (var fs = new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // If the tracker file is suddely smaller we can *almost* safetly assume it's a new file.
                // TODO: As a precaution check the seed as well, should cover 99% of the cases.
                if (lastOffset > fs.Length)
                {
                    Print("Tracker size is smaller than last recorded size, clearing obtained items.", ConsoleColor.Yellow);
                    ObtainedItems.Clear();
                    lastOffset = fs.Length;
                    return;
                }

                if (lastOffset < fs.Length)
                {
                    byte[] buffer = new byte[fs.Length];
                    fs.Read(buffer, 0, (int)fs.Length);
                    content = Encoding.UTF8.GetString(buffer);
                    lastOffset = fs.Length;
                }
            }

            if (string.IsNullOrEmpty(content))
            {
                Print($"Failed to read any bytes from {TrackerFileName}");
                return;
            }

            var lines = content.Split('\n'); 

            foreach (var line in lines)
            {
                if (!line.StartsWith("ITEM")) continue;

                MatchCollection matchCollection = Regex.Matches(line, pattern);
                Match[] matches = new Match[matchCollection.Count];
                matchCollection.CopyTo(matches, 0);

                
                if (!matches.All(x => x?.Success ?? false)) continue;

                string item = matches[0].Groups[1].Value;
                string location = matches[1].Groups[1].Value;

                if (!ObtainedItems.Contains(item))
                {
                    if (showObtainedItems)
                        Print($"Obtained: {item} at {location}", ConsoleColor.Yellow);
                    ObtainedItems.Add(item);

                    if (takeScreenshot && !IsIgnoredItem(item) && !IsIgnoredLocation(location))
                    {
                        int time = CinematicItems.Contains(item) ? cinematicScreenshotDelay : normalScreenshotDelay;
                        Task.Factory.StartNew(async () =>
                        {
                            await Task.Delay(time);
                            Screenshot(null, null);
                        });
                    }
                }
            }
        }

        private static bool IsIgnoredItem(string item)
        {
            if (captureExtras) return false;

            foreach(var extra in IgnoredPickups)
            {
                if (item.Contains(extra)) return true;
            }

            return false;
        }

        private static bool IsIgnoredLocation(string location)
        {
            if (captureShopkeepers) return false;

            foreach(var vendor in Shopkeepers)
            {
                if (vendor == location) return true;
            }

            return false;
        }

        private static void Screenshot(object source, ElapsedEventArgs e)
        {
            Process[] processes = Process.GetProcessesByName("hollow_knight");
            foreach (var process in processes)
                PostMessage(process.MainWindowHandle, WM_KEYDOWN, screenshotKeyCode, 0);

            foreach (var process in processes)
                PostMessage(process.MainWindowHandle, WM_KEYUP, screenshotKeyCode, 0);
        }

        private static void Print(string message, ConsoleColor color)
        {
            var c = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = c;
        }

        private static void Print(string message)
        {
            Console.WriteLine(message);
        }
    }
}