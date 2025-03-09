using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.Misc;

public class Updates
{
    public static async void updatecheck()
    {
        var updatecheck = new HttpClient();
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "temp_update.exe")))
        {
            File.Delete(Path.Combine(Environment.CurrentDirectory, "temp_update.exe"));
            Thread.Sleep(500);
            Notif.notificationManager.Show("Update!", "League Account Manager was updated successfully",
                NotificationType.Notification);
            LogManager.GetCurrentClassLogger().Info("File removed");
        }

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
            Notif.notificationManager.Show("Update!", msg, NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, () => UpdateAndRestart(), "Update now!",
                () => launchupdate(), "Go to github", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
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

    public static void FinishUpdate()
    {
        var currentExePath = Path.Combine(Environment.CurrentDirectory, "temp_update.exe");
        while (true)
            try
            {
                var processName = "League_Account_Manager.exe";

                // Find the process by name
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length > 0)
                    // Terminate the process
                    foreach (var process in processes)
                        process.Kill();
                File.Copy(currentExePath, Path.Combine(Environment.CurrentDirectory, "League_Account_Manager.exe"),
                    true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.CurrentDirectory, "League_Account_Manager.exe"),
                    UseShellExecute = true
                });
                break;
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error(ex, "Error loading data");
            }

        Environment.Exit(1);
    }

    private static void UpdateAndRestart()
    {
        var downloadUrl =
            "https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest/download/League_Account_Manager.exe";
        var downloadPath = Path.Combine(Environment.CurrentDirectory, "temp_update.exe");
        var currentExePath = Environment.ProcessPath;
        var backupExePath = currentExePath + ".backup";
        try
        {
            using (var client = new WebClient())
            {
                client.DownloadFile(downloadUrl, downloadPath);
            }

            var ps = Process.Start(new ProcessStartInfo
            {
                FileName = downloadPath,
                UseShellExecute = true
            });

            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error loading data");
        }
    }
}