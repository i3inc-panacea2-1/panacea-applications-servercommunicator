using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PanaceaLib;

namespace ServerCommunicator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : SingleInstanceApp
    {
	    public App() : base("ServerCommunicator")
	    {
		    InitializeComponent();
#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
		    DispatcherUnhandledException += Application_DispatcherUnhandledException;
		    System.Windows.Forms.Application.ThreadException += Application_ThreadException;
		    TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#endif

        }

	    private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
		{
            LogError(e.Exception);
            Dispatcher.Invoke(() =>
            {
                Shutdown();
            });
		}

		private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
		{
            LogError(e.Exception);
			e.Handled = true;
			Shutdown();
		}

		private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
            LogError(e.ExceptionObject as Exception);
            Shutdown();
		}

        private void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            LogError(e.Exception);
            Shutdown();
        }

        static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                using (var sw = new StreamWriter(Common.Path() + "sc-error.txt"))
                {
                    sw.Write(DateTime.Now.ToLongDateString());
                    sw.Write(DateTime.Now.ToLongTimeString());
                    sw.WriteLine(ex.Message);
                    sw.WriteLine(ex.StackTrace);
                    if (ex.InnerException != null)
                    {
                        sw.WriteLine(ex.InnerException.Message);
                        sw.WriteLine(ex.InnerException.StackTrace);
                    }
                    sw.WriteLine();
                }
            }
            catch
            {
            }
        }


        public static string Server { get; private set; }
        public static string HospitalServer { get; private set; }

        private async void App_OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                var info = await PanaceaRegistry.GetServerInformation();
                Server = info.ManagementServer;
                HospitalServer = info.HospitalServer;
                if (string.IsNullOrEmpty(HospitalServer))
                {
                    Application.Current.Shutdown();
                    return;
                }
                new WebSocketCommunicator().Start();
            }
            catch
            {
                Application.Current.Shutdown();
                //exit
            }
        }

        public override bool SignalExternalCommandLineArgs(IList<string> args)
        {
            return true;
        }

		
	}
}
