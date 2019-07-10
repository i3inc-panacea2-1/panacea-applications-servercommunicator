using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Interop;
using Newtonsoft.Json;
using SocketIOClient;
using WebSocketCommunication;
using PanaceaLib;
using ServiceStack.Text;
using WebSocket4Net;
using TerminalIdentification;

namespace ServerCommunicator
{
    public class WebSocketCommunicator: IEmitter
    {
        private Client _webSocket;
        static readonly object Lock=new object();
        private readonly TcpListener _listener;
        private readonly Dictionary<TcpClient, StreamWriter> _writers = new Dictionary<TcpClient, StreamWriter>();
        
        private bool _closing = false;

        public WebSocketCommunicator()
        {
            BuildWebSocket();
            _listener = new TcpListener(IPAddress.Loopback, 9007);
            Application.Current.Exit += (sender, args) =>
            {
	            try
	            {
		            _closing = true;
		            Stop();
		            _webSocket.Close();
	            }
	            catch
	            {
                    //ignore
	            }
            };
          
        }

        private void BuildWebSocket()
        {
            var uri = new Uri(App.HospitalServer);
            var uribuilder = new UriBuilder(uri);
            uribuilder.Port += 100;
            _webSocket = new Client(uribuilder.Uri.ToString());
            PrepareWebSocket();
        }

        private async Task Reconnect()
        {
            if (_webSocket.ReadyState == WebSocketState.Open) return;
            if (_closing) return;
            Console.WriteLine("Attempting to reconnect");
            _manager?.CloseCmds();
            foreach (var client in _writers.Values.ToList())
            {
                try
                {
                    client.WriteLine(JsonConvert.SerializeObject(new Message()
                    {
                        Target = Target.Hospital,
                        Verb = "ConnectionStatus",
                        Object = "false"
                    }));
                }
                catch
                {
                    //ignore
                }
            }
            _webSocket.Close();
			_webSocket.Dispose();
			BuildWebSocket();
            await Task.Delay(new Random().Next(2000, 10000));
            ConnectSocket();
        }

        private void ConnectSocket()
        {
	        try
	        {
		        _webSocket.Connect();
	            WebSocketMonitor.Client = _webSocket;

                /*
	            await Task.Delay(15000);
	           
	            for (var i= 0; i < 1000 ; i++)
	            {
	                EmitToClients(new Message(Target.Hospital,"computrition_notify", false, @"{ ""isReminder"" : true, ""mainMessage"" : ""Please take this time to make your meal selection by selecting the link below. Automatic selections will be provided if no food selections are made."",
""reminderMessage"" : ""Please make your meal selection by selecting the below link within the next 15 minutes. If a selection is not made, an automatic meal selection will be provided. Please have your tray table cleared for food services prior to meal time."" }"));
	                await Task.Delay(20);
	            }
                */
	        }
	        catch
	        {
		        Application.Current.Shutdown();
	        }
        }
        /*
         [DataMember(Name = "mainMessage")]
         public string MainMessage { get; set; }


         [DataMember(Name = "reminderMessage")]
         public string ReminderMessage { get; set; }

         [DataMember(Name = "isReminder")]
         public bool IsReminder { get; set; }
         */


        public void Emit(string eventName, object data)
        {
            lock (Lock)
            {
                try
                {
                   
                    _webSocket.Emit(eventName, data);
                }
                catch
                {
                }
            }
        }

        private readonly List<Message> queue = new List<Message>();

        private void ExecuteCommand(Message msg)
        {
            if (_webSocket.ReadyState != WebSocketState.Open)
            {
                if (msg.AddToQueue)
                    queue.Add(msg);
                return;
            }
            var socket = _webSocket;
            if (socket == null) return;
            if (msg.Object != null)
            {
                Emit(msg.Verb, JsonConvert.DeserializeObject(msg.Object));
            }
        }

        private CmdManager _manager;
        private void PrepareWebSocket()
        {
            _manager = new CmdManager(_webSocket, this);
            _webSocket.RetryConnectionAttempts = 0;
            _webSocket.Error += _webSocket_Error;
            _webSocket.SocketConnectionClosed += _webSocket_SocketConnectionClosed;
            _webSocket.On("connect", async fn =>
            {
                queue.RemoveAll(m => m.Verb == "online");
                Emit("online", new {mac = (await TerminalIdentificationManager.GetIdentificationInfoAsync()).Putik});
                await Task.Delay(new Random().Next(350, 2600));
                var queue2 = queue.ToList();
                foreach (var msg in queue2)
                {
                    ExecuteCommand(msg);
                    queue.Remove(msg);
                    await Task.Delay(new Random().Next(150, 300));
                }
                foreach (var client in _writers.Values.ToList())
                {
                    try
                    {
                        client.WriteLine(JsonConvert.SerializeObject(new Message()
                        {
                            Target = Target.Hospital,
                            Verb = "ConnectionStatus",
                            Object = "true"
                        }));
                        client.WriteLine(JsonConvert.SerializeObject(new Message()
                        {
                            Target = Target.Hospital,
                            Verb = "connect",
                            Object = "{}"
                        }));
                        await Task.Delay(new Random().Next(150, 300));
                    }
                    catch
                    {
                    }
                }

            });

            _webSocket.Message += async (sender, args) =>
            {
                if (args.Message.Event == null || args.Message.Event == "connect") return;


                try
                {
                    MessageFromServer o =
                        JsonConvert.DeserializeObject<MessageFromServer>(args.Message.Json.Args != null &&
                                                                         args.Message.Json.Args[0] != null
                            ? args.Message.Json.Args[0].ToString()
                            : "{}");
                    if (o.Action == "reboot")
                    {
                        try
                        {
                            if (o.Data != null)
                            {
                                if (o.Data.Delay > 0)
                                {
                                    await Task.Delay(o.Data.Delay * 1000);
                                }

                            }
                        }
                        catch { }
                        Stop();
                        Process.Start("shutdown.exe", "-f -r -t 0");
                        Application.Current.Shutdown();
                    }

                }
                catch
                {
                    //ignore
                }


                var msg = new Message()
                {
                    Verb = args.Message.Event,
                    Object =
                        args.Message.Json.Args != null && args.Message.Json.Args[0] != null
                            ? args.Message.Json.Args[0].ToString()
                            : "{}"
                };
                EmitToClients(msg);
                
            };
        }

        void EmitToClients(Message msg)
        {
            var w = _writers.Values.ToList();
            foreach (var writer in w)
            {
                try
                {
                    writer.WriteLine(JsonConvert.SerializeObject(msg));
                }
                catch
                {
                    //ignore
                }

            }
        }

        private async void _webSocket_SocketConnectionClosed(object sender, EventArgs e)
        {
           await  Reconnect();
        }

        private async void _webSocket_Error(object sender, SocketIOClient.ErrorEventArgs e)
        {
            await Reconnect();
        }

        private void StartServer()
        {
            Task.Factory.StartNew(() =>
            {
                _listener.Start();
                while (true)
                {
                    var client = _listener.AcceptTcpClient();
                    AcceptClient(client);
                }

            }, TaskCreationOptions.LongRunning);
        }

        private void AcceptClient(TcpClient client)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    _writers.Add(client,new StreamWriter(client.GetStream()){AutoFlush = true});
                    _writers[client].WriteLine(JsonConvert.SerializeObject(new Message()
                    {
                        Target = Target.Hospital,
                        Verb = "ConnectionStatus",
                        Object = (_webSocket.ReadyState == WebSocketState.Open).ToString().ToLower()
                    }));
                    using (var reader = new StreamReader(client.GetStream()))
                    {
                        while (true)
                        {
                            var text = reader.ReadLine();
                            var msg = JsonConvert.DeserializeObject<Message>(text);
                            ExecuteCommand(msg);
                        }
                    }
                }
                catch
                {
                    _writers.Remove(client);
                }
            }, TaskCreationOptions.LongRunning);
        }

        private async void Stop()
        {
           
            Emit("offline", new { mac = (await TerminalIdentificationManager.GetIdentificationInfoAsync()).Putik });
            _webSocket.Close();
            _listener.Stop();
        }

        public void Start()
        {
            Task.Run(() =>
            {
                StartServer();
                ConnectSocket();
            });
        }
    }


    [DataContract]
    public class MessageFromServer
    {
        [DataMember(Name = "action")]
        public string Action { get; set; }

        [DataMember(Name = "data")]
        public MessageFromServerData Data { get; set; }
    }


    [DataContract]
    public class MessageFromServerData
    {
        [DataMember(Name = "delay")]
        public int Delay { get; set; }

        [DataMember(Name = "logout")]
        public bool Logout { get; set; }
    }


}
