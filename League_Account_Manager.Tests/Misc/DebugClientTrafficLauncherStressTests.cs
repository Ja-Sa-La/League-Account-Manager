using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class DebugClientTrafficLauncherStressTests
{
    [TestMethod]
    public async Task ForwardProxy_ForwardsConcurrentRequestsWithDelayedUpstream()
    {
        using var upstream = new HttpListener();
        var upstreamPort = GetFreePort();
        upstream.Prefixes.Add($"http://127.0.0.1:{upstreamPort}/");
        upstream.Start();
        using var proxy = new DebugClientTrafficLauncher.ForwardProxy(
            $"http://127.0.0.1:{upstreamPort}", new HttpClient());
        proxy.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var upstreamCts = new CancellationTokenSource();
        var upstreamTask = Task.Run(async () =>
        {
            while (!upstreamCts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await upstream.GetContextAsync(); }
                catch (HttpListenerException) { break; }
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (path == "/slow")
                    await Task.Delay(300);
                var bytes = Encoding.UTF8.GetBytes(path);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });

        try
        {
            var requests = Enumerable.Range(0, 7)
                .Select(index => client.GetStringAsync($"http://127.0.0.1:{proxy.Port}/fast/{index}"))
                .Append(client.GetStringAsync($"http://127.0.0.1:{proxy.Port}/slow"))
                .ToArray();

            var responses = await Task.WhenAll(requests);

            Assert.HasCount(8, responses);
            StringAssert.Contains(responses[^1], "/slow");
            Assert.IsTrue(responses.Take(7).All(response => response.StartsWith("/fast/")));
        }
        finally
        {
            upstreamCts.Cancel();
            upstream.Stop();
            await upstreamTask;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}