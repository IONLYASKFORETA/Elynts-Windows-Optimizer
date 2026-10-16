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

Every CI run packs a beta installer (`ElyntOptimizer-beta-setup` under the run's Artifacts). Pushes to the default
branch also publish it to the public releases repo. Run the Setup file; the app installs and updates its own
background service (one UAC prompt). Friends follow [docs/install-guide.md](docs/install-guide.md); channels and
promotion to stable are in [docs/releasing.md](docs/releasing.md).

Or, from source, register the service by hand from an elevated terminal (a developer build doesn't manage the
service itself):

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
- Whole-store backups (BCD, firewall, power plans, scheduled tasks, drivers, hosts), taken before the first change
  of each kind after the service starts: `%ProgramData%\ElyntOptimizer\backups\<time>-<store>`

## Undo

Every change is journaled with the exact prior state before it is made. The Undo history page lists every change
(time, tweak, state) and can undo any one of them or restore everything, newest first. Undo refuses to overwrite a
value that something else changed since, and says which one. Before changes the service also attempts a System
Restore point (at most once a day; Windows skips more anyway) and tells you when Windows skipped it.

Logs roll daily and keep 14 days.
