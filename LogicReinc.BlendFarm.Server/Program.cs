using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace LogicReinc.BlendFarm.Server
{
    //Should clean this up.
    //Contains lots of test code
    public class Program
    {
        private static string _override_os = null;

        private static BlenderManager _blender = new BlenderManager();

        public static RenderServer Server { get; private set; }

        static void Main(string[] args)
        {
            string localIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
            Console.WriteLine($"Host: {localIP}");
            Console.WriteLine($"Port: {ServerSettings.Instance.Port}");

            Console.WriteLine("Cleaning up old sessions..");
            CleanupOldSessions();

            Server = new RenderServer(ServerSettings.Instance.Port, ServerSettings.Instance.BroadcastPort, true);

            Server.Start();
            Console.WriteLine("Server Started");

            HandleConsole();

            Server.Stop();
        }

        public static void HandleConsole()
        {
            string line = null;
            while((line = Console.ReadLine()) != "exit")
            {
                try
                {
                    switch (line)
                    {
                        case "download":
                            DownloadVersion();
                            break;
                        case "render":
                            RenderVersion();
                            break;
                        case "setos":
                            SetOS();
                            break;
                    }
                }
                catch(Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.Message);
                    Console.ResetColor();
                }
            }

        }

        public static void CleanupOldSessions()
        {
            try
            {
                if (Directory.Exists(ServerSettings.Instance.BlenderFiles))
                    Directory.Delete(ServerSettings.Instance.BlenderFiles, true);
            }
            catch { }
        }

        public static void SetOS()
        {
            Console.WriteLine("Changing OS will cause things to malfunction... restart if not indented");
            Console.WriteLine("OS:");
            string os = Console.ReadLine();

            _override_os = os;
            Console.WriteLine("OS set to " + os);
        }
        public static void RenderVersion()
        {
            Console.WriteLine("Version:");
            string version = Console.ReadLine();
            Console.WriteLine("BlenderFile:");
            string blend = Console.ReadLine();

            Console.WriteLine("Cut? (Y/n)");

            decimal x = 0;
            decimal x2 = 1;
            decimal y = 0;
            decimal y2 = 1;
            if(Console.ReadLine() == "Y")
            {
                Console.WriteLine("Rectangle: (x x2 y y2)");
                string[] parts = Console.ReadLine().Split(' ');
                if (parts.Length != 4)
                    throw new ArgumentException("Invalid format");
                x = decimal.Parse(parts[0]);
                x2 = decimal.Parse(parts[1]);
                y = decimal.Parse(parts[2]);
                y2 = decimal.Parse(parts[3]);
            }
            
            if(!_blender.TryPrepare(version))
            {
                Console.WriteLine($"Failed to prepare Blender version {version}");
                return;
            }
            _blender.Render(version, blend, new BlenderRenderSettings()
            {
                Frame = 1,
                X = x,
                X2 = x2,
                Y = y,
                Y2 = y2
            });
        }

        public static void DownloadVersion()
        {
            Console.WriteLine("Version:");
            string version = Console.ReadLine();
            _blender.Prepare(version);
        }

    }
}
