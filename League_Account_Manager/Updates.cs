using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager;

public class Updates
{
    public static async void updatecheck()
    {
        var updatecheck = new HttpClient();

        updatecheck.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true
        };

        dynamic response =
            await updatecheck.GetAsync(
                "https://raw.githubusercontent.com/Ja-Sa-La/League-Account-Manager/master/Version");
        var responseBody2 = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (responseBody2["Version"] != Assembly.GetExecutingAssembly().GetName().Version.ToString())
        {
            string msg = "New update " + responseBody2["Version"] +
                         " is available, click here to download the new version!";
            notif.notificationManager.Show("Update!", msg, NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), () => launchupdate());
            LogManager.GetCurrentClassLogger().Info("Update available");
        }

        updatecheck.Dispose();
    }

    public static void launchupdate()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest",
            UseShellExecute = true
        });
    }
}