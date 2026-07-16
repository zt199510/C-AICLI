using System.Buffers;
using System.Globalization;
using System.Text;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

public static class DesktopProtocolFraming
{
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    public static async ValueTask<byte[]?> ReadFrameAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] header = await ReadHeaderAsync(input, cancellationToken).ConfigureAwait(false);
        if (header.Length == 0)
        {
            return null;
        }

        int contentLength = ParseContentLength(header);
        byte[] body = GC.AllocateUninitializedArray<byte>(contentLength);
        int offset = 0;
        while (offset < body.Length)
        {
            int read = await input.ReadAsync(body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new DesktopProtocolException("frame-body-incomplete", "Protocol frame body ended early.");
            }

            offset += read;
        }

        return body;
    }

    public static async ValueTask WriteFrameAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.Length > DesktopProtocolDefinition.MaxBodyBytes)
        {
            throw new DesktopProtocolException("frame-body-too-large", "Protocol frame body exceeds the configured limit.");
        }

        byte[] header = Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]> ReadHeaderAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(DesktopProtocolDefinition.MaxHeaderBytes);
        int length = 0;
        try
        {
            while (length < DesktopProtocolDefinition.MaxHeaderBytes)
            {
                int read = await input.ReadAsync(rented.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (length == 0)
                    {
                        return [];
                    }

                    throw new DesktopProtocolException("frame-header-incomplete", "Protocol frame header ended early.");
                }

                length += read;
                if (length >= HeaderTerminator.Length &&
                    rented.AsSpan(length - HeaderTerminator.Length, HeaderTerminator.Length)
                        .SequenceEqual(HeaderTerminator))
                {
                    return rented.AsSpan(0, length - HeaderTerminator.Length).ToArray();
                }
            }

            throw new DesktopProtocolException("frame-header-too-large", "Protocol frame header exceeds the configured limit.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int ParseContentLength(byte[] headerBytes)
    {
        string header = Encoding.ASCII.GetString(headerBytes);
        int? contentLength = null;
        foreach (string line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new DesktopProtocolException("frame-header-invalid", "Protocol frame header is malformed.");
            }

            string name = line[..separator].Trim();
            if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (contentLength.HasValue ||
                !int.TryParse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < 0)
            {
                throw new DesktopProtocolException("frame-content-length-invalid", "Protocol content length is invalid.");
            }

            contentLength = parsed;
        }

        if (!contentLength.HasValue)
        {
            throw new DesktopProtocolException("frame-content-length-missing", "Protocol content length is required.");
        }

        if (contentLength.Value > DesktopProtocolDefinition.MaxBodyBytes)
        {
            throw new DesktopProtocolException("frame-body-too-large", "Protocol frame body exceeds the configured limit.");
        }

        return contentLength.Value;
    }
}
