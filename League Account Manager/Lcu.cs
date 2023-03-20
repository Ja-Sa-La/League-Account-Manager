using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace League_Account_Manager;

internal class lcu
{
    public static Vals Riot;
    public static Vals League;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint FindWindow(string strClassName, string strWindowName);

    public static async Task<dynamic> Connector(string target, string mode, string endpoint, string data)
    {
        var clientHandler = new HttpClientHandler();
        clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
        {
            return true;
        };
        var client = new HttpClient(clientHandler);

        string[] smth;
        string[] smth1;
        byte[] token;

        if (target == "riot")
        {
            var processes = Process.GetProcessesByName("RiotClientUx");
            if (processes.Length > 0)
            {
                string Value;
                ProcessCommandLine.Retrieve(processes[0], out Value);
                Riot.Value = Value;
                Riot.port = showMatch(Riot.Value, "-app-port=(\\d*)");
                Riot.token = showMatch(Riot.Value, "--remoting-auth-token=([\\w-]*)");
                Riot.path = processes[0].MainModule.FileName;
                Riot.version = FileVersionInfo.GetVersionInfo(Riot.path);
            }
            else
            {
                var processes2 = Process.GetProcessesByName("LeagueClientUx");
                string Value;
                ProcessCommandLine.Retrieve(processes2[0], out Value);
                Riot.Value = Value;
                Riot.port = showMatch(Riot.Value, "--riotclient-app-port=(\\d*)");
                Riot.token = showMatch(Riot.Value, "--riotclient-auth-token=([\\w-]*)");
                Riot.path = processes2[0].MainModule.FileName;
                Riot.version = FileVersionInfo.GetVersionInfo(Riot.path);
            }

            smth = Riot.port.Split("=");
            smth1 = Riot.token.Split("=");
            token = Encoding.UTF8.GetBytes("riot:" + smth1[1]);
            client.DefaultRequestHeaders.Add("Host", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(token));
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Access-Control-Allow-Credentials", "true");
            client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "127.0.0.1");
            client.DefaultRequestHeaders.Add("Origin", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) RiotClient/" +
                Riot.version.FileVersion + " (CEF 74) Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Riot-Source", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("sec-ch-ua", "Chromium");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?F");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Referer", "https://127.0.0.1:" + smth[1] + "/index.html");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }
        else
        {
            var processes = Process.GetProcessesByName("LeagueClientUx");
            if (processes.Length < 1) return 0;
            string Value;
            ProcessCommandLine.Retrieve(processes[0], out Value);
            League.Value = Value;
            League.port = showMatch(League.Value, "--app-port=(\\d*)");
            League.token = showMatch(League.Value, "--remoting-auth-token=([\\w-]*)");
            League.path = processes[0].MainModule.FileName;
            League.version = FileVersionInfo.GetVersionInfo(League.path);
            smth = League.port.Split("=");
            smth1 = League.token.Split("=");
            token = Encoding.UTF8.GetBytes("riot:" + smth1[1]);
            client.DefaultRequestHeaders.Add("Host", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(token));
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Origin", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("Access-Control-Allow-Credentials", "true");
            client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "127.0.0.1");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) RiotClient/" +
                League.version.FileVersion + " (CEF 74) Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Riot-Source", "127.0.0.1:" + smth[1]);
            client.DefaultRequestHeaders.Add("sec-ch-ua", "Chromium");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?F");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Referer", "https://127.0.0.1:" + smth[1] + "/index.html");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }

        HttpResponseMessage response;
        switch (mode)
        {
            case "get":
                if (data != null)
                {
                    HttpContent content = new StringContent(data, Encoding.UTF8, "application/json");
                    response = await client.GetAsync("https://127.0.0.1:" + smth[1] + endpoint);
                    client.Dispose();
                    return response;
                }
                else
                {
                    HttpContent content = new StringContent("", Encoding.UTF8, "application/json");
                    response = await client.GetAsync("https://127.0.0.1:" + smth[1] + endpoint);
                    client.Dispose();
                    return response;
                }

                ;
            case "post":
                if (data != null)
                {
                    HttpContent content = new StringContent(data, Encoding.UTF8, "application/json");
                    response = await client.PostAsync("https://127.0.0.1:" + smth[1] + endpoint, content);
                    client.Dispose();
                    return response;
                }
                else
                {
                    HttpContent content = new StringContent("", Encoding.UTF8, "application/json");
                    response = await client.PostAsync("https://127.0.0.1:" + smth[1] + endpoint, content);
                    client.Dispose();
                    return response;
                }

                ;
            case "put":
                if (data != null)
                {
                    HttpContent content = new StringContent(data, Encoding.UTF8, "application/json");
                    response = await client.PutAsync("https://127.0.0.1:" + smth[1] + endpoint, content);
                    client.Dispose();
                    return response;
                }
                else
                {
                    HttpContent content = new StringContent("", Encoding.UTF8, "application/json");
                    response = await client.PutAsync("https://127.0.0.1:" + smth[1] + endpoint, content);
                    client.Dispose();
                    return response;
                }

                ;
        }

        return "NoResponse";
    }

    private static string showMatch(string text, string expr)
    {
        var mc = Regex.Matches(text, expr);

        foreach (Match m in mc) return m.ToString();
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

    public static string ErrorToString(int error)
    {
        return new[]
        {
            "Success",
            "Failed to open process for reading",
            "Failed to query process information",
            "PEB address was null",
            "Failed to read PEB information",
            "Failed to read process parameters",
            "Failed to read command line from process"
        }[Math.Abs(error)];
    }

    public static int Retrieve(Process process, out string commandLine)
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

    public static IReadOnlyList<string> CommandLineToArgs(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return Array.Empty<string>();

        var argv = Win32Native.CommandLineToArgv(commandLine, out var argc);
        if (argv == nint.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var args = new string[argc];
            for (var i = 0; i < args.Length; ++i)
            {
                var p = Marshal.ReadIntPtr(argv, i * nint.Size);
                args[i] = Marshal.PtrToStringUni(p);
            }

            return args.ToList().AsReadOnly();
        }
        finally
        {
            Marshal.FreeHGlobal(argv);
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