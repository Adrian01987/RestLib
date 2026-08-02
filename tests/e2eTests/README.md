# End-to-end tests

RestLib has two Bash-based E2E runners:

- `run-all.sh` tests the compact sample in `samples/RestLib.Sample`.
- `ecommerce/run-all.sh` tests the ecommerce reference sample in `samples/RestLib.Sample.Ecommerce`.

Each runner builds and starts its sample by default, executes every registered suite, writes logs under `TestResults/`, and stops the server processes it started. The runners validate their suite inventories before building, so an unregistered, missing, or duplicate suite fails with a clear error.

## Prerequisites

- .NET 10 SDK, as pinned by `global.json`
- `curl`
- `jq`
- Bash: Git for Windows Bash on Windows, or Bash on Linux

`bc` is not an E2E dependency. CI uses it separately when calculating the unit-test coverage threshold.

## Native Windows without WSL

Install [Git for Windows](https://git-scm.com/download/win), the .NET 10 SDK, `curl`, and `jq`. If a package was installed while a terminal or Codex session was already open, restart that process so its `PATH` includes the new executable.

From PowerShell at the repository root, verify that Git Bash can see every prerequisite:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -lc 'dotnet --version; curl --version | head -n 1; jq --version'
```

Run both E2E gates with the explicit Git for Windows path:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' tests/e2eTests/run-all.sh
& 'C:\Program Files\Git\bin\bash.exe' tests/e2eTests/ecommerce/run-all.sh
```

Use this explicit path even when `bash` is available in PowerShell: on some Windows installations, the unqualified command resolves to the WSL launcher.

## Linux and CI

Run the same Bash implementations directly:

```bash
bash tests/e2eTests/run-all.sh
bash tests/e2eTests/ecommerce/run-all.sh
```

CI installs `curl` and `jq`; the .NET setup step supplies the SDK.

## Runner options

Both runners accept:

- `--no-build` to reuse an existing Release build.
- `--no-server` to test an already-running sample.
- `--check-inventory` to validate suite registration without building or starting a server.
- `SUITE=<name>` to run one registered suite.
- `BASE_URL=<url>` to override the client URL.

Examples:

```bash
SUITE=crud bash tests/e2eTests/run-all.sh
SUITE=payment-flow bash tests/e2eTests/ecommerce/run-all.sh
bash tests/e2eTests/run-all.sh --check-inventory
bash tests/e2eTests/ecommerce/run-all.sh --check-inventory
```

The main sample binds to `http://localhost:5000` by default. The ecommerce payment flow also starts isolated servers on ports 5064 and 5065. See each runner's header for additional URL overrides.

Every suite emits a machine-readable `E2E_RESULT` record. The aggregate runner validates that record and reports total, passed, failed, and skipped scenarios alongside its suite counts.
