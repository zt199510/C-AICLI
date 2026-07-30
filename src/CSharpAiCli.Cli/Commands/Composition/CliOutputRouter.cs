using System.Text;

namespace CSharpAiCli.Cli;

internal sealed class CliOutputRouter : TextWriter
{
    private readonly Stack<TextWriter> destinations = new();

    public CliOutputRouter(TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destinations.Push(destination);
    }

    public override Encoding Encoding => Current.Encoding;

    public IDisposable Redirect(TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destinations.Push(destination);
        return new RedirectScope(this, destination);
    }

    public override void Flush() => Current.Flush();

    public override Task FlushAsync() => Current.FlushAsync();

    public override void Write(char value) => Current.Write(value);

    public override void Write(char[]? buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Current.Write(buffer, index, count);
    }

    public override void Write(string? value) => Current.Write(value);

    public override void WriteLine() => Current.WriteLine();

    public override void WriteLine(string? value) => Current.WriteLine(value);

    private TextWriter Current => destinations.Peek();

    private void Restore(TextWriter destination)
    {
        if (destinations.Count <= 1 || !ReferenceEquals(destinations.Peek(), destination))
        {
            throw new InvalidOperationException("CLI output redirection scopes must be disposed in order.");
        }

        destinations.Pop();
    }

    private sealed class RedirectScope(CliOutputRouter owner, TextWriter destination) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            owner.Restore(destination);
            disposed = true;
        }
    }
}
