using System.Text.Json;

namespace CSharpAiCli.Core;

public static class LocalApiPreviewConstants
{
    public const int SchemaVersion = 1;
    public const int DefaultPort = 8787;
    public const int MinimumPort = 1024;
    public const int MaximumPort = 65535;
    public const int DefaultListLimit = 50;
    public const int MaximumListLimit = 100;
    public const string BindAddress = "127.0.0.1";
    public const string Status = "preview";
}

public sealed record LocalApiRouteContract(
    string Method,
    string Route,
    string OperationId,
    string Access,
    string Source,
    string Summary);

public static class LocalApiRouteCatalog
{
    private static readonly IReadOnlyList<LocalApiRouteContract> PreviewRoutes =
    [
        new("GET", "/v1/health", "health.get", "read-only", "daemon runtime",
            "Return bounded daemon readiness and Preview metadata."),
        new("GET", "/v1/jobs", "jobs.list", "read-only", "JobRecordStore + JobsJsonRenderer",
            "List bounded, redacted local job metadata."),
        new("GET", "/v1/jobs/{jobId}", "jobs.show", "read-only", "JobRecordStore + JobsJsonRenderer",
            "Read one bounded, redacted local job record."),
        new("GET", "/v1/queue", "queue.list", "read-only", "TaskQueueStore + TaskQueueJsonRenderer",
            "List bounded, redacted local queue metadata."),
        new("GET", "/v1/queue/{queueId}", "queue.show", "read-only", "TaskQueueStore + TaskQueueJsonRenderer",
            "Read one bounded, redacted local queue item."),
    ];

    public static IReadOnlyList<LocalApiRouteContract> Routes => PreviewRoutes;
}

public static class LocalApiDaemonBindPolicy
{
    public static bool TryNormalize(string? requestedBind, out string bindAddress)
    {
        string value = string.IsNullOrWhiteSpace(requestedBind)
            ? "localhost"
            : requestedBind.Trim();
        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, LocalApiPreviewConstants.BindAddress, StringComparison.Ordinal))
        {
            bindAddress = LocalApiPreviewConstants.BindAddress;
            return true;
        }

        bindAddress = string.Empty;
        return false;
    }

    public static bool IsValidPort(int port) =>
        port is >= LocalApiPreviewConstants.MinimumPort and <= LocalApiPreviewConstants.MaximumPort;
}

public sealed class LocalApiDaemonTextRenderer
{
    private readonly TextWriter output;

    public LocalApiDaemonTextRenderer(TextWriter output)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void WriteDoctor()
    {
        output.WriteLine("local API daemon doctor");
        output.WriteLine($"status: {LocalApiPreviewConstants.Status}");
        output.WriteLine("defaultEnabled: false");
        output.WriteLine("startRequiresPreview: true");
        output.WriteLine($"bind: {LocalApiPreviewConstants.BindAddress} only");
        output.WriteLine($"defaultPort: {LocalApiPreviewConstants.DefaultPort}");
        output.WriteLine("access: read-only");
        output.WriteLine("authentication: none");
        output.WriteLine("tls: none");
        output.WriteLine("controlRoutes: deferred");
        output.WriteLine("sse: deferred");
    }
}

public sealed class LocalApiPreviewTextRenderer
{
    private readonly TextWriter output;

    public LocalApiPreviewTextRenderer(TextWriter output)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void WriteRoutes()
    {
        output.WriteLine("local API routes");
        output.WriteLine($"status: {LocalApiPreviewConstants.Status}");
        output.WriteLine("defaultEnabled: false");
        output.WriteLine("bind: 127.0.0.1 only");
        output.WriteLine("controlRoutes: deferred");
        output.WriteLine("sse: deferred");
        foreach (LocalApiRouteContract route in LocalApiRouteCatalog.Routes)
        {
            output.WriteLine($"- {route.Method} {route.Route} [{route.Access}] {route.OperationId}");
            output.WriteLine($"  source: {route.Source}");
            output.WriteLine($"  summary: {route.Summary}");
        }
    }
}

public sealed class LocalApiPreviewJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TextWriter output;

    public LocalApiPreviewJsonRenderer(TextWriter output)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void WriteRoutes()
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = LocalApiPreviewConstants.SchemaVersion,
            type = "api.routes",
            status = LocalApiPreviewConstants.Status,
            defaultEnabled = false,
            bindAddresses = new[] { LocalApiPreviewConstants.BindAddress },
            controlRoutes = "deferred",
            sse = "deferred",
            routes = LocalApiRouteCatalog.Routes,
        }, JsonOptions));
    }

    public void WriteDoctor()
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = LocalApiPreviewConstants.SchemaVersion,
            type = "daemon.doctor",
            status = LocalApiPreviewConstants.Status,
            defaultEnabled = false,
            startRequiresPreview = true,
            bindAddresses = new[] { LocalApiPreviewConstants.BindAddress },
            defaultPort = LocalApiPreviewConstants.DefaultPort,
            access = "read-only",
            authentication = "none",
            tls = "none",
            controlRoutes = "deferred",
            sse = "deferred",
        }, JsonOptions));
    }
}
