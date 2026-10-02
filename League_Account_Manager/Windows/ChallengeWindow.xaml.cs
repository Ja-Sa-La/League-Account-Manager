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
                "Content-Type: text/html; charset=utf-8");
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
            // Messages may arrive as a raw string (postToHost stringifies the payload) or as
            // JSON (native webview postMessage with an object).
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
        sb.AppendLine("<style>body{background:#1e1e2e;color:#cdd6f4;font-family:Segoe UI,sans-serif;margin:0;padding:16px;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:calc(100vh - 32px);box-sizing:border-box}#status{margin-bottom:12px;font-size:14px}#checkbox{min-width:303px;min-height:78px}</style>");
        sb.AppendLine("<script>function hcaptchaOnLoad(){ window.__tabascoReady = true; }</script>");
        sb.AppendLine($"<script src='{scriptUrl}'></script>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<div id='status'>Loading challenge…</div>");
        sb.AppendLine("<div id='checkbox'></div>");
        sb.AppendLine("<script>");
        sb.AppendLine("function postToHost(msg){");
        sb.AppendLine("  try { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(JSON.stringify(msg)); return; } } catch(e){}");
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
        sb.AppendLine("  if (!(window.__tabascoReady && typeof hcaptcha !== 'undefined')) { setTimeout(waitForReady, 100); return; }");
        sb.AppendLine("  setTimeout(startChallenge, 500);");
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
