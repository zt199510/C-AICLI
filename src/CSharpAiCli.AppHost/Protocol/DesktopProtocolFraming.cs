using System.Buffers;
using System.Globalization;
using System.Text;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

public static class DesktopProtocolFraming
{
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const string ReviewedContentType = "application/vscode-jsonrpc; charset=utf-8";

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
                throw new DesktopProtocolException(
                    DesktopProtocolDefinition.FrameBodyIncompleteError,
                    "Protocol frame body ended early.");
            }

            offset += read;
        }

        ValidateUtf8(body);

        return body;
    }

    public static async ValueTask WriteFrameAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.IsEmpty)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameBodyEmptyError,
                "Protocol frame body must not be empty.");
        }

        if (payload.Length > DesktopProtocolDefinition.MaxBodyBytes)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameBodyTooLargeError,
                "Protocol frame body exceeds the configured limit.");
        }

        ValidateUtf8(payload.Span);

        byte[] header = Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]> ReadHeaderAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(
            DesktopProtocolDefinition.MaxHeaderBytes + HeaderTerminator.Length);
        int length = 0;
        try
        {
            while (length < DesktopProtocolDefinition.MaxHeaderBytes + HeaderTerminator.Length)
            {
                int read = await input.ReadAsync(rented.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (length == 0)
                    {
                        return [];
                    }

                    throw new DesktopProtocolException(
                        DesktopProtocolDefinition.FrameHeaderIncompleteError,
                        "Protocol frame header ended early.");
                }

                byte current = rented[length];
                if (current > 0x7f || current == 0x7f ||
                    (current < 0x20 && current is not (byte)'\r' and not (byte)'\n'))
                {
                    throw new DesktopProtocolException(
                        DesktopProtocolDefinition.FrameHeaderInvalidError,
                        "Protocol frame header is malformed.");
                }

                if (current == (byte)'\n' && (length == 0 || rented[length - 1] != (byte)'\r'))
                {
                    throw new DesktopProtocolException(
                        DesktopProtocolDefinition.FrameHeaderInvalidError,
                        "Protocol frame header is malformed.");
                }

                if (length > 0 && rented[length - 1] == (byte)'\r' && current != (byte)'\n')
                {
                    throw new DesktopProtocolException(
                        DesktopProtocolDefinition.FrameHeaderInvalidError,
                        "Protocol frame header is malformed.");
                }

                length += read;
                if (length >= HeaderTerminator.Length &&
                    rented.AsSpan(length - HeaderTerminator.Length, HeaderTerminator.Length)
                        .SequenceEqual(HeaderTerminator))
                {
                    int headerLength = length - HeaderTerminator.Length;
                    if (headerLength > DesktopProtocolDefinition.MaxHeaderBytes)
                    {
                        throw new DesktopProtocolException(
                            DesktopProtocolDefinition.FrameHeaderTooLargeError,
                            "Protocol frame header exceeds the configured limit.");
                    }

                    return rented.AsSpan(0, headerLength).ToArray();
                }
            }

            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameHeaderTooLargeError,
                "Protocol frame header exceeds the configured limit.");
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
        bool contentTypeSeen = false;
        foreach (string line in header.Split("\r\n", StringSplitOptions.None))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new DesktopProtocolException(
                    DesktopProtocolDefinition.FrameHeaderInvalidError,
                    "Protocol frame header is malformed.");
            }

            string name = line[..separator];
            string value = line[(separator + 1)..].Trim(' ');
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (contentTypeSeen || !string.Equals(value, ReviewedContentType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DesktopProtocolException(
                        DesktopProtocolDefinition.FrameHeaderInvalidError,
                        "Protocol frame header is malformed.");
                }

                contentTypeSeen = true;
                continue;
            }

            if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                throw new DesktopProtocolException(
                    DesktopProtocolDefinition.FrameHeaderInvalidError,
                    "Protocol frame header is malformed.");
            }

            if (contentLength.HasValue ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < 0)
            {
                throw new DesktopProtocolException(
                    DesktopProtocolDefinition.FrameContentLengthInvalidError,
                    "Protocol content length is invalid.");
            }

            contentLength = parsed;
        }

        if (!contentLength.HasValue)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameContentLengthMissingError,
                "Protocol content length is required.");
        }

        if (contentLength.Value > DesktopProtocolDefinition.MaxBodyBytes)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameBodyTooLargeError,
                "Protocol frame body exceeds the configured limit.");
        }

        if (contentLength.Value == 0)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameBodyEmptyError,
                "Protocol frame body must not be empty.");
        }

        return contentLength.Value;
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> payload)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(payload);
        }
        catch (DecoderFallbackException)
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.FrameUtf8InvalidError,
                "Protocol frame body is not valid UTF-8.");
        }
    }
}
