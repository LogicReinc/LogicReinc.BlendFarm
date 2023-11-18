using LogicReinc.BlendFarm.Client.Exceptions;
using LogicReinc.BlendFarm.Shared.Communication;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Underlying client that handles communication
    /// </summary>
    public class RenderClient
    {
        private int bufferSize = 1024;

        private CancellationToken _receivingToken = new CancellationToken();

        private TcpRenderClient Socket = null;
        public string Address { get; set; }
        public int Port { get; set; }
        private Dictionary<string, Action<BlendFarmMessage>> _respHandlers = new Dictionary<string, Action<BlendFarmMessage>>();

        public bool Connected { get; set; }

        public event Action<RenderClient, BlendFarmMessage> OnPacket;

        public event Action<RenderClient> OnConnected;
        public event Action<RenderClient> OnDisconnected;
        public event Action<RenderClient, string> OnMessage;

        protected bool _lastDisconnectIsError = false;
        protected string _lastDisconnectReason = null;


        public RenderClient(string address)
        {
            string[] parts = address.Split(':');
            if (parts.Length != 2)
                throw new ArgumentException("Address does not contain port..");
            Address = parts[0];
            Port = int.Parse(parts[1]);
        }
        public RenderClient(string ip, int port)
        {
            Address = ip;
            Port = port;
        }

        public static async Task<RenderClient> Connect(string address)
        {
            RenderClient client = new RenderClient(address);
            client.OnPacket += (packClient, pack) =>
            {
                if(pack is BlendFarmDisconnected packDis)
                {
                    client._lastDisconnectIsError = packDis.IsError;
                    client._lastDisconnectReason = packDis.Reason;
                }
            };

            if (!await client.Connect())
                return null;
            if (!client.Connected)
                return null;

            //ComputerInfoResponse info = await client.GetComputerInfoAsync();

            if (!client.Connected)
                return null;

            return client;
        }

        #region Protocol

        public void Send(BlendFarmMessage msg)
        {
            Socket.SendPacket(msg);
        }
        public async Task<T> Send<T>(BlendFarmMessage msg, CancellationToken cancel) where T : BlendFarmMessage
        {
            string reqID = Guid.NewGuid().ToString();

            msg.RequestID = reqID;

            BlendFarmMessage response = null;

            SemaphoreSlim sema = new SemaphoreSlim(1);

            //Releases on callback
            _respHandlers.Add(reqID, (resp) =>
            {
                response = resp;
                sema.Release();
            });

            //Consume Initial
            sema.Wait();

            Send(msg);


            TaskCompletionSource<BlendFarmMessage> completionSource = new TaskCompletionSource<BlendFarmMessage>();

            await sema.WaitAsync(cancel);
            sema.Dispose();

            if (response is BlendFarmDisconnected respDisc)
            {
                if(string.IsNullOrEmpty(respDisc.Reason))
                    throw new BlendFarmDisconnectedException()
                    {
                        IsError = respDisc.IsError,
                        Reason = respDisc.Reason
                    };
                else
                    throw new BlendFarmDisconnectedException(respDisc.Reason)
                    {
                        IsError = respDisc.IsError,
                        Reason = respDisc.Reason
                    };
            }

            return (T)response;
        }

        private void HandleConnected()
        {
            Connected = true;
            if (OnConnected != null)
                OnConnected(this);
        }
        private void HandleDisconnected()
        {
            Connected = false;

            OnDisconnected?.Invoke(this);

            foreach (var handler in _respHandlers.Values.ToList())
                try
                {
                    handler(new BlendFarmDisconnected()
                    {
                        IsError = _lastDisconnectIsError,
                        Reason = _lastDisconnectReason
                    });
                }
                catch { }

        }
        #endregion


        public async Task<bool> Connect()
        {
            //await Socket.ConnectAsync(new Uri(Address), CancellationToken.None);
            Socket = await TcpRenderClient.Connect(Address, Port);
            Socket.OnMessage += (c, packetObj) =>
            {
                if (OnPacket != null)
                    OnPacket(this, packetObj);

                if (packetObj.ResponseID != null && _respHandlers.ContainsKey(packetObj.ResponseID))
                {
                    Action<BlendFarmMessage> respHandler = _respHandlers[packetObj.ResponseID];
                    _respHandlers.Remove(packetObj.ResponseID);

                    respHandler.Invoke(packetObj);
                }
            };
            Socket.OnDisconnected += (c) => HandleDisconnected();

            HandleConnected();
            
            return true;
        }
        public void Disconnect()
        {
            Connected = false;
            _receivingToken.ThrowIfCancellationRequested();
            Socket.Disconnect();
        }
    }
}
