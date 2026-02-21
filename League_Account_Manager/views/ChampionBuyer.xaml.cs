using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for ChampionBuyer.xaml
/// </summary>
public partial class ChampionBuyer : Page
{
    private readonly List<ChampionEntry> _buyableChampions = new();

    public ChampionBuyer()
    {
        InitializeComponent();
        LoadBuyableData();
    }

    private async void BuySelectedChampions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalcost = 0;
            var count = 1;
            var championsBoughtLog = " Bought champions \n";
            var championsFailedLog = "Failed to Buy \n";
            foreach (ChampionEntry champ in BuyableChampionsList.SelectedItems)
            {
                var id = champ.ID;
                var price = champ.Price;
                var val = await Lcu.Connector("league", "post", "/lol-purchase-widget/v2/purchaseItems",
                    "{\"items\":[{\"itemKey\":{\"inventoryType\":\"CHAMPION\",\"itemId\":" + champ.ID +
                    "},\"purchaseCurrencyInfo\":{\"currencyType\":\"IP\",\"price\":" + champ.Price +
                    ",\"purchasable\":true},\"source\":\"cdp\",\"quantity\":1}]}");
                if (val.ToString() == "0")
                {
                    Notif.notificationManager.Show("Error", "League of legends client is not running!",
                        NotificationType.Notification, "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null,
                        () => Notif.donothing(), "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                    return;
                }

                var val2 = JsonArray.Parse(await val.Content.ReadAsStringAsync().ConfigureAwait(false));
                try
                {
                    totalcost += Convert.ToInt32(val2["items"][0]["purchaseCurrencyInfo"]["price"].ToString());
                    StatusMessageLabel.Content = "Bought: " + champ.Name + "! progress: " + count + "/" +
                                                 BuyableChampionsList.SelectedItems.Count + " Total BE used: " +
                                                 totalcost;
                    championsBoughtLog = championsBoughtLog + champ.Name + "\n";
                    BuyLog.Text = championsBoughtLog;
                }
                catch (Exception value)
                {
                    StatusMessageLabel.Content = "Bought: " + champ.Name + "! progress: " + count + "/" +
                                                 BuyableChampionsList.SelectedItems.Count + " Total BE used: " +
                                                 totalcost;
                    championsFailedLog = championsFailedLog + champ.Name + " " + val2["message"] + "\n";
                    BuyLog.Text = championsFailedLog;
                }

                Thread.Sleep(500);
                count++;
            }

            StatusMessageLabel.Content = "All champions bought! Total BE used: " + totalcost;
            LoadBuyableData();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void LoadBuyableData()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;
            _buyableChampions.Clear();
            var responseBody = await Lcu.Connector("league", "get", "/lol-store/v1/getStoreUrl", "");
            string storeurl = await responseBody.Content.ReadAsStringAsync().ConfigureAwait(false);
            responseBody = await Lcu.Connector("league", "get", "/lol-rso-auth/v1/authorization/access-token", "");
            JObject authtoken = JObject.Parse(await responseBody.Content.ReadAsStringAsync().ConfigureAwait(false));
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 500
            };
            var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            client.Timeout = TimeSpan.FromSeconds(15);
            var returnmessage = "";
            client.DefaultRequestHeaders.Add("Accept-Encoding", "deflate, gzip");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 6.2; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) LeagueOfLegendsClient/14.6.568.8373 (CEF 91) Safari/537.36");
            client.DefaultRequestHeaders.Add("AUTHORIZATION", "Bearer " + authtoken["token"]);
            responseBody =
                await client.GetAsync(storeurl.Replace("\"", "") + "/storefront/v3/view/champions?language=en_US");
            JObject finalresp = JObject.Parse(await responseBody.Content.ReadAsStringAsync().ConfigureAwait(false));
            foreach (var champ in finalresp["catalog"])
            {
                var champObject = champ as JObject;
                if (!champObject.ContainsKey("ownedQuantity"))
                    _buyableChampions.Add(new ChampionEntry
                    {
                        ID = Convert.ToInt32(champ["itemId"]),
                        Price = Convert.ToInt32(champ["ip"]),
                        Name = champ["name"].ToString(),
                        DisplayName = champ["name"] + " " + champ["ip"],
                        IconUrl = $"https://cdn.communitydragon.org/latest/champion/{champ["itemId"]}/square"
                    });
            }

            BuyableChampionsList.ItemsSource = _buyableChampions;
            BuyableChampionsList.Items.SortDescriptions.Add(new SortDescription("Price", ListSortDirection.Ascending));
            client.Dispose();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private class ChampionEntry
    {
        public int ID { get; set; }
        public int Price { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }

        public string IconUrl { get; set; }

        // keep legacy property name for bindings
        public string namelist
        {
            get => DisplayName;
            set => DisplayName = value;
        }
    }
}