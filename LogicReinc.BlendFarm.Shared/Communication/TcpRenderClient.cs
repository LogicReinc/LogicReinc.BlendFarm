using LogicReinc.BlendFarm.Shared.Exceptions;
using System;
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

        private static Dictionary<Type, Dictionary<string, MethodInfo>> _typeHandlers = new Dictionary<Type, Dictionary<string, MethodInfo>>();
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
                        Console.WriteLine($"Exception in handling [{header}] due to {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception in handling [{header}] due to {ex.Message}");
                    throw;
                }
            });
        }


        private async Task Listen()
        {
            byte[] headerBytes = new byte[MAX_HEADER_SIZE];
            byte[] sizeBytes = new byte[4];
            Stream str = Client.GetStream();
            BinaryReader reader = new BinaryReader(str);
            while (Listening)
            {

                int read = 0;
                if ((read = await str.ReadAsync(headerBytes, 0, MAX_HEADER_SIZE, _cancel.Token)) != MAX_HEADER_SIZE)
                    throw new InvalidDataException($"Expected header of length {MAX_HEADER_SIZE}, found {read}");
                string header = Encoding.UTF8.GetString(headerBytes).Trim('_');

                if ((read = await str.ReadAsync(sizeBytes, 0, 4, _cancel.Token)) != sizeBytes.Length)
                    throw new InvalidDataException($"Expected size of length {sizeBytes.Length}, found {read}");
                int size = (int)BinaryParser.Deserialize(sizeBytes, typeof(int));

                if(header != "consoleActivityResponse")
                    Console.WriteLine($"Received {header} [{size}] from {Client.Client.RemoteEndPoint}");

                HandlePacket(header, reader);
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
            if (!_typeHandlers.ContainsKey(type))
                _typeHandlers.Add(type, type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => x.GetCustomAttribute<BlendFarmHeaderAttribute>() != null)
                    .ToDictionary(x => x.GetCustomAttribute<BlendFarmHeaderAttribute>().Header, y => y));
            return _typeHandlers[type];
        }
    }
}
