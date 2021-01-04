using Avalonia;
using LogicReinc.BlendFarm.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm
{
    public static class LocalServer
    {
        private static RenderServer _server = null;

        public static bool Available => _server != null;

        public static int ServerPort { get; } = ServerSettings.Instance.Port;
        public static int BroadcastPort { get; } = ServerSettings.Instance.BroadcastPort;

        public static event Action<RenderServer, Exception> OnServerException;
        public static event Action<RenderServer, Exception> OnBroadcastException;
        public static event Action<string, string, int> OnDiscoveredServer;

        public static void Start()
        {
            if (Available)
                return;

            if (_server == null)
            {
                _server = new RenderServer(ServerPort, BroadcastPort, false);
                _server.OnServerException += (a, b) => OnServerException?.Invoke(a, b);
                _server.OnBroadcastException += (a, b) => OnBroadcastException?.Invoke(a, b);
                _server.OnServerDiscovered += (a, b, c) => OnDiscoveredServer?.Invoke(a, b, c);
            }
            _server.Start();

        }

        public static void Stop()
        {
            if(_server != null)
                _server.Stop();
        }
    }
}
