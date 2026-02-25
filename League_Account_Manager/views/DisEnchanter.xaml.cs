using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for DisEnchanter.xaml
/// </summary>
public partial class DisEnchanter : Page
{
    private const string GenericIcon = "https://cdn.communitydragon.org/latest/champion/generic/square";
    private bool champsSelected;

    public List<LootItem> LootChampsList = new();
    public List<LootItem> LootSkinsList = new();
    private bool skinsSelected;

    public DisEnchanter()
    {
        InitializeComponent();
        UpdateLootAsync();
    }


    private async void UpdateLootAsync()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;
            LootChampsList.Clear();
            LootSkinsList.Clear();
            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
            JToken responseBody = JToken.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            foreach (var jtoken in responseBody)
            foreach (var thing in jtoken)
            {
                DebugConsole.WriteLine(thing.ToString());
                if (thing["disenchantLootName"].ToString() == "CURRENCY_champion")
                {
                    var tilePath = thing["tilePath"]?.ToString();
                    LootChampsList.Add(new LootItem
                    {
                        Name = thing["itemDesc"] + " x " + thing["count"],
                        Id = thing["lootId"].ToString(), Count = Convert.ToInt32(thing["count"]),
                        Price = Convert.ToInt32(thing["count"]), Value = Convert.ToInt32(thing["disenchantValue"]),
                        DisenchantRecipeName = thing["disenchantRecipeName"].ToString(),
                        IconUrl = BuildTileIconUrl(tilePath)
                    });
                }
                else if (thing["disenchantLootName"].ToString() == "CURRENCY_cosmetic")
                {
                    var skinName = thing["itemDesc"].ToString();
                    if (string.IsNullOrWhiteSpace(skinName)) skinName = thing["localizedName"].ToString();
                    var tilePath = thing["tilePath"]?.ToString();
                    if (thing["displayCategories"].ToString() == "SKIN" || thing["displayCategories"].ToString() == "ETERNALS")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + thing["count"],
                            Id = thing["lootId"].ToString(),
                            Count = Convert.ToInt32(thing["count"]),
                            Price = Convert.ToInt32(thing["count"]),
                            Value = Convert.ToInt32(thing["disenchantValue"]),
                            DisenchantRecipeName = thing["disenchantRecipeName"].ToString(),
                            IconUrl = BuildTileIconUrl(tilePath)
                        });
                    }
                    else if (thing["displayCategories"].ToString() == "WARDSKIN")
                    {
                        DebugConsole.WriteLine(BuildTileIconUrlWards(tilePath));
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + thing["count"],
                            Id = thing["lootId"].ToString(),
                            Count = Convert.ToInt32(thing["count"]),
                            Price = Convert.ToInt32(thing["count"]),
                            Value = Convert.ToInt32(thing["disenchantValue"]),
                            DisenchantRecipeName = thing["disenchantRecipeName"].ToString(),
                            IconUrl = BuildTileIconUrlWards(tilePath)
                        });

                    }
                    else if (thing["displayCategories"].ToString() == "SUMMONERICON")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + thing["count"],
                            Id = thing["lootId"].ToString(),
                            Count = Convert.ToInt32(thing["count"]),
                            Price = Convert.ToInt32(thing["count"]),
                            Value = Convert.ToInt32(thing["disenchantValue"]),
                            DisenchantRecipeName = thing["disenchantRecipeName"].ToString(),
                            IconUrl = BuildTileIconUrlSummonerIcon(tilePath)
                        });

                    }
                    else if (thing["displayCategories"].ToString() == "EMOTE")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + thing["count"],
                            Id = thing["lootId"].ToString(),
                            Count = Convert.ToInt32(thing["count"]),
                            Price = Convert.ToInt32(thing["count"]),
                            Value = Convert.ToInt32(thing["disenchantValue"]),
                            DisenchantRecipeName = thing["disenchantRecipeName"].ToString(),
                            IconUrl = BuildTileIconUrlEmotes(tilePath)
                        });

                    }
                    }
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

    private async void CraftSelectedLootAsync()
    {
        foreach (LootItem champ in ChampLootTable.SelectedItems)
        {
            var resp = await Lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.DisenchantRecipeName + "/craft?repeat=1", "[\"" + champ.Id + "\"]");
        }

        foreach (LootItem champ in SkinLootTable.SelectedItems)
        {
            var resp = await Lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.DisenchantRecipeName + "/craft?repeat=1", "[\"" + champ.Id + "\"]");
        }

        UpdateLootAsync();
    }

    private void ButtonBase_OnClick1(object sender, RoutedEventArgs e)
    {
        CraftSelectedLootAsync();
    }

    private void SelectChamps(object sender, RoutedEventArgs e)
    {
        if (!champsSelected)
        {
            ChampLootTable.SelectAll();
            champsSelected = true;
        }
        else
        {
            ChampLootTable.UnselectAll();
            champsSelected = false;
        }
    }

    private void SelectSkins(object sender, RoutedEventArgs e)
    {
        if (!skinsSelected)
        {
            SkinLootTable.SelectAll();
            skinsSelected = true;
        }
        else
        {
            SkinLootTable.UnselectAll();
            skinsSelected = false;
        }
    }

    private void ChampLootTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int oe = 0, be = 0;
        foreach (LootItem champ in ChampLootTable.SelectedItems) be += Convert.ToInt32(champ.Value);
        foreach (LootItem champ in SkinLootTable.SelectedItems) oe += Convert.ToInt32(champ.Value);

        belabel.Content = "Blue essense to be gained: " + be;
        oelabel.Content = "Orange essense to be gained: " + oe;
    }


    private static string BuildTileIconUrl(string? tilePath)
    {
        if (string.IsNullOrWhiteSpace(tilePath)) return null;

        var startIndex = tilePath.IndexOf("/Characters", StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            startIndex = tilePath.IndexOf("/assets", StringComparison.OrdinalIgnoreCase);
            if (startIndex >= 0)
                // move past "/assets" to keep path consistent
                startIndex += "/assets".Length;
        }

        if (startIndex < 0 || startIndex >= tilePath.Length) return null;

        var pathPart = tilePath[startIndex..].Trim();
        var lowered = pathPart.ToLowerInvariant().TrimStart('/');
        const string baseUrl =
            "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets";
        return baseUrl + "/" + lowered;
    }
    private static string BuildTileIconUrlWards(string? tilePath)
    {
        if (string.IsNullOrWhiteSpace(tilePath)) return null;

        var startIndex = tilePath.IndexOf("/content", StringComparison.OrdinalIgnoreCase);

        if (startIndex < 0 || startIndex >= tilePath.Length) return null;

        var pathPart = tilePath[startIndex..].Trim();
        var lowered = pathPart.ToLowerInvariant().TrimStart('/');
        const string baseUrl =
            "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default";
        return baseUrl + "/" + lowered;
    }
    private static string BuildTileIconUrlEmotes(string? tilePath)
    {
        if (string.IsNullOrWhiteSpace(tilePath)) return null;

        var startIndex = tilePath.IndexOf("/assets", StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0 || startIndex >= tilePath.Length) return null;

        var pathPart = tilePath[(startIndex + "/assets".Length)..].Trim();
        var lowered = pathPart.ToLowerInvariant().TrimStart('/');
        const string baseUrl = "https://raw.communitydragon.org/latest/game";
        return baseUrl + "/" + lowered;
    }

    private static string BuildTileIconUrlSummonerIcon(string? tilePath)
    {
        if (string.IsNullOrWhiteSpace(tilePath)) return null;

        var startIndex = tilePath.IndexOf("/assets", StringComparison.OrdinalIgnoreCase);

        if (startIndex < 0 || startIndex >= tilePath.Length) return null;

        var pathPart = tilePath[(startIndex + "/assets".Length)..].Trim();
        var lowered = pathPart.ToLowerInvariant().TrimStart('/');
        const string baseUrl =
            "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default";
        return baseUrl + "/" + lowered;
    }

    public class LootItem
    {
        public string? Name { get; set; }
        public string? Id { get; set; }
        public int? Count { get; set; }
        public int? Price { get; set; }
        public int? Value { get; set; }
        public string? DisenchantRecipeName { get; set; }
        public string? IconUrl { get; set; }
    }
}