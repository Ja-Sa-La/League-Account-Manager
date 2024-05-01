using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page9.xaml
/// </summary>
public partial class Page9 : Page
{
    private int champs;

    public List<LootChamps> LootChampsList = new();
    public List<LootChamps> LootSkinsList = new();
    private int skins;

    public Page9()
    {
        InitializeComponent();
        UpdateShit();
    }


    private async void UpdateShit()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;
            LootChampsList.Clear();
            LootSkinsList.Clear();
            var resp = await lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
            JToken responseBody = JToken.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            foreach (var jtoken in responseBody)
            foreach (var thing in jtoken)
            {
                Console.WriteLine(thing);
                if (thing["disenchantLootName"].ToString() == "CURRENCY_champion")
                    LootChampsList.Add(new LootChamps
                    {
                        name = thing["itemDesc"] + " x " + thing["count"] + " " + thing["disenchantValue"] + " BE",
                        id = thing["lootId"].ToString(), count = Convert.ToInt32(thing["count"]),
                        price = Convert.ToInt32(thing["count"]), value = Convert.ToInt32(thing["disenchantValue"]),
                        disenchantRecipeName = thing["disenchantRecipeName"].ToString()
                    });
                else if (thing["disenchantLootName"].ToString() == "CURRENCY_cosmetic")
                    if (thing["itemDesc"].ToString() != "")
                        LootSkinsList.Add(new LootChamps
                        {
                            name = thing["itemDesc"] + " x " + thing["count"] + " " + thing["disenchantValue"] + " OE",
                            id = thing["lootId"].ToString(), count = Convert.ToInt32(thing["count"]),
                            price = Convert.ToInt32(thing["count"]), value = Convert.ToInt32(thing["disenchantValue"]),
                            disenchantRecipeName = thing["disenchantRecipeName"].ToString()
                        });
                    else
                        LootSkinsList.Add(new LootChamps
                        {
                            name = thing["localizedName"] + " x " + thing["count"] + " " + thing["disenchantValue"] +
                                   " OE",
                            id = thing["lootId"].ToString(), count = Convert.ToInt32(thing["count"]),
                            price = Convert.ToInt32(thing["count"]), value = Convert.ToInt32(thing["disenchantValue"]),
                            disenchantRecipeName = thing["disenchantRecipeName"].ToString()
                        });
            }

            SkinLootTable.ItemsSource = null;
            SkinLootTable.ItemsSource = LootSkinsList;
            SkinLootTable.Items.Refresh();
            ChampLootTable.ItemsSource = null;
            ChampLootTable.ItemsSource = LootChampsList;
            ChampLootTable.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void BuyShit()
    {
        foreach (LootChamps champ in ChampLootTable.SelectedItems)
        {
            var resp = await lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.disenchantRecipeName + "/craft?repeat=1", "[\"" + champ.id + "\"]");
        }

        foreach (LootChamps champ in SkinLootTable.SelectedItems)
        {
            var resp = await lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.disenchantRecipeName + "/craft?repeat=1", "[\"" + champ.id + "\"]");
        }

        UpdateShit();
    }

    private async void ButtonBase_OnClick1(object sender, RoutedEventArgs e)
    {
        BuyShit();
    }

    private void SelectChamps(object sender, RoutedEventArgs e)
    {
        if (champs == 0)
        {
            ChampLootTable.SelectAll();
            champs = 1;
        }
        else
        {
            ChampLootTable.UnselectAll();
            champs = 0;
        }
    }

    private void SelectSkins(object sender, RoutedEventArgs e)
    {
        if (skins == 0)
        {
            SkinLootTable.SelectAll();
            skins = 1;
        }
        else
        {
            SkinLootTable.UnselectAll();
            skins = 0;
        }
    }

    private void ChampLootTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int oe = 0, be = 0;
        foreach (LootChamps champ in ChampLootTable.SelectedItems) be += Convert.ToInt32(champ.value);
        foreach (LootChamps champ in SkinLootTable.SelectedItems) oe += Convert.ToInt32(champ.value);

        belabel.Content = "Blue essense to be gained: " + be;
        oelabel.Content = "Orange essense to be gained: " + oe;
    }

    public class LootChamps
    {
        public string? name { get; set; }
        public string? id { get; set; }
        public int? count { get; set; }
        public int? price { get; set; }
        public int? value { get; set; }
        public string? disenchantRecipeName { get; set; }
    }
}