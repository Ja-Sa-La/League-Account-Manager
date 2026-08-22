using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Windows;
using League_Account_Manager;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.Misc;

public class Updates
{
    private const string StableDownloadUrl =
        "https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest/download/League_Account_Manager.exe";
    private const string StableReleaseUrl =
        "https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest";
    private static readonly string ApplicationDirectory = AppContext.BaseDirectory;
    private static readonly string ApplicationPath = Environment.ProcessPath ??
                                                     Path.Combine(ApplicationDirectory, "League_Account_Manager.exe");
    private const string UpdateCompletionFileName = "update-complete.json";

    public static async Task UpdateCheckAsync()
    {
        var temporaryUpdatePath = Path.Combine(ApplicationDirectory, "temp_update.exe");
        try
        {
            if (File.Exists(temporaryUpdatePath))
            {
                File.Delete(temporaryUpdatePath);
                LogManager.GetCurrentClassLogger().Info("Temporary update file removed");

                var completedUpdate = TakeUpdateCompletion(ApplicationDirectory);
                var mainWindow = Application.Current?.MainWindow as MainWindow;
                if (completedUpdate != null && mainWindow != null)
                    await mainWindow.ShowUpdatedModalAsync(completedUpdate.Version, completedUpdate.Channel,
                        completedUpdate.PatchNotes);
                else
                    Notif.notificationManager.Show("Update!", "League Account Manager was updated successfully",
                        NotificationType.Notification);
            }

            using var updateClient = new HttpClient();
            updateClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true
            };

            using var response = await updateClient.GetAsync(
                "https://raw.githubusercontent.com/Ja-Sa-La/League-Account-Manager/master/Version");
            response.EnsureSuccessStatusCode();
            var responseBody = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            var release = SelectRelease(responseBody, Settings.settingsloaded.ReleaseChannel, currentVersion);
            if (release != null)
            {
                release.PatchNotes = await GetPatchNotesAsync(updateClient, release.ReleaseUrl).ConfigureAwait(true);
                var mainWindow = Application.Current?.MainWindow as MainWindow;
                var result = mainWindow == null
                    ? MessageBoxResult.Cancel
                    : await mainWindow.ShowUpdateModalAsync(release.Version, release.Channel, release.PatchNotes,
                        true,
                        () => _ = UpdateAndRestartAsync(release),
                        () => LaunchUpdate(release.ReleaseUrl));
                DebugConsole.WriteLine($"[Updates] Update dialog closed with result: {result}");
                LogManager.GetCurrentClassLogger().Info("{Channel} update available: {Version}", release.Channel,
                    release.Version);
            }
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Warn(ex, "Unable to check for updates");
        }
    }

    public static void launchupdate()
    {
        LaunchUpdate(StableReleaseUrl);
    }

    private static void LaunchUpdate(string releaseUrl)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUrl,
            UseShellExecute = true
        });
    }

    internal static UpdateRelease? SelectRelease(JObject manifest, string? configuredChannel,
        string? currentVersion)
    {
        var channel = Enum.TryParse<UpdateReleaseChannel>(configuredChannel, true, out var parsedChannel)
            ? parsedChannel
            : UpdateReleaseChannel.Stable;
        if (!Version.TryParse(currentVersion, out var current))
            return null;

        var releases = new List<UpdateRelease?>
        {
            ReadRelease(manifest, channel)
        };
        if (channel == UpdateReleaseChannel.Beta)
            releases.Add(ReadRelease(manifest, UpdateReleaseChannel.Stable));

        return releases
            .Where(release => release != null && Version.Parse(release.Version) > current)
            .OrderByDescending(release => Version.Parse(release!.Version))
            .FirstOrDefault();
    }

    private static UpdateRelease? ReadRelease(JObject manifest, UpdateReleaseChannel channel)
    {
        var channelName = channel.ToString();
        var channelData = manifest[channelName] as JObject;
        var availableVersion = channelData?["Version"]?.Value<string>();
        if (channel == UpdateReleaseChannel.Stable && string.IsNullOrWhiteSpace(availableVersion))
            availableVersion = manifest["Version"]?.Value<string>();
        if (!Version.TryParse(availableVersion, out _))
            return null;

        var downloadUrl = channelData?["DownloadUrl"]?.Value<string>();
        var releaseUrl = channelData?["ReleaseUrl"]?.Value<string>();
        if (channel == UpdateReleaseChannel.Beta &&
            (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(releaseUrl)))
            return null;
        return new UpdateRelease(channelName, availableVersion!, downloadUrl ?? StableDownloadUrl,
            releaseUrl ?? StableReleaseUrl);
    }

    private static async Task<string> GetPatchNotesAsync(HttpClient client, string releaseUrl)
    {
        try
        {
                        var apiUrl = releaseUrl.EndsWith("/latest", StringComparison.OrdinalIgnoreCase)
                                ? "https://api.github.com/repos/Ja-Sa-La/League-Account-Manager/releases/latest"
                                : $"https://api.github.com/repos/Ja-Sa-La/League-Account-Manager/releases/tags/" +
                                    Uri.EscapeDataString(releaseUrl[(releaseUrl.LastIndexOf('/') + 1)..]);
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("League-Account-Manager", "1.0"));
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return "Patch notes are not available for this release.";

            var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))["body"]?.Value<string>();
            return string.IsNullOrWhiteSpace(body)
                ? "No patch notes were provided for this release."
                : body.Trim();
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Debug(ex, "Unable to load release patch notes");
            return "Patch notes are not available right now.";
        }
    }

    public static void FinishUpdate(string applicationPath)
    {
        var temporaryUpdatePath = Path.Combine(ApplicationDirectory, "temp_update.exe");
        const int maxAttempts = 40;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
            try
            {
                File.Copy(temporaryUpdatePath, applicationPath, true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = applicationPath,
                    UseShellExecute = true
                });
                Environment.Exit(0);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts - 1)
                {
                    LogManager.GetCurrentClassLogger().Error(ex, "Unable to finish application update");
                    return;
                }

                Thread.Sleep(250);
            }
    }

    internal static void SaveUpdateCompletion(string directory, UpdateCompletion completion)
    {
        var completionPath = Path.Combine(directory, UpdateCompletionFileName);
        File.WriteAllText(completionPath, JsonConvert.SerializeObject(completion));
    }

    internal static UpdateCompletion? TakeUpdateCompletion(string directory)
    {
        var completionPath = Path.Combine(directory, UpdateCompletionFileName);
        if (!File.Exists(completionPath))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<UpdateCompletion>(File.ReadAllText(completionPath));
        }
        finally
        {
            File.Delete(completionPath);
        }
    }

    private static async Task UpdateAndRestartAsync(UpdateRelease release)
    {
        var downloadPath = Path.Combine(ApplicationDirectory, "temp_update.exe");
        try
        {
            using var client = new HttpClient();
            var updateBytes = await client.GetByteArrayAsync(release.DownloadUrl);
            await File.WriteAllBytesAsync(downloadPath, updateBytes);
            SaveUpdateCompletion(ApplicationDirectory,
                new UpdateCompletion(release.Version, release.Channel, release.PatchNotes));

            var startInfo = new ProcessStartInfo
            {
                FileName = downloadPath,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("--finish-update");
            startInfo.ArgumentList.Add(ApplicationPath);
            Process.Start(startInfo);

            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error loading data");
        }
    }
}

internal enum UpdateReleaseChannel
{
    Stable,
    Beta
}

internal sealed record UpdateRelease(string Channel, string Version, string DownloadUrl, string ReleaseUrl)
{
    public string PatchNotes { get; set; } = string.Empty;
}

internal sealed record UpdateCompletion(string Version, string Channel, string PatchNotes);