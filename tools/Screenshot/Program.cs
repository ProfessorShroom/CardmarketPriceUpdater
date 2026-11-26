using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Tools.Screenshot
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string pricesOutput = Path.GetFullPath(args.Length > 0 ? args[0] : "images/cardmarketPriceUpdater.png");
            string collectionOutput = Path.GetFullPath(args.Length > 1 ? args[1] : "images/cardmarketPriceUpdaterCollection.png");
            string template = Path.GetFullPath(args.Length > 2 ? args[2] : "Template.ods");

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont()
                .SetupWithoutStarting();

            var window = new MainWindow();
            window.Show();

            Pump();
            Capture(window, pricesOutput);

            var tabs = window.FindControl<TabControl>("Tabs")
                ?? throw new InvalidOperationException("Tabs control not found.");
            var collection = window.FindControl<CollectionView>("Collection")
                ?? throw new InvalidOperationException("Collection view not found.");

            tabs.SelectedIndex = 1;
            Task load = collection.LoadAsync(template);
            while (!load.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(50);
            }
            load.GetAwaiter().GetResult();

            Pump();
            Capture(window, collectionOutput);

            Environment.Exit(0);
            return 0;
        }

        private static void Pump()
        {
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(100);
            }
        }

        private static void Capture(Window window, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("No frame was rendered.");
            frame.Save(output, PngBitmapEncoderOptions.Default);
            Console.WriteLine($"Saved {output}");
        }
    }
}
