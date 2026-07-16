import path from "node:path";
import { describe, expect, it } from "vitest";
import { resolveAppHostLaunch } from "./apphost-launch";

describe("AppHost launch resolution", () => {
  it("uses the packaged resource without a cwd-dependent lookup", () => {
    const result = resolveAppHostLaunch({
      appIsPackaged: true,
      appPath: "C:\\Program Files\\C-AICLI Desktop\\resources\\app.asar",
      resourcesPath: "C:\\Program Files\\C-AICLI Desktop\\resources",
      environment: {},
    });

    expect(result.command).toBe(
      path.join(
        "C:\\Program Files\\C-AICLI Desktop\\resources",
        "apphost",
        "CSharpAiCli.AppHost.exe",
      ),
    );
    expect(result.args).toEqual([]);
    expect(result.cwd).toBe("C:\\Program Files\\C-AICLI Desktop\\resources");
  });

  it("uses a reviewed dotnet host override only in development", () => {
    const result = resolveAppHostLaunch({
      appIsPackaged: false,
      appPath: "D:\\repo\\apps\\desktop",
      resourcesPath: "D:\\repo\\apps\\desktop\\resources",
      environment: {
        CAICLI_DOTNET_HOST: "C:\\Users\\dev\\.dotnet\\dotnet.exe",
        CAICLI_APPHOST_CONFIGURATION: "Release",
      },
    });

    expect(result.command).toBe("C:\\Users\\dev\\.dotnet\\dotnet.exe");
    expect(result.args[0]).toContain(
      path.join("src", "CSharpAiCli.AppHost", "bin", "Release", "net9.0"),
    );
  });
});
