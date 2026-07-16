using System.Text;
using System.Text.Json;
using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.Tests;

public sealed class DesktopProtocolTests
{
    [Fact]
    public async Task Framing_round_trips_multiline_utf8_with_partial_reads()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"text\":\"line one\\nline two 中文\"}");
        using MemoryStream framed = new();
        await DesktopProtocolFraming.WriteFrameAsync(framed, payload);
        framed.Position = 0;
        using ChunkedReadStream chunks = new(framed, maxChunkSize: 2);

        byte[]? read = await DesktopProtocolFraming.ReadFrameAsync(chunks);

        Assert.Equal(payload, read);
        Assert.Null(await DesktopProtocolFraming.ReadFrameAsync(chunks));
    }

    [Theory]
    [InlineData("Content-Length: nope\r\n\r\n", "frame-content-length-invalid")]
    [InlineData("Content-Type: application/json\r\n\r\n", "frame-content-length-missing")]
    [InlineData("Content-Length: 1048577\r\n\r\n", "frame-body-too-large")]
    public async Task Framing_rejects_invalid_or_oversized_headers(string header, string expectedError)
    {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes(header));

        DesktopProtocolException exception = await Assert.ThrowsAsync<DesktopProtocolException>(
            async () => await DesktopProtocolFraming.ReadFrameAsync(stream));

        Assert.Equal(expectedError, exception.ErrorCode);
    }

    [Fact]
    public async Task Server_requires_handshake_then_opens_workspace_and_shuts_down()
    {
        using TempDirectory temp = TempDirectory.Create();
        using MemoryStream input = new();
        await WriteRequest(input, 1, DesktopProtocolDefinition.InitializeMethod, new
        {
            protocolVersion = DesktopProtocolDefinition.Version,
            clientName = "desktop-tests",
            clientVersion = "1.0.0"
        });
        await WriteRequest(input, 2, DesktopProtocolDefinition.WorkspaceOpenMethod, new { path = temp.Path });
        await WriteRequest(input, 3, DesktopProtocolDefinition.ShutdownMethod, new { reason = "test-complete" });
        input.Position = 0;
        using MemoryStream output = new();
        DesktopRpcServer server = new();

        await server.RunAsync(input, output, CancellationToken.None);

        output.Position = 0;
        using JsonDocument initialize = await ReadResponse(output);
        using JsonDocument workspace = await ReadResponse(output);
        using JsonDocument shutdown = await ReadResponse(output);
        Assert.Equal(DesktopProtocolDefinition.Version,
            initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.False(initialize.RootElement.GetProperty("result").GetProperty("security")
            .GetProperty("rendererNodeAccess").GetBoolean());
        Assert.True(workspace.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.Equal(Path.GetFullPath(temp.Path),
            workspace.RootElement.GetProperty("result").GetProperty("rootPath").GetString());
        Assert.True(shutdown.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        Assert.Null(await DesktopProtocolFraming.ReadFrameAsync(output));
    }

    [Fact]
    public void Contract_source_matches_generated_protocol_constants()
    {
        string contractPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DesktopProtocol", "contract.json");
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        JsonElement root = contract.RootElement;
        string[] methods = root.GetProperty("methods")
            .EnumerateArray()
            .Select(method => method.GetProperty("name").GetString()!)
            .ToArray();

        Assert.Equal(DesktopProtocolDefinition.Version, root.GetProperty("protocolVersion").GetString());
        Assert.Equal(DesktopProtocolDefinition.MaxHeaderBytes,
            root.GetProperty("limits").GetProperty("maxHeaderBytes").GetInt32());
        Assert.Equal(DesktopProtocolDefinition.MaxBodyBytes,
            root.GetProperty("limits").GetProperty("maxBodyBytes").GetInt32());
        Assert.Equal(
            [
                DesktopProtocolDefinition.InitializeMethod,
                DesktopProtocolDefinition.WorkspaceOpenMethod,
                DesktopProtocolDefinition.ShutdownMethod
            ],
            methods);
    }

    private static async Task WriteRequest(Stream output, int id, string method, object parameters)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });
        await DesktopProtocolFraming.WriteFrameAsync(output, payload);
    }

    private static async Task<JsonDocument> ReadResponse(Stream input)
    {
        byte[] payload = Assert.IsType<byte[]>(await DesktopProtocolFraming.ReadFrameAsync(input));
        return JsonDocument.Parse(payload);
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly int maxChunkSize;

        public ChunkedReadStream(Stream inner, int maxChunkSize)
        {
            this.inner = inner;
            this.maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxChunkSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunkSize)], cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-apphost-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
