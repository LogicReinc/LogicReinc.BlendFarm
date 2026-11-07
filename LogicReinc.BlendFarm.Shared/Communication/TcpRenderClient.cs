using LogicReinc.BlendFarm.Shared.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    public class TcpRenderClient
    {
        private const int MAX_HEADER_SIZE = 24;

        private static ConcurrentDictionary<Type, Dictionary<string, MethodInfo>> _typeHandlers = new ConcurrentDictionary<Type, Dictionary<string, MethodInfo>>();
        private Dictionary<string, MethodInfo> _handlers = null;

        public TcpClient Client { get; private set; }
        public bool Listening { get; private set; }

        private CancellationTokenSource _cancel = new CancellationTokenSource();
        private Thread _listenThread = null;

        public event Action<TcpRenderClient> OnDisconnected;
        public event Action<TcpRenderClient, BlendFarmMessage> OnMessage;


        protected bool _disconnectIsError = false;
        protected string _disconnectReason = null;


        public TcpRenderClient(TcpClient client)
        {
            //Cache?
            _handlers = GetTypeHandlers(GetType());
            Client = client;
            EnableKeepAlive(client);

            Listening = true;
            _listenThread = new Thread(async () =>
            {
                Thread.Sleep(100);
                try
                {
                    await Listen();
                }
                catch(Exception ex)
                {
                    if (string.IsNullOrWhiteSpace(_disconnectReason))
                    {
                        _disconnectIsError = true;
                        _disconnectReason = ex.Message;
                    }
                    Console.WriteLine("TCP listening exception: " + ex.Message);
                }
                finally
                {
                    Listening = false;
                    Disconnect();
                }
            });
            _listenThread.Start();
        }


        protected virtual void HandleDisconnected()
        {
        }

        //Values of SocketOptionName.TcpKeepAliveTime/Interval/RetryCount, not exposed in netstandard2.0
        private const SocketOptionName TCP_KEEPALIVE_TIME = (SocketOptionName)3;
        private const SocketOptionName TCP_KEEPALIVE_INTERVAL = (SocketOptionName)17;
        private const SocketOptionName TCP_KEEPALIVE_RETRYCOUNT = (SocketOptionName)16;

        /// <summary>
        /// A peer that loses power or network never closes the connection, so without keepalive a
        /// pending render waits forever. Probe after 30s idle, every 5s, give up after 4 missed probes (~50s).
        /// </summary>
        private static void EnableKeepAlive(TcpClient client)
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, TCP_KEEPALIVE_TIME, 30);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, TCP_KEEPALIVE_INTERVAL, 5);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, TCP_KEEPALIVE_RETRYCOUNT, 4);
            }
            catch (Exception ex)
            {
                //Older runtimes/platforms may not support the tuning options, plain keepalive still applies
                Console.WriteLine("Could not configure TCP keepalive: " + ex.Message);
            }
        }

        public static async Task<TcpRenderClient> Connect(string address, int port)
        {
            TcpClient client = new TcpClient();
            await client.ConnectAsync(address, port);
            return new TcpRenderClient(client);
        }
        public void Disconnect()
        {
            Listening = false;
            Client.Close();
            _cancel.Cancel();
            HandleDisconnected();
            OnDisconnected?.Invoke(this);
        }


        public void HandlePacket(string header, BinaryReader reader)
        {
            if (!BlendFarmMessage.HasPackageType(header))
                return;

            Type packetType = BlendFarmMessage.GetPackageType(header);
            BlendFarmMessage req = null;
            try
            {
                req = (BlendFarmMessage)BinaryParser.Deserialize(reader, packetType);
            }
            catch(Exception ex)
            {
                throw new InvalidDataException($"Failed to parse {packetType.Name} due to:" + ex.Message);
            }

            Task.Run(() =>
            {
                try
                {

                    if (_handlers.ContainsKey(header))
                    {
                        MethodInfo method = _handlers[header];
                        object resp = method.Invoke(this, new object[] { req });

                        if (resp != null && resp is BlendFarmMessage)
                        {
                            BlendFarmMessage bfresp = ((BlendFarmMessage)resp);
                            if (req.RequestID != null)
                                bfresp.ResponseID = req.RequestID;

                            SendPacket((BlendFarmMessage)resp);
                        }
                    }
                    else
                        OnMessage?.Invoke(this, req);
                }
                catch(TargetInvocationException ex)
                {
                    if(ex.InnerException is ClientStateException)
                    {
                        _disconnectIsError = true;
                        _disconnectReason = ex.InnerException.Message;
                        SendPacket(new BlendFarmDisconnected()
                        {
                            IsError = true,
                            Reason = ex.InnerException.Message
                        });
                        Disconnect();
                    }
                    else
                    {
                        Console.WriteLine($"Exception in handling [{header}] due to {ex.InnerException?.Message ?? ex.Message}");
                        SendFailureReply(req, ex.InnerException ?? ex);
                    }
                }
                catch (Exception ex)
                {
                    //Includes failing to serialize or send the response
                    Console.WriteLine($"Exception in handling [{header}] due to {ex.Message}");
                    SendFailureReply(req, ex);
                }
            });
        }


        /// <summary>
        /// Replies to a request whose handler (or response) failed, so the requesting side doesn't wait forever.
        /// A disconnected packet carrying the ResponseID completes the request on any client version,
        /// newer clients recognise the ResponseID and report it as a failed request rather than a disconnect.
        /// </summary>
        private void SendFailureReply(BlendFarmMessage req, Exception ex)
        {
            if (req?.RequestID == null || !Listening)
                return;
            try
            {
                SendPacket(new BlendFarmDisconnected()
                {
                    ResponseID = req.RequestID,
                    IsError = true,
                    Reason = $"Failed to handle {req.GetType().Name}: {ex.Message}"
                });
            }
            catch (Exception sendEx)
            {
                Console.WriteLine($"Failed to send failure reply due to {sendEx.Message}");
            }
        }

        private async Task Listen()
        {
            byte[] headerBytes = new byte[MAX_HEADER_SIZE];
            byte[] sizeBytes = new byte[4];
            Stream str = Client.GetStream();
            BinaryReader reader = new BinaryReader(str);
            while (Listening)
            {

                await ReadExactlyAsync(str, headerBytes, MAX_HEADER_SIZE);
                string header = Encoding.UTF8.GetString(headerBytes).Trim('_');

                await ReadExactlyAsync(str, sizeBytes, sizeBytes.Length);
                int size = (int)BinaryParser.Deserialize(sizeBytes, typeof(int));
                if (size < 0)
                    throw new InvalidDataException($"Invalid packet size {size}");

                if(header != "consoleActivityResponse")
                    Console.WriteLine($"Received {header} [{size}] from {Client.Client.RemoteEndPoint}");

                //Unknown packet, skip its body to stay in sync with the stream
                if (!BlendFarmMessage.HasPackageType(header))
                {
                    await SkipBytesAsync(str, size);
                    continue;
                }

                HandlePacket(header, reader);
            }
        }

        /// <summary>
        /// Reads exactly count bytes, TCP may return a partial read
        /// </summary>
        private async Task ReadExactlyAsync(Stream str, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count)
            {
                int read = await str.ReadAsync(buffer, done, count - done, _cancel.Token);
                if (read == 0)
                    throw new IOException("Connection closed by remote host");
                done += read;
            }
        }
        private async Task SkipBytesAsync(Stream str, int count)
        {
            byte[] buffer = new byte[Math.Min(count, 8192)];
            while (count > 0)
            {
                int read = await str.ReadAsync(buffer, 0, Math.Min(count, buffer.Length), _cancel.Token);
                if (read == 0)
                    throw new IOException("Connection closed by remote host");
                count -= read;
            }
        }

        public void SendPacket(BlendFarmMessage message)
        {
            Type t = message.GetType();
            BlendFarmHeaderAttribute attr = t.GetCustomAttribute<BlendFarmHeaderAttribute>();

            if (attr != null && BlendFarmMessage.HasPackageType(attr.Header))
            {
                string paddedHeader = attr.Header.PadRight(MAX_HEADER_SIZE, '_');
                byte[] header = Encoding.UTF8.GetBytes(paddedHeader);

                if (header.Length != MAX_HEADER_SIZE)
                    throw new ArgumentException($"Header cannot be more than {MAX_HEADER_SIZE} characters long, Was {header.Length}");

                byte[] body = null;
                try
                {
                    body = BinaryParser.Serialize(message);
                }
                catch(Exception ex)
                {
                    throw new InvalidDataException($"Failed to serialize {t.Name}");
                }

                byte[] size = BinaryParser.Serialize(body.Length);

                Send((str) =>
                {
                    str.Write(header, 0, MAX_HEADER_SIZE);
                    str.Write(size, 0, 4);
                    str.Write(body, 0, body.Length);
                });
            }
        }

        public void Send(byte[] data)
        {
            Send((str) =>
            {
                str.Write(data, 0, data.Length);
            });
        }
        public void Send(Action<NetworkStream> stream)
        {
            lock (Client)
                stream(Client.GetStream());
        }


        private static Dictionary<string, MethodInfo> GetTypeHandlers(Type type)
        {
            //Clients are constructed from several threads, the dictionary is never mutated after creation
            return _typeHandlers.GetOrAdd(type, t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<BlendFarmHeaderAttribute>() != null)
                .ToDictionary(x => x.GetCustomAttribute<BlendFarmHeaderAttribute>().Header, y => y));
        }
    }
}
