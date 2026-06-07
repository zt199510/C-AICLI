namespace CSharpAiCli.Core;

internal static class TextFileUtilities
{
    private const int BinaryProbeBytes = 8192;

    public static bool IsLikelyBinary(string path)
    {
        using FileStream stream = File.OpenRead(path);
        int length = (int)Math.Min(BinaryProbeBytes, stream.Length);
        Span<byte> buffer = length <= 1024 ? stackalloc byte[length] : new byte[length];
        int read = stream.Read(buffer);

        for (int index = 0; index < read; index++)
        {
            if (buffer[index] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
