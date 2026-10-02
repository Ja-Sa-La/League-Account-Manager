// LoopTest — standalone harness that exercises the clientless RSO login flow
// (Tabasco in ChallengeWindow + clientless complete) in a loop and reports the pass rate.
// The challenge window auto-executes the invisible challenge; no user interaction needed.
// Usage: dotnet run --project LoopTest
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;

const string Username = "Eehanelliperr";
const string Password = "2Zub86znPk";
const int TargetPasses = 100;
const int MaxAttempts = 200;
const int MaxConsecutiveFailures = 10;
var challengeTimeout = TimeSpan.FromMinutes(3);

DebugConsole.WriteLine($"[Loop] Challenge loop test: account={Username}, target={TargetPasses} passes", ConsoleColor.Green);

var ui = await StartStaDispatcherAsync();

int passes = 0, fails = 0, attempts = 0, consecutiveFails = 0, rateLimitHits = 0;
var failures = new List<string>();
var rateLimitDelay = TimeSpan.FromSeconds(60);

while (passes < TargetPasses && attempts < MaxAttempts)
{
    attempts++;
    DebugConsole.WriteLine(
        $"[Loop] ===== Attempt {attempts}: passes={passes}/{TargetPasses} fails={fails} rateLimits={rateLimitHits} =====", ConsoleColor.Gray);
    string detail;
    try
    {
        detail = await RunOnceAsync(ui, challengeTimeout);
    }
    catch (Exception ex)
    {
        detail = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
    }

    if (detail.StartsWith("PASS", StringComparison.Ordinal))
    {
        passes++;
        consecutiveFails = 0;
        rateLimitDelay = TimeSpan.FromSeconds(60);
        DebugConsole.WriteLine($"[Loop] PASS  ({detail})", ConsoleColor.Green);
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    else if (detail.Contains("user_rate_limited", StringComparison.Ordinal))
    {
        // Server-side account throttle, not a challenge failure: back off and retry.
        rateLimitHits++;
        consecutiveFails = 0;
        attempts--; // does not count as an attempt
        failures.Add(detail);
        DebugConsole.WriteLine(
            $"[Loop] RATE-LIMITED — cooling down {rateLimitDelay.TotalSeconds:0}s (hit #{rateLimitHits})", ConsoleColor.Yellow);
        if (rateLimitHits >= 15)
        {
            DebugConsole.WriteLine("[Loop] 15 rate-limit hits — account is throttled hard, aborting.", ConsoleColor.Red);
            break;
        }
        await Task.Delay(rateLimitDelay);
        rateLimitDelay = TimeSpan.FromTicks(Math.Min(rateLimitDelay.Ticks * 2, TimeSpan.FromMinutes(5).Ticks));
    }
    else
    {
        fails++;
        consecutiveFails++;
        failures.Add(detail);
        DebugConsole.WriteLine($"[Loop] FAIL  ({detail})", ConsoleColor.Red);
        if (consecutiveFails >= MaxConsecutiveFailures)
        {
            DebugConsole.WriteLine(
                $"[Loop] {MaxConsecutiveFailures} consecutive failures — aborting loop.", ConsoleColor.Red);
            break;
        }
        await Task.Delay(TimeSpan.FromSeconds(3)); // longer cooldown after a failure
    }
}

var rate = attempts == 0 ? 0 : 100.0 * passes / attempts;
DebugConsole.WriteLine(
    $"[Loop] DONE: {passes}/{attempts} passed ({rate:0.0}% success rate), {fails} failed.", ConsoleColor.Green);
foreach (var g in failures.GroupBy(f => f).OrderByDescending(g => g.Count()))
    DebugConsole.WriteLine($"[Loop]   {g.Count()}x {g.Key}", ConsoleColor.Yellow);
return rate >= 100.0 ? 0 : 1;

static async Task<Dispatcher> StartStaDispatcherAsync()
{
    var tcs = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
    var sta = new Thread(() =>
    {
        try
        {
            // Minimal WPF Application providing the three resources ChallengeWindow.xaml
            // resolves via StaticResource (avoids loading the full app Theme.xaml).
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x15));
            var panel = new Style(typeof(Border));
            panel.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x18, 0x1b, 0x1e))));
            app.Resources["SurfacePanelStyle"] = panel;
            app.Resources["SectionTitleStyle"] = new Style(typeof(TextBlock));

            tcs.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    });
    sta.SetApartmentState(ApartmentState.STA);
    sta.IsBackground = true;
    sta.Start();
    return await tcs.Task;
}

static Task<string?> SolveChallengeAsync(Dispatcher ui, string siteKey, string? rqData, string? host, TimeSpan timeout)
{
    var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    ui.InvokeAsync(() =>
    {
        try
        {
            var win = new ChallengeWindow(siteKey, rqData, host)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Topmost = false,
                ShowInTaskbar = false,
                Left = SystemParameters.WorkArea.Right - 400,
                Top = SystemParameters.WorkArea.Bottom - 440
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            var deadline = DateTime.UtcNow + timeout;
            timer.Tick += (s, e) =>
            {
                if (DateTime.UtcNow <= deadline) return;
                timer.Stop();
                try { win.Close(); } catch { /* already closing */ }
            };
            timer.Start();
            DebugConsole.WriteLine("[Loop] Challenge window shown (auto-executes, no interaction needed)…");
            win.ShowDialog(); // blocks this callback but pumps messages while the widget executes
            timer.Stop();
            done.TrySetResult(win.Token);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[Loop] Challenge window failed: {ex.Message}", ConsoleColor.Red);
            done.TrySetResult(null);
        }
    });
    return done.Task;
}

static async Task<string> RunOnceAsync(Dispatcher ui, TimeSpan challengeTimeout)
{
    var challenge = await RsoLoginService.ClientlessStartSessionAsync();
    if (!challenge.RequiresChallenge)
        return "PASS no-challenge-required";

    var token = await SolveChallengeAsync(ui, challenge.SiteKey!, challenge.RqData,
        RsoLoginService.ClientlessHost, challengeTimeout);
    if (string.IsNullOrWhiteSpace(token))
        return "FAIL no-token (window closed/timeout without solving)";

    var result = await RsoLoginService.ClientlessCompleteAuthAsync(Username, Password, remember: false, token);
    // Challenge was accepted when we receive a login token, or the flow advances to MFA.
    if (!string.IsNullOrWhiteSpace(result.LoginToken))
        return "PASS login_token received";
    if (string.Equals(result.Type, "multifactor", StringComparison.OrdinalIgnoreCase))
        return "PASS challenge accepted (multifactor next)";
    return $"FAIL complete error={result.Error ?? "(none)"} type={result.Type}";
}
