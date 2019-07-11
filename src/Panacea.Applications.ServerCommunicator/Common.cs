using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace PanaceaLib
{
    public static class Common
    {
        public static NameValueCollection ParseStartUpArgs()
        {
            var keys = new NameValueCollection();
            var arg = Environment.GetCommandLineArgs();
            keys["no-animations"] = "1";
            foreach (string s in ConfigurationManager.AppSettings.Keys)
            {
                keys[s] = ConfigurationManager.AppSettings[s];
            }
            var path = Common.Path();
            foreach (var split in arg.Where(s=>!s.StartsWith(path)).Select(s => s.Split('=')))
            {
                if (split.Length > 2)
                {
                    for (var i = 2; i < split.Length; i++) split[1] += "=" + split[i];
                }
                if (split.Length >= 2)
                {
                    keys[split[0]] = split[1];
                }
                else
                {
                    keys[split[0]] = null;
                }
            }

            return keys;
        } 

        public static string Path()
        {
            return System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\";
        }


        private static string mac;
        private const int MIN_MAC_ADDR_LENGTH = 12;


        public static IEnumerable<string> GetAllMacAddresses()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Unknown) continue;
                var tempMac = nic.GetPhysicalAddress().ToString();
                var macAddress = tempMac;
	            if (string.IsNullOrEmpty(macAddress)) continue;

	            macAddress = macAddress.Substring(0, 2) + "-" + macAddress.Substring(2, 2) + "-" +
	                         macAddress.Substring(4, 2) + "-" + macAddress.Substring(6, 2) + "-" +
	                         macAddress.Substring(8, 2) + "-" + macAddress.Substring(10, 2);
                if (macAddress.StartsWith("00-00-00-00-00-00")) continue;
                yield return macAddress;
            }
        }

        public static Task WaitForNetwork()
        {
            return Task.Run(()=>GetMacAddress());
        }

        public static string GetMacAddress(bool skip = false)
        {
            if (mac != null) return mac;
            
            var macAddress = "";
            long maxSpeed = -1;
            var count = 0;
            while (string.IsNullOrEmpty(mac) && count < 100)
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Unknown) continue;
                    if (nic.OperationalStatus != OperationalStatus.Up
                        || string.IsNullOrEmpty(nic.GetPhysicalAddress().ToString())) continue;

                    var tempMac = nic.GetPhysicalAddress().ToString();
                    if (nic.Speed > maxSpeed && tempMac.Length >= MIN_MAC_ADDR_LENGTH)
                    {
                        maxSpeed = nic.Speed;
                        macAddress = tempMac;
                        try
                        {
                            macAddress = macAddress.Substring(0, 2) + "-" + macAddress.Substring(2, 2) + "-" +
                                         macAddress.Substring(4, 2) + "-" + macAddress.Substring(6, 2) + "-" +
                                         macAddress.Substring(8, 2) + "-" + macAddress.Substring(10, 2);
                        }
                        catch
                        {
                            //ignore
                        }
                    }
                }

                mac = macAddress;
                count++;
                if (string.IsNullOrEmpty(mac)) Thread.Sleep(1000);
            }
            return mac;
            
        }

        public static async Task<bool> MoveDirectory(string source, string target, bool delete = true, bool overwrite = true)
        {
            return await Task.Run(() =>
            {
                bool moved = false;
                var stack = new Stack<Folders>();
                stack.Push(new Folders(source, target));

                while (stack.Count > 0)
                {
                    Folders folders = stack.Pop();
                    if (!Directory.Exists(folders.Target) && !String.IsNullOrEmpty(folders.Target))
                    {
                        Directory.CreateDirectory(folders.Target);
                    }
                    if (Directory.Exists(folders.Source))
                    {
                        foreach (var file in Directory.GetFiles(folders.Source, "*.*"))
                        {
                            string targetFile = System.IO.Path.Combine(folders.Target, System.IO.Path.GetFileName(file));

                            if (File.Exists(targetFile))
                            {
                                if(overwrite)
                                    File.Delete(targetFile);
                                else continue;
                                
                            }
                            if (!String.IsNullOrEmpty(targetFile) && !Directory.Exists(System.IO.Path.GetDirectoryName(targetFile)))
                                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetFile));
                            if (delete)
                                File.Move(file, targetFile);
                            else File.Copy(file, targetFile);
                            moved = true;
                        }

                        foreach (var folder in Directory.GetDirectories(folders.Source))
                        {
                            stack.Push(new Folders(folder, System.IO.Path.Combine(folders.Target, System.IO.Path.GetFileName(folder))));
                        }
                    }
                }
                if (Directory.Exists(source) && delete)

                    Directory.Delete(source, true);
                return moved;
            });

        }

        public static void BringWindowInFront(Window window)
        {
            window.Topmost = true;  // important
            window.Topmost = false;
            window.Activate();
            window.Focus();
        }

        public static bool SetMachineName(string newName)
        {
            RegistryKey key = Registry.LocalMachine;

            string activeComputerName = "SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ActiveComputerName";
            RegistryKey activeCmpName = key.CreateSubKey(activeComputerName);
            activeCmpName.SetValue("ComputerName", newName);
            activeCmpName.Close();
            string computerName = "SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName";
            RegistryKey cmpName = key.CreateSubKey(computerName);
            cmpName.SetValue("ComputerName", newName);
            cmpName.Close();
            string _hostName = "SYSTEM\\CurrentControlSet\\services\\Tcpip\\Parameters\\";
            RegistryKey hostName = key.CreateSubKey(_hostName);
            hostName.SetValue("Hostname", newName);
            hostName.SetValue("NV Hostname", newName);
            hostName.Close();
            SetComputerName(newName);
            return true;
        }
        [DllImport("kernel32.dll")]
        static extern bool SetComputerName(string lpComputerName);

    }

    [DataContract]
    public class ServerResponse<T>
    {
        [DataMember(Name = "success")]
        public Boolean Success { get; set; }


        [DataMember(Name = "result")]
        public T Result { get; set; }


        [DataMember(Name = "error")]
        public string Error { get; set; }

        public string Json { get; set; }
    }

    public class Folders
    {
        public Folders(string source, string target)
        {
            Source = source;
            Target = target;
        }

        public string Source { get; private set; }
        public string Target { get; private set; }
    }
    
}
