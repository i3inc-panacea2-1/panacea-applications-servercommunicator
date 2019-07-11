using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PanaceaLib
{
    public static class PanaceaRegistry
    {
        static string Serialize<T>(T obj)
        {
            return ServiceStack.Text.JsonSerializer.SerializeToString(obj);
        }

        static T Deserialize<T>(string json)
        {
            return ServiceStack.Text.JsonSerializer.DeserializeFromString<T>(json);
        }

        public static async Task<ServerInformation> GetServerInformation(bool throwException = true)
        {
            return await Task.Run(() =>
            {
                
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey("Software", false))
                using (var panacea = key?.OpenSubKey("Panacea"))
                {
                    
                    if (panacea == null)
                    {
                        if (throwException) throw new Exception("Terminal reg key is missing...");
                        return new ServerInformation()
                        {
                            ManagementServer = "http://management.i3panacea.com:1337/"
                        };
                    }
                    var ts = panacea.GetValue("TerminalServer", null);
                    var hs = panacea.GetValue("HospitalServer", null);
                    var responseText = panacea.GetValue("TerminalServerResponse", null);
                    var noupdate = (int)panacea.GetValue("NoUpdate", 0);
                    var runtimepath = (string)panacea.GetValue("RuntimePath", "");
                    if (ts == null)
                        ts = "http://management.i3panacea.com:1337/";
                    if (throwException && (hs == null || responseText == null))
                        throw new Exception("Panacea Updater has to be executed at least once.");
                    return new ServerInformation()
                    {
                        HospitalServer = hs != null ? hs.ToString() : "",
                        ManagementServer = ts.ToString(),
                        NoUpdate = noupdate,
                        RuntimePath = runtimepath,
                        ManagementServerResponse =
                            responseText != null
                                ? Deserialize<ServerResponse<GetHospitalServersResponse>>(responseText.ToString())
                                : null
                    };

                }
            });
        }

        public static void SetManagementServer(string url)
        {
            using (var key = Registry.LocalMachine.OpenSubKey("Software", true))
            using (var panacea = key.CreateSubKey("Panacea"))
            {
                panacea.SetValue("TerminalServer", url);
            }
        }
    }

    [DataContract]
    public class GetHospitalServersResponse
    {
        [DataMember(Name = "teamviewer_id")]
        public string TeamviewerId { get; set; }

        [DataMember(Name = "hospital_servers")]
        public List<string> HospitalServers { get; set; }

        [DataMember(Name = "crutch")]
        public string Crutch { get; set; }

        [DataMember(Name = "terminal_type")]
        public TerminalType TerminalType { get; set; }
    }

    [DataContract]
    public class TerminalType
    {
        [DataMember(Name = "pairs")]
        public string Pairs { get; set; }

    }


    public class ServerInformation
    {
        public string ManagementServer { get; set; }
        public string HospitalServer { get; set; }
        public int NoUpdate { get; set; }
        public string RuntimePath { get; set; }
        public ServerResponse<GetHospitalServersResponse> ManagementServerResponse { get; set; }
    }

}
