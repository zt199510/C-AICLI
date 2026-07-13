using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class LocalApiDaemonClient
{
    public static async Task<int> SmokeAsync(
        int port,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        using HttpClient client = new(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://{LocalApiPreviewConstants.BindAddress}:{port}"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        try
        {
            JsonObject? health = await client.GetFromJsonAsync<JsonObject>("/v1/health", cancellationToken)
                .ConfigureAwait(false);
            bool valid = health is not null &&
                string.Equals(health["type"]?.GetValue<string>(), "daemon.health", StringComparison.Ordinal) &&
                string.Equals(health["status"]?.GetValue<string>(), "ready", StringComparison.Ordinal) &&
                health["preview"]?.GetValue<bool>() == true &&
                health["readOnly"]?.GetValue<bool>() == true &&
                string.Equals(
                    health["bindAddress"]?.GetValue<string>(),
                    LocalApiPreviewConstants.BindAddress,
                    StringComparison.Ordinal);
            if (!valid)
            {
                output.WriteLine("api smoke failed: daemon health response did not match the read-only Preview contract.");
                return 1;
            }

            output.WriteLine($"api smoke passed: read-only Preview is ready on {LocalApiPreviewConstants.BindAddress}:{port}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            output.WriteLine("api smoke failed: the localhost Preview did not return a valid health response.");
            return 1;
        }
    }
}
