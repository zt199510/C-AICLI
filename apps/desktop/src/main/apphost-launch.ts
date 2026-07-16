import path from "node:path";

export interface AppHostLaunchSpec {
  command: string;
  args: string[];
  cwd: string;
}

export interface AppHostLaunchContext {
  appIsPackaged: boolean;
  appPath: string;
  resourcesPath: string;
  environment: NodeJS.ProcessEnv;
}

export function resolveAppHostLaunch(context: AppHostLaunchContext): AppHostLaunchSpec {
  if (context.appIsPackaged) {
    return {
      command: path.join(context.resourcesPath, "apphost", "CSharpAiCli.AppHost.exe"),
      args: [],
      cwd: context.resourcesPath,
    };
  }

  const repoRoot = path.resolve(context.appPath, "../..");
  const configuration = context.environment.CAICLI_APPHOST_CONFIGURATION ?? "Release";
  const appHostDll = path.join(
    repoRoot,
    "src",
    "CSharpAiCli.AppHost",
    "bin",
    configuration,
    "net9.0",
    "CSharpAiCli.AppHost.dll",
  );
  return {
    command: context.environment.CAICLI_DOTNET_HOST ?? "dotnet",
    args: [appHostDll],
    cwd: repoRoot,
  };
}
