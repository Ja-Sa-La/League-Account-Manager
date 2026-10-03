using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using League_Account_Manager.Windows;
using Notification.Wpf;

namespace League_Account_Manager.Misc;

/// <summary>
///     Drives the app's OWN logins: asks whether the session should persist (remember me),
///     redeems a raw RSO login token on a running or freshly started Riot Client, handles the
///     EULA loop and launches the product. Token sharing with other users (clipboard format,
///     URI scheme, minting) lives in <see cref="ProxyLoginTokenManager"/> instead.
/// </summary>
internal static class RiotLoginManager
{
    private const string ProductLeague = "league";
    private const string ProductValorant = "valorant";
    private static readonly TimeSpan LoginReadinessTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How long to wait for a deleted lifecycle session to actually disappear. The delete
    ///     returns 204 before the session is gone, and creating a new one in that window is
    ///     rejected with 409 "Session already exist".
    /// </summary>

    private static readonly TimeSpan LoginPollDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Asks whether the session should be persisted. Personal login flows honour the saved
    ///     Always/Never preference; <paramref name="alwaysPrompt"/> bypasses it so flows that
    ///     hand the token to other users (Generate token) always ask.
    /// </summary>
    public static async Task<bool?> PromptPersistLoginAsync(bool alwaysPrompt = false)
    {
        if (alwaysPrompt)
        {
            LogFlow("TOKEN", "Generated tokens are shared with other users; always asking about persist login.");
        }
        else
        {
            // Respect the configured persistent-login mode (Ask / Always / Never).
            switch (Settings.settingsloaded.PersistentLoginMode)
            {
                case PersistentLoginMode.Always:
                    LogFlow("TOKEN", "PersistentLoginMode=Always, persisting login without prompting.");
                    return true;
                case PersistentLoginMode.Never:
                    LogFlow("TOKEN", "PersistentLoginMode=Never, skipping persist without prompting.");
                    return false;
            }
        }

        if (Application.Current?.Dispatcher == null)
            return false;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Generated tokens are handed to other users; hide the "remember my choice" shortcut
            // so a one-off share answer can never overwrite the personal login preference.
            var prompt = new PersistLoginPromptWindow("Allow user to stay logged in?", !alwaysPrompt);
            prompt.ShowDialog();
            if (!alwaysPrompt && prompt.RememberChoiceSelected)
            {
                Settings.settingsloaded.PersistentLoginMode = prompt.PersistLogin
                    ? PersistentLoginMode.Always
                    : PersistentLoginMode.Never;
                Settings.Save();
                LogFlow("TOKEN",
                    $"PersistentLoginMode saved as {Settings.settingsloaded.PersistentLoginMode}.");
            }

            return prompt.PersistLogin;
        });
    }

    /// <summary>
    ///     Logs in on a freshly started Riot Client with a raw RSO login token and launches League.
    ///     <paramref name="persistLogin"/> drives the trust level requested from rso-auth.
    /// </summary>
    public static async Task<bool> UseLoginTokenAsync(string rawLoginToken, bool persistLogin = true)
    {
        try
        {
            LogFlow("League", "Token login flow started.");
            if (!await CheckLeague()) throw new Exception("League not installed");
            LogFlow("League", "Riot executable path validated.");

            LogFlow("League", "Killing existing client processes (KillLeagueFunc2).");
            Utils.KillLeagueFunc2();
            var riotProcess = Process.Start(Settings.settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            LogFlow("League", riotProcess != null
                ? $"Riot launch command executed. PID={riotProcess.Id}"
                : "Riot launch command executed. Process object is null.");

            await WaitForRiotClientProcessAsync("League");
            await WaitForRsoReadyStateAsync("League");

            LogFlow("League", "Redeeming login token via the client protocol handler.");
            if (!await RedeemLoginTokenViaProtocolAsync("League", rawLoginToken, persistLogin))
                return false;

            if (!await AuthorizeTrustedSessionAsync("League", persistLogin))
                return false;

            // The redeemed token is single use, so the trust request above only covers this run.
            // Remember-me has to be a fresh token minted from the session that is now signed in.
            if (!await PersistRememberedSessionAsync("League", persistLogin))
                return false;

            await AcceptEulaAsync("League");

            LogFlow("League", "Launching League product patchline.");
            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
            LogFlow("League", "League product launch request sent successfully.");
            LogFlow("League", "UseLoginTokenAsync finished. Success=true");

            return true;
        }
        catch (Exception ex)
        {
            LogFlow("League", $"Failed to use login token: {ex}", ConsoleColor.Red);
            return false;
        }
    }

    /// <summary>
    ///     Logs in on a freshly started Riot Client with a raw RSO login token and launches Valorant.
    ///     <paramref name="persistLogin"/> drives the trust level requested from rso-auth.
    /// </summary>
    public static async Task<bool> UseLoginTokenValorantAsync(string rawLoginToken, bool persistLogin = true)
    {
        try
        {
            LogFlow("Valorant", "Token login flow started.");
            if (!await CheckLeague()) throw new Exception("valorant not installed");
            LogFlow("Valorant", "Riot executable path validated.");

            LogFlow("Valorant", "Killing existing client processes (KillLeagueFunc2).");
            Utils.KillLeagueFunc2();
            var riotProcess = Process.Start(Settings.settingsloaded.riotPath,
                "--launch-product=valorant --launch-patchline=live");
            LogFlow("Valorant", riotProcess != null
                ? $"Riot launch command executed. PID={riotProcess.Id}"
                : "Riot launch command executed. Process object is null.");

            await WaitForRiotClientProcessAsync("Valorant");
            await WaitForRsoReadyStateAsync("Valorant");

            LogFlow("Valorant", "Redeeming login token via the client protocol handler.");
            if (!await RedeemLoginTokenViaProtocolAsync("Valorant", rawLoginToken, persistLogin))
                return false;

            if (!await AuthorizeTrustedSessionAsync("Valorant", persistLogin))
                return false;

            // The redeemed token is single use, so the trust request above only covers this run.
            // Remember-me has to be a fresh token minted from the session that is now signed in.
            if (!await PersistRememberedSessionAsync("Valorant", persistLogin))
                return false;

            await AcceptEulaAsync("Valorant");

            LogFlow("Valorant", "Launching Valorant product patchline.");
            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/valorant/patchlines/live", "");
            LogFlow("Valorant", "Valorant product launch request sent successfully.");
            LogFlow("Valorant", "UseLoginTokenValorantAsync finished. Success=true");

            return true;
        }
        catch (Exception ex)
        {
            LogFlow("Valorant", $"Failed to use login token: {ex}", ConsoleColor.Red);
            return false;
        }
    }

    /// <summary>
    ///     Redeems a raw login token on the <b>already running</b> Riot Client without restarting
    ///     it (protocol-handler redemption + product authorization). Used by the default login, which —
    ///     like the normal login — starts the client once and then completes authentication on it.
    /// </summary>
    public static async Task<bool> RedeemRawLoginTokenOnRunningClientAsync(string rawLoginToken,
        string product = ProductLeague, bool persistLogin = true)
    {
        var flow = NormalizeProduct(product) == ProductValorant ? "Valorant" : "League";

        LogFlow(flow, "Redeeming login token on the running client.");
        if (!await RedeemLoginTokenViaProtocolAsync(flow, rawLoginToken, persistLogin))
            return false;

        if (!await AuthorizeTrustedSessionAsync(flow, persistLogin))
            return false;

        return await PersistRememberedSessionAsync(flow, persistLogin);
    }

    /// <summary>
    ///     Turns a session that is already signed in into the record the client restores on its
    ///     next launch. The login token that signed the client in is spent by the redemption, so a
    ///     new one is minted from the session and exchanged for a refresh token. Does nothing when
    ///     remember-me was not chosen.
    /// </summary>
    internal static Task<bool> PersistRememberedSessionAsync(string flow) =>
        PersistRememberedSessionAsync(flow, persistLogin: true);

    private static async Task<bool> PersistRememberedSessionAsync(string flow, bool persistLogin)
    {
        if (!persistLogin)
            return true;

        LogFlow(flow, "Minting a fresh login token from the signed-in session to persist it.");
        var sessionToken = await ProxyLoginTokenManager.MintLoginTokenFromSessionAsync();
        if (sessionToken == null || !await RsoLoginService.PersistLoginTokenAsync(sessionToken))
        {
            LogFlow(flow, "Signed in, but the remembered session could not be saved.", ConsoleColor.Red);
            Notif.notificationManager.Show("Login",
                "Signed in, but the remembered session could not be saved.",
                NotificationType.Error);
            return false;
        }

        LogFlow(flow, "Remembered session saved.");
        return true;
    }

    /// <summary>
    ///     Redeems a raw RSO login token the way the client's own protocol handler does: one
    ///     <c>PUT /player-session-lifecycle/v1/login-token</c> carrying the token and the
    ///     <c>remember</c> flag, sent against the session the client already created at launch.
    ///     The login strategy is deliberately not set first — doing so makes the client try to
    ///     authenticate from command-line credentials it does not have and reject the token. The
    ///     session is also left alone: the client will not delete it, and it refuses to change
    ///     <c>persistLogin</c> on a session that already exists. The
    ///     <c>{scheme}://auth/v1/{token}</c> protocol URL takes the token alone, so it is only the
    ///     fallback for when the lifecycle plugin refuses the token. The deprecated
    ///     <c>PUT /rso-auth/v1/session/login-token</c> endpoint is not used.
    /// </summary>
    private static async Task<bool> RedeemLoginTokenViaProtocolAsync(string flow, string rawLoginToken,
        bool persistLogin)
    {
        if (await RedeemLoginTokenViaSessionLifecycleAsync(flow, rawLoginToken, persistLogin))
            return true;

        LogFlow(flow, "Session-lifecycle redemption did not stick; falling back to the protocol handler.",
            ConsoleColor.Yellow);
        return await RedeemLoginTokenViaAppCommandAsync(flow, rawLoginToken);
    }

    /// <summary>
    ///     Hands the token to the player-session-lifecycle plugin, which is what actually writes
    ///     the persisted session the client restores on next launch.
    /// </summary>
    private static async Task<bool> RedeemLoginTokenViaSessionLifecycleAsync(string flow,
        string rawLoginToken, bool persistLogin)
    {
        // This is the request the client's own riotclient://auth/v1/{token} handler builds. It
        // sends it to the session that already exists, without touching the login strategy:
        // setting the strategy to login_token makes the client look for command-line credentials,
        // fail, and answer every later token submission with 404. The remember flag is what that
        // handler reads back from the session and forwards, so it is sent explicitly here.
        var tokenPayload = JsonSerializer.Serialize(new { loginToken = rawLoginToken, remember = persistLogin });
        LogFlow(flow,
            $"Submitting login token to /player-session-lifecycle/v1/login-token (remember={persistLogin.ToString().ToLowerInvariant()}).");
        if (!await PutLifecycleAsync(flow, "/player-session-lifecycle/v1/login-token", tokenPayload))
            return false;

        // The plugin exchanges the token asynchronously. The session response carries no puuid;
        // it reports progress through "type" (authenticated / error) and "state".
        LogFlow(flow, "Waiting for the lifecycle session to authenticate...");
        var deadline = DateTimeOffset.UtcNow + LoginReadinessTimeout;
        while (true)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                LogFlow(flow, "Timed out waiting for the lifecycle session.", ConsoleColor.Yellow);
                return false;
            }

            try
            {
                var sessionResponse =
                    await Lcu.Connector("riot", "get", "/player-session-lifecycle/v1/session", "");
                if (sessionResponse is HttpResponseMessage { IsSuccessStatusCode: true } sessionHttp)
                {
                    var sessionBody = await sessionHttp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var node = JsonNode.Parse(sessionBody);
                    var type = node?["type"]?.GetValue<string>();
                    if (string.Equals(type, "authenticated", StringComparison.Ordinal))
                    {
                        var persisted = node?["persistLogin"]?.ToJsonString();
                        LogFlow(flow, $"Lifecycle session authenticated (persistLogin={persisted ?? "n/a"}).");
                        return true;
                    }

                    if (string.Equals(type, "error", StringComparison.Ordinal))
                    {
                        LogFlow(flow, $"Lifecycle session reported an error: {Truncate(sessionBody)}",
                            ConsoleColor.Yellow);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogFlow(flow, $"Lifecycle session poll failed: {ex.Message}; retrying.", ConsoleColor.Yellow);
            }

            await Task.Delay(LoginPollDelay);
        }
    }

    /// <summary>
    ///     Sends one PUT to the player-session-lifecycle plugin and reports whether it was accepted.
    /// </summary>
    private static async Task<bool> PutLifecycleAsync(string flow, string endpoint, string payload)
    {
        try
        {
            var response = await Lcu.Connector("riot", "put", endpoint, payload);
            if (response is not HttpResponseMessage http)
            {
                LogFlow(flow, $"{endpoint} returned no response.", ConsoleColor.Yellow);
                return false;
            }

            var body = await http.Content.ReadAsStringAsync().ConfigureAwait(false);
            LogFlow(flow,
                $"{endpoint} response {(int)http.StatusCode}: {(body.Length == 0 ? "(empty)" : Truncate(body))}",
                http.IsSuccessStatusCode ? ConsoleColor.White : ConsoleColor.Yellow);
            return http.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            LogFlow(flow, $"{endpoint} request failed: {ex.Message}", ConsoleColor.Yellow);
            return false;
        }
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 300 ? value : value[..300] + "…";

    /// <summary>
    ///     Fallback redemption: the token is handed to RiotClientServices.exe through the protocol
    ///     handler as <c>{scheme}://auth/v1/{login_token}</c>. This URL accepts no other arguments,
    ///     so it signs the client in but does not by itself make the session persist.
    /// </summary>
    private static async Task<bool> RedeemLoginTokenViaAppCommandAsync(string flow, string rawLoginToken)
    {
        // 1. Ask the running client which protocol scheme it listens on.
        string? scheme = null;
        try
        {
            var schemeResponse = await Lcu.Connector("riot", "post", "/riot-client-app-command/v1/uri-handler", "");
            if (schemeResponse is HttpResponseMessage { IsSuccessStatusCode: true } schemeHttp)
            {
                var schemeBody = await schemeHttp.Content.ReadAsStringAsync().ConfigureAwait(false);
                LogFlow(flow, $"uri-handler response: {(schemeBody.Length <= 200 ? schemeBody : schemeBody[..200] + "…")}");
                var node = JsonNode.Parse(schemeBody);
                scheme = node is JsonObject obj
                    ? obj.Select(kvp => kvp.Value).OfType<JsonValue>().FirstOrDefault()?.GetValue<string>()
                    : null;
            }
            else
            {
                LogFlow(flow, "uri-handler request was not successful; falling back to 'riotclient'.",
                    ConsoleColor.Yellow);
            }
        }
        catch (Exception ex)
        {
            LogFlow(flow, $"uri-handler request failed: {ex.Message}; falling back to 'riotclient'.",
                ConsoleColor.Yellow);
        }

        if (string.IsNullOrWhiteSpace(scheme))
            scheme = "riotclient";

        // 2. Hand the token to the client through the OS protocol handler. RiotClientServices.exe
        //    (--app-command="%1") consumes it natively and completes the RSO session.
        var authUrl = $"{scheme}://auth/v1/{rawLoginToken}";
        LogFlow(flow, $"Opening protocol URL for token redemption ({scheme}://auth/v1/…).");
        try
        {
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogFlow(flow, $"Failed to open protocol URL: {ex.Message}", ConsoleColor.Red);
            Notif.notificationManager.Show("Token login failed",
                "Could not hand the login token to the Riot Client.",
                NotificationType.Error);
            return false;
        }

        // 3. Wait until the client reports an authenticated RSO session.
        LogFlow(flow, "Waiting for authenticated RSO session after token redemption...");
        var deadline = DateTimeOffset.UtcNow + LoginReadinessTimeout;
        while (true)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                LogFlow(flow, "Timed out waiting for token redemption.", ConsoleColor.Red);
                Notif.notificationManager.Show("Token login failed",
                    "The Riot Client did not accept the login token in time. It may be expired — generate a new one.",
                    NotificationType.Error);
                return false;
            }

            try
            {
                var authResp = await Lcu.Connector("riot", "get", "/rso-auth/v1/authorization", "");
                if (authResp is HttpResponseMessage { IsSuccessStatusCode: true } authHttp)
                {
                    var authBody = await authHttp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var node = JsonNode.Parse(authBody);
                    var subject = node?["subject"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(subject))
                    {
                        LogFlow(flow, $"RSO session established (subject present).");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogFlow(flow, $"Authorization poll failed: {ex.Message}; retrying.", ConsoleColor.Yellow);
            }

            await Task.Delay(LoginPollDelay);
        }
    }

    /// <summary>
    ///     Marks the freshly authenticated session as always trusted so the Riot Client keeps it
    ///     across restarts (the "remember me" half that lives on the rso-auth side). When
    ///     <paramref name="persistLogin"/> is false no trust is requested and the session is
    ///     left as-is (forgotten on restart).
    /// </summary>
    private static async Task<bool> AuthorizeTrustedSessionAsync(string flow, bool persistLogin)
    {
        if (!persistLogin)
        {
            LogFlow(flow,
                "persist=false: skipping /rso-auth/v2/authorizations (session left untrusted/forgotten).");
            return true;
        }

        LogFlow(flow, "Preparing /rso-auth/v2/authorizations payload.");
        var authorizationPayload = JsonSerializer.Serialize(new
        {
            clientId = "riot-client",
            trustLevels = new[] { "always_trusted" }
        });

        LogFlow(flow, "Sending /rso-auth/v2/authorizations payload.");
        HttpResponseMessage? authorizationResponse;
        try
        {
            authorizationResponse =
                await Lcu.Connector("riot", "post", "/rso-auth/v2/authorizations", authorizationPayload);
            LogFlow(flow, "/rso-auth/v2/authorizations request completed.");
        }
        catch (Exception ex)
        {
            LogFlow(flow, $"/rso-auth/v2/authorizations failed: {ex.Message}", ConsoleColor.Red);
            return false;
        }

        if (authorizationResponse is not HttpResponseMessage { IsSuccessStatusCode: true })
        {
            LogFlow(flow, "/rso-auth/v2/authorizations was not successful.", ConsoleColor.Red);
            return false;
        }

        LogFlow(flow, "Token authentication stage completed: true");
        return true;
    }

    private static async Task AcceptEulaAsync(string flow)
    {
        LogFlow(flow, "Checking EULA acceptance state...");
        string? lastEulaStatus = null;
        var eulaDeadline = DateTimeOffset.UtcNow + LoginReadinessTimeout;
        while (true)
        {
            if (DateTimeOffset.UtcNow >= eulaDeadline)
            {
                LogFlow(flow, "Timed out waiting for EULA acceptance.", ConsoleColor.Red);
                return;
            }

            var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
            string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.Equals(status, lastEulaStatus, StringComparison.Ordinal))
            {
                lastEulaStatus = status;
                DebugConsole.WriteLine($"[RiotLogin][{flow}] EULA status changed: {status}");
            }

            if (status == "\"Accepted\"") break;
            if (status == "\"AcceptanceRequired\"")
            {
                LogFlow(flow, "EULA acceptance required; sending acceptance request.");
                await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                await Task.Delay(LoginPollDelay);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
        LogFlow(flow, "EULA accepted.");
    }

    private static async Task WaitForRiotClientProcessAsync(string flow)
    {
        LogFlow(flow, "Waiting for Riot client process to appear...");
        var num = 0;
        while (true)
        {
            if (Process.GetProcessesByName("Riot Client").Length != 0) break;

            if (Process.GetProcessesByName("RiotClientUx").Length != 0) break;

            await Task.Delay(200);
            num++;
            if (num == 200)
            {
                LogFlow(flow, "Riot client process did not appear in time.", ConsoleColor.Red);
                throw new InvalidOperationException("Riot client process did not appear in time.");
            }
        }
        LogFlow(flow, "Riot client process detected.");
    }

    private static async Task WaitForRsoReadyStateAsync(string flow)
    {
        LogFlow(flow, "Waiting for /rso-auth ready state...");
        var readyDeadline = DateTimeOffset.UtcNow + LoginReadinessTimeout;
        while (true)
        {
            if (DateTimeOffset.UtcNow >= readyDeadline)
            {
                LogFlow(flow, "Timed out waiting for Riot ready state.", ConsoleColor.Red);
                throw new InvalidOperationException("Timed out waiting for Riot ready state.");
            }

            var readyResp = await Lcu.Connector("riot", "get", "/rso-auth/configuration/v3/ready-state", "");
            if (readyResp != null)
            {
                var readyBody = await readyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    var node = JsonNode.Parse(readyBody);
                    var ready = node?["ready"]?.GetValue<bool>() ?? false;
                    if (ready)
                        break;
                }
                catch
                {
                    LogFlow(flow, "Ready-state payload parse failed; retrying.", ConsoleColor.Yellow);
                }
            }

            await Task.Delay(LoginPollDelay);
        }
        LogFlow(flow, "Riot ready state reached.");
    }

    private static async Task<bool> CheckLeague()
    {
        if (File.Exists(Settings.settingsloaded.riotPath))
            return true;
        return false;
    }

    private static string NormalizeProduct(string? product)
    {
        if (string.Equals(product, ProductValorant, StringComparison.OrdinalIgnoreCase))
            return ProductValorant;

        return ProductLeague;
    }

    private static void LogFlow(string flow, string message, ConsoleColor color = ConsoleColor.White)
    {
        DebugConsole.WriteLine($"[RiotLogin][{flow}] {message}", color);
    }
}
