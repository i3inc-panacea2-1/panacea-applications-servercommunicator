using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using SocketIOClient;
using WebSocket4Net;
using System.Windows;
using PanaceaLib;

namespace ServerCommunicator
{
    static class WebSocketMonitor
    {
        private static Client _client;
        private static Timer _timer;
        private static DateTime _lastConnectionCheck;

        static WebSocketMonitor()
        {
            _timer = new Timer
            {
                Interval = new Random().Next(60000, 90000)
            };
            _timer.Elapsed += _timer_Elapsed;
        }

        private static void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Client.ReadyState == WebSocketState.Open)
            {
                _lastConnectionCheck = DateTime.Now;
                return;
            }
            if (DateTime.Now.Subtract(_lastConnectionCheck).TotalMinutes > 5)
            {
                try
                {
                    using (var sr = new StreamWriter(Common.Path() + "sc-app-close.txt", false))
                    {
                        sr.WriteLine(DateTime.Now.ToString("dd/MM/yy hh:mm:ss") + " shutdown");
                    }
                }
                catch
                {
                    //ignore
                }
                Application.Current.Shutdown(0);
            }
        }

        public static Client Client
        {
            get { return _client; }
            set
            {
                _timer.Stop();
                _lastConnectionCheck = DateTime.Now;
                _client = value;
                _timer.Start();
            }
        }
    }
}
