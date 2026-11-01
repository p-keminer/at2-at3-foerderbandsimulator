using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using Plant.Core;

namespace Plant.Wpf
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            string profile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "plant.example.json");
            try
            {
                var config = Json.LoadProfile(profile);
                var app = new Application(); var window = new MainWindow(config);
                if (args.Length > 0 && args[0] == "--smoke")
                    window.ContentRendered += (_, _) => SmokeChecks.Start(window, args.Length > 1 ? args[1] : "ui-evidence");
                app.Run(window); return SmokeChecks.Failed ? 1 : 0;
            }
            catch (Exception e)
            {
                string error = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt");
                File.WriteAllText(error, e.ToString()); return 1;
            }
        }
    }
}
