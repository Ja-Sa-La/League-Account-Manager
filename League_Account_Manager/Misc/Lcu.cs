using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.Misc;

internal class Lcu
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string ValorantClientPlatformHeader =
        "ew0KCSJwbGF0Zm9ybVR5cGUiOiAiUEMiLA0KCSJwbGF0Zm9ybU9TIjogIldpbmRvd3MiLA0KCSJwbGF0Zm9ybU9TVmVyc2lvbiI6ICIxMC4wLjE5MDQyLjEuMjU2LjY0Yml0IiwNCgkicGxhdGZvcm1DaGlwc2V0IjogIlVua25vd24iDQp9";

    public static Vals Riot = new() { path = "", port = "", token = "", Value = "" };
    public static Vals League;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint FindWindow(string strClassName, string strWindowName);

    public static Task<(string RiotPort, string RiotToken, string LeaguePort, string LeagueToken)> GetClientInfo()
    {
        var ingame = Process.GetProcessesByName("League of Legends");
        if (ingame.Length != 0)
            return Task.FromResult(("0", "0", "0", "0"));
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
                    if (TryGetConnectionDetails(Riot, out riotPort, out riotToken, out _))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Failed to retrieve Riot process command line for process {ProcessId}", ritoprocess.Id);
                }
        }
        else if (leagueClientProcess != null)
        {
            try
            {
                ProcessCommandLine.Retrieve(leagueClientProcess, out var value);
                SetRiotValues(leagueClientProcess, value, true);
                TryGetConnectionDetails(Riot, out riotPort, out riotToken, out _);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to retrieve League client command line for Riot connection");
            }
        }

        var leagueClientProcess2 = Process.GetProcessesByName("LeagueClientUx");
        foreach (var leagueprocess in leagueClientProcess2)
            try
            {
                ProcessCommandLine.Retrieve(leagueprocess, out var value);
                SetLeagueValues(leagueprocess, value);
                if (TryGetConnectionDetails(League, out leaguePort, out leagueToken, out _))
                    break;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to retrieve League client command line for process {ProcessId}", leagueprocess.Id);
            }

        return Task.FromResult((riotPort, riotToken, leaguePort, leagueToken));
    }

    public static async Task<dynamic> Connector(string target, string mode, string endpoint, string data,
        CancellationToken cancellationToken = default)
    {
        var ingame = Process.GetProcessesByName("League of Legends");
        if (ingame.Length != 0)
            return "";
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
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Failed to retrieve Riot process command line in Connector for process {ProcessId}", ritoprocess.Id);
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
        }
        else
        {
            var leagueClientProcess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueClientProcess.Length == 0) return 0;
            foreach (var leagueprocess in leagueClientProcess)
                try
                {
                    ProcessCommandLine.Retrieve(leagueprocess, out var value);
                    SetLeagueValues(leagueprocess, value);
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Failed to retrieve League client command line in Connector for process {ProcessId}", leagueprocess.Id);
                }
        }

        var values = target == "riot" ? Riot : League;
        if (!TryGetConnectionDetails(values, out var port, out var authToken, out var version))
            return 0;

        var clientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate |
                                     System.Net.DecompressionMethods.Brotli
        };
        var client = new HttpClient(clientHandler);
        var token = Encoding.UTF8.GetBytes("riot:" + authToken);
        SetClientHeaders(client, port, token, version);
        return await SendRequest(client, target, mode, endpoint, data, port, cancellationToken);
    }

    public static async Task<(HttpClient Client, string AccessToken, string EntitlementsToken, string Puuid, string
            IdToken)>
        CreateValorantClientAsync()
    {
        var entitlementsResponse = await Connector("riot", "get", "/entitlements/v1/token", "") as HttpResponseMessage;
        if (entitlementsResponse == null)
            throw new InvalidOperationException("Failed to get entitlements token.");

        var entitlementsBody = await entitlementsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        var entitlementsJson = JObject.Parse(entitlementsBody);
        var accessToken = entitlementsJson["accessToken"]?.ToString();
        var entitlementsToken = entitlementsJson["token"]?.ToString();
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(entitlementsToken))
            throw new InvalidOperationException("Missing entitlement or access token.");

        var authResponse =
            await Connector("riot", "get", "/riot-client-auth/v1/authorization", "") as HttpResponseMessage;
        if (authResponse == null)
            throw new InvalidOperationException("Failed to get authorization details.");

        var authBody = await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        var authJson = JObject.Parse(authBody);
        var puuid = authJson["puuid"]?.ToString();
        var idToken = authJson["idToken"]?["token"]?.ToString();
        if (string.IsNullOrWhiteSpace(puuid) || string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("Missing PUUID or id token.");

        using var versionClient = new HttpClient();
        var versionBody = await versionClient.GetStringAsync("https://valorant-api.com/v1/version")
            .ConfigureAwait(false);
        var versionJson = JObject.Parse(versionBody);
        var clientVersion = versionJson["data"]?["version"]?.ToString();
        if (string.IsNullOrWhiteSpace(clientVersion))
            throw new InvalidOperationException("Missing Valorant client version.");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-Riot-Entitlements-JWT", entitlementsToken);
        client.DefaultRequestHeaders.Add("X-Riot-ClientPlatform", ValorantClientPlatformHeader);
        client.DefaultRequestHeaders.Add("X-Riot-ClientVersion", clientVersion);
        client.DefaultRequestHeaders.Add("Accept", "*/*");

        return (client, accessToken, entitlementsToken, puuid, idToken);
    }

    private static void SetRiotValues(Process process, string value, bool isLeagueClient = false)
    {
        Riot.Value = value;
        Riot.port = showMatch(Riot.Value, isLeagueClient ? "--riotclient-app-port=(\\d*)" : "-app-port=(\\d*)");
        Riot.token = showMatch(Riot.Value,
            isLeagueClient ? "--riotclient-auth-token=([\\w-]*)" : "--remoting-auth-token=([\\w-]*)");
        Riot.path = process.MainModule?.FileName ??
                throw new InvalidOperationException("Unable to read the Riot client executable path.");
        Riot.version = FileVersionInfo.GetVersionInfo(Riot.path);
    }

    private static void SetLeagueValues(Process process, string value)
    {
        League.Value = value;
        League.port = showMatch(League.Value, "--app-port=(\\d*)");
        League.token = showMatch(League.Value, "--remoting-auth-token=([\\w-]*)");
        League.path = process.MainModule?.FileName ??
                      throw new InvalidOperationException("Unable to read the League client executable path.");
        League.version = FileVersionInfo.GetVersionInfo(League.path);
    }

    private static bool TryGetConnectionDetails(Vals values, out string port, out string token, out string version)
    {
        port = string.Empty;
        token = string.Empty;
        version = values.version?.FileVersion ?? string.Empty;

        var portParts = values.port.Split('=', 2);
        var tokenParts = values.token.Split('=', 2);
        if (portParts.Length != 2 || tokenParts.Length != 2 ||
            string.IsNullOrWhiteSpace(portParts[1]) || string.IsNullOrWhiteSpace(tokenParts[1]))
            return false;

        port = portParts[1];
        token = tokenParts[1];
        return true;
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
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    }

    private static async Task<HttpResponseMessage> SendRequest(HttpClient client, string target, string method,
        string endpoint, string data, string port, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        // Simplify URL construction
        var url = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"https://127.0.0.1:{port}{endpoint}";

        //Console.Writeline(url); // Consider removing or using a logging framework

        // Create HttpMethod based on the method argument
        var httpMethod = new HttpMethod(method.ToLowerInvariant());

        // Initialize HttpRequestMessage
        var request = new HttpRequestMessage(httpMethod, url);
        LcuRequestRecord? requestRecord = null;

        // For methods other than GET, set the content
        if (method.ToLowerInvariant() != "get")
            request.Content =
                new StringContent(string.IsNullOrEmpty(data) ? "" : data, Encoding.UTF8, "application/json");
        else if (!string.IsNullOrEmpty(data))
            // Append data as query string for GET requests
            request.RequestUri = new Uri($"{url}?{data}");

        try
        {
            var requestHeaders = FormatRequestHeaders(client, request);
            requestRecord = LcuRequestLog.Add(
                target,
                method,
                request.RequestUri?.PathAndQuery ?? endpoint,
                data,
                null,
                "Pending",
                string.Empty,
                0,
                requestHeaders: requestHeaders,
                direction: "Outgoing");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseHeaders = FormatResponseHeaders(response);
            started.Stop();
            LcuRequestLog.Update(
                requestRecord.Id,
                (int)response.StatusCode,
                response.ReasonPhrase ?? response.StatusCode.ToString(),
                responseBody,
                started.ElapsedMilliseconds,
                responseHeaders: responseHeaders);
            return response;
        }
        catch (Exception ex)
        {
            started.Stop();
            request.Dispose();
            client.Dispose();
            if (requestRecord is not null)
                LcuRequestLog.Update(requestRecord.Id, null,
                    ex is OperationCanceledException ? "Cancelled" : "Failed", string.Empty,
                    started.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private static string FormatRequestHeaders(HttpClient client, HttpRequestMessage request)
    {
        var headers = client.DefaultRequestHeaders
            .Concat(request.Headers)
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {string.Join(", ", group.SelectMany(header => header.Value))}");

        return string.Join(Environment.NewLine, headers);
    }

    private static string FormatResponseHeaders(HttpResponseMessage response)
    {
        return string.Join(Environment.NewLine, response.Headers.Concat(response.Content.Headers)
            .SelectMany(header => header.Value.Select(value => $"{header.Key}: {value}")));
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
        public FileVersionInfo? version { get; set; }
    }
}

public static class ProcessCommandLine
{
    private static bool ReadStructFromProcessMemory<TStruct>(
        nint hProcess, nint lpBaseAddress, out TStruct val) where TStruct : struct
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
            commandLine = string.Empty;
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
                                                commandLine = Marshal.PtrToStringUni(memCL) ?? string.Empty;
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
            if (cmdProcess == null)
            {
                commandLine = string.Empty;
                return -1;
            }

            cmdProcess.WaitForExit();
            commandLine = cmdProcess.StandardOutput.ReadToEnd().Trim();
            return cmdProcess.ExitCode;
        }

        throw new PlatformNotSupportedException();
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