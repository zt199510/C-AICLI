# Gerber/TIFF Fake Driver Fixtures

This directory contains protocol-v1 fixtures for default, model-free,
network-free Project Pack tests. It is not a real Gerber/TIFF converter and
must never be reported as real-tool or business validation.

`fake-driver.ps1` is an explicitly invoked test asset. Runtime pack discovery
does not scan for or execute repository scripts. Supported modes are:

- `success`: writes `protocol-v1/success.json` and exits 0.
- `failure`: writes `protocol-v1/failure.json` and exits 1.
- `partial-output`: writes `protocol-v1/partial-output.json` and exits 1.
- `timeout`: writes no JSON and waits long enough for runner timeout/cancel
  and process cleanup tests.

Protocol results contain logical output ids, sizes, hashes, and bounded
diagnostics only. They never contain machine paths or real tool parameters.
