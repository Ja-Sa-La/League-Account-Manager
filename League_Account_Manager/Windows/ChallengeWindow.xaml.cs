using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using League_Account_Manager.Misc;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace League_Account_Manager.Windows;

/// <summary>
///     Renders Tabasco in an embedded WebView2 and returns the solved token via <see cref="Token" />.
///     The challenge is executed with the sitekey + rqdata supplied by the RSO authenticator start
///     response, mirroring the Riot Client's own <c>executeChallenge</c> invocation.
/// </summary>
public partial class ChallengeWindow : Window
{
    private WebView2? _webView;
    private readonly string _siteKey;
    private readonly string? _rqData;
    private readonly string? _host;

    public ChallengeWindow(string siteKey, string? rqData, string? host)
    {
        InitializeComponent();
        var main = Application.Current?.MainWindow;
        if (main != null && main.IsVisible)
            Owner = main;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _siteKey = siteKey;
        _rqData = rqData ?? string.Empty;
        _host = host;
        Loaded += OnLoaded;
    }

    /// <summary>Solved challenge token, or null if the user cancelled / closed the window.</summary>
    public string? Token { get; private set; }

    /// <summary>Shows the embedded challenge and returns its token and failure state.</summary>
    public static (string? Token, bool ChallengeFailed) Solve(string siteKey, string? rqData, string? host)
    {
        var window = new ChallengeWindow(siteKey, rqData, host);
        window.ShowDialog();
        return (window.Token, window.ChallengeFailed);
    }

    /// <summary>
    ///     True when the sign-in challenge itself failed or rejected the solve. A burned browser
    ///     profile is the usual cause, so the caller deletes the profile before the next attempt.
    ///     Closing the window or solving the challenge does not set this.
    /// </summary>
    public bool ChallengeFailed { get; private set; }

    /// <summary>
    ///     The one profile every challenge window shares. WebView2 would otherwise create
    ///     "&lt;exe name&gt;.WebView2" beside the executable, so a renamed copy grows a new folder
    ///     on every login.
    /// </summary>
    public static string UserDataFolder => UserDataFolderOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "League Account Manager", "WebView2");

    /// <summary>Test hook. Production leaves this null and uses <see cref="UserDataFolder"/>.</summary>
    internal static string? UserDataFolderOverride { get; set; }

    /// <summary>
    ///     Browser identity of the installed Riot Client (Chrome 150 / Electron 43.8.4).
    ///     The challenge provider compares this with the client that normally solves on the
    ///     authenticator origin, so the embedded browser has to present the same family.
    /// </summary>
    internal const string RiotClientUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/150.0.7871.250 Electron/43.8.4 Safari/537.36";

    private static int _consecutiveLoginFailures;

    /// <summary>A completed sign-in, so the next failure starts the count again.</summary>
    public static void NoteLoginSucceeded() => _consecutiveLoginFailures = 0;

    /// <summary>
    ///     Counts a failed sign-in. The second failure in a row deletes the challenge profile,
    ///     because a profile the challenge provider has rejected keeps failing until it is replaced.
    ///     Returns true when the profile was deleted.
    /// </summary>
    public static bool NoteLoginFailed()
    {
        _consecutiveLoginFailures++;
        if (_consecutiveLoginFailures < 2)
            return false;

        _consecutiveLoginFailures = 0;
        return TryDeleteUserData();
    }

    /// <summary>
    ///     Deletes the shared WebView2 profile so the next challenge starts from a clean browser.
    ///     The challenge provider keeps reputation in that profile and eventually refuses to solve
    ///     for it. Files still locked by an open challenge window are left in place; the rest is
    ///     removed.
    /// </summary>
    public static bool TryDeleteUserData()
    {
        var folder = UserDataFolder;
        if (!Directory.Exists(folder))
            return true;

        try
        {
            Directory.Delete(folder, recursive: true);
            DebugConsole.WriteLine($"[ChallengeWindow] Deleted WebView2 profile: {folder}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var removed = 0;
            var locked = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                try
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: false);
                    else
                        File.Delete(entry);
                    removed++;
                }
                catch (Exception lockedEx) when (lockedEx is IOException or UnauthorizedAccessException)
                {
                    locked++;
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder, recursive: false);
            }
            catch (Exception leftoverEx) when (leftoverEx is IOException or UnauthorizedAccessException)
            {
                locked++;
            }

            DebugConsole.WriteLine(
                $"[ChallengeWindow] WebView2 profile reset removed {removed} entries; {locked} were still in use. {ex.Message}",
                locked == 0 ? ConsoleColor.Gray : ConsoleColor.Yellow);
            return locked == 0;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webView = new WebView2
            {
                DefaultBackgroundColor = Color.Transparent
            };
            WebViewHost.Child = _webView;
            var userDataFolder = UserDataFolder;
            Directory.CreateDirectory(userDataFolder);
            // Riot Client is Electron, whose user agent is Chrome plus "Electron/<version>".
            // A stock WebView2 agent ("Edg/") is a different browser family and is what the
            // challenge provider uses to single this window out. Riot does not disable Blink
            // automation features, so adding that switch would itself be a difference.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-features=msSmartScreenProtection,RendererCodeIntegrity,msWebView2EnableDraggableRegions",
            };
            options.EnableTrackingPrevention = false;
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await _webView.EnsureCoreWebView2Async(environment);
            var settings = _webView.CoreWebView2.Settings;
            settings.UserAgent = RiotClientUserAgent;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            // The page has to report a solved token, but window.chrome.webview only exists in
            // WebView2. Keep that object out of the document and let the page call a plain function.
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__lamReport=function(message){window.chrome.webview.postMessage(message);};" +
                "try{delete window.chrome.webview;}catch(e){}");
            // Setting UserAgent clears Sec-CH-UA and navigator.userAgentData. Chrome 150 still
            // reports a Chromium brand there, so restore that instead of leaving Edge's data empty.
            await ApplyRiotClientHintsAsync();
            // Network.enable attaches DevTools before the widget loads. The challenge provider
            // can see that debugger, and Riot Client does not attach one, so leave it off.

            // Tabasco tokens are validated against the page origin the widget runs on. The real
            // client runs the widget on the authenticator service_url origin, so we navigate to
            // https://<host>/challenge.html and intercept ONLY that document with our local HTML
            // (WebResourceRequested). Every other request to that origin — the widget's
            // checksiteconfig/challenge-fetch calls to Riot's Tabasco proxy — passes through to the
            // real server, keeping us identical to the Riot Client.
            if (string.IsNullOrWhiteSpace(_host))
                throw new Exception("Authenticator host not resolved; cannot bind challenge to correct origin.");

            var pageUri = new Uri($"https://{_host}/challenge.html");
            _webView.CoreWebView2.AddWebResourceRequestedFilter(
                pageUri.ToString(), CoreWebView2WebResourceContext.Document);
            _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _webView.CoreWebView2.Navigate(pageUri.ToString());
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChallengeWindow] Failed to initialize WebView2: {ex.Message}", ConsoleColor.Red);
            DialogResult = false;
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var html = Encoding.UTF8.GetBytes(BuildHtml());
            var hash = Convert.ToHexString(SHA256.HashData(html));
            DebugConsole.WriteLine($"[ChallengeWindow] Served challenge HTML: {html.Length} bytes, sha256={hash[..16]}…", ConsoleColor.DarkGray);
            var stream = new MemoryStream(html);
            var response = _webView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
            e.Response = response;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChallengeWindow] Failed to serve local challenge page: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    ///     Restores the client hints that <see cref="CoreWebView2Settings.UserAgent"/> clears.
    ///     Riot Client is Chromium 150 branded as Chrome, not Microsoft Edge.
    /// </summary>
    private async Task ApplyRiotClientHintsAsync()
    {
        try
        {
            const string metadata = """
                {
                  "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.7871.250 Electron/43.8.4 Safari/537.36",
                  "acceptLanguage": "en-US,en",
                  "platform": "Windows",
                  "userAgentMetadata": {
                    "brands": [
                      { "brand": "Not;A=Brand", "version": "99" },
                      { "brand": "Chromium", "version": "150" },
                      { "brand": "Google Chrome", "version": "150" }
                    ],
                    "fullVersionList": [
                      { "brand": "Not;A=Brand", "version": "99.0.0.0" },
                      { "brand": "Chromium", "version": "150.0.7871.250" },
                      { "brand": "Google Chrome", "version": "150.0.7871.250" }
                    ],
                    "fullVersion": "150.0.7871.250",
                    "platform": "Windows",
                    "platformVersion": "15.0.0",
                    "architecture": "x86",
                    "model": "",
                    "mobile": false,
                    "bitness": "64",
                    "wow64": false
                  }
                }
                """;
            await _webView!.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Emulation.setUserAgentOverride", metadata);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine(
                $"[ChallengeWindow] Could not apply Riot client hints: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            // {"type":"tabasco_result","token":"..."} or {"type":"tabasco_error","error":"..."}
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            var type = node?["type"]?.GetValue<string>();
            if (type == "tabasco_result")
            {
                Token = node?["token"]?.GetValue<string>();
                DialogResult = !string.IsNullOrWhiteSpace(Token);
            }
            else if (type is "tabasco_closed" or "tabasco_expired")
            {
                // Riot treats a dismissed or expired challenge as a user outcome, not a rejected
                // browser. Closing the window lets the caller start a fresh challenge.
                DebugConsole.WriteLine($"[ChallengeWindow] Challenge {type}.", ConsoleColor.DarkGray);
                DialogResult = false;
            }
            else if (type == "tabasco_error")
            {
                var err = node?["error"]?.GetValue<string>();
                ChallengeFailed = true;
                DebugConsole.WriteLine($"[ChallengeWindow] Challenge error: {err}", ConsoleColor.Yellow);
                DialogResult = false;
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChallengeWindow] WebMessage parse failed: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private string BuildHtml()
    {
        var rqDataJson = System.Text.Json.JsonSerializer.Serialize(_rqData ?? string.Empty);
        var siteKeyJson = System.Text.Json.JsonSerializer.Serialize(_siteKey);
        var hostJson = System.Text.Json.JsonSerializer.Serialize(_host ?? string.Empty);
        // Matches the language ("en_US") sent in the RSO start request: posixToBCP47 → "en-US".
        const string hl = "en-US";

        // The client's React wrapper loads api.js asynchronously with render=explicit and
        // recaptchacompat=off, then renders an invisible widget. A unique comment, hidden
        // canvas, or status text is not part of that page and changes the document fingerprint.
        var scriptUrl = "https://js.hcaptcha.com/1/api.js?render=explicit&onload=hcaptchaOnLoad&hl=" +
                        hl + "&recaptchacompat=off";
        if (!string.IsNullOrWhiteSpace(_host))
            scriptUrl += "&host=" + Uri.EscapeDataString(_host);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        // Riot Client's document background is #121212. The challenge prompt itself uses the
        // white theme passed to hcaptcha.render below, not a dark widget theme.
        sb.AppendLine("<style>html,body{background:#121212;margin:0;height:100%}#checkbox{position:absolute;width:0;height:0;overflow:hidden}</style>");
        sb.AppendLine("<script>function hcaptchaOnLoad(){window.__tabascoReady=true;}</script>");
        sb.AppendLine($"<script src=\"{scriptUrl}\" async></script>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<div id=\"checkbox\"></div>");
        sb.AppendLine("<script>");
        sb.AppendLine("function waitForReady(){");
        sb.AppendLine("  if (!(window.__tabascoReady && typeof hcaptcha !== 'undefined')) { setTimeout(waitForReady, 50); return; }");
        sb.AppendLine("  try{");
        sb.AppendLine($"    var opts = {{sitekey:{siteKeyJson}, size:'invisible', hl:'{hl}',");
        sb.AppendLine("      theme:{component:{prompt:{main:{fill:'#ffffff',border:'#ffffff',text:'#000000'}}}},");
        sb.AppendLine("      'open-callback':function(){},");
        sb.AppendLine("      'close-callback':function(){ window.__lamReport({type:'tabasco_closed'}); },");
        sb.AppendLine("      'error-callback':function(code){ window.__lamReport({type:'tabasco_error', error:String(code)}); },");
        sb.AppendLine("      'chalexpired-callback':function(){ window.__lamReport({type:'tabasco_expired'}); },");
        sb.AppendLine("      'expired-callback':function(){ window.__lamReport({type:'tabasco_expired'}); },");
        sb.AppendLine("      callback:function(token){ if (token) window.__lamReport({type:'tabasco_result', token:token}); }};");
        sb.AppendLine($"    if ({hostJson} !== '') opts.host = {hostJson};");
        sb.AppendLine("    var widgetId = hcaptcha.render('checkbox', opts);");
        // Without async:true, execute() returns undefined and the promise chain throws
        // "Cannot read properties of undefined (reading 'then')", which closes the window.
        sb.AppendLine($"    var pending = hcaptcha.execute(widgetId, {{rqdata:{rqDataJson}, async:true}});");
        sb.AppendLine("    if (!pending || typeof pending.then !== 'function') return;");
        sb.AppendLine("    pending.then(function(result){");
        sb.AppendLine("      var token = (result && typeof result === 'object') ? result.response : result;");
        sb.AppendLine("      if (token) window.__lamReport({type:'tabasco_result', token:token});");
        sb.AppendLine("    }).catch(function(err){");
        sb.AppendLine("      window.__lamReport({type:'tabasco_error', error:String(err)});");
        sb.AppendLine("    });");
        sb.AppendLine("  }catch(ex){");
        sb.AppendLine("    window.__lamReport({type:'tabasco_error', error:String(ex)});");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("waitForReady();");
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}