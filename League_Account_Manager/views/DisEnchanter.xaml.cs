using System.Diagnostics;
using System.Net.Http;
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
        _ = UpdateLootAsync();
    }


    private async Task UpdateLootAsync()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;
            LootChampsList.Clear();
            LootSkinsList.Clear();
            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "")
                as HttpResponseMessage;
            if (resp == null)
                return;

            JToken responseBody = JToken.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            foreach (var jtoken in responseBody)
            foreach (var thing in jtoken)
            {
                DebugConsole.WriteLine(thing.ToString());
                var disenchantLootName = thing["disenchantLootName"]?.ToString();
                var lootId = thing["lootId"]?.ToString();
                var recipeName = thing["disenchantRecipeName"]?.ToString();
                var count = thing["count"]?.Value<int?>();
                var disenchantValue = thing["disenchantValue"]?.Value<int?>();
                if (string.IsNullOrWhiteSpace(lootId) || string.IsNullOrWhiteSpace(recipeName) ||
                    !count.HasValue || !disenchantValue.HasValue)
                    continue;

                if (disenchantLootName == "CURRENCY_champion")
                {
                    var tilePath = thing["tilePath"]?.ToString();
                    LootChampsList.Add(new LootItem
                    {
                        Name = (thing["itemDesc"]?.ToString() ?? "Champion shard") + " x " + count.Value,
                        Id = lootId, Count = count.Value,
                        Price = count.Value, Value = disenchantValue.Value,
                        DisenchantRecipeName = recipeName,
                        IconUrl = BuildTileIconUrl(tilePath)
                    });
                }
                else if (disenchantLootName == "CURRENCY_cosmetic")
                {
                    var skinName = thing["itemDesc"]?.ToString();
                    if (string.IsNullOrWhiteSpace(skinName))
                        skinName = thing["localizedName"]?.ToString() ?? "Cosmetic shard";
                    var tilePath = thing["tilePath"]?.ToString();
                    var category = thing["displayCategories"]?.ToString();
                    if (category == "SKIN" || category == "ETERNALS")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + count.Value,
                            Id = lootId,
                            Count = count.Value,
                            Price = count.Value,
                            Value = disenchantValue.Value,
                            DisenchantRecipeName = recipeName,
                            IconUrl = BuildTileIconUrl(tilePath)
                        });
                    }
                    else if (category == "WARDSKIN")
                    {
                        DebugConsole.WriteLine(BuildTileIconUrlWards(tilePath) ?? "Ward icon unavailable");
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + count.Value,
                            Id = lootId,
                            Count = count.Value,
                            Price = count.Value,
                            Value = disenchantValue.Value,
                            DisenchantRecipeName = recipeName,
                            IconUrl = BuildTileIconUrlWards(tilePath)
                        });
                    }
                    else if (category == "SUMMONERICON")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + count.Value,
                            Id = lootId,
                            Count = count.Value,
                            Price = count.Value,
                            Value = disenchantValue.Value,
                            DisenchantRecipeName = recipeName,
                            IconUrl = BuildTileIconUrlSummonerIcon(tilePath)
                        });
                    }
                    else if (category == "EMOTE")
                    {
                        LootSkinsList.Add(new LootItem
                        {
                            Name = skinName + " x " + count.Value,
                            Id = lootId,
                            Count = count.Value,
                            Price = count.Value,
                            Value = disenchantValue.Value,
                            DisenchantRecipeName = recipeName,
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

    private async Task CraftSelectedLootAsync()
    {
        foreach (LootItem champ in ChampLootTable.SelectedItems)
        {
            await Lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.DisenchantRecipeName + "/craft?repeat=1", "[\"" + champ.Id + "\"]");
        }

        foreach (LootItem champ in SkinLootTable.SelectedItems)
        {
            await Lcu.Connector("league", "post",
                "/lol-loot/v1/recipes/" + champ.DisenchantRecipeName + "/craft?repeat=1", "[\"" + champ.Id + "\"]");
        }

        await UpdateLootAsync();
    }

    private async void OnDisenchantSelectedClick(object sender, RoutedEventArgs e)
    {
        await CraftSelectedLootAsync();
    }

    private void OnToggleSelectChampionsClick(object sender, RoutedEventArgs e)
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

    private void OnToggleSelectSkinsClick(object sender, RoutedEventArgs e)
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

    private void OnLootSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int oe = 0, be = 0;
        foreach (LootItem champ in ChampLootTable.SelectedItems) be += Convert.ToInt32(champ.Value);
        foreach (LootItem champ in SkinLootTable.SelectedItems) oe += Convert.ToInt32(champ.Value);

        belabel.Content = "Blue essense to be gained: " + be;
        oelabel.Content = "Orange essense to be gained: " + oe;
    }


    private static string? BuildTileIconUrl(string? tilePath)
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

    private static string? BuildTileIconUrlWards(string? tilePath)
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

    private static string? BuildTileIconUrlEmotes(string? tilePath)
    {
        if (string.IsNullOrWhiteSpace(tilePath)) return null;

        var startIndex = tilePath.IndexOf("/assets", StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0 || startIndex >= tilePath.Length) return null;

        var pathPart = tilePath[(startIndex + "/assets".Length)..].Trim();
        var lowered = pathPart.ToLowerInvariant().TrimStart('/');
        const string baseUrl = "https://raw.communitydragon.org/latest/game";
        return baseUrl + "/" + lowered;
    }

    private static string? BuildTileIconUrlSummonerIcon(string? tilePath)
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