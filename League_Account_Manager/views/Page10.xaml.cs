using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using NLog;
using Button = System.Windows.Controls.Button;



namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page10.xaml
/// </summary>
public partial class Page10 : Page
{
    private readonly List<IconData>? listChamps = new();
    private readonly Dictionary<string, (bool, Task, CancellationTokenSource)> toggles = new();
    private JObject champselectaction;
    private JObject champselectJObject;
    private JObject ChampselectTeamJObject;
    private JObject queueJObject;
    private Chat champSelect;
    private bool sentmsg = false;

    public Page10()
    {
        InitializeComponent();
   
        {
            Task.Run(() => BackgroundDataFunction1());
            Task.Run(() => BackgroundDataFunction2());
            Task.Run(() => LoadBuyableData());
    
   
        }

    }

    private async void LoadBuyableData()

    {
        try
        {
            var resp = await lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject summonerdata = JObject.Parse(responseBody);
            resp = await lcu.Connector("league", "get",
                $"/lol-champions/v1/inventories/{(string)summonerdata["summonerId"]}/champions-minimal", "");
            responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var champList = JArray.Parse(responseBody);

            foreach (var champ in champList)
                listChamps.Add(new IconData
                {
                    Name = champ["name"].ToString(),
                    ID = champ["id"].ToString()
                });
            Dispatcher.Invoke(() =>
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
        } catch (Exception ex) {
            LogManager.GetCurrentClassLogger().Error(ex, "Error loading data");
        }
    }

    private void ToggleTask(string taskName, Func<CancellationToken, Task> taskFunc, object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (!toggles.ContainsKey(taskName))
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => taskFunc(cts.Token), cts.Token);
            toggles[taskName] = (true, task, cts);
            button.Content = $"Disable {taskName}";
        }
        else
        {
            var (running, task, cts) = toggles[taskName];
            if (running)
            {
                cts.Cancel();
                toggles[taskName] = (false, task, cts);
                button.Content = $"Enable {taskName}";
            }
            else
            {
                cts = new CancellationTokenSource();
                task = Task.Run(() => taskFunc(cts.Token), cts.Token);
                toggles[taskName] = (true, task, cts);
                button.Content = $"Disable {taskName}";
            }
        }
    }

    private async Task BackgroundDataFunction1()
    {
        try
        {
            while (true)
            {
                if (toggles.Any(t => t.Value.Item1) && queueJObject != null && queueJObject.ContainsKey("phase") &&
                    (queueJObject["phase"].ToString() == "ChampSelect" ||
                     queueJObject["phase"].ToString() == "ReadyCheck"))
                {
                    var resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
                    champselectJObject = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (champselectJObject.ContainsKey("actions"))
                    {
                        var found = false;
                        foreach (JArray actionArray in (JArray)champselectJObject["actions"])
                        {
                            foreach (JObject action in actionArray)
                                if ((bool)action["isInProgress"] && action["actorCellId"].ToString() ==
                                    champselectJObject["localPlayerCellId"].ToString() &&
                                    champselectJObject["timer"]["phase"].ToString() != "PLANNING")
                                {
                                    champselectaction = action;
                                    found = true;
                                    break;
                                }

                            if (found) break;
                            champselectaction = null;
                        }

                        
                        foreach (JObject teamArray in (JArray)champselectJObject["myTeam"])
                            if (teamArray["cellId"].ToString() == champselectJObject["localPlayerCellId"].ToString())
                            {
                                ChampselectTeamJObject = teamArray;
                                break;
                            }
                    }
                    else
                    {
                        champselectaction = null;
                    }
                }
                else
                {
                    champselectaction = null;
                    champselectJObject = null;
                    ChampselectTeamJObject = null;
                }


                await Task.Delay(500);
            }
        }
        catch (Exception e)
        {
        }
    }

    private async Task BackgroundDataFunction2()
    {
        try
        {
            while (true)
            {
                if (toggles.Any(t => t.Value.Item1))
                {
                    var resp = await lcu.Connector("league", "get", "/lol-gameflow/v1/session", "");
                    queueJObject = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                }

                await Task.Delay(1000);
            }
        }catch (Exception e) {
            LogManager.GetCurrentClassLogger().Error(e, "Error loading data");
        }        

    }



    private void ToggleAutoAccept(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptQueue", StartAutoAcceptTask, sender, e);
    }

    private void ToggleAutoPick(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptPick", StartAutoPickTask, sender, e);
    }

    private void ToggleAutoBan(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptBan", StartAutoBanTask, sender, e);
    }

    private void ToggleAutoMessage(object sender, RoutedEventArgs e)
    {
        ToggleTask("AutoAcceptMessage", StartAutoMessageTask, sender, e);
    }

    private async Task StartAutoAcceptTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && toggles["AutoAcceptQueue"].Item1)
        {
            if (queueJObject != null && queueJObject.ContainsKey("phase") &&
                queueJObject["phase"].ToString() == "ReadyCheck")
                await lcu.Connector("league", "post", "/lol-matchmaking/v1/ready-check/accept", "");
            await Task.Delay(3000, ct);
        }
    }

    private async Task StartAutoPickTask(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && toggles["AutoAcceptPick"].Item1)
            {
                if (champselectaction != null && champselectaction.ContainsKey("type") &&
                    champselectaction["type"].ToString() == "pick")
                {
                    var resp = await lcu.Connector("league", "patch",
                        "/lol-champ-select/v1/session/actions/" + champselectaction["id"],
                        "{\"completed\":true,\"championId\":" + await getpickchampid() + "}");
                }

                await Task.Delay(300, ct); // Delay for 1 second before checking again
            }
        }
        catch (Exception e)
        {
            throw;
        }
    }

    private async Task<string> getpickchampid()
    {
        var resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/pickable-champion-ids", "");
        JArray Pickable = JArray.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var returnva = Dispatcher.Invoke(() =>
        {
            var position = ChampselectTeamJObject["assignedPosition"].ToString().ToUpper();
            var positions = new List<string> { position, "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            string id = "1";
            foreach (var pos in positions)
            {
                var champName = "";
                switch (pos)
                {
                    case "TOP":
                        champName = topPickChampion.Text;
                        break;
                    case "JUNGLE":
                        champName = junglePickChampion.Text;
                        break;
                    case "MIDDLE":
                        champName = midPickChampion.Text;
                        break;
                    case "BOTTOM":
                        champName = botPickChampion.Text;
                        break;
                    case "UTILITY":
                        champName = supportPickChampion.Text;
                        break;
                    default:
                        champName = blindPickChampion.Text;
                        break;
                }

                // If the text box for the current position is not empty
                if (!string.IsNullOrEmpty(champName))
                {
                    // Get the champion ID
                    id = listChamps.First(c => c.Name == champName).ID;
                    // If the selected champion ID is in the Pickable list
                    foreach (string jToken in Pickable)
                    {
                        if (id != jToken && !champselectJObject["bans"]["myTeamBans"].Values<int>().Contains(int.Parse(id)) &&
                            !champselectJObject["bans"]["theirTeamBans"].Values<int>().Contains(int.Parse(id)))
                        {
                            return id;
                        }
                    }
                }
            }

            return id;
        });
        return returnva;
    }

    private async Task<string> getbanchampid()
    {
        var resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/bannable-champion-ids", "");
        JArray banable = JArray.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var returnval = Dispatcher.Invoke(() =>
        {
            string[] banChampions = { ban1Champion.Text, ban2Champion.Text, ban3Champion.Text };
            var result = "1"; // Default value is "1"

            foreach (var champion in banChampions)
            {
                if (!string.IsNullOrEmpty(champion))
                {
                    var champId = listChamps.FirstOrDefault(c => c.Name == champion)?.ID;
                    if (banable.ToString().Contains(result))
                    {
                        // Check if the JObject contains the champion ID in the myTeamBans or theirTeamBans array
                        if (!champselectJObject["bans"]["myTeamBans"].Values<int>().Contains(int.Parse(champId)) &&
                            !champselectJObject["bans"]["theirTeamBans"].Values<int>().Contains(int.Parse(champId)))
                        {
                            result = champId;
                            break;
                        }
                    }
                }
            }

            return result;
        });

        // If none of the champions are in the banable array, return an empty string or handle it appropriately
        return returnval;
    }
    private async Task StartAutoBanTask(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && toggles["AutoAcceptBan"].Item1)
            {
                if (champselectaction != null && champselectaction.ContainsKey("type") &&
                    champselectaction["type"].ToString() == "ban")
                {
                    Task.Delay(1000, ct);
                    var resp = await lcu.Connector("league", "patch",
                        "/lol-champ-select/v1/session/actions/" + champselectaction["id"],
                        "{\"completed\":true,\"championId\":" + await getbanchampid() + "}");
                }

                await Task.Delay(2000, ct); // Delay for 1 second before checking again
            }
        }
        catch (Exception e)
        {

        }
    }


    private async Task sendmsg(string msg)
    {
        try
        {
            var response = await lcu.Connector("league", "get", "/lol-chat/v1/conversations", "");
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            List<Chat> chats = JArray.Parse(responseContent).ToObject<List<Chat>>();
            champSelect = chats.FirstOrDefault(chat => chat.type == "championSelect");
            if (champSelect == null)
                return;
            var resp = await lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var summonerinfo = JObject.Parse(responseBody2);
            string postdata = "{\"type\":\"chat\",\"fromId\":\"" + champSelect.id +
                              "\",\"fromSummonerId\":" + summonerinfo["accountId"] +
                              ",\"isHistorical\":false,\"timestamp\":\"" +
                              DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"body\":\"" + msg + "\"}";
            resp = await lcu.Connector("league", "post", "/lol-chat/v1/conversations/" + champSelect.pid + "/messages",
                postdata);
        }
        catch (Exception e)
        {
            throw;
        }
    }


    private async Task StartAutoMessageTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && toggles["AutoAcceptMessage"].Item1)
        {
            if (champselectaction != null && champselectaction.ContainsKey("type") && !sentmsg)
            {
                Thread.Sleep(1000);
                string msg = "";
                Dispatcher.Invoke(() =>
                {
                    msg = MessageContainer.Text;
                });
                if (!string.IsNullOrEmpty(msg))
                {
                    await sendmsg(msg);
                    sentmsg = true;
                }
            }
            else if (queueJObject == null ||( queueJObject.ContainsKey("phase") && queueJObject["phase"].ToString() != "ChampSelect"))
            {
                sentmsg = false;
            }
            await Task.Delay(1000, ct); 
        }
    }


    public class IconData
    {
        public string? Name { get; set; }
        public string? ID { get; set; }
    }
    public class Chat
    {
        public string? gameName { get; set; }
        public string? gameTag { get; set; }
        public string? id { get; set; }
        public string? inviterId { get; set; }
        public bool isMuted { get; set; }
        public Lastmessage? lastMessage { get; set; }
        public Mucjwtdto? mucJwtDto { get; set; }
        public string? name { get; set; }
        public string? password { get; set; }
        public string? pid { get; set; }
        public string? targetRegion { get; set; }
        public string? type { get; set; }
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