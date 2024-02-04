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

        /// <summary>
        /// Blender Manager
        /// </summary>
        public BlenderManager Blender { get; private set; }

        /// <summary>
        /// TCP Port for RenderNode
        /// </summary>
        public int Port { get; private set; } = 15000;
        /// <summary>
        /// UDP Port for RenderNode broadcasting
        /// </summary>
        public int BroadcastPort { get; private set; } = 16342;
        /// <summary>
        /// If the server should also listen for other render node broadcasts
        /// </summary>
        public bool NoBroadcastListen { get; private set; }
        /// <summary>
        /// If the server is active
        /// </summary>
        public bool Active { get; set; } = false;
        /// <summary>
        /// TCP Listener for RenderNode
        /// </summary>
        public TcpListener Listener { get; private set; } = null;
        /// <summary>
        /// UDP Listener for RenderNodes
        /// </summary>
        public UdpClient ListenerUDP { get; private set; } = null;
        /// <summary>
        /// UDP Client for broadcasting
        /// </summary>
        public UdpClient BroadcasterUDP { get; private set; } = null;

        //Background threads
        private Thread _listenerThread = null;
        private Thread _listenerUDPThread = null;
        private Thread _broadcastThread = null;

        private List<RenderServerClientTcp> Clients { get; } = new List<RenderServerClientTcp>();

        /// <summary>
        /// Event whenever an exception occurs in the server
        /// </summary>
        public event Action<RenderServer, Exception> OnServerException;
        /// <summary>
        /// Event whenever an exception occurs while broadcasting
        /// </summary>
        public event Action<RenderServer, Exception> OnBroadcastException;
        /// <summary>
        /// Event whenever a server is discovered from a broadcast
        /// </summary>
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

            Console.WriteLine("Server OS: " + SystemInfo.GetOSName());

            //AddWebSocket<RenderServerClient>("", "RenderClients");
        }

        /// <summary>
        /// Start all relevant listeners and clients
        /// </summary>
        public void Start()
        {
            if (_listenerThread != null || Active)
                return;
            Active = true;

            StartRenderListener();

            if (BroadcastPort > 0)
            {
                if (!NoBroadcastListen)
                    StartBroadcastListener();
                StartBroadcaster();
            }
        }

        /// <summary>
        /// Starts the Listener background thread for rendering
        /// </summary>
        private void StartRenderListener()
        {
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
        }
        /// <summary>
        /// Starts the broadcast listener for discovery
        /// </summary>
        private void StartBroadcastListener()
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
        /// <summary>
        /// Starts the broadcaster background thread for discovery
        /// </summary>
        private void StartBroadcaster()
        {
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


        /// <summary>
        /// Stops all listeners and threads
        /// </summary>
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
