using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using League_Account_Manager.Misc;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json.Nodes;
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webView = new WebView2
            {
                DefaultBackgroundColor = Color.Transparent
            };
            WebViewHost.Child = _webView;
            await _webView.EnsureCoreWebView2Async();
            await AttachNetworkLoggingAsync();

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
    ///     Enables CDP network domain and logs every request, response and failure inside the
    ///     challenge webview so the full traffic picture is visible in the debug console.
    /// </summary>
    private async Task AttachNetworkLoggingAsync()
    {
        try
        {
            var cdp = _webView!.CoreWebView2;
            await cdp.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            var requested = cdp.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            requested.DevToolsProtocolEventReceived += (s, e) =>
            {
                try
                {
                    var node = JsonNode.Parse(e.ParameterObjectAsJson);
                    var req = node?["request"];
                    var method = req?["method"]?.GetValue<string>();
                    var url = req?["url"]?.GetValue<string>();
                    var post = req?["postData"]?.GetValue<string>();
                    var type = node?["type"]?.GetValue<string>();
                    DebugConsole.WriteLine($"[ChallengeNet] → {method} {url} [{type}]" +
                        (string.IsNullOrEmpty(post) ? "" : $" body={TruncateForLog(post)}"), ConsoleColor.DarkCyan);
                }
                catch { /* ignore malformed CDP payloads */ }
            };

            var received = cdp.GetDevToolsProtocolEventReceiver("Network.responseReceived");
            received.DevToolsProtocolEventReceived += (s, e) =>
            {
                try
                {
                    var node = JsonNode.Parse(e.ParameterObjectAsJson);
                    var resp = node?["response"];
                    var status = resp?["status"]?.GetValue<int>();
                    var url = resp?["url"]?.GetValue<string>();
                    var mime = resp?["mimeType"]?.GetValue<string>();
                    DebugConsole.WriteLine($"[ChallengeNet] ← HTTP {status} {url} [{mime}]", ConsoleColor.Cyan);
                }
                catch { /* ignore malformed CDP payloads */ }
            };

            var failed = cdp.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
            failed.DevToolsProtocolEventReceived += (s, e) =>
            {
                try
                {
                    var node = JsonNode.Parse(e.ParameterObjectAsJson);
                    var errorText = node?["errorText"]?.GetValue<string>();
                    var canceled = node?["canceled"]?.GetValue<bool>();
                    DebugConsole.WriteLine($"[ChallengeNet] ✗ FAILED: {errorText} (canceled={canceled})", ConsoleColor.Red);
                }
                catch { /* ignore malformed CDP payloads */ }
            };

            DebugConsole.WriteLine("[ChallengeWindow] CDP Network logging enabled.", ConsoleColor.DarkGray);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChallengeWindow] Network logging unavailable: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private static string TruncateForLog(string text) =>
        text.Length <= 300 ? text : text[..300] + "…";

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
            else if (type == "tabasco_error")
            {
                var err = node?["error"]?.GetValue<string>();
                DebugConsole.WriteLine($"[ChallengeWindow] Challenge error: {err}", ConsoleColor.Yellow);
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

        // Script URL identical to the client's loader i0(): base params render=explicit + onload,
        // then remaining props become query params (hl, host, recaptchacompat=off — the
        // script loads synchronously (loadAsync:false), so no async/defer attributes.
        var scriptUrl = $"https://js.hcaptcha.com/1/api.js?render=explicit&onload=hcaptchaOnLoad&hl={hl}&recaptchacompat=off";
        if (!string.IsNullOrWhiteSpace(_host))
            scriptUrl += $"&host={Uri.EscapeDataString(_host)}";

        // Random per-serve decorations: hidden canvas + random-length comment so the served
        // HTML has a unique hash on every load without touching the Tabasco API surface.
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var noise = Convert.ToHexString(RandomNumberGenerator.GetBytes(Random.Shared.Next(48, 224)));

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>body{background:#1e1e2e;color:#cdd6f4;font-family:Segoe UI,sans-serif;margin:0;padding:16px;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:calc(100vh - 32px);box-sizing:border-box}#status{margin-bottom:12px;font-size:14px}#checkbox{min-width:303px;min-height:78px}</style>");
        sb.AppendLine("<script>function hcaptchaOnLoad(){ window.__tabascoReady = true; }</script>");
        sb.AppendLine($"<script src='{scriptUrl}'></script>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<!-- r:{nonce}:{noise} -->");
        sb.AppendLine($"<canvas id='cvr{nonce[..6]}' width='24' height='24' data-n='{nonce}' style='display:none'></canvas>");
        sb.AppendLine("<div id='status'>Loading challenge…</div>");
        sb.AppendLine("<div id='checkbox'></div>");
        sb.AppendLine("<script>");
        sb.AppendLine("try{var __cv=document.querySelector('canvas[data-n]');var __cx=__cv.getContext('2d');for(var __i=0;__i<96;__i++){__cx.fillStyle='rgba('+Math.floor(Math.random()*256)+','+Math.floor(Math.random()*256)+','+Math.floor(Math.random()*256)+',0.5)';__cx.fillRect(Math.floor(Math.random()*__cv.width),Math.floor(Math.random()*__cv.height),1,1);}}catch(__e){}");
        sb.AppendLine("function waitForReady(){");
        sb.AppendLine("  if (!(window.__tabascoReady && typeof hcaptcha !== 'undefined')) { setTimeout(waitForReady, 100); return; }");
        sb.AppendLine("  document.getElementById('status').textContent='Solving challenge…';");
        sb.AppendLine("  try{");
        // Render opts mirror the client's widget render call: sitekey, size invisible, host
        // (authenticator service_url hostname), hl, plus the standard callbacks.
        sb.AppendLine($"    var opts = {{sitekey:{siteKeyJson}, size:'invisible', hl:'{hl}',");
        sb.AppendLine("      'open-callback':function(){}, 'close-callback':function(){},");
        sb.AppendLine("      'error-callback':function(code){ window.chrome.webview.postMessage({type:'tabasco_error', error:String(code)}); },");
        sb.AppendLine("      'chalexpired-callback':function(){ document.getElementById('status').textContent='Challenge expired.'; },");
        sb.AppendLine("      'expired-callback':function(){ document.getElementById('status').textContent='Token expired.'; },");
        sb.AppendLine("      callback:function(token){ if (token) { window.chrome.webview.postMessage({type:'tabasco_result', token:token}); document.getElementById('status').textContent='Challenge solved.'; } }};");
        sb.AppendLine($"    if ({hostJson} !== '') opts.host = {hostJson};");
        sb.AppendLine("    var widgetId = hcaptcha.render('checkbox', opts);");
        // Execute identical to the client's executeChallenge(): {rqdata, async:true}; the async
        // result is an object {response, key} — the client destructures .response.
        sb.AppendLine($"    hcaptcha.execute(widgetId, {{rqdata:{rqDataJson}, async:true}}).then(function(result){{");
        sb.AppendLine("      var token = (result && typeof result === 'object') ? result.response : result;");
        sb.AppendLine("      if (!token) {");
        sb.AppendLine("        window.chrome.webview.postMessage({type:'tabasco_error', error:'empty response'});");
        sb.AppendLine("        return;");
        sb.AppendLine("      }");
        sb.AppendLine("      window.chrome.webview.postMessage({type:'tabasco_result', token:token});");
        sb.AppendLine("      document.getElementById('status').textContent='Challenge solved.';");
        sb.AppendLine("    }).catch(function(err){");
        sb.AppendLine("      window.chrome.webview.postMessage({type:'tabasco_error', error:String(err)});");
        sb.AppendLine("      document.getElementById('status').textContent='Challenge failed. See log.';");
        sb.AppendLine("    });");
        sb.AppendLine("  }catch(ex){");
        sb.AppendLine("    window.chrome.webview.postMessage({type:'tabasco_error', error:String(ex)});");
        sb.AppendLine("    document.getElementById('status').textContent='Error: '+String(ex);");
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