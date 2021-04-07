using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LogicReinc.BlendFarm.Server;

namespace LogicReinc.BlendFarm
{
    class Program
    {
        public static Stream GetIconStream()
        {
            MemoryStream str = new MemoryStream();

            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("LogicReinc.BlendFarm.Images.render.ico"))
            {
                res.CopyTo(str);
            }
            str.Seek(0, SeekOrigin.Begin);
            return str;
        }


        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            Server.Program.CleanupOldSessions();

            Console.WriteLine("Saving current setting states");
            BlendFarmSettings.Instance.Save();
            ServerSettings.Instance.Save();

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
        {
            AppBuilder builder = AppBuilder.Configure<App>()
                .UsePlatformDetect();
            if (SystemInfo.IsOS(SystemInfo.OS_LINUX64))
                builder = Avalonia.Dialogs.ManagedFileDialogExtensions.UseManagedSystemDialogs(builder);
            return builder.LogToDebug();
        }
    }
}
