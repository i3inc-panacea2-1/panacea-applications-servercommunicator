using System;
using System.IO;
using System.Windows;
using System.Diagnostics;
using PanaceaLib;

namespace ServerCommunicator
{
    public class Program
    {

	    [STAThread]
	    public static void Main()
	    {
			var app = new App();


#if DEBUG
			app.Run();
#else
           
            try
            {
                app.Run();
            }
            catch (Exception ex)
            {
                
            }
#endif
	    }

	    public static App Application;
        
    }
}