using System.Drawing;
using System.IO;
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

    // The Riot Client renders the widget inside CEF, whose UA is plain Chromium — no "Edg"
    // token (WebView2's default) and no "HeadlessChrome". Matching it keeps the UA consistent
    // with the client's own challenge submissions on the same sitekey.
    private const string RiotClientCefUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    // Injected into every document before any page script runs. Normalizes the JS environment
    // so it does not advertise an embedded WebView2: stash the host bridge under a private
    // name and try to strip the detectable window.chrome.webview property (messaging falls
    // back to the stashed handle, and ultimately to window.external.notify).
    private const string HardeningJs = @"
(function(){
  try {
    window.__hostBridge = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
    if (window.chrome && window.chrome.webview) { try { delete window.chrome.webview; } catch (e) {} }
  } catch (e) {}
  try {
    Object.defineProperty(navigator, 'language',  { get: function(){ return 'en-US'; }, configurable: true });
    Object.defineProperty(navigator, 'languages', { get: function(){ return ['en-US','en']; }, configurable: true });
  } catch (e) {}
})();";

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

            var settings = _webView.CoreWebView2.Settings;
            settings.UserAgent = RiotClientCefUserAgent;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            // Applied before any document script (including the widget's) can probe the env.
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HardeningJs);

            // Tabasco tokens are validated against the page origin the widget runs on. The real
            // client runs the widget on the authenticator service_url origin, so we navigate to
            // https://<host>/challenge.html and intercept ONLY that document with our local HTML
            // (WebResourceRequested). Every other request to that origin — the widget's
            // checksiteconfig/getchallenge calls to Riot's Tabasco proxy — passes through to the
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
            var stream = new MemoryStream(html);
            var response = _webView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK",
                "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer");
            e.Response = response;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChallengeWindow] Failed to serve local challenge page: {ex.Message}", ConsoleColor.Red);
        }
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // Messages may arrive as a raw string (postToHost stringifies via the stashed
            // bridge / window.external.notify) or as JSON (native webview postMessage).
            var text = e.TryGetWebMessageAsString();
            var json = string.IsNullOrWhiteSpace(text) ? e.WebMessageAsJson : text;
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

    /// <summary>
    ///     Emits per-request, non-deterministic filler markup (offscreen canvases + a random
    ///     comment nonce) so the served document never serializes to the same bytes twice —
    ///     any host-page HTML hashing sees a fresh fingerprint on every attempt instead of
    ///     one stable value that could be allow/deny-listed. The canvases are positioned
    ///     offscreen and pointer-inert, so the widget layout and the visible UI are untouched.
    /// </summary>
    private static string BuildHtmlDecorations()
    {
        var rng = Random.Shared;
        var sb = new StringBuilder();
        sb.Append($"<!--{Guid.NewGuid():N}-->");
        var canvasCount = rng.Next(1, 5);
        for (var i = 0; i < canvasCount; i++)
        {
            var width = rng.Next(16, 257);
            var height = rng.Next(16, 257);
            var id = "c" + Guid.NewGuid().ToString("N")[..8];
            sb.Append(
                $"<canvas id='{id}' width='{width}' height='{height}' aria-hidden='true' tabindex='-1' " +
                "style='position:fixed;left:-10000px;top:-10000px;pointer-events:none;opacity:0.01'></canvas>");
        }

        return sb.ToString();
    }

    private string BuildHtml()
    {
        var rqDataJson = System.Text.Json.JsonSerializer.Serialize(_rqData ?? string.Empty);
        var siteKeyJson = System.Text.Json.JsonSerializer.Serialize(_siteKey);
        var hostJson = System.Text.Json.JsonSerializer.Serialize(_host ?? string.Empty);
        // Matches the language ("en_US") sent in the RSO start request: posixToBCP47 → "en-US".
        const string hl = "en-US";

        // Script URL identical to the client's loader i0(): base params render=explicit + onload,
        // then remaining props become query params (hl, host, recaptchacompat=off — the client
        // passes reChallengeCompat:false). Client loads the script synchronously (loadAsync:false),
        // so no async/defer attributes.
        var scriptUrl = $"https://js.hcaptcha.com/1/api.js?render=explicit&onload=hcaptchaOnLoad&hl={hl}&recaptchacompat=off";
        if (!string.IsNullOrWhiteSpace(_host))
            scriptUrl += $"&host={Uri.EscapeDataString(_host)}";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'>");
        // Never leak a referrer to the widget/proxy requests.
        sb.AppendLine("<meta name='referrer' content='no-referrer'>");
        sb.AppendLine("<style>body{background:#1e1e2e;color:#cdd6f4;font-family:Segoe UI,sans-serif;margin:0;padding:16px;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:calc(100vh - 32px);box-sizing:border-box}#status{margin-bottom:12px;font-size:14px}#checkbox{min-width:303px;min-height:78px}</style>");
        sb.AppendLine("<script>function hcaptchaOnLoad(){ window.__tabascoReady = true; }</script>");
        sb.AppendLine($"<script src='{scriptUrl}'></script>");
        sb.AppendLine("</head><body>");
        // Per-request decoy markup keeps the host-page HTML hash unique on every load.
        sb.AppendLine(BuildHtmlDecorations());
        sb.AppendLine("<div id='status'>Loading challenge…</div>");
        sb.AppendLine("<div id='checkbox'></div>");
        sb.AppendLine("<script>");
        // Host bridge that survives the hardening script's cleanup: prefers the stashed
        // webview handle, falls back to window.external.notify.
        sb.AppendLine("function postToHost(msg){");
        sb.AppendLine("  try { if (window.__hostBridge) { window.__hostBridge.postMessage(JSON.stringify(msg)); return; } } catch(e){}");
        sb.AppendLine("  try { window.external.notify(JSON.stringify(msg)); } catch(e){}");
        sb.AppendLine("}");
        sb.AppendLine("function reportError(err){ postToHost({type:'tabasco_error', error:String(err)}); }");
        sb.AppendLine("function startChallenge(){");
        sb.AppendLine("  document.getElementById('status').textContent='Solving challenge…';");
        sb.AppendLine("  try{");
        // Render opts mirror the client's renderChallenge(): sitekey, size invisible, host
        // (authenticator service_url hostname), hl, plus the standard callbacks.
        sb.AppendLine($"    var opts = {{sitekey:{siteKeyJson}, size:'invisible', hl:'{hl}',");
        sb.AppendLine("      'open-callback':function(){}, 'close-callback':function(){},");
        sb.AppendLine("      'error-callback':function(code){ reportError(code); },");
        sb.AppendLine("      'chalexpired-callback':function(){ document.getElementById('status').textContent='Challenge expired.'; },");
        sb.AppendLine("      'expired-callback':function(){ document.getElementById('status').textContent='Token expired.'; },");
        sb.AppendLine("      callback:function(token){ if (token) { postToHost({type:'tabasco_result', token:token}); document.getElementById('status').textContent='Challenge solved.'; } }};");
        sb.AppendLine($"    if ({hostJson} !== '') opts.host = {hostJson};");
        sb.AppendLine("    var widgetId = hcaptcha.render('checkbox', opts);");
        // Execute identical to the client's executeChallenge(): {rqdata, async:true}; the async
        // result is an object {response, key} — the client destructures .response.
        sb.AppendLine($"    hcaptcha.execute(widgetId, {{rqdata:{rqDataJson}, async:true}}).then(function(result){{");
        sb.AppendLine("      var token = (result && typeof result === 'object') ? result.response : result;");
        sb.AppendLine("      if (!token) {");
        sb.AppendLine("        reportError('empty response');");
        sb.AppendLine("        return;");
        sb.AppendLine("      }");
        sb.AppendLine("      postToHost({type:'tabasco_result', token:token});");
        sb.AppendLine("      document.getElementById('status').textContent='Challenge solved.';");
        sb.AppendLine("    }).catch(function(err){");
        sb.AppendLine("      reportError(err);");
        sb.AppendLine("      document.getElementById('status').textContent='Challenge failed. See log.';");
        sb.AppendLine("    });");
        sb.AppendLine("  }catch(ex){");
        sb.AppendLine("    reportError(ex);");
        sb.AppendLine("    document.getElementById('status').textContent='Error: '+String(ex);");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("function waitForReady(){");
        sb.AppendLine("  if (!(window.__tabascoReady && typeof hcaptcha !== 'undefined')) { setTimeout(waitForReady, 80 + Math.floor(Math.random()*70)); return; }");
        // Human-like start delay: never execute at a fixed offset after widget load.
        sb.AppendLine("  setTimeout(startChallenge, 420 + Math.floor(Math.random()*720));");
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
