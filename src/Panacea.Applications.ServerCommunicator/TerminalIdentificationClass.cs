using Microsoft.Win32;
using PanaceaLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TerminalIdentification
{
    public static class TerminalIdentificationManager
    {
        static string _fileName;
        public static async Task<HttpWebResponse> GetHttpResponseAsync(this HttpWebRequest request, int timeout)
        {
            try
            {
                var ct = new CancellationTokenSource(timeout);
                using (ct.Token.Register(() => request.Abort(), useSynchronizationContext: false))
                {
                    var response = await request.GetResponseAsync();
                    ct.Token.ThrowIfCancellationRequested();
                    return (HttpWebResponse)response;
                }
            }
            catch (WebException ex)
            {
                // only handle protocol errors that have valid responses
                if (ex.Response == null || ex.Status != WebExceptionStatus.ProtocolError)
                    throw;

                return (HttpWebResponse)ex.Response;
            }
        }
        static TerminalIdentificationManager()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"software\panacea"))
            {
                var tisFilePath = Path.GetPathRoot(Assembly.GetExecutingAssembly().Location);
                string path = null;
                if ((path = key?.GetValue("TisFilePath", null)?.ToString()) != null)
                {
                    tisFilePath = path;
                }
                _fileName = Path.Combine(tisFilePath, "tis.txt");
            }
        }

        public static async Task IdentifyAsync()
        {
            
            var req = WebRequest.CreateHttp("https://tis.i3panacea.com/PUTIK/request/");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = 20000;
            
            var macs = Common.GetAllMacAddresses();
           
            var driveSerials = CpuID.GetAllDiskDrives().ToList();
            var suggestedId = CpuID.ProcessorId();
           
            using (var rsa = new RSACryptoServiceProvider(1024))

            {
                dynamic obj = new
                {
                    MACs = macs,
                    HDIDs = driveSerials,
                    CPUID = suggestedId,
                    publicKey = ExportPublicKey(rsa),
                    computerName = Environment.MachineName
                };
                using (var writer = new StreamWriter(req.GetRequestStream()))
                {
                    writer.Write(JsonSerializer.SerializeToString(obj));
                }
                using (var resp = await req.GetHttpResponseAsync(20000))
                using (var reader = new StreamReader(resp.GetResponseStream()))
                using (var writer = new StreamWriter(_fileName))
                {
                    var json = await reader.ReadToEndAsync();

                    var dobj = JsonObject.Parse(json);
                    _identificationInfo = new IdentificationInfo()
                    {
                        Putik = dobj.Get<string>("PUTIK"),
                        PublicKey = ExportPublicKey(rsa),
                        PrivateKey = ExportPrivateKey(rsa)
                    };
                    writer.Write(JsonSerializer.SerializeToString(_identificationInfo));

                }
            }

        }

        private static string ExportPublicKey(RSACryptoServiceProvider csp)
        {
            var parameters = csp.ExportParameters(false);
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write((byte)0x30); // SEQUENCE
                using (var innerStream = new MemoryStream())
                {
                    var innerWriter = new BinaryWriter(innerStream);
                    innerWriter.Write((byte)0x30); // SEQUENCE
                    EncodeLength(innerWriter, 13);
                    innerWriter.Write((byte)0x06); // OBJECT IDENTIFIER
                    var rsaEncryptionOid = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 };
                    EncodeLength(innerWriter, rsaEncryptionOid.Length);
                    innerWriter.Write(rsaEncryptionOid);
                    innerWriter.Write((byte)0x05); // NULL
                    EncodeLength(innerWriter, 0);
                    innerWriter.Write((byte)0x03); // BIT STRING
                    using (var bitStringStream = new MemoryStream())
                    {
                        var bitStringWriter = new BinaryWriter(bitStringStream);
                        bitStringWriter.Write((byte)0x00); // # of unused bits
                        bitStringWriter.Write((byte)0x30); // SEQUENCE
                        using (var paramsStream = new MemoryStream())
                        {
                            var paramsWriter = new BinaryWriter(paramsStream);
                            EncodeIntegerBigEndian(paramsWriter, parameters.Modulus); // Modulus
                            EncodeIntegerBigEndian(paramsWriter, parameters.Exponent); // Exponent
                            var paramsLength = (int)paramsStream.Length;
                            EncodeLength(bitStringWriter, paramsLength);
                            bitStringWriter.Write(paramsStream.GetBuffer(), 0, paramsLength);
                        }
                        var bitStringLength = (int)bitStringStream.Length;
                        EncodeLength(innerWriter, bitStringLength);
                        innerWriter.Write(bitStringStream.GetBuffer(), 0, bitStringLength);
                    }
                    var length = (int)innerStream.Length;
                    EncodeLength(writer, length);
                    writer.Write(innerStream.GetBuffer(), 0, length);
                }

                var base64 = Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);

                return base64;

            }
        }

        private static string ExportPrivateKey(RSACryptoServiceProvider csp)
        {
            if (csp.PublicOnly) throw new ArgumentException("CSP does not contain a private key", "csp");
            var parameters = csp.ExportParameters(true);
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write((byte)0x30); // SEQUENCE
                using (var innerStream = new MemoryStream())
                {
                    var innerWriter = new BinaryWriter(innerStream);
                    EncodeIntegerBigEndian(innerWriter, new byte[] { 0x00 }); // Version
                    EncodeIntegerBigEndian(innerWriter, parameters.Modulus);
                    EncodeIntegerBigEndian(innerWriter, parameters.Exponent);
                    EncodeIntegerBigEndian(innerWriter, parameters.D);
                    EncodeIntegerBigEndian(innerWriter, parameters.P);
                    EncodeIntegerBigEndian(innerWriter, parameters.Q);
                    EncodeIntegerBigEndian(innerWriter, parameters.DP);
                    EncodeIntegerBigEndian(innerWriter, parameters.DQ);
                    EncodeIntegerBigEndian(innerWriter, parameters.InverseQ);
                    var length = (int)innerStream.Length;
                    EncodeLength(writer, length);
                    writer.Write(innerStream.GetBuffer(), 0, length);
                }

                var base64 = Convert.ToBase64String(stream.GetBuffer(), 0, (int) stream.Length);
                return base64;
                
            }
        }

        private static void EncodeLength(BinaryWriter stream, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length", "Length must be non-negative");
            if (length < 0x80)
            {
                // Short form
                stream.Write((byte)length);
            }
            else
            {
                // Long form
                var temp = length;
                var bytesRequired = 0;
                while (temp > 0)
                {
                    temp >>= 8;
                    bytesRequired++;
                }
                stream.Write((byte)(bytesRequired | 0x80));
                for (var i = bytesRequired - 1; i >= 0; i--)
                {
                    stream.Write((byte)(length >> (8 * i) & 0xff));
                }
            }
        }

        private static void EncodeIntegerBigEndian(BinaryWriter stream, byte[] value, bool forceUnsigned = true)
        {
            stream.Write((byte)0x02); // INTEGER
            var prefixZeros = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != 0) break;
                prefixZeros++;
            }
            if (value.Length - prefixZeros == 0)
            {
                EncodeLength(stream, 1);
                stream.Write((byte)0);
            }
            else
            {
                if (forceUnsigned && value[prefixZeros] > 0x7f)
                {
                    // Add a prefix zero to force unsigned if the MSB is 1
                    EncodeLength(stream, value.Length - prefixZeros + 1);
                    stream.Write((byte)0);
                }
                else
                {
                    EncodeLength(stream, value.Length - prefixZeros);
                }
                for (var i = prefixZeros; i < value.Length; i++)
                {
                    stream.Write(value[i]);
                }
            }
        }

        static IdentificationInfo _identificationInfo;

        public static async Task<IdentificationInfo> GetIdentificationInfoAsync()
        {
            if (_identificationInfo != null) return _identificationInfo;
            if (File.Exists(_fileName))
            {
                using (var reader = new StreamReader(_fileName))
                {
                    var json = await reader.ReadToEndAsync();
                    _identificationInfo = JsonSerializer.DeserializeFromString<IdentificationInfo>(json);
                    return _identificationInfo;
                }
            }
            return null;
        }

    
    }


    public class IdentificationInfo
    {
        public string Putik { get; set; }

        public string PrivateKey { get; set; }

        public string PublicKey { get; set; }
    }


    public class CpuID
    {
        [DllImport("user32", EntryPoint = "CallWindowProcW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr CallWindowProcW([In] byte[] bytes, IntPtr hWnd, int msg, [In, Out] byte[] wParam, IntPtr lParam);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool VirtualProtect([In] byte[] bytes, IntPtr size, int newProtect, out int oldProtect);

        const int PAGE_EXECUTE_READWRITE = 0x40;

        public static IEnumerable<string> GetAllDiskDrives()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject wmi_HD in searcher.Get())
            {
                var str = wmi_HD.GetPropertyValue("SerialNumber")?.ToString();
                if (str != null)
                    yield return str;
            }

        }

        public static string ProcessorId()
        {
            byte[] sn = new byte[8];

            if (!ExecuteCode(ref sn))
                return "ND";

            return string.Format("{0}{1}", BitConverter.ToUInt32(sn, 4).ToString("X8"), BitConverter.ToUInt32(sn, 0).ToString("X8"));
        }

        private static bool ExecuteCode(ref byte[] result)
        {
            int num;

            /* The opcodes below implement a C function with the signature:
             * __stdcall CpuIdWindowProc(hWnd, Msg, wParam, lParam);
             * with wParam interpreted as an 8 byte unsigned character buffer.
             * */

            byte[] code_x86 = new byte[] {
                0x55,                      /* push ebp */
                0x89, 0xe5,                /* mov  ebp, esp */
                0x57,                      /* push edi */
                0x8b, 0x7d, 0x10,          /* mov  edi, [ebp+0x10] */
                0x6a, 0x01,                /* push 0x1 */
                0x58,                      /* pop  eax */
                0x53,                      /* push ebx */
                0x0f, 0xa2,                /* cpuid    */
                0x89, 0x07,                /* mov  [edi], eax */
                0x89, 0x57, 0x04,          /* mov  [edi+0x4], edx */
                0x5b,                      /* pop  ebx */
                0x5f,                      /* pop  edi */
                0x89, 0xec,                /* mov  esp, ebp */
                0x5d,                      /* pop  ebp */
                0xc2, 0x10, 0x00,          /* ret  0x10 */
            };
            byte[] code_x64 = new byte[] {
                0x53,                                     /* push rbx */
                0x48, 0xc7, 0xc0, 0x01, 0x00, 0x00, 0x00, /* mov rax, 0x1 */
                0x0f, 0xa2,                               /* cpuid */
                0x41, 0x89, 0x00,                         /* mov [r8], eax */
                0x41, 0x89, 0x50, 0x04,                   /* mov [r8+0x4], edx */
                0x5b,                                     /* pop rbx */
                0xc3,                                     /* ret */
            };

            byte[] code;

            if (IsX64Process())
                code = code_x64;
            else
                code = code_x86;

            IntPtr ptr = new IntPtr(code.Length);

            if (!VirtualProtect(code, ptr, PAGE_EXECUTE_READWRITE, out num))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

            ptr = new IntPtr(result.Length);

            try
            {
                return (CallWindowProcW(code, IntPtr.Zero, 0, result, ptr) != IntPtr.Zero);
            }
            catch { Console.WriteLine("Memory corrupted"); return false; }
        }

        private static bool IsX64Process()
        {
            return IntPtr.Size == 8;
        }

    }
}
