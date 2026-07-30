using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ApiCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("api", "Inspect the local HTTP API Preview contract.");
        Command routesCommand = new("routes", "List the local HTTP API Preview routes without starting a listener.");
        Option<string> routesOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        CliCommandContext.AddTextJsonOutputValidator(routesOutputOption);
        routesCommand.Options.Add(routesOutputOption);
        routesCommand.SetAction(parseResult =>
        {
            string outputMode = parseResult.GetValue(routesOutputOption) ?? "text";
            if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                new LocalApiPreviewJsonRenderer(context.Output).WriteRoutes();
            }
            else
            {
                new LocalApiPreviewTextRenderer(context.Output).WriteRoutes();
            }

            return 0;
        });

        Command smokeCommand = new("smoke", "Check an explicitly started localhost API daemon Preview.");
        Option<int> smokePortOption = new("--port")
        {
            Description = $"Loopback port ({LocalApiPreviewConstants.MinimumPort}-{LocalApiPreviewConstants.MaximumPort}).",
            DefaultValueFactory = _ => LocalApiPreviewConstants.DefaultPort,
        };
        smokeCommand.Options.Add(smokePortOption);
        smokeCommand.SetAction(parseResult =>
        {
            int port = parseResult.GetValue(smokePortOption);
            if (!LocalApiDaemonBindPolicy.IsValidPort(port))
            {
                context.Output.WriteLine(
                    $"api smoke refused: --port must be between {LocalApiPreviewConstants.MinimumPort} and {LocalApiPreviewConstants.MaximumPort}.");
                return 2;
            }

            return LocalApiDaemonClient.SmokeAsync(port, context.Output).GetAwaiter().GetResult();
        });

        command.Subcommands.Add(routesCommand);
        command.Subcommands.Add(smokeCommand);
        return command;
    }
}
