using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class DaemonCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("daemon", "Inspect or start the localhost-only API daemon Preview.");
        Command doctorCommand = new("doctor", "Inspect the API daemon Preview boundary without starting it.");
        Option<string> doctorOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        CliCommandContext.AddTextJsonOutputValidator(doctorOutputOption);
        doctorCommand.Options.Add(doctorOutputOption);
        doctorCommand.SetAction(parseResult =>
        {
            string outputMode = parseResult.GetValue(doctorOutputOption) ?? "text";
            if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                new LocalApiPreviewJsonRenderer(context.Output).WriteDoctor();
            }
            else
            {
                new LocalApiDaemonTextRenderer(context.Output).WriteDoctor();
            }

            return 0;
        });

        Command startCommand = new("start", "Start the localhost-only read-only API daemon Preview.");
        Option<bool> previewOption = new("--preview")
        {
            Description = "Acknowledge and explicitly enable the unauthenticated local Preview.",
        };
        Option<string> bindOption = new("--bind")
        {
            Description = "Bind address. Only localhost or 127.0.0.1 is accepted.",
            DefaultValueFactory = _ => "localhost",
        };
        Option<int> portOption = new("--port")
        {
            Description = $"Loopback port ({LocalApiPreviewConstants.MinimumPort}-{LocalApiPreviewConstants.MaximumPort}).",
            DefaultValueFactory = _ => LocalApiPreviewConstants.DefaultPort,
        };
        startCommand.Options.Add(previewOption);
        startCommand.Options.Add(bindOption);
        startCommand.Options.Add(portOption);
        startCommand.SetAction(parseResult =>
        {
            if (!parseResult.GetValue(previewOption))
            {
                context.Output.WriteLine("daemon start refused: --preview is required because the local API is disabled by default.");
                return 2;
            }

            string requestedBind = parseResult.GetValue(bindOption) ?? "localhost";
            if (!LocalApiDaemonBindPolicy.TryNormalize(requestedBind, out _))
            {
                context.Output.WriteLine("daemon start refused: only localhost or 127.0.0.1 is allowed; remote and wildcard binds are disabled.");
                return 2;
            }

            int port = parseResult.GetValue(portOption);
            if (!LocalApiDaemonBindPolicy.IsValidPort(port))
            {
                context.Output.WriteLine(
                    $"daemon start refused: --port must be between {LocalApiPreviewConstants.MinimumPort} and {LocalApiPreviewConstants.MaximumPort}.");
                return 2;
            }

            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            return LocalApiDaemonHost.RunAsync(snapshot, port, context.Output).GetAwaiter().GetResult();
        });

        command.Subcommands.Add(doctorCommand);
        command.Subcommands.Add(startCommand);
        return command;
    }
}
