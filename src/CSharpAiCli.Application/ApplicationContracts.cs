using System.Collections.ObjectModel;
using System.Text;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public static class ApplicationErrorCategory
{
    public const string Validation = "validation";
    public const string Workspace = "workspace";
    public const string NotFound = "not-found";
    public const string Denied = "denied";
    public const string Conflict = "conflict";
    public const string CorruptState = "corrupt-state";
    public const string LimitExceeded = "limit-exceeded";
    public const string Unavailable = "unavailable";
    public const string Internal = "internal";

    public static bool IsKnown(string? value) => value is Validation
        or Workspace
        or NotFound
        or Denied
        or Conflict
        or CorruptState
        or LimitExceeded
        or Unavailable
        or Internal;
}

public static class ApplicationLimits
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxCatalogItems = 200;
    public const int MaxChangedFiles = 500;
    public const int MaxReportSummaryBytes = 256 * 1024;
    public const int MaxDiagnostics = 100;
    public const int MaxDiagnosticBytes = 4 * 1024;
    public const int TargetAggregateBytes = 768 * 1024;

    internal static ApplicationError? ValidatePageSize(int pageSize)
    {
        return pageSize is >= 1 and <= MaxPageSize
            ? null
            : new ApplicationError(
                "application-page-size-invalid",
                ApplicationErrorCategory.Validation,
                $"Page size must be between 1 and {MaxPageSize}.",
                Retryable: false);
    }
}

public sealed record ApplicationError
{
    public ApplicationError(string Code, string Category, string SafeMessage, bool Retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        if (!ApplicationErrorCategory.IsKnown(Category))
        {
            throw new ArgumentException("Application error category is invalid.", nameof(Category));
        }

        this.Code = ApplicationProjection.Safe(Code, 256);
        this.Category = Category;
        this.SafeMessage = ApplicationProjection.Safe(SafeMessage, ApplicationLimits.MaxDiagnosticBytes);
        this.Retryable = Retryable;
    }

    public string Code { get; }

    public string Category { get; }

    public string SafeMessage { get; }

    public bool Retryable { get; }
}

public sealed record ApplicationDiagnostic
{
    public ApplicationDiagnostic(string Code, string Category, string SafeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        if (!ApplicationErrorCategory.IsKnown(Category))
        {
            throw new ArgumentException("Application diagnostic category is invalid.", nameof(Category));
        }

        this.Code = ApplicationProjection.Safe(Code, 256);
        this.Category = Category;
        this.SafeMessage = ApplicationProjection.Safe(SafeMessage, ApplicationLimits.MaxDiagnosticBytes);
    }

    public string Code { get; }

    public string Category { get; }

    public string SafeMessage { get; }
}

public sealed record ApplicationResult<T>
{
    private ApplicationResult(
        bool Succeeded,
        T? Data,
        ApplicationError? Error,
        IReadOnlyList<ApplicationDiagnostic>? Diagnostics,
        bool Truncated)
    {
        this.Succeeded = Succeeded;
        this.Data = Data;
        this.Error = Error;
        this.Diagnostics = new ReadOnlyCollection<ApplicationDiagnostic>((Diagnostics ?? [])
            .Take(ApplicationLimits.MaxDiagnostics)
            .ToArray());
        this.Truncated = Truncated || (Diagnostics?.Count ?? 0) > ApplicationLimits.MaxDiagnostics;
    }

    public bool Succeeded { get; }

    public T? Data { get; }

    public ApplicationError? Error { get; }

    public IReadOnlyList<ApplicationDiagnostic> Diagnostics { get; }

    public bool Truncated { get; }

    public static ApplicationResult<T> Success(
        T data,
        IReadOnlyList<ApplicationDiagnostic>? diagnostics = null,
        bool truncated = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ApplicationResult<T>(true, data, null, diagnostics, truncated);
    }

    public static ApplicationResult<T> Failure(
        ApplicationError error,
        IReadOnlyList<ApplicationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApplicationResult<T>(false, default, error, diagnostics, false);
    }
}

internal static class ApplicationProjection
{
    public static string Safe(string? value, int maxBytes = ApplicationLimits.MaxDiagnosticBytes)
    {
        string redacted = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        if (Encoding.UTF8.GetByteCount(redacted) <= maxBytes)
        {
            return redacted;
        }

        int low = 0;
        int high = redacted.Length;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (Encoding.UTF8.GetByteCount(redacted.AsSpan(0, middle)) <= maxBytes)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low > 0 && char.IsHighSurrogate(redacted[low - 1]))
        {
            low--;
        }

        return redacted[..low];
    }

    public static string? SafeOrNull(string? value, int maxBytes = ApplicationLimits.MaxDiagnosticBytes) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxBytes);

    public static string CategoryForCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ApplicationErrorCategory.Internal;
        }

        if (code.Contains("not-found", StringComparison.Ordinal) ||
            code.Contains("missing", StringComparison.Ordinal))
        {
            return ApplicationErrorCategory.NotFound;
        }

        if (code.Contains("denied", StringComparison.Ordinal) ||
            code.Contains("unsafe", StringComparison.Ordinal) ||
            code.Contains("reparse", StringComparison.Ordinal) ||
            code.Contains("external", StringComparison.Ordinal))
        {
            return ApplicationErrorCategory.Denied;
        }

        if (code.Contains("conflict", StringComparison.Ordinal) ||
            code.Contains("mismatch", StringComparison.Ordinal) ||
            code.Contains("changed", StringComparison.Ordinal))
        {
            return ApplicationErrorCategory.Conflict;
        }

        if (code.Contains("corrupt", StringComparison.Ordinal) ||
            code.Contains("schema", StringComparison.Ordinal) ||
            code.Contains("invalid", StringComparison.Ordinal))
        {
            return ApplicationErrorCategory.CorruptState;
        }

        return ApplicationErrorCategory.Unavailable;
    }
}
