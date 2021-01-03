using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging.Serilog;

namespace LogicReinc.BlendFarm
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            Server.Program.CleanupOldSessions();

            try
            {
                if (Directory.Exists(BlendFarmSettings.Instance.LocalBlendFiles))
                    Directory.Delete(BlendFarmSettings.Instance.LocalBlendFiles, true);
            }
            catch { }
            Directory.CreateDirectory(BlendFarmSettings.Instance.LocalBlendFiles);

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToDebug();
    }
}
