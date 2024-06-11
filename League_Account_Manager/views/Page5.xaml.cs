using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page5.xaml
/// </summary>
public partial class Page5 : Page
{
    private readonly List<Champs> Buyable = new();

    public Page5()
    {
        InitializeComponent();
        LoadBuyableData();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalcost = 0;
            var count = 1;
            var championsbought = " Bought champions \n";
            var championsfailed = "Failed to Buy \n";
            foreach (Champs champ in buyableChampsList.SelectedItems)
            {
                var id = champ.ID;
                var price = champ.Price;
                var val = await lcu.Connector("league", "post", "/lol-purchase-widget/v2/purchaseItems",
                    "{\"items\":[{\"itemKey\":{\"inventoryType\":\"CHAMPION\",\"itemId\":" + champ.ID +
                    "},\"purchaseCurrencyInfo\":{\"currencyType\":\"IP\",\"price\":" + champ.Price +
                    ",\"purchasable\":true},\"source\":\"cdp\",\"quantity\":1}]}");
                if (val.ToString() == "0")
                {
                    notif.notificationManager.Show("Error", "League of legends client is not running!",
                        NotificationType.Notification, "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null,
                        () => notif.donothing(), "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                    return;
                }

                var val2 = JsonArray.Parse(await val.Content.ReadAsStringAsync().ConfigureAwait(false));
                try
                {
                    totalcost += Convert.ToInt32(val2["items"][0]["purchaseCurrencyInfo"]["price"].ToString());
                    statusmessage.Content = "Bought: " + champ.Name + "! progress: " + count + "/" + buyableChampsList.SelectedItems.Count + " Total BE used: " +
                                            totalcost;
                    championsbought = championsbought + champ.Name + "\n";
                    success.Text = championsbought;
                }
                catch (Exception value)
                {
                    statusmessage.Content = "Bought: " + champ.Name + "! progress: " + count + "/" + buyableChampsList.SelectedItems.Count + " Total BE used: " +
                                            totalcost;
                    championsfailed = championsfailed + champ.Name + " " + val2["message"] + "\n";
                    success.Text = championsfailed;
                }

                Thread.Sleep(500);
                count++;
            }

            statusmessage.Content = "All champions bought! Total BE used: " + totalcost;
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
            Buyable.Clear();
            var responseBody = await lcu.Connector("league", "get", "/lol-store/v1/getStoreUrl", "");
            string storeurl = await responseBody.Content.ReadAsStringAsync().ConfigureAwait(false);
            responseBody = await lcu.Connector("league", "get", "/lol-rso-auth/v1/authorization/access-token", "");
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
                    Buyable.Add(new Champs
                    {
                        ID = Convert.ToInt32(champ["itemId"]),
                        Price = Convert.ToInt32(champ["ip"]),
                        Name = champ["name"].ToString(),
                        namelist = champ["name"] + " " + champ["ip"]
                    });
            }

            buyableChampsList.ItemsSource = Buyable;
            buyableChampsList.Items.SortDescriptions.Add(new SortDescription("Price", ListSortDirection.Ascending));
            client.Dispose();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private class Champs
    {
        public int ID { get; set; }
        public int Price { get; set; }
        public string Name { get; set; }
        public string namelist { get; set; }
    }
}