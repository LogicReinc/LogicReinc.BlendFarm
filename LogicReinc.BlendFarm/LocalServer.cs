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

        public static int ServerPort { get; } = new Random().Next(14000, 14500);


        public static void Start()
        {
            if (Available)
                return;

            if (_server == null)
                _server = new RenderServer(ServerPort);

            _server.Start();

        }

        public static void Stop()
        {
            if(_server != null)
                _server.Stop();
        }
    }
}
