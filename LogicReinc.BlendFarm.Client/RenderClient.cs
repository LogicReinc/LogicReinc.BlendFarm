using LogicReinc.BlendFarm.Client.Exceptions;
using LogicReinc.BlendFarm.Shared.Communication;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Concurrent;
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
        //Added from sending threads, removed from packet handler threads
        private ConcurrentDictionary<string, Action<BlendFarmMessage>> _respHandlers = new ConcurrentDictionary<string, Action<BlendFarmMessage>>();

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
                //A disconnected packet with a ResponseID is a failure reply to one request, not a disconnect
                if(pack is BlendFarmDisconnected packDis && pack.ResponseID == null)
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
        public Task<T> Send<T>(BlendFarmMessage msg, CancellationToken cancel) where T : BlendFarmMessage
        {
            return Send<T>(msg, cancel, Timeout.InfiniteTimeSpan);
        }
        /// <summary>
        /// Sends a request and waits for its response.
        /// Throws BlendFarmDisconnectedException if the connection is (or gets) lost,
        /// TimeoutException if timeout passes first (no timeout by default, renders may take hours).
        /// </summary>
        public async Task<T> Send<T>(BlendFarmMessage msg, CancellationToken cancel, TimeSpan timeout) where T : BlendFarmMessage
        {
            string reqID = Guid.NewGuid().ToString();

            msg.RequestID = reqID;

            TaskCompletionSource<BlendFarmMessage> completion = new TaskCompletionSource<BlendFarmMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            //Completes on response, or with a BlendFarmDisconnected on disconnect (see HandleDisconnected)
            _respHandlers[reqID] = (resp) => completion.TrySetResult(resp);

            BlendFarmMessage response = null;
            CancellationTokenSource timeoutSource = null;
            try
            {
                //Checked after registering the handler, a disconnect after this point completes it
                if (!Connected || Socket == null)
                    throw new BlendFarmDisconnectedException("Render node not connected")
                    {
                        IsError = true,
                        Reason = "Render node not connected"
                    };

                try
                {
                    Send(msg);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
                {
                    //The connection is broken, close it so the node reports disconnected and can be recovered
                    try
                    {
                        Socket.Disconnect();
                    }
                    catch { }
                    throw new BlendFarmDisconnectedException($"Failed to send to render node ({ex.Message})")
                    {
                        IsError = true,
                        Reason = ex.Message
                    };
                }

                CancellationToken waitToken = cancel;
                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    timeoutSource.CancelAfter(timeout);
                    waitToken = timeoutSource.Token;
                }

                using (waitToken.Register(() => completion.TrySetCanceled(waitToken)))
                {
                    try
                    {
                        response = await completion.Task;
                    }
                    catch (TaskCanceledException) when (!cancel.IsCancellationRequested && timeoutSource != null)
                    {
                        throw new TimeoutException($"Render node did not respond to {msg.GetType().Name} within {timeout.TotalSeconds}s");
                    }
                }
            }
            finally
            {
                //Don't leave a handler behind for a failed, cancelled or timed out request
                _respHandlers.TryRemove(reqID, out _);
                timeoutSource?.Dispose();
            }

            if (response is BlendFarmDisconnected respDisc)
            {
                //Sent by the node in reply to this request when handling it failed, the connection is still fine
                if (respDisc.ResponseID == reqID)
                    throw new InvalidOperationException("Render node failed to handle request: " + (respDisc.Reason ?? "Unknown error"));

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

            foreach (string key in _respHandlers.Keys.ToList())
                if (_respHandlers.TryRemove(key, out var handler))
                try
                {
                    handler(new BlendFarmDisconnected()
                    {
                        IsError = _lastDisconnectIsError,
                        Reason = string.IsNullOrWhiteSpace(_lastDisconnectReason)
                            ? "Render node disconnected"
                            : _lastDisconnectReason
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

                if (packetObj.ResponseID != null && _respHandlers.TryRemove(packetObj.ResponseID, out var respHandler))
                    respHandler.Invoke(packetObj);
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
