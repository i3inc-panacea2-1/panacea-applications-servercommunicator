using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using JsonSerializer = ServiceStack.Text.JsonSerializer;


namespace WebSocketCommunication
{
    public class WebSocketCommunicator
    {
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;

        public bool IsConnected
        {
            get { return client.Connected; }
        }

       
        public bool IsConnectedWithHospitalServer { get; set; }

        public WebSocketCommunicator(string macAddress)
        {

            this.mac = macAddress;
        }

        async Task TryConnect()
        {
            client = new TcpClient();
            while (!client.Connected)
            {
                try
                {
                    client.Connect("127.0.0.1", 9007);
                    
                    reader = new StreamReader(client.GetStream());
                    writer = new StreamWriter(client.GetStream()) {AutoFlush = true};
                    break;
                }
                catch
                {
                    await Task.Delay(8000);
                }
                
            }
        }
        public async Task Connect()
        {
            await Task.Run(async() =>
            {
                await TryConnect();
                Task.Run(() =>
                {
                    try
                    {
                        while (true)
                        {
                            var line = reader.ReadLine();
                            if (line == null) continue;
                            var msg = (Message) JsonSerializer.DeserializeFromString<Message>(line.Trim('\0'));
                            if (msg == null) continue;
                            if (msg.Verb == "ConnectionStatus")
                            {
                                var status = JsonSerializer.DeserializeFromString<bool>(msg.Object);
                                IsConnectedWithHospitalServer = status;
                                continue;
                            }
                            OnMessage(msg);
                            if (!_websocketOnActionsT.ContainsKey(msg.Verb)) continue;
                            foreach (var action in _websocketOnActionsT[msg.Verb])
                            {
                                try
                                {
                                    var obj = JsonSerializer.DeserializeFromString(msg.Object, action.Type);
                                    action.Action(obj);
                                }
                                catch (Exception ex)
                                {
                                    if (Debugger.IsAttached) throw ex;
                                }
                            }
                        }
                    }
                    catch
                    {
                        IsConnectedWithHospitalServer = false;
                    }
                    client.Close();
                    Connect();

                });
            });
        }
        public event EventHandler<MessageEventArgs> Message;

        void OnMessage(Message m)
        {
            var h = Message;
            if (h != null) h(this, new MessageEventArgs() {Message = m});
        }
        public void Emit<T>(string verb, T obj, bool addToQueue = true,Target target = Target.Hospital)
        {
            var str =
            JsonConvert.SerializeObject(new Message(target, verb,addToQueue,
                JsonConvert.SerializeObject(new { mac = mac, data = obj })));
            Write(str);
        }

        private void Write(string json)
        {
            if (writer == null || !client.Connected) return;
            try
            {
                writer.WriteLine(json);
            }
            catch
            {
                //ignore
            }
        }

        private readonly Dictionary<string, List<TypeActionPair>> _websocketOnActionsT = new Dictionary<string, List<TypeActionPair>>();

        public void On<T>(string _event, Action<T> act)
        {
            if (!_websocketOnActionsT.ContainsKey(_event))
            {
                _websocketOnActionsT.Add(_event, new List<TypeActionPair>());
            }
            var pair = new TypeActionPair() {Callback = act, Action = o => act((T) o), Type = typeof (T)};
            _websocketOnActionsT[_event].Add(pair);
        }

        public void Remove(string verb, object action)
        {
            if (!_websocketOnActionsT.ContainsKey(verb) || !_websocketOnActionsT[verb].Any(p => p.Callback == action))
                return;

            var actions = _websocketOnActionsT[verb].Where(p => p.Callback == action).ToList();
            foreach(var act in actions)
                _websocketOnActionsT[verb].Remove(act);
        }

        private string mac;
       
    }

    public class MessageEventArgs : EventArgs
    {
        public Message Message { get; set; }
    }

    public class TypeActionPair
    {
        public Type Type { get; set; }
        public Action<object> Action { get; set; }
        public object Callback { get; set; }
    }

    [Serializable]
    public class Message
    {
        public Message()
        {
        }

        public Message(Target target, string verb, bool addToQueue, string obj)
        {
            Target = target;
            Verb = verb;
            Object = obj;
            AddToQueue = addToQueue;
        }

        public Target Target { get; set; }

        public string Verb { get; set; }
        public string Object { get; set; }
        public bool AddToQueue { get; set; }
    }

    public enum Target
    {
        Hospital,
        Management
    }

}
