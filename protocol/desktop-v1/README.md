# Desktop Protocol v1

`contract.json` is the reviewed source for the protocol version, schema version, capabilities,
limits, methods, notifications, errors, and transport data types. `contract.schema.json` closes
the DSL shape, while `examples/methods.json` provides a valid params/result example for every
method plus the `thread.changed` notification and a JSON-RPC error.

Run the Desktop contract generator before building the Electron app:

```powershell
npm --prefix apps/desktop run generate:contracts
npm --prefix apps/desktop run check:contracts
```

Generated outputs are checked in so .NET builds do not require Node:

- `src/CSharpAiCli.AppHost/Protocol/Generated/DesktopProtocolContracts.g.cs`
- `apps/desktop/src/generated/desktop-contracts.ts`

The transport is JSON-RPC 2.0 over stdio with ASCII `Content-Length` framing. AppHost stdout is reserved for protocol frames; bounded process diagnostics use stderr.

`desktop-v1` contains 16 methods: initialize/cancel/shutdown, workspace open, six thread
query/mutation methods, and catalog/changes/report/artifact queries. Business results use typed
Application outcome envelopes. The optional `thread.changed` capability enables process-scoped,
ordered mutation notifications; clients must use list/get plus revision for reconnect resync.

The generator computes a canonical lowercase SHA256 over `contract.json` and emits the same hash,
strict DTOs, constants, method workspace/mutation/timeout metadata, outcome invariants, and runtime
validators for C# and TypeScript. UTC validators accept only valid calendar timestamps with a zero
offset. `--check` validates the DSL, requires the initialize example to carry the canonical
version/hash, validates all examples, and checks the generated output without writing files.
