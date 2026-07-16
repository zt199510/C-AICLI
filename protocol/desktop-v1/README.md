# Desktop Protocol v1

`contract.json` is the reviewed source for protocol version, limits, methods and shared data types. Run the Desktop contract generator before building the Electron app:

```powershell
npm --prefix apps/desktop run generate:contracts
npm --prefix apps/desktop run check:contracts
```

Generated outputs are checked in so .NET builds do not require Node:

- `src/CSharpAiCli.AppHost/Protocol/Generated/DesktopProtocolContracts.g.cs`
- `apps/desktop/src/generated/desktop-contracts.ts`

The transport is JSON-RPC 2.0 over stdio with ASCII `Content-Length` framing. AppHost stdout is reserved for protocol frames; bounded process diagnostics use stderr.
