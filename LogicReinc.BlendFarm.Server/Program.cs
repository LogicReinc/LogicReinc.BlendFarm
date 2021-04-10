using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace LogicReinc.BlendFarm.Server
{
    //Should clean this up.
    //Contains lots of test code
    public class Program
    {
        public static RenderServer Server { get; private set; }

        static void Main(string[] args)
        {
            try
            {
                //List IP addreses of this machine
                string[] addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(x => x?.ToString())
                    .ToArray();
                Console.WriteLine("IP Addresses of this Server:");
                for (int i = 0; i < addresses.Length; i++)
                {
                    string address = addresses[i];
                    Console.WriteLine($"Host Address #{(i + 1)}: {address}");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Failed to obtain host address due to [{ex.GetType().Name}]: {ex.Message}");
            }
            //List host port
            Console.WriteLine($"Port: {ServerSettings.Instance.Port}");

            //Clears any sessions left after previous close
            Console.WriteLine("Cleaning up old sessions..");
            CleanupOldSessions();

            //RenderNode Server
            Server = new RenderServer(ServerSettings.Instance.Port, ServerSettings.Instance.BroadcastPort, true);

            Server.Start();
            Console.WriteLine("Server Started");


            //ReadLines in main loop removed for deployment as this can freeze background threads under specific conditions
            while (true)
                Thread.Sleep(500);

            Server.Stop();
        }

        /// <summary>
        /// Deletes the BlenderFiles directory, which is used for temporary file storage
        /// </summary>
        public static void CleanupOldSessions()
        {
            try
            {
                string path = SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderFiles);
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }


    }
}
