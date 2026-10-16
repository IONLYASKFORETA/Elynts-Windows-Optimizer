# Elynt's Windows Optimizer

A private Windows 11 app that optimizes an existing install for gaming first, but not only for gaming. Every change is
a described tweak with its own switch, and every tweak can be undone exactly. The full spec is issue #24.

## How it fits together

| Project | What it is |
|---|---|
| `src/Elynt.Core` | Tweak catalogue, change journal, Engine and the pipe protocol. Cross-platform: it touches Windows only through adapter interfaces. |
| `src/Elynt.Windows` | The real system adapters (registry today). The only code that touches Windows. |
| `src/Elynt.Service` | The elevated Windows Service. Owns the journal and makes every change. |
| `src/Elynt.App` | The WPF window. Runs without admin rights and sends every change to the service over a named pipe. |
| `tests/Elynt.Core.Tests` | Logic tests: Engine commands against fake adapters, and the pipe contract. |
| `tests/Elynt.Windows.Tests` | The same round trip against the real registry. Runs on GitHub's Windows runners; skipped elsewhere. |

The Engine command surface (`IEngine`) is the single test seam. Tests drive the app through the same commands the
window sends.

## Build and test

Needs the .NET 10 SDK.

```
dotnet build ElyntOptimizer.slnx
dotnet test --solution ElyntOptimizer.slnx
```

## Try it on Windows

Until the installer lands, register the service by hand from an elevated terminal:

```
dotnet publish src/Elynt.Service -c Release -r win-x64 --self-contained -o C:\ElyntOptimizer\service
sc.exe create ElyntOptimizer binPath= "C:\ElyntOptimizer\service\Elynt.Service.exe" start= auto
sc.exe start ElyntOptimizer
```

Then run the app normally (not as admin): `dotnet run --project src/Elynt.App`.

To remove it: `sc.exe stop ElyntOptimizer` then `sc.exe delete ElyntOptimizer`.

## Logs and journal

- Service log: `%ProgramData%\ElyntOptimizer\logs\service-*.log`
- App log: `%LocalAppData%\ElyntOptimizer\logs\app-*.log`
- Change journal: `%ProgramData%\ElyntOptimizer\journal\journal.json`

Logs roll daily and keep 14 days.
