using System.Net;
using System.Net.Http.Headers;

namespace Ghostwriter.Core.Providers;

/// <summary>The one long-lived HttpClient of the app: pooled, HTTP/2, kept alive, so connections can be warmed up.</summary>
public static class LlmHttp
{
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Keep the TLS connection around between uses; HTTP/2 pings stop idle NATs and servers from dropping it.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            // Timeouts are handled per request (time to first token, then gaps between chunks).
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ghostwriter", "1.0"));
        return client;
    }
}
