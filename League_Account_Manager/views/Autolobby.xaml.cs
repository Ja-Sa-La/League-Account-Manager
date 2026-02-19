using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Button = System.Windows.Controls.Button;

namespace League_Account_Manager.views;

public partial class Autolobby : Page
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly List<IconData> listChamps = new();
    private readonly ConcurrentDictionary<string, ToggleTaskInfo> toggles = new();

    private Chat champSelect;
    private JObject champselectaction;
    private JObject champselectJObject;
    private JObject ChampselectTeamJObject;
    private JObject queueJObject;

    private bool sentmsg;

    public Autolobby()
    {
        InitializeComponent();

        Task.Run(BackgroundDataFunction1);
        Task.Run(BackgroundDataFunction2);
        Task.Run(LoadBuyableData);
    }

    // =====================================================
    // TOGGLE SYSTEM
    // =====================================================

    private class ToggleTaskInfo
    {
        public bool Running { get; set; }
        public Task Task { get; set; }
        public CancellationTokenSource Cts { get; set; }
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
            return;
        }

        var info = toggles[taskName];

        if (info.Running)
        {
            info.Running = false;
            info.Cts.Cancel();
            button.Content = $"Enable {taskName}";
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
        }
    }

    // =====================================================
    // LOAD CHAMPION DATA
    // =====================================================

    private async Task LoadBuyableData()
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var summonerdata = JObject.Parse(responseBody);

            resp = await Lcu.Connector("league", "get",
                $"/lol-champions/v1/inventories/{(string)summonerdata["summonerId"]}/champions-minimal", "");

            responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var champList = JArray.Parse(responseBody);

            listChamps.Clear();

            foreach (var champ in champList)
            {
                listChamps.Add(new IconData
                {
                    Name = champ["name"]?.ToString(),
                    ID = champ["id"]?.ToString()
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
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading champion data");
        }
    }

    // =====================================================
    // BACKGROUND LOOPS (RUN FOREVER)
    // =====================================================

    private async Task BackgroundDataFunction1()
    {
        while (true)
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
                        champselectJObject = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));

                        champselectaction = null;
                        ChampselectTeamJObject = null;

                        if (champselectJObject.TryGetValue("actions", out var actionsToken) &&
                            actionsToken is JArray actionsArray)
                        {
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

            await Task.Delay(500);
        }
    }

    private async Task BackgroundDataFunction2()
    {
        while (true)
        {
            try
            {
                if (AnyFeatureEnabled())
                {
                    var resp = await Lcu.Connector("league", "get", "/lol-gameflow/v1/session", "");
                    queueJObject = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in BackgroundDataFunction2");
            }

            await Task.Delay(1000);
        }
    }

    // =====================================================
    // BUTTON TOGGLES
    // =====================================================

    private void ToggleAutoAccept(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptQueue", StartAutoAcceptTask, sender);
    }

    private void ToggleAutoPick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptPick", StartAutoPickTask, sender);
    }

    private void ToggleAutoBan(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptBan", StartAutoBanTask, sender);
    }

    private void ToggleAutoMessage(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptMessage", StartAutoMessageTask, sender);
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
                {
                    await Lcu.Connector("league", "post", "/lol-matchmaking/v1/ready-check/accept", "");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoAccept");
            }

            await Task.Delay(3000, ct);
        }
    }

    // =====================================================
    // PICK FIXED
    // =====================================================

    private async Task StartAutoPickTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (champselectaction != null &&
                    champselectaction["type"]?.ToString() == "pick")
                {
                    var champId = await getpickchampid();

                    if (!string.IsNullOrEmpty(champId))
                    {
                        await Lcu.Connector("league", "patch",
                            "/lol-champ-select/v1/session/actions/" + champselectaction["id"],
                            "{\"completed\":true,\"championId\":" + champId + "}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoPick");
            }

            await Task.Delay(300, ct);
        }
    }

    private async Task<string> getpickchampid()
    {
        try
        {
            if (ChampselectTeamJObject == null || champselectJObject == null)
                return "";

            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/pickable-champion-ids", "");
            var pickableArray = JArray.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));

            var pickableIds = pickableArray.Values<int>().ToHashSet();

            string position = ChampselectTeamJObject["assignedPosition"]?.ToString()?.ToUpper() ?? "";

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
                string champName = pos switch
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

                if (!pickableIds.Contains(champIdInt))
                    continue;

                var myBans = champselectJObject["bans"]?["myTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();
                var theirBans = champselectJObject["bans"]?["theirTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();

                if (myBans.Contains(champIdInt) || theirBans.Contains(champIdInt))
                    continue;

                return champ.ID;
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in getpickchampid");
            return "";
        }
    }

    // =====================================================
    // BAN FIXED
    // =====================================================

    private async Task StartAutoBanTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (champselectaction != null &&
                    champselectaction["type"]?.ToString() == "ban")
                {
                    await Task.Delay(1000, ct);

                    var champId = await getbanchampid();

                    if (!string.IsNullOrEmpty(champId))
                    {
                        await Lcu.Connector("league", "patch",
                            "/lol-champ-select/v1/session/actions/" + champselectaction["id"],
                            "{\"completed\":true,\"championId\":" + champId + "}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AutoBan");
            }

            await Task.Delay(2000, ct);
        }
    }

    private async Task<string> getbanchampid()
    {
        try
        {
            if (champselectJObject == null)
                return "";

            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/bannable-champion-ids", "");
            var bannableArray = JArray.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));

            var bannableIds = bannableArray.Values<int>().ToHashSet();

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

                if (!bannableIds.Contains(champIdInt))
                    continue;

                var myBans = champselectJObject["bans"]?["myTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();
                var theirBans = champselectJObject["bans"]?["theirTeamBans"]?.Values<int>() ?? Enumerable.Empty<int>();

                if (myBans.Contains(champIdInt) || theirBans.Contains(champIdInt))
                    continue;

                return champ.ID;
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in getbanchampid");
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

                    string msg = "";
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
    // DATA CLASSES
    // =====================================================

    public class IconData
    {
        public string Name { get; set; }
        public string ID { get; set; }
    }

    public class Chat
    {
        public string gameName { get; set; }
        public string gameTag { get; set; }
        public string id { get; set; }
        public string inviterId { get; set; }
        public bool isMuted { get; set; }
        public Lastmessage lastMessage { get; set; }
        public Mucjwtdto mucJwtDto { get; set; }
        public string name { get; set; }
        public string password { get; set; }
        public string pid { get; set; }
        public string targetRegion { get; set; }
        public string type { get; set; }
        public long unreadMessageCount { get; set; }
    }

    public class Mucjwtdto
    {
        public string channelClaim { get; set; }
        public string domain { get; set; }
        public string jwt { get; set; }
        public string targetRegion { get; set; }
    }

    public class Lastmessage
    {
        public string body { get; set; }
        public string fromId { get; set; }
        public long fromObfuscatedSummonerId { get; set; }
        public string fromPid { get; set; }
        public long fromSummonerId { get; set; }
        public string id { get; set; }
        public bool isHistorical { get; set; }
        public DateTime timestamp { get; set; }
        public string type { get; set; }
    }
}