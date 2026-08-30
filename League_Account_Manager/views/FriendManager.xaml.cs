using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.views;

public partial class FriendManager : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ObservableCollection<FriendEntry> _friends = new();
    private readonly ICollectionView _friendsView;

    public FriendManager()
    {
        InitializeComponent();
        _friendsView = CollectionViewSource.GetDefaultView(_friends);
        _friendsView.Filter = FilterFriend;
        FriendsGrid.ItemsSource = _friends;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (FriendsGrid.SelectedItem is not FriendEntry friend)
        {
            StatusText.Text = "Select a friend first";
            return;
        }

        if (new RemoveFriendsConfirmation { Owner = Window.GetWindow(this) }.ShowDialog() != true)
            return;

        await RemoveFriendAsync(friend);
    }

    private async void OnRemoveAllClick(object sender, RoutedEventArgs e)
    {
        var friends = _friends.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
        if (friends.Count == 0) return;
        if (new RemoveFriendsConfirmation { Owner = Window.GetWindow(this) }.ShowDialog() != true)
            return;

        RefreshButton.IsEnabled = false;
        StatusText.Text = "Removing friends...";
        try
        {
            foreach (var friend in friends)
                await RemoveFriendAsync(friend, false);
            await RefreshAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task RemoveFriendAsync(FriendEntry friend, bool refresh = true)
    {
        try
        {
            if (await Lcu.Connector("league", "delete", $"/lol-chat/v1/friends/{Uri.EscapeDataString(friend.Id)}", "")
                is not HttpResponseMessage response)
            {
                StatusText.Text = "League client unavailable";
                return;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Friend removal returned {(int)response.StatusCode}.");
            }

            if (refresh)
            {
                StatusText.Text = $"Removed {friend.DisplayName}";
                await RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to remove friend {FriendName}", friend.DisplayName);
            StatusText.Text = "Could not remove friend";
        }
    }

    private async Task EnrichLastPlayedAsync()
    {
        foreach (var friend in _friends.Where(x => !string.IsNullOrWhiteSpace(x.Puuid)).ToList())
        {
            try
            {
                if (await GetJsonAsync($"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(friend.Puuid)}/matches?begIndex=0&endIndex=1")
                    is JObject history)
                {
                    var gameCreation = history["games"]?["games"]?[0]?["gameCreation"]?.Value<long?>();
                    friend.LastPlayed = gameCreation.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(gameCreation.Value).ToLocalTime().ToString("dd MMM yyyy, HH:mm")
                        : "Inactive account";
                }
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Failed to fetch last played data for {FriendName}", friend.DisplayName);
                friend.LastPlayed = "Unavailable";
            }
        }
    }

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Loading...";
        try
        {
            var chatFriends = await GetJsonAsync("/lol-chat/v1/friends");
            var giftFriends = await GetStorefrontFriendsAsync();
            if (chatFriends == null || giftFriends == null)
            {
                StatusText.Text = "League client unavailable";
                return;
            }

            var merged = MergeFriends(
                chatFriends as JArray ?? throw new InvalidDataException("Chat friends response was not an array."),
                giftFriends["friends"] as JArray ?? throw new InvalidDataException("Storefront friends response was incomplete."));
            _friends.Clear();
            foreach (var friend in merged.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                _friends.Add(friend);

            StatusText.Text = "Loading last played...";
            await EnrichLastPlayedAsync();
            _friendsView.Refresh();
            UpdateCounts();
            StatusText.Text = $"Updated {DateTime.Now:HH:mm}";
            if (FriendsGrid.SelectedItem == null && _friendsView.Cast<FriendEntry>().FirstOrDefault() is { } first)
                FriendsGrid.SelectedItem = first;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load friends");
            StatusText.Text = "Could not load friends";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static async Task<JToken?> GetJsonAsync(string endpoint)
    {
        if (await Lcu.Connector("league", "get", endpoint, "") is not HttpResponseMessage response)
            return null;

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"LCU request {endpoint} returned {(int)response.StatusCode}.");
            return JToken.Parse(body);
        }
    }

    private static async Task<JToken?> GetStorefrontFriendsAsync()
    {
        var storeUrlResponse = await GetJsonAsync("/lol-store/v1/getStoreUrl");
        var storeUrl = storeUrlResponse?.ToString().Trim('"').TrimEnd('/');
        var tokenResponse = await GetJsonAsync("/lol-rso-auth/v1/authorization/access-token");
        var accessToken = tokenResponse?["token"]?.ToString();
        if (string.IsNullOrWhiteSpace(storeUrl) || string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidDataException("League storefront URL or access token was unavailable.");

        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        using var response = await client.GetAsync($"{storeUrl}/storefront/v3/gift/friends?language=en_US");
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Storefront request returned {(int)response.StatusCode}.");
        return JToken.Parse(body);
    }

    private static IEnumerable<FriendEntry> MergeFriends(JArray chatFriends, JArray giftFriends)
    {
        var giftBySummonerId = giftFriends
            .Select(x => x as JObject)
            .Where(x => x != null)
            .Select(x => new { Item = x!, Id = x!["summonerId"]?.ToString() })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id!, x => x.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var token in chatFriends.OfType<JObject>())
        {
            var summonerId = token["summonerId"]?.ToString() ?? string.Empty;
            giftBySummonerId.TryGetValue(summonerId, out var gift);
            var gameName = token["gameName"]?.ToString() ?? string.Empty;
            var gameTag = token["gameTag"]?.ToString() ?? string.Empty;
            var displayName = string.IsNullOrWhiteSpace(gameName)
                ? token["name"]?.ToString() ?? "Unknown friend"
                : gameName;
            var riotId = string.IsNullOrWhiteSpace(gameName) ? "" : $"{gameName}#{gameTag}";
            var availability = token["availability"]?.ToString() ?? "unknown";

            yield return new FriendEntry
            {
                Id = token["id"]?.ToString() ?? string.Empty,
                DisplayName = displayName,
                RiotId = riotId,
                Presence = FormatPresence(availability),
                LastSeen = FormatLastSeen(token["lastSeenOnlineTimestamp"]),
                FriendsSince = FormatFriendsSince(gift?["friendsSince"]),
                SummonerId = summonerId,
                Puuid = token["puuid"]?.ToString() ?? string.Empty,
                StatusMessage = token["statusMessage"]?.ToString() ?? "",
                IconUrl = GetIconUrl(token["icon"]?.Value<int?>())
            };
        }

        foreach (var gift in giftBySummonerId.Values.Where(x => !chatFriends.Any(c => c["summonerId"]?.ToString() == x["summonerId"]?.ToString())))
        {
            yield return new FriendEntry
            {
                DisplayName = gift["nick"]?.ToString() ?? "Unknown friend",
                RiotId = gift["nick"]?.ToString() ?? "",
                Presence = "Not in chat list",
                LastSeen = "-",
                FriendsSince = FormatFriendsSince(gift["friendsSince"]),
                SummonerId = gift["summonerId"]?.ToString() ?? "",
                StatusMessage = "",
                IconUrl = GetIconUrl(null)
            };
        }
    }

    private bool FilterFriend(object item)
    {
        if (item is not FriendEntry friend) return false;
        var query = SearchBox.Text.Trim();
        return string.IsNullOrWhiteSpace(query) ||
               $"{friend.DisplayName} {friend.RiotId} {friend.Presence} {friend.StatusMessage}"
                   .Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _friendsView.Refresh();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        var visible = _friendsView.Cast<FriendEntry>().ToList();
        FriendCountText.Text = $"{visible.Count} friend{(visible.Count == 1 ? "" : "s")}";
        OnlineCountText.Text = $"{visible.Count(x => x.Presence is "Online" or "Away" or "Do not disturb")} online";
    }

    private void OnFriendSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FriendsGrid.SelectedItem is not FriendEntry friend) return;
        SelectedIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(friend.IconUrl));
        SelectedName.Text = friend.DisplayName;
        SelectedRiotId.Text = friend.RiotId;
        SelectedPresence.Text = friend.Presence;
        SelectedFriendsSince.Text = friend.FriendsSince;
        SelectedSummonerId.Text = string.IsNullOrWhiteSpace(friend.SummonerId) ? "-" : friend.SummonerId;
        SelectedStatusMessage.Text = string.IsNullOrWhiteSpace(friend.StatusMessage) ? "No status message" : friend.StatusMessage;
        SelectedLastPlayed.Text = friend.LastPlayed;
        _ = LoadMessagesAsync(friend);
    }

    private async Task LoadMessagesAsync(FriendEntry friend)
    {
        MessagesList.Items.Clear();
        MessagesList.Items.Add("Loading messages...");
        try
        {
            var conversations = await GetJsonAsync("/lol-chat/v1/conversations") as JArray;
            var conversation = conversations?.OfType<JObject>().FirstOrDefault(item =>
                item["participants"]?.OfType<JObject>().Any(participant =>
                    string.Equals(participant["id"]?.ToString(), friend.Id, StringComparison.OrdinalIgnoreCase)) == true);
            var conversationId = conversation?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                MessagesList.Items.Clear();
                MessagesList.Items.Add("No conversation found.");
                return;
            }

            var messages = await GetJsonAsync(BuildMessagesEndpoint(conversationId)) as JArray;
            MessagesList.Items.Clear();
            foreach (var message in messages?.OfType<JObject>().TakeLast(50) ?? Enumerable.Empty<JObject>())
            {
                var sender = message["fromSummonerId"]?.ToString() ?? message["from"]?.ToString() ?? "Unknown";
                var body = message["body"]?.ToString() ?? string.Empty;
                var timestamp = message["timestamp"]?.ToString();
                var time = DateTime.TryParse(timestamp, out var parsed) ? parsed.ToString("HH:mm") : "";
                MessagesList.Items.Add($"{time}  {sender}: {body}".Trim());
            }

            if (MessagesList.Items.Count == 0)
                MessagesList.Items.Add("No messages yet.");
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to load messages for {FriendName}", friend.DisplayName);
            MessagesList.Items.Clear();
            MessagesList.Items.Add("Messages unavailable.");
        }
    }

    private async void OnSendMessageClick(object sender, RoutedEventArgs e)
    {
        if (FriendsGrid.SelectedItem is not FriendEntry friend || string.IsNullOrWhiteSpace(MessageBox.Text.Trim()))
            return;

        try
        {
            var conversations = await GetJsonAsync("/lol-chat/v1/conversations") as JArray;
            var conversation = conversations?.OfType<JObject>().FirstOrDefault(item =>
                item["participants"]?.OfType<JObject>().Any(participant =>
                    string.Equals(participant["id"]?.ToString(), friend.Id, StringComparison.OrdinalIgnoreCase)) == true);
            var conversationId = conversation?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                StatusText.Text = "No conversation found";
                return;
            }

            var body = MessageBox.Text.Trim();
                if (await Lcu.Connector("league", "post", BuildMessagesEndpoint(conversationId),
                    JsonConvert.SerializeObject(new { body })) is not HttpResponseMessage response)
            {
                StatusText.Text = "League client unavailable";
                return;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Message send returned {(int)response.StatusCode}.");
            }

            MessageBox.Clear();
            await LoadMessagesAsync(friend);
            StatusText.Text = "Message sent";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to send message to {FriendName}", friend.DisplayName);
            StatusText.Text = "Could not send message";
        }
    }

    private static string BuildMessagesEndpoint(string conversationId) =>
        $"/lol-chat/v1/conversations/{Uri.EscapeDataString(Uri.UnescapeDataString(conversationId))}/messages";

    private static string FormatPresence(string value) => value.ToLowerInvariant() switch
    {
        "online" => "Online",
        "away" => "Away",
        "dnd" or "busy" => "Do not disturb",
        "offline" => "Offline",
        _ => "Unknown"
    };

    private static string FormatFriendsSince(JToken? value) =>
        DateTime.TryParse(value?.ToString(), out var date) ? date.ToString("dd MMM yyyy") : "-";

    private static string FormatLastSeen(JToken? value) =>
        long.TryParse(value?.ToString(), out var timestamp)
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("dd MMM yyyy, HH:mm")
            : "-";

    private static string GetIconUrl(int? icon) => icon.HasValue
        ? $"https://ddragon.leagueoflegends.com/cdn/16.17.1/img/profileicon/{icon.Value}.png"
        : "https://ddragon.leagueoflegends.com/cdn/16.17.1/img/profileicon/29.png";

    private sealed class FriendEntry
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string RiotId { get; init; } = string.Empty;
        public string Presence { get; init; } = string.Empty;
        public string LastSeen { get; init; } = string.Empty;
        public string FriendsSince { get; init; } = string.Empty;
        public string SummonerId { get; init; } = string.Empty;
        public string Puuid { get; init; } = string.Empty;
        public string StatusMessage { get; init; } = string.Empty;
        public string LastPlayed { get; set; } = "Loading...";
        public string IconUrl { get; init; } = string.Empty;
    }
}
