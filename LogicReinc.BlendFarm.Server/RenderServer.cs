using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        private const int BROADCAST_INTERVAL = 1500;

        public BlenderManager Blender { get; private set; }

        public int Port { get; private set; } = 15000;
        public int BroadcastPort { get; private set; } = 16342;
        public bool NoBroadcastListen { get; private set; }
        public bool Active { get; set; } = false;
        public TcpListener Listener { get; private set; } = null;
        public UdpClient ListenerUDP { get; private set; } = null;
        public UdpClient BroadcasterUDP { get; private set; } = null;

        private Thread _listenerThread = null;
        private Thread _listenerUDPThread = null;
        private Thread _broadcastThread = null;

        private List<RenderServerClientTcp> Clients { get; } = new List<RenderServerClientTcp>();

        public event Action<RenderServer, Exception> OnServerException;
        public event Action<RenderServer, Exception> OnBroadcastException;

        public event Action<string, string, int> OnServerDiscovered;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="port">communication tcp port</param>
        /// <param name="broadcastPort"><=0 means no broadcasting</param>
        public RenderServer(int port, int broadcastPort, bool noBroadcastListen)
        {
            Port = port;
            BroadcastPort = BroadcastPort;
            NoBroadcastListen = noBroadcastListen;
            Blender = new BlenderManager();

            //AddWebSocket<RenderServerClient>("", "RenderClients");
        }

        public void Start()
        {
            if (_listenerThread != null || Active)
                return;
            Active = true;

            _listenerThread = new Thread(async () =>
            {
                try
                {
                    Listener = new TcpListener(IPAddress.Any, Port);
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
                    OnServerException?.Invoke(this, ex);
                }
                finally
                {
                    Active = false;
                }
            });
            _listenerThread.Start();
            if (BroadcastPort > 0)
            {
                if (!NoBroadcastListen)
                {
                    _listenerUDPThread = new Thread(async () =>
                    {
                        try
                        {
                            ListenerUDP = new UdpClient();
                            ListenerUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            ListenerUDP.ExclusiveAddressUse = false;
                            ListenerUDP.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));
                            IPEndPoint broadcastAddress = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
                            string myIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork).ToString();
                            while (Active)
                            {
                                try
                                {
                                    UdpReceiveResult received = await ListenerUDP.ReceiveAsync();
                                    string ip = received.RemoteEndPoint.ToString();
                                    if (ip.Contains(":"))
                                        ip = ip.Substring(0, ip.IndexOf(':'));

                                    if (ip != myIP)
                                    {
                                        string msg = Encoding.UTF8.GetString(received.Buffer);
                                        if (msg.StartsWith("BLENDFARM||||"))
                                        {
                                            string[] broadcastParts = msg.Split("||||");
                                            string name = broadcastParts[1];
                                            int port = int.Parse(broadcastParts[2]);
                                            OnServerDiscovered?.Invoke(name, ip, port);
                                        }
                                    }
                                    Thread.Sleep(100);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to receive broadcast due to:" + ex.Message);
                                    Thread.Sleep(1000);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OnBroadcastException?.Invoke(this, ex);
                        }
                    });
                    _listenerUDPThread.Start();
                }
                
                _broadcastThread = new Thread(async () =>
                {
                    try
                    {
                        BroadcasterUDP = new UdpClient();
                        BroadcasterUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        BroadcasterUDP.ExclusiveAddressUse = false;
                        BroadcasterUDP.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));
                        IPEndPoint broadcastAddress = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
                        byte[] broadcastMsg = Encoding.UTF8.GetBytes($"BLENDFARM||||{Environment.MachineName}||||{Port}");
                        while (Active)
                        {
                            try
                            {
                                BroadcasterUDP.Send(broadcastMsg, broadcastMsg.Length, broadcastAddress);
                                Thread.Sleep(BROADCAST_INTERVAL);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to send broadcast due to:" + ex.Message);
                                Thread.Sleep(1000);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnBroadcastException?.Invoke(this, ex);
                    }
                });
                _broadcastThread.Start();
            }
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
