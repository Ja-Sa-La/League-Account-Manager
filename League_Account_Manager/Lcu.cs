using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace League_Account_Manager;

internal class lcu
{
    public static Vals Riot = new Vals { path = "", port = "", token = "", Value = "", version = null }; 
    public static Vals League = new Vals();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint FindWindow(string strClassName, string strWindowName);

    public static async Task<(string RiotPort, string RiotToken, string LeaguePort, string LeagueToken)> GetClientInfo()
    {
        var ingame = Process.GetProcessesByName("League of Legends");
        if (ingame.Length != 0)
            return ("0", "0", "0", "0");
        string[] portSplit = { "1", "2" }, tokenSplit;
        byte[] token;
        string riotPort = "", riotToken = "", leaguePort = "", leagueToken = "";

        var riotProcess = Process.GetProcessesByName("Riot Client");
        if (riotProcess.Length == 0)
            riotProcess = Process.GetProcessesByName("RiotClientUx");

        var leagueClientProcess = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();

        if (riotProcess.Length > 0)
        {
            foreach (var ritoprocess in riotProcess)
                try
                {
                    ProcessCommandLine.Retrieve(ritoprocess, out var value);
                    SetRiotValues(ritoprocess, value);
                    if (Riot.port[1].ToString() != "2")
                    {
                        portSplit = Riot.port.Split("=");
                        tokenSplit = Riot.token.Split("=");
                        riotPort = portSplit[1];
                        riotToken = tokenSplit[1];
                        break;
                    }
                }
                catch (Exception)
                {
                }
        }
        else if (leagueClientProcess != null)
        {
            ProcessCommandLine.Retrieve(leagueClientProcess, out var value);
            SetRiotValues(leagueClientProcess, value, true);
            portSplit = Riot.port.Split("=");
            tokenSplit = Riot.token.Split("=");
            riotPort = portSplit[1];
            riotToken = tokenSplit[1];
        }

        var leagueClientProcess2 = Process.GetProcessesByName("LeagueClientUx");
        if (leagueClientProcess2 != null)
        {
            foreach (var leagueprocess in leagueClientProcess2)
                try
                {
                    ProcessCommandLine.Retrieve(leagueprocess, out var value);
                    SetLeagueValues(leagueprocess, value);
                    portSplit = League.port.Split("=");
                    tokenSplit = League.token.Split("=");
                    leaguePort = portSplit[1];
                    leagueToken = tokenSplit[1];
                }
                catch (Exception)
                {
                }
        }

        return (riotPort, riotToken, leaguePort, leagueToken);
    }

    public static async Task<dynamic> Connector(string target, string mode, string endpoint, string data)
    {
        var ingame = Process.GetProcessesByName("League of Legends");
        if (ingame.Length != 0)
            return "";
            var clientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        var client = new HttpClient(clientHandler);
        
        string[] portSplit = { "1", "2" }, tokenSplit;
        byte[] token;
        if (target == "riot")
        {
            var riotProcess = Process.GetProcessesByName("Riot Client");
            if (riotProcess.Length == 0)
                riotProcess = Process.GetProcessesByName("RiotClientUx");

            var leagueClientProcess = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();

            if (riotProcess.Length > 0)
            {
                foreach (var ritoprocess in riotProcess)
                    try
                    {
                        ProcessCommandLine.Retrieve(ritoprocess, out var value);
                        SetRiotValues(ritoprocess, value);
                        if (Riot.port[1].ToString() != "2")
                            break;
                    }
                    catch (Exception)
                    {
                    }
            }
            else if (leagueClientProcess != null)
            {
                ProcessCommandLine.Retrieve(leagueClientProcess, out var value);
                SetRiotValues(leagueClientProcess, value, true);
            }
            else
            {
                return 0;
            }

            portSplit = Riot.port.Split("=");
            tokenSplit = Riot.token.Split("=");
            token = Encoding.UTF8.GetBytes("riot:" + tokenSplit[1]);
            SetClientHeaders(client, portSplit[1], token, Riot.version.FileVersion);
        }
        else
        {
            var leagueClientProcess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueClientProcess == null) return 0;
            foreach (var leagueprocess in leagueClientProcess)
                try
                {
                    ProcessCommandLine.Retrieve(leagueprocess, out var value);
                    SetLeagueValues(leagueprocess, value);
                    portSplit = League.port.Split("=");
                    tokenSplit = League.token.Split("=");
                    token = Encoding.UTF8.GetBytes("riot:" + tokenSplit[1]);
                    SetClientHeaders(client, portSplit[1], token, League.version.FileVersion);
                }
                catch (Exception)
                {
                }
        }

        return await SendRequest(client, mode, endpoint, data, portSplit[1]);
    }

    private static void SetRiotValues(Process process, string value, bool isLeagueClient = false)
    {
        Riot.Value = value;
        Riot.port = showMatch(Riot.Value, isLeagueClient ? "--riotclient-app-port=(\\d*)" : "-app-port=(\\d*)");
        Riot.token = showMatch(Riot.Value,
            isLeagueClient ? "--riotclient-auth-token=([\\w-]*)" : "--remoting-auth-token=([\\w-]*)");
        Riot.path = process.MainModule.FileName;
        Riot.version = FileVersionInfo.GetVersionInfo(Riot.path);
    }

    private static void SetLeagueValues(Process process, string value)
    {
        League.Value = value;
        League.port = showMatch(League.Value, "--app-port=(\\d*)");
        League.token = showMatch(League.Value, "--remoting-auth-token=([\\w-]*)");
        League.path = process.MainModule.FileName;
        League.version = FileVersionInfo.GetVersionInfo(League.path);
    }

    private static void SetClientHeaders(HttpClient client, string port, byte[] token, string version)
    {
        client.DefaultRequestHeaders.Add("Host", "127.0.0.1:" + port);
        client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        client.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(token));
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("Access-Control-Allow-Credentials", "true");
        client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "127.0.0.1");
        client.DefaultRequestHeaders.Add("Origin", "127.0.0.1:" + port);
        client.DefaultRequestHeaders.Add("User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) RiotClient/{version} (CEF 74) Safari/537.36");
        client.DefaultRequestHeaders.Add("X-Riot-Source", "127.0.0.1:" + port);
        client.DefaultRequestHeaders.Add("sec-ch-ua", "Chromium");
        client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?F");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        client.DefaultRequestHeaders.Add("Referer", "https://127.0.0.1:" + port + "/index.html");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, be");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    }

    private static async Task<HttpResponseMessage> SendRequest(HttpClient client, string method, string endpoint,
        string data, string port)
    {
        // Simplify URL construction
        var url = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"https://127.0.0.1:{port}{endpoint}";

        //Console.Writeline(url); // Consider removing or using a logging framework

        // Create HttpMethod based on the method argument
        var httpMethod = new HttpMethod(method.ToLowerInvariant());

        // Initialize HttpRequestMessage
        var request = new HttpRequestMessage(httpMethod, url);

        // For methods other than GET, set the content
        if (method.ToLowerInvariant() != "get")
            request.Content =
                new StringContent(string.IsNullOrEmpty(data) ? "" : data, Encoding.UTF8, "application/json");
        else if (!string.IsNullOrEmpty(data))
            // Append data as query string for GET requests
            request.RequestUri = new Uri($"{url}?{data}");

        // Send the request
        var response = await client.SendAsync(request);
        client.Dispose();
        return response;
    }

    private static string showMatch(string text, string expr)
    {
        dynamic mc;
        try
        {
            mc = Regex.Matches(text, expr);

            foreach (Match m in mc) return m.ToString();
        }
        catch (Exception)
        {
            return "error";
        }

        return "error";
    }

    public struct Vals
    {
        public string Value { get; set; }
        public string port { get; set; }
        public string token { get; set; }
        public string path { get; set; }
        public FileVersionInfo version { get; set; }
    }
}

public static class ProcessCommandLine
{
    private static bool ReadStructFromProcessMemory<TStruct>(
        nint hProcess, nint lpBaseAddress, out TStruct val)
    {
        val = default;
        var structSize = Marshal.SizeOf<TStruct>();
        var mem = Marshal.AllocHGlobal(structSize);
        try
        {
            if (Win32Native.ReadProcessMemory(
                    hProcess, lpBaseAddress, mem, (uint)structSize, out var len) &&
                len == structSize)
            {
                val = Marshal.PtrToStructure<TStruct>(mem);
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }

        return false;
    }


    public static int Retrieve(Process process, out string commandLine)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var rc = 0;
        commandLine = null;
        var hProcess = Win32Native.OpenProcess(
            Win32Native.OpenProcessDesiredAccessFlags.PROCESS_QUERY_INFORMATION |
            Win32Native.OpenProcessDesiredAccessFlags.PROCESS_VM_READ, false, (uint)process.Id);
        if (hProcess != nint.Zero)
            try
            {
                var sizePBI = Marshal.SizeOf<Win32Native.ProcessBasicInformation>();
                var memPBI = Marshal.AllocHGlobal(sizePBI);
                try
                {
                    var ret = Win32Native.NtQueryInformationProcess(
                        hProcess, Win32Native.PROCESS_BASIC_INFORMATION, memPBI,
                        (uint)sizePBI, out var len);
                    if (0 == ret)
                    {
                        var pbiInfo = Marshal.PtrToStructure<Win32Native.ProcessBasicInformation>(memPBI);
                        if (pbiInfo.PebBaseAddress != nint.Zero)
                        {
                            if (ReadStructFromProcessMemory<Win32Native.PEB>(hProcess,
                                    pbiInfo.PebBaseAddress, out var pebInfo))
                            {
                                if (ReadStructFromProcessMemory<Win32Native.RtlUserProcessParameters>(
                                        hProcess, pebInfo.ProcessParameters, out var ruppInfo))
                                {
                                    var clLen = ruppInfo.CommandLine.MaximumLength;
                                    var memCL = Marshal.AllocHGlobal(clLen);
                                    try
                                    {
                                        if (Win32Native.ReadProcessMemory(hProcess,
                                                ruppInfo.CommandLine.Buffer, memCL, clLen, out len))
                                        {
                                            commandLine = Marshal.PtrToStringUni(memCL);
                                            rc = 0;
                                        }
                                        else
                                        {
                                            // couldn't read command line buffer
                                            rc = -6;
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(memCL);
                                    }
                                }
                                else
                                {
                                    // couldn't read ProcessParameters
                                    rc = -5;
                                }
                            }
                            else
                            {
                                // couldn't read PEB information
                                rc = -4;
                            }
                        }
                        else
                        {
                            // PebBaseAddress is null
                            rc = -3;
                        }
                    }
                    else
                    {
                        // NtQueryInformationProcess failed
                        rc = -2;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(memPBI);
                }
            }
            finally
            {
                Win32Native.CloseHandle(hProcess);
            }
        else
            // couldn't open process for VM read
            rc = -1;

        return rc;
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"-c \"ps -p {process.Id} -o command=\""
        };

        using var cmdProcess = Process.Start(startInfo);
        cmdProcess.WaitForExit();
        commandLine = cmdProcess.StandardOutput.ReadToEnd().Trim();
        return cmdProcess.ExitCode;
    }
else
{
    throw new PlatformNotSupportedException();
}
    }

    public static class Win32Native
    {
        [Flags]
        public enum OpenProcessDesiredAccessFlags : uint
        {
            PROCESS_VM_READ = 0x0010,
            PROCESS_QUERY_INFORMATION = 0x0400
        }

        public const uint PROCESS_BASIC_INFORMATION = 0;

        [DllImport("ntdll.dll")]
        public static extern uint NtQueryInformationProcess(
            nint ProcessHandle,
            uint ProcessInformationClass,
            nint ProcessInformation,
            uint ProcessInformationLength,
            out uint ReturnLength);

        [DllImport("kernel32.dll")]
        public static extern nint OpenProcess(
            OpenProcessDesiredAccessFlags dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            nint hProcess, nint lpBaseAddress, nint lpBuffer,
            uint nSize, out uint lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint hObject);

        [DllImport("shell32.dll", SetLastError = true,
            CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")]
        public static extern nint CommandLineToArgv(string lpCmdLine, out int pNumArgs);

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessBasicInformation
        {
            public nint Reserved1;
            public nint PebBaseAddress;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public nint[] Reserved2;

            public nint UniqueProcessId;
            public nint Reserved3;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public nint Buffer;
        }

        // This is not the real struct!
        // I faked it to get ProcessParameters address.
        // Actual struct definition:
        // https://learn.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-peb
        [StructLayout(LayoutKind.Sequential)]
        public struct PEB
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public nint[] Reserved;

            public nint ProcessParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RtlUserProcessParameters
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Reserved1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public nint[] Reserved2;

            public UnicodeString ImagePathName;
            public UnicodeString CommandLine;
        }
    }
}