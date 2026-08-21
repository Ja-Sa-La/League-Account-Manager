using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Button = System.Windows.Controls.Button;

namespace League_Account_Manager.views;

public partial class Autolobby : Page
{
    private const int ChampionActionPollIntervalMs = 200;
    private const int ChampionActionLockInThresholdMs = 1250;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CancellationTokenSource _pageLifetimeCts = new();
    private bool _backgroundWorkersStarted;

    private readonly List<IconData> listChamps = new();
    private readonly ConcurrentDictionary<string, ToggleTaskInfo> toggles = new();
    private string _lastQueuePhase = string.Empty;
    private string _lastTimerPhase = string.Empty;

    private Chat? champSelect;
    private JObject? champselectaction;
    private JObject? champselectJObject;
    private JObject? ChampselectTeamJObject;
    private JObject? queueJObject;

    private bool sentmsg;

    public Autolobby()
    {
        InitializeComponent();

        Loaded += Autolobby_Loaded;
    }

    private void Autolobby_Loaded(object sender, RoutedEventArgs e)
    {
        if (_backgroundWorkersStarted)
            return;

        _backgroundWorkersStarted = true;
        var token = _pageLifetimeCts.Token;

        Task.Run(() => BackgroundDataFunction1(token), token);
        Task.Run(() => BackgroundDataFunction2(token), token);
        Task.Run(() => LoadBuyableData(token), token);
    }

    private void Log(string message)
    {
        var formatted = $"[Autolobby] {message}";
        DebugConsole.WriteLine(formatted);
        _logger.Info(message);
    }

    private void LogResponse(string name, string body, int maxConsoleLength = 800)
    {
        if (string.IsNullOrEmpty(body))
        {
            Log($"{name}: <empty>");
            return;
        }

        // Write full payload to debug log
        _logger.Debug($"{name}: {body}");

        // Trim console/info output to keep readability
        if (body.Length > maxConsoleLength)
            Log($"{name} (truncated {maxConsoleLength}/{body.Length} chars): {body[..maxConsoleLength]}...");
        else
            Log($"{name}: {body}");
    }

    private bool AnyFeatureEnabled()
    {
        return toggles.Any(t => t.Value.Running);
    }

    private void ToggleTask(string taskName, Func<CancellationToken, Task> taskFunc, object sender)
    {
        if (sender is not Button button)
            return;

        if (!toggles.ContainsKey(taskName))
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => taskFunc(cts.Token), cts.Token);

            toggles[taskName] = new ToggleTaskInfo
            {
                Running = true,
                Task = task,
                Cts = cts
            };

            button.Content = $"Disable {taskName}";
            Log($"Enabled {taskName}");
            return;
        }

        var info = toggles[taskName];

        if (info.Running)
        {
            info.Running = false;
            info.Cts.Cancel();
            button.Content = $"Enable {taskName}";
            Log($"Disabled {taskName}");
        }
        else
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => taskFunc(cts.Token), cts.Token);

            toggles[taskName] = new ToggleTaskInfo
            {
                Running = true,
                Task = task,
                Cts = cts
            };

            button.Content = $"Disable {taskName}";
            Log($"Enabled {taskName}");
        }
    }

    // =====================================================
    // LOAD CHAMPION DATA
    // =====================================================

    private async Task LoadBuyableData(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            LogResponse("Summoner info", responseBody);

            var summonerdata = JObject.Parse(responseBody);

            ct.ThrowIfCancellationRequested();
            resp = await Lcu.Connector("league", "get",
                $"/lol-champions/v1/inventories/{(string)summonerdata["summonerId"]}/champions-minimal", "");

            responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            LogResponse("Champion inventory", responseBody);

            var champList = JArray.Parse(responseBody);
            Log($"Loaded {champList.Count} champions from inventory.");

            listChamps.Clear();

            foreach (var champ in champList)
            {
                var name = champ["name"]?.ToString();
                var id = champ["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                    continue;

                listChamps.Add(new IconData
                {
                    Name = name!,
                    ID = id!
                });
            }

            await Dispatcher.InvokeAsync(() =>
            {
                blindPickChampion.OriginalItemsSource = listChamps;
                topPickChampion.OriginalItemsSource = listChamps;
                junglePickChampion.OriginalItemsSource = listChamps;
                midPickChampion.OriginalItemsSource = listChamps;
                botPickChampion.OriginalItemsSource = listChamps;
                supportPickChampion.OriginalItemsSource = listChamps;
                ban1Champion.OriginalItemsSource = listChamps;
                ban2Champion.OriginalItemsSource = listChamps;
                ban3Champion.OriginalItemsSource = listChamps;
            }, System.Windows.Threading.DispatcherPriority.Normal, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading champion data");
        }
    }

    // =====================================================
    // BACKGROUND LOOPS (RUN FOREVER)
    // =====================================================

    private async Task BackgroundDataFunction1(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (AnyFeatureEnabled() && queueJObject != null &&
                    queueJObject.TryGetValue("phase", out var phaseToken))
                {
                    var phase = phaseToken.ToString();

                    if (phase == "ChampSelect" || phase == "ReadyCheck")
                    {
                        var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
                        var sessionBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        LogResponse("Champ select session", sessionBody);
                        champselectJObject = JObject.Parse(sessionBody);
                        Log("Fetched champ select session.");

                        champselectaction = null;
                        ChampselectTeamJObject = null;

                        if (champselectJObject.TryGetValue("actions", out var actionsToken) &&
                            actionsToken is JArray actionsArray)
                            foreach (var actionGroup in actionsArray)
                            {
                                if (actionGroup is not JArray innerArray)
                                    continue;

                                foreach (var act in innerArray)
                                {
                                    if (act is not JObject actionObj)
                                        continue;

                                    var isInProgress = actionObj["isInProgress"]?.Value<bool>() ?? false;
                                    var actorCellId = actionObj["actorCellId"]?.ToString();
                                    var localCellId = champselectJObject["localPlayerCellId"]?.ToString();
                                    var timerPhase = champselectJObject["timer"]?["phase"]?.ToString();

                                    if (!string.IsNullOrEmpty(timerPhase) && !string.Equals(timerPhase, _lastTimerPhase,
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        _lastTimerPhase = timerPhase;
                                        Log($"Champ Select timer phase changed: {_lastTimerPhase}");
                                    }

                                    if (isInProgress &&
                                        actorCellId == localCellId &&
                                        timerPhase != "PLANNING")
                                    {
                                        champselectaction = actionObj;
                                        break;
                                    }
                                }

                                if (champselectaction != null)
                                    break;
                            }

                        if (champselectJObject.TryGetValue("myTeam", out var myTeamToken) &&
                            myTeamToken is JArray myTeamArray)
                        {
                            var localCellId = champselectJObject["localPlayerCellId"]?.ToString();

                            foreach (var t in myTeamArray)
                            {
                                if (t is not JObject teamObj)
                                    continue;

                                if (teamObj["cellId"]?.ToString() == localCellId)
                                {
                                    ChampselectTeamJObject = teamObj;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        champselectaction = null;
                        champselectJObject = null;
                        ChampselectTeamJObject = null;
                    }
                }
                else
                {
                    champselectaction = null;
                    champselectJObject = null;
                    ChampselectTeamJObject = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in BackgroundDataFunction1");
            }

            await Task.Delay(queueJObject?["phase"]?.ToString() == "ChampSelect"
                ? ChampionActionPollIntervalMs
                : 1000, ct);
        }
    }

    private async Task BackgroundDataFunction2(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (AnyFeatureEnabled())
                {
                    var resp = await Lcu.Connector("league", "get", "/lol-gameflow/v1/session", "");
                    var queueBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LogResponse("Gameflow session", queueBody);
                    queueJObject = JObject.Parse(queueBody);

                    var phase = queueJObject?["phase"]?.ToString();
                    if (!string.IsNullOrEmpty(phase) &&
                        !string.Equals(phase, _lastQueuePhase, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastQueuePhase = phase;
                        Log($"Queue phase changed: {_lastQueuePhase}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in BackgroundDataFunction2");
            }

            await Task.Delay(1000, ct);
        }
    }

    // =====================================================
    // BUTTON TOGGLES
    // =====================================================

    private void OnToggleAutoAcceptClick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptQueue", StartAutoAcceptTask, sender);
    }

    private void OnToggleAutoPickClick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptPick", StartAutoPickTask, sender);
    }

    private void OnToggleAutoBanClick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptBan", StartAutoBanTask, sender);
    }

    private void OnToggleAutoMessageClick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptMessage", StartAutoMessageTask, sender);
    }

    private void OnToggleAutoMuteClick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoMuteAll", StartAutoMuteTask, sender);
    }

    // =====================================================
    // AUTO ACCEPT
    // =====================================================

    private async Task StartAutoAcceptTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (queueJObject != null &&
                    queueJObject.TryGetValue("phase", out var phaseToken) &&
                    phaseToken.ToString() == "ReadyCheck")
                    await Lcu.Connector("league", "post", "/lol-matchmaking/v1/ready-check/accept", "");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoAccept");
            }

            await Task.Delay(3000, ct);
        }
    }

    // =====================================================
    // AUTO MUTE TEAMMATES
    // =====================================================

    private readonly HashSet<string> _mutedThisChampSelect = new();
    private string _muteSessionKey = string.Empty;

    private async Task StartAutoMuteTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var session = champselectJObject;
                if (session != null &&
                    session.TryGetValue("myTeam", out var myTeamToken) &&
                    myTeamToken is JArray myTeam)
                {
                    // new champ select -> forget who we muted last time
                    var sessionKey = session["gameId"]?.ToString() ?? "";
                    if (sessionKey != _muteSessionKey)
                    {
                        _muteSessionKey = sessionKey;
                        _mutedThisChampSelect.Clear();
                    }

                    // authoritative muted list, so we never toggle someone back off
                    var alreadyMuted = new HashSet<string>();
                    try
                    {
                        var mutedResp = await Lcu.Connector("league", "get",
                            "/lol-champ-select/v1/muted-players", "");
                        var mutedBody = await mutedResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        foreach (var m in JArray.Parse(mutedBody))
                        {
                            var p = m["puuid"]?.ToString();
                            var op = m["obfuscatedPuuid"]?.ToString();
                            if (!string.IsNullOrEmpty(p)) alreadyMuted.Add(p);
                            if (!string.IsNullOrEmpty(op)) alreadyMuted.Add(op);
                        }
                    }
                    catch
                    {
                        // list unavailable outside champ select; fall back to local tracking
                    }

                    var localCellId = session["localPlayerCellId"]?.ToString();

                    foreach (var t in myTeam)
                    {
                        if (t is not JObject member)
                            continue;
                        if (member["cellId"]?.ToString() == localCellId)
                            continue; // never mute ourselves

                        var puuid = member["puuid"]?.ToString() ?? "";
                        var obfuscatedPuuid = member["obfuscatedPuuid"]?.ToString() ?? "";
                        var summonerId = member["summonerId"]?.Value<long>() ?? 0;

                        // bots have no identity to mute
                        if (string.IsNullOrEmpty(puuid) && string.IsNullOrEmpty(obfuscatedPuuid) && summonerId == 0)
                            continue;

                        var key = !string.IsNullOrEmpty(puuid) ? puuid
                            : !string.IsNullOrEmpty(obfuscatedPuuid) ? obfuscatedPuuid
                            : "cell:" + member["cellId"];

                        if (_mutedThisChampSelect.Contains(key))
                            continue;
                        if (alreadyMuted.Contains(puuid) || alreadyMuted.Contains(obfuscatedPuuid))
                        {
                            _mutedThisChampSelect.Add(key);
                            continue;
                        }

                        var body = new JObject
                        {
                            ["puuid"] = puuid,
                            ["summonerId"] = summonerId,
                            ["obfuscatedPuuid"] = obfuscatedPuuid,
                            ["obfuscatedSummonerId"] = member["obfuscatedSummonerId"]?.Value<long>() ?? 0
                        };
                        var resp = await Lcu.Connector("league", "post",
                                           "/lol-champ-select/v1/toggle-player-muted",
                                           body.ToString(Newtonsoft.Json.Formatting.None)) as HttpResponseMessage;

                        if (resp != null && resp.IsSuccessStatusCode)
                        {
                            _mutedThisChampSelect.Add(key);
                            Log($"AutoMute: muted teammate in cell {member["cellId"]}");
                        }
                        else
                        {
                            Log($"AutoMute: mute request failed for cell {member["cellId"]} " +
                                $"({resp?.StatusCode.ToString() ?? "no response"})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoMute");
            }

            await Task.Delay(2000, ct);
        }
    }

    // =====================================================
    // PICK FIXED
    // =====================================================

    private async Task StartAutoPickTask(CancellationToken ct)
    {
        await RunChampionActionTask("pick", getpickchampid, ct);
    }

    private async Task<string> getpickchampid()
    {
        try
        {
            if (ChampselectTeamJObject == null || champselectJObject == null)
                return "";

            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/pickable-champion-ids", "");
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            LogResponse("Pickable champions", body);

            var pickableToken = JToken.Parse(body);
            var pickableArray = pickableToken as JArray;
            if (pickableArray == null)
            {
                Log("Pickable champions response was not an array; skipping pick.");
                return "";
            }

            var pickableIds = pickableArray.Values<int>().ToHashSet();
            Log($"Pickable champions count: {pickableIds.Count}");

            var position = ChampselectTeamJObject["assignedPosition"]?.ToString()?.ToUpper() ?? "";

            var positions = new List<string> { position, "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

            string blind = "", top = "", jungle = "", mid = "", bot = "", supp = "";

            await Dispatcher.InvokeAsync(() =>
            {
                blind = blindPickChampion.Text;
                top = topPickChampion.Text;
                jungle = junglePickChampion.Text;
                mid = midPickChampion.Text;
                bot = botPickChampion.Text;
                supp = supportPickChampion.Text;
            });

            foreach (var pos in positions)
            {
                var champName = pos switch
                {
                    "TOP" => top,
                    "JUNGLE" => jungle,
                    "MIDDLE" => mid,
                    "BOTTOM" => bot,
                    "UTILITY" => supp,
                    _ => blind
                };

                if (string.IsNullOrWhiteSpace(champName))
                    continue;

                var champ = listChamps.FirstOrDefault(c => c.Name == champName);

                if (champ?.ID == null)
                    continue;

                if (!int.TryParse(champ.ID, out var champIdInt))
                    continue;

                if (!ChampSelectActionTiming.IsChampionAvailable(pickableIds, champIdInt))
                    continue;

                var myBans = champselectJObject["bans"]?["myTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();
                var theirBans = champselectJObject["bans"]?["theirTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();

                if (myBans.Contains(champIdInt) || theirBans.Contains(champIdInt))
                    continue;

                Log($"Auto-pick selecting {champ.Name} (ID {champIdInt})");
                return champ.ID;
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in getpickchampid");
            Log("Error while resolving auto-pick champion; see log for details.");
            return "";
        }
    }

    // =====================================================
    // BAN FIXED
    // =====================================================

    private async Task StartAutoBanTask(CancellationToken ct)
    {
        await RunChampionActionTask("ban", getbanchampid, ct);
    }

    private async Task RunChampionActionTask(string actionType, Func<Task<string>> resolveChampionId,
        CancellationToken ct)
    {
        string? hoveredActionId = null;
        string? hoveredChampionId = null;
        string? resolvedActionId = null;
        string? resolvedChampionId = null;
        string? deadlineActionId = null;
        DateTimeOffset? actionDeadline = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var action = champselectaction;
                if (action == null ||
                    !string.Equals(action["type"]?.ToString(), actionType, StringComparison.OrdinalIgnoreCase) ||
                    (action["completed"]?.Value<bool>() ?? false))
                {
                    hoveredActionId = null;
                    hoveredChampionId = null;
                    resolvedActionId = null;
                    resolvedChampionId = null;
                    deadlineActionId = null;
                    actionDeadline = null;
                    await Task.Delay(ChampionActionPollIntervalMs, ct);
                    continue;
                }

                var actionId = action["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    await Task.Delay(ChampionActionPollIntervalMs, ct);
                    continue;
                }

                if (deadlineActionId != actionId)
                {
                    deadlineActionId = actionId;
                    actionDeadline = ChampSelectActionTiming.CreateDeadline(champselectJObject?["timer"],
                        DateTimeOffset.UtcNow);
                }

                if (resolvedActionId != actionId || string.IsNullOrWhiteSpace(resolvedChampionId))
                {
                    resolvedChampionId = await resolveChampionId();
                    resolvedActionId = actionId;
                }

                if (string.IsNullOrWhiteSpace(resolvedChampionId))
                {
                    await Task.Delay(ChampionActionPollIntervalMs, ct);
                    continue;
                }

                var championId = resolvedChampionId;

                if (hoveredActionId != actionId || hoveredChampionId != championId)
                {
                    var hovered = await PatchChampionAction(actionId, championId, false, actionType);
                    if (hovered)
                    {
                        hoveredActionId = actionId;
                        hoveredChampionId = championId;
                        Log($"Auto-{actionType} hovering champion ID {championId} for action {actionId}.");
                    }
                    else
                    {
                        resolvedActionId = null;
                        resolvedChampionId = null;
                    }
                }

                var timer = champselectJObject?["timer"];
                if (ChampSelectActionTiming.ShouldComplete(timer, actionDeadline, DateTimeOffset.UtcNow,
                    ChampionActionLockInThresholdMs))
                {
                    var completed = await PatchChampionAction(actionId, championId, true, actionType);
                    if (completed)
                    {
                        Log($"Auto-{actionType} completed champion ID {championId} for action {actionId}.");
                    }
                    else
                    {
                        hoveredActionId = null;
                        hoveredChampionId = null;
                        resolvedActionId = null;
                        resolvedChampionId = null;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in Auto{actionType}");
            }

            await Task.Delay(ChampionActionPollIntervalMs, ct);
        }
    }

    private async Task<bool> PatchChampionAction(string actionId, string championId, bool completed,
        string actionType)
    {
        var body = new JObject
        {
            ["completed"] = completed,
            ["championId"] = int.Parse(championId)
        }.ToString(Newtonsoft.Json.Formatting.None);

        var response = await Lcu.Connector("league", "patch",
            $"/lol-champ-select/v1/session/actions/{actionId}", body);

        if (response.IsSuccessStatusCode)
            return true;

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Log($"Auto-{actionType} {(completed ? "completion" : "hover")} failed for action {actionId}: " +
            $"{(int)response.StatusCode} {response.StatusCode}. {responseBody}");
        return false;
    }

    private async Task<string> getbanchampid()
    {
        try
        {
            if (champselectJObject == null)
                return "";

            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/bannable-champion-ids", "");
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            LogResponse("Bannable champions", body);

            var bannableToken = JToken.Parse(body);
            var bannableArray = bannableToken as JArray;
            if (bannableArray == null)
            {
                Log("Bannable champions response was not an array; skipping ban.");
                return "";
            }

            var bannableIds = bannableArray.Values<int>().ToHashSet();
            Log($"Bannable champions count: {bannableIds.Count}");

            string ban1 = "", ban2 = "", ban3 = "";

            await Dispatcher.InvokeAsync(() =>
            {
                ban1 = ban1Champion.Text;
                ban2 = ban2Champion.Text;
                ban3 = ban3Champion.Text;
            });

            var banNames = new[] { ban1, ban2, ban3 };

            foreach (var championName in banNames)
            {
                if (string.IsNullOrWhiteSpace(championName))
                    continue;

                var champ = listChamps.FirstOrDefault(c => c.Name == championName);

                if (champ?.ID == null)
                    continue;

                if (!int.TryParse(champ.ID, out var champIdInt))
                    continue;

                if (!ChampSelectActionTiming.IsChampionAvailable(bannableIds, champIdInt))
                    continue;

                var myBans = champselectJObject["bans"]?["myTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();
                var theirBans = champselectJObject["bans"]?["theirTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();

                if (myBans.Contains(champIdInt) || theirBans.Contains(champIdInt))
                    continue;

                Log($"Auto-ban selecting {champ.Name} (ID {champIdInt})");
                return champ.ID;
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in getbanchampid");
            Log("Error while resolving auto-ban champion; see log for details.");
            return "";
        }
    }

    // =====================================================
    // AUTO MESSAGE FIXED
    // =====================================================

    private async Task StartAutoMessageTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (champselectaction != null && champselectaction.ContainsKey("type") && !sentmsg)
                {
                    await Task.Delay(1000, ct);

                    var msg = "";
                    await Dispatcher.InvokeAsync(() => msg = MessageContainer.Text);

                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        await sendmsg(msg);
                        sentmsg = true;
                    }
                }
                else if (queueJObject == null ||
                         (queueJObject.TryGetValue("phase", out var phaseToken) &&
                          phaseToken.ToString() != "ChampSelect"))
                {
                    sentmsg = false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoMessage");
            }

            await Task.Delay(1000, ct);
        }
    }

    private async Task sendmsg(string msg)
    {
        try
        {
            var response = await Lcu.Connector("league", "get", "/lol-chat/v1/conversations", "");
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Explicitly typed variable
            List<Chat> chats = JArray.Parse(responseContent).ToObject<List<Chat>>() ?? new List<Chat>();

            // Now FirstOrDefault works safely
            champSelect = chats.FirstOrDefault(c => c.type == "championSelect");

            if (champSelect == null)
                return;

            var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var summonerinfo = JObject.Parse(responseBody2);

            string postdata =
                "{\"type\":\"chat\",\"fromId\":\"" + champSelect.id +
                "\",\"fromSummonerId\":" + summonerinfo["accountId"] +
                ",\"isHistorical\":false,\"timestamp\":\"" +
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
                "\",\"body\":\"" + msg.Replace("\"", "\\\"") + "\"}";

            await Lcu.Connector("league", "post",
                "/lol-chat/v1/conversations/" + champSelect.pid + "/messages",
                postdata);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error sending message");
        }
    }

    // =====================================================
    // TOGGLE SYSTEM
    // =====================================================

    private class ToggleTaskInfo
    {
        public bool Running { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
        public CancellationTokenSource Cts { get; set; } = new();
    }

    // =====================================================
    // DATA CLASSES
    // =====================================================

    public class IconData
    {
        public string Name { get; set; } = string.Empty;
        public string ID { get; set; } = string.Empty;
    }

    public class Chat
    {
        public string gameName { get; set; } = string.Empty;
        public string gameTag { get; set; } = string.Empty;
        public string id { get; set; } = string.Empty;
        public string inviterId { get; set; } = string.Empty;
        public bool isMuted { get; set; }
        public Lastmessage lastMessage { get; set; } = new();
        public Mucjwtdto mucJwtDto { get; set; } = new();
        public string name { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public string pid { get; set; } = string.Empty;
        public string targetRegion { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public long unreadMessageCount { get; set; }
    }

    public class Mucjwtdto
    {
        public string channelClaim { get; set; } = string.Empty;
        public string domain { get; set; } = string.Empty;
        public string jwt { get; set; } = string.Empty;
        public string targetRegion { get; set; } = string.Empty;
    }

    public class Lastmessage
    {
        public string body { get; set; } = string.Empty;
        public string fromId { get; set; } = string.Empty;
        public long fromObfuscatedSummonerId { get; set; }
        public string fromPid { get; set; } = string.Empty;
        public long fromSummonerId { get; set; }
        public string id { get; set; } = string.Empty;
        public bool isHistorical { get; set; }
        public DateTime timestamp { get; set; }
        public string type { get; set; } = string.Empty;
    }
}