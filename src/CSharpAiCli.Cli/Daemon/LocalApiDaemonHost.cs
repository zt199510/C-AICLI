using System.Net;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CSharpAiCli.Cli;

public static class LocalApiDaemonHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(
        CliEnvironmentSnapshot snapshot,
        int port,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxConcurrentConnections = 16;
                options.Limits.MaxConcurrentUpgradedConnections = 0;
                options.Limits.MaxRequestBodySize = 16 * 1024;
                options.Limits.MaxRequestHeaderCount = 32;
                options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
                options.Listen(IPAddress.Loopback, port);
            });

            await using WebApplication app = builder.Build();
            ConfigurePipeline(app, snapshot, port);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            output.WriteLine($"daemon: started (Preview, read-only) http://{LocalApiPreviewConstants.BindAddress}:{port}");
            output.WriteLine("Press Ctrl+C to stop.");
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            output.WriteLine("daemon start failed: the localhost listener could not be started.");
            return 1;
        }
    }

    private static void ConfigurePipeline(WebApplication app, CliEnvironmentSnapshot snapshot, int port)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (!IsAllowedHost(context.Request.Host.Host))
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "invalid-host",
                    "Host must be localhost or 127.0.0.1 for the local API Preview.").ConfigureAwait(false);
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method))
            {
                await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "method-not-allowed",
                    "Only GET is supported by the read-only API Preview.").ConfigureAwait(false);
                return;
            }

            if (context.Request.ContentLength is > 0)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "request-body-not-supported",
                    "Request bodies are not supported by the read-only API Preview.").ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        app.MapGet("/v1/health", () => Results.Json(new
        {
            schemaVersion = LocalApiPreviewConstants.SchemaVersion,
            type = "daemon.health",
            status = "ready",
            preview = true,
            readOnly = true,
            bindAddress = LocalApiPreviewConstants.BindAddress,
            port,
            controlRoutes = "deferred",
            sse = "deferred",
        }, JsonOptions));

        app.MapGet("/v1/jobs", (HttpContext context) =>
        {
            if (!TryReadLimit(context, out int limit))
            {
                return ApiError(StatusCodes.Status400BadRequest, "invalid-limit",
                    $"limit must be between 1 and {LocalApiPreviewConstants.MaximumListLimit}.");
            }

            JobRecordListResult result = JobRecordStore.Create(snapshot).List(limit);
            return RenderJson(writer => new JobsJsonRenderer(writer).WriteList(result));
        });

        app.MapGet("/v1/jobs/{jobId}", (string jobId) =>
        {
            JobRecordReadResult result = JobRecordStore.Create(snapshot).Read(jobId);
            return result.Succeeded && result.Record is not null
                ? RenderJson(writer => new JobsJsonRenderer(writer).WriteShow(result.Record))
                : ApiError(StatusCodes.Status404NotFound,
                    result.Diagnostic?.ErrorCode ?? "job-not-found",
                    result.Diagnostic?.Summary ?? "Job record was not found.");
        });

        app.MapGet("/v1/queue", (HttpContext context) =>
        {
            if (!TryReadLimit(context, out int limit))
            {
                return ApiError(StatusCodes.Status400BadRequest, "invalid-limit",
                    $"limit must be between 1 and {LocalApiPreviewConstants.MaximumListLimit}.");
            }

            string? status = context.Request.Query["status"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(status) && !TaskQueueStatus.IsKnown(status))
            {
                return ApiError(StatusCodes.Status400BadRequest, "invalid-queue-status",
                    "status must be pending, running, succeeded, failed, or canceled.");
            }

            TaskQueueListResult result = TaskQueueStore.Create(snapshot).List(limit, status);
            return RenderJson(writer => new TaskQueueJsonRenderer(writer).WriteList(result));
        });

        app.MapGet("/v1/queue/{queueId}", (string queueId) =>
        {
            TaskQueueReadResult result = TaskQueueStore.Create(snapshot).Read(queueId);
            return result.Succeeded && result.Item is not null
                ? RenderJson(writer => new TaskQueueJsonRenderer(writer).WriteShow(result.Item))
                : ApiError(StatusCodes.Status404NotFound,
                    result.Diagnostic?.ErrorCode ?? TaskQueueErrorCode.NotFound,
                    result.Diagnostic?.Summary ?? "Queue item was not found.");
        });

        app.MapFallback(() => ApiError(StatusCodes.Status404NotFound, "route-not-found",
            "The requested API Preview route was not found."));
    }

    private static bool IsAllowedHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, LocalApiPreviewConstants.BindAddress, StringComparison.Ordinal);

    private static bool TryReadLimit(HttpContext context, out int limit)
    {
        string? value = context.Request.Query["limit"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            limit = LocalApiPreviewConstants.DefaultListLimit;
            return true;
        }

        return int.TryParse(value, out limit) &&
            limit is >= 1 and <= LocalApiPreviewConstants.MaximumListLimit;
    }

    private static IResult RenderJson(Action<TextWriter> render)
    {
        using StringWriter writer = new();
        render(writer);
        return Results.Text(writer.ToString().TrimEnd(), "application/json", Encoding.UTF8);
    }

    private static IResult ApiError(int statusCode, string errorCode, string summary) =>
        Results.Json(new
        {
            schemaVersion = LocalApiPreviewConstants.SchemaVersion,
            type = "api.error",
            status = "failed",
            errorCode = DiagnosticSecretRedactor.Redact(errorCode),
            summary = DiagnosticSecretRedactor.Redact(summary),
        }, JsonOptions, statusCode: statusCode);

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string summary)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            schemaVersion = LocalApiPreviewConstants.SchemaVersion,
            type = "api.error",
            status = "failed",
            errorCode,
            summary,
        }, JsonOptions)).ConfigureAwait(false);
    }
}
