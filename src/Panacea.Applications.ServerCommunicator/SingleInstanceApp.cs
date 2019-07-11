using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PanaceaLib
{
	public abstract class SingleInstanceApp: Application, ISingleInstanceApp
	{
		private readonly string _name ;

		protected SingleInstanceApp(string uniqueAppName)
		{
			_name = uniqueAppName;
			ShutdownMode = ShutdownMode.OnExplicitShutdown;
		}

        protected override void OnExit(ExitEventArgs e)
        {
            SingleInstance<SingleInstanceApp>.Cleanup();
            base.OnExit(e);
        }
	    public new void Run()
	    {
		   
            if (!SingleInstance<SingleInstanceApp>.InitializeAsFirstInstance(_name))
                return;
#if DEBUG

            base.Run();
#else
           
            try
            {
                base.Run();
            }
            catch (Exception ex)
            {
                try
                {
					SingleInstance<SingleInstanceApp>.Cleanup();
                    var sw = new StreamWriter("error.txt", true);
                    sw.WriteLine(DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "|" + ex.Message);
                    sw.WriteLine(ex.StackTrace);

                    if (ex.InnerException != null)
                    {
                        sw.WriteLine(ex.InnerException.Message);
                        sw.WriteLine(ex.InnerException.StackTrace);
                    }
                    sw.Close();
                }
                catch
                {
                }
                throw;
            }
#endif
			SingleInstance<SingleInstanceApp>.Cleanup();
		}

		public abstract bool SignalExternalCommandLineArgs(IList<string> args);
	}
}
