using Avalonia;
using Avalonia.X11;
using Cardmarket_Price_Updater.Core;
using CardPriceUpdaterGui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cardmarket_Price_Updater
{
    internal static class Program
    {
        private static bool quiet = false;
        private static string? logFile = null;
        private static CurrencyMode currencyMode = CurrencyMode.AUTO;
        private static GameType? gameOverride = null;

        static void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            if (!quiet)
                Console.WriteLine(line);
            if (!string.IsNullOrEmpty(logFile))
                File.AppendAllText(logFile, line + Environment.NewLine);
        }

        static void DrawProgress(int current, int total, string label)
        {
            if (quiet) return;
            int width = 30;
            double pct = (double)current / total;
            int filled = (int)(pct * width);
            string bar = "[" + new string('#', filled) + new string('-', width - filled) + "]";
            Console.Write($"\r{bar} {current}/{total} {label}   ");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static string CrashLogPath =>
            Path.Combine(AppContext.BaseDirectory, "crash.log");

        private static void ReportFatal(string context, Exception ex)
        {
            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL during {context}:{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            try { File.AppendAllText(CrashLogPath, text); } catch { }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    MessageBoxW(IntPtr.Zero,
                        $"Cardmarket Price Updater failed to start.\n\n{ex.GetType().Name}: {ex.Message}\n\nFull details written to:\n{CrashLogPath}",
                        "Startup Error", 0x10);
                }
                catch { }
            }
            else
            {
                Console.Error.WriteLine(text);
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ReportFatal("AppDomain.UnhandledException", (Exception)e.ExceptionObject);

            try
            {
                if (args.Length > 0)
                {
                    RunCli(args);
                    return;
                }

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                ReportFatal("Main", ex);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

            // If we fall back to X11/XWayland, prefer GLX then EGL then software rendering
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
            {
                builder.With(new X11PlatformOptions
                {
                    RenderingMode = new[] { X11RenderingMode.Glx, X11RenderingMode.Egl, X11RenderingMode.Software }
                });
            }

            return builder;
        }

        private static void RunCli(string[] args)
        {
            var config = AppConfig.Load();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "/q" || arg == "/quiet" || arg == "/s" || arg == "/silent")
                    quiet = true;
                if (arg == "/log" && i + 1 < args.Length)
                    logFile = args[i + 1];
            }

            string? file = null;
            string? dir = null;
            bool recursive = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "/f" && i + 1 < args.Length) file = args[i + 1];
                if (arg == "/d")
                {
                    dir = (i + 1 < args.Length && !args[i + 1].StartsWith("/"))
                        ? args[i + 1]
                        : AppContext.BaseDirectory;
                }
                if (arg == "/r") recursive = true;
                if (arg == "/c" && i + 1 < args.Length)
                {
                    currencyMode = args[i + 1].ToLowerInvariant() switch
                    {
                        "p" => CurrencyMode.GBP,
                        "e" => CurrencyMode.EUR,
                        "u" => CurrencyMode.USD,
                        _ => CurrencyMode.AUTO
                    };
                }
                if (arg == "/g" && i + 1 < args.Length)
                {
                    gameOverride = GameTypeExtensions.ParseGame(args[i + 1]);
                    if (gameOverride is null)
                        Log($"Unrecognised game \"{args[i + 1]}\" for /g (use m, p or y) - using {config.DefaultGame.DisplayName()}.");
                }
            }

            var updater = new PriceUpdater(Log, config, currencyMode, null, gameOverride);

            List<string> files = new();
            if (!string.IsNullOrWhiteSpace(file)) files.Add(file);
            else if (!string.IsNullOrWhiteSpace(dir))
                files = Directory.EnumerateFiles(dir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var name = Path.GetFileName(f);
                        return !name.StartsWith("~$") // Excel's temporary lock files
                            && (name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                             || name.EndsWith(".ods", StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();
            else { Log("No input provided (/f or /d)"); return; }

            int total = files.Count;
            for (int i = 0; i < total; i++)
            {
                DrawProgress(i + 1, total, Path.GetFileName(files[i]));
                updater.Run(files[i]);
            }

            Log("ALL FILES COMPLETE");

            if (!quiet)
            {
                Console.WriteLine("Press any key to exit...");
                DateTime end = DateTime.Now.AddSeconds(15);
                while (DateTime.Now < end)
                {
                    if (Console.KeyAvailable) { Console.ReadKey(true); break; }
                    Thread.Sleep(100);
                }
            }
        }
    }
}
