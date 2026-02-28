using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using static League_Account_Manager.Misc.Lcu;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for MiscTools.xaml
/// </summary>
public partial class MiscTools : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static int RemoveFriendsChoice = new();

    private readonly string[] list =
    {
        "C:\\ProgramData\\Riot Games\\",
        "C:\\Riot Games\\Riot Client\\UX\\GPUCache",
        "C:\\Users\\" + Environment.UserName + "\\AppData\\Local\\Riot Games\\",
        "C:\\Riot Games\\Riot Client\\UX\\databases-incognito",
        "C:\\Users\\" + Environment.UserName + "\\AppData\\LocalLow\\Microsoft\\CryptnetUrlCache\\",
        "C:\\Riot Games\\Riot Client\\UX\\icudtl.dat",
        "C:\\Riot Games\\League of Legends\\databases-off-the-record",
        "C:\\Riot Games\\League of Legends\\debug.log",
        "C:\\Riot Games\\League of Legends\\Logs",
        "C:\\Riot Games\\League of Legends\\Config",
        "C:\\Riot Games\\League of Legends\\icudtl.dat",
        "C:\\Riot Games\\League of Legends\\system.yaml",
        "C:\\Riot Games\\League of Legends\\snapshot_blob.bin",
        "C:\\Riot Games\\League of Legends\\natives_blob.bin",
        "C:\\Riot Games\\Riot Client\\snapshot_blob.bin",
        "C:\\Riot Games\\Riot Client\\natives_blob.bin",
        "C:\\Riot Games\\Riot Client\\UX\\icudtl.dat",
        "C:\\Riot Games\\Riot Client\\UX\\v8_context_snapshot.bin",
        "C:\\Riot Games\\Riot Client\\UX\\snapshot_blob.bin",
        "C:\\Riot Games\\Riot Client\\UX\\natives_blob.bin",
        "C:\\Riot Games\\League of Legends\\DATA",
        "C:\\Riot Games\\League of Legends\\v8_context_snapshot.bin"
    };

    public MiscTools()
    {
        InitializeComponent();
    }

    private void OnNukeLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var deletionLog = "Files \n";
            Utils.KillLeagueFunc();
            DeleteFilesAndFolders(list, deletionLog);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed while cleaning logs");
        }
    }

    public void DeleteFilesAndFolders(string[] paths, string deletionLog)
    {
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        deletionLog = deletionLog + "Deleted Item: " + path + "\n";
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        deletionLog = deletionLog + "Deleted Item: " + path + "\n";
                    }
                    else
                    {
                        deletionLog = deletionLog + "Failed to delete item or item does not exist: " + path +
                                      "\n";
                    }
                }
                catch (Exception e)
                {
                    deletionLog = deletionLog + "Failed to delete item or item does not exist: " + path +
                                  " , make sure that LAM is running as admin\n";
                }

                success.Text = deletionLog;
            }

            deletionLog = deletionLog + "LOGS HAVE BEEN CLEANED!!!";
            success.Text = deletionLog;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed deleting items during log cleanup");
        }
    }

    private async void OnNukeFriendsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            new RemoveFriendsConfirmation().ShowDialog();
            if (RemoveFriendsChoice == 1)
            {
                var deletionLog = "Friends \n";
                var resp = await Connector("league", "get", "/lol-chat/v1/friends", "");
                if (resp.ToString() == "0")
                {
                    Notif.notificationManager.Show("Error", "League of legends client is not running!",
                        NotificationType.Notification, "WindowArea", onClick: Notif.donothing);
                    return;
                }

                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rankedinfo = JArray.Parse(responseBody2);
                foreach (var friend in rankedinfo)
                {
                    resp = await Connector("league", "delete", "/lol-chat/v1/friends/" + friend["id"], "");
                    deletionLog = deletionLog + "Deleted Friend: " + friend["gameName"] + "\n";
                    success.Text = deletionLog;
                    Thread.Sleep(400);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load friend list for deletion");
        }
    }

    private async void OnGetFriendsClick(object sender, RoutedEventArgs e)
    {
        var friendlist = "";
        var resp = await Connector("league", "get", "/lol-chat/v1/friends", "");
        if (resp.ToString() == "0")
        {
            Notif.notificationManager.Show("Error", "League of legends client is not running!",
                NotificationType.Notification, "WindowArea", onClick: Notif.donothing);
            return;
        }

        var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        JArray rankedinfo = JArray.Parse(responseBody2);
        foreach (var friend in rankedinfo)
            try
            {
                var resp2 = await Connector("league", "get",
                    "/lol-match-history/v1/products/lol/" + friend["puuid"] +
                    "/matches?begIndex=0&endIndex=0", "");
                var Game = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rankedinfo2 = JObject.Parse(Game);
                if (rankedinfo2["games"]["gameCount"] == 0)
                {
                    friendlist = friendlist + "Friend name: " + friend["name"] + " ,RiotID: " +
                                 friend["gameName"] + "#" + friend["gameTag"] + " ,LastPlayed: " +
                                 "Inactive account" + "\n";
                    success.Text = friendlist;
                }
                else
                {
                    var date = (long)rankedinfo2["games"]["games"][0]["gameCreation"] / 1000;
                    friendlist = friendlist + "Friend name: " + friend["name"] + " ,RiotID: " +
                                 friend["gameName"] + "#" + friend["gameTag"] + " ,LastPlayed: " +
                                 DateTimeOffset.FromUnixTimeSeconds(date).ToString("dd/MM/yyyy") + "\n";
                    success.Text = friendlist;
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to fetch last played info for friend {FriendName}", friend?["name"]);
            }
    }

    private void OnUninstallLeagueClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Utils.KillLeagueFunc();
            var installPath = (string)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game league_of_legends.live",
                "UninstallString", null);
            if (installPath != null)
                Notif.notificationManager.Show("Error", "League of legends is not installed or missing registry keys",
                    NotificationType.Notification, "WindowArea", onClick: Notif.donothing);
            var pattern = "\"(.*?)\"";
            var match = Regex.Match(installPath, pattern);

            if (match.Success)
            {
                var pathInQuotes = match.Groups[1].Value;

                // Extract arguments after the double quotes
                var arguments = installPath.Substring(match.Length).Trim();
                Process.Start(pathInQuotes, arguments);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to launch client from registry path");
        }
    }

    private async void OnDisableAutolaunchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Connector("riot", "delete", "/startup-config/v1/registry-config", "");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to disable auto launch");
        }
    }

    private async void OnGetRiotHwidClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Connector("riot", "get", "/riotclient/machine-id", "");
            var Game = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            success.Text = "HWID = " + Game;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to retrieve Riot HWID");
        }
    }

    private async void OnRestartLeagueUxClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Connector("league", "post", "/riotclient/kill-and-restart-ux", "");
            var Game = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            success.Text = "response = " + Game;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to restart Riot UX");
        }
    }
}