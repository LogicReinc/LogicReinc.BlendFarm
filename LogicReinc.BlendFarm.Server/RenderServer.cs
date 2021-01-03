using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Top-level server for receiving clients
    /// </summary>
    public class RenderServer
    {

        public BlenderManager Blender { get; private set; }

        public bool UseTCP { get; set; } = true;
        public int Port { get; set; } = 15000;
        public bool Active { get; set; } = false;
        public TcpListener Listener { get; private set; } = null;

        private Thread _listenerThread = null;

        private List<RenderServerClientTcp> Clients { get; } = new List<RenderServerClientTcp>();

        public RenderServer(int port)
        {
            Port = port;
            Blender = new BlenderManager();

            //AddWebSocket<RenderServerClient>("", "RenderClients");
        }

        public void Start()
        {
            if (_listenerThread != null || Active)
                return;
            Active = true;

            Listener = new TcpListener(Port);

            _listenerThread = new Thread(async () =>
            {
                try
                {
                    Listener.Start();
                    while (Active)
                    {
                        TcpClient client = await Listener.AcceptTcpClientAsync();

                        RenderServerClientTcp rClient = new RenderServerClientTcp(Blender, client);
                        lock (Clients)
                            Clients.Add(rClient);
                        rClient.OnDisconnect += (client) =>
                        {
                            lock (Clients)
                                Clients.Remove(client);
                        };

                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("TCP Exception:" + ex.Message);
                }
                finally
                {
                    Active = false;
                }
            });
            _listenerThread.Start();
        }
        public void Stop()
        {
            Active = false;
            Listener?.Stop();
            _listenerThread.Join();
            Listener = null;

            List<RenderServerClientTcp> clients = null;
            lock (Clients)
                clients = Clients.ToList();
            foreach (RenderServerClientTcp client in clients)
            {
                try
                {
                    client.Disconnect();
                }
                catch { }
            }
        }
    }
}
