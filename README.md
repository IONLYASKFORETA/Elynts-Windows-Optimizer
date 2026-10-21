# Elynt's Windows Optimizer

A private Windows 11 app that optimizes an existing install for gaming first, but not only for gaming. Every change is
a described tweak with its own switch, and every tweak can be undone exactly. The full spec is issue #24.

## How it fits together

| Project | What it is |
|---|---|
| `src/Elynt.Core` | Tweak catalogue, change journal, Engine and the pipe protocol. Cross-platform: it touches Windows only through adapter interfaces. |
| `src/Elynt.Windows` | The real system adapters (registry, hardware readouts, AMD and Intel GPU tuning). The only code that touches Windows. |
| `native/ElyntAdlx` | A thin C bridge to AMD ADLX for GPU tuning, built by hand with MSVC and the ADLX SDK (see the file header). Without `elynt_adlx.dll` next to the service, AMD tuning shows as unavailable. |
| `native/ElyntIgcl` | The same kind of bridge to Intel IGCL (Arc GPU tuning), built by hand with MSVC and Intel's control-library headers (see the file header). Without `elynt_igcl.dll` next to the service, Intel tuning shows as unavailable. |
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
- Repair runs: `%ProgramData%\ElyntOptimizer\logs\repair\repair-*.log` (one per run)
- App log: `%LocalAppData%\ElyntOptimizer\logs\app-*.log`

The Repair section's "Copy app log" button copies the newest app, service and repair logs for a bug report.
- Change journal: `%ProgramData%\ElyntOptimizer\journal\journal.json`
- Whole-store backups (BCD, firewall, power plans, scheduled tasks, drivers, hosts), taken before the first change
  of each kind after the service starts: `%ProgramData%\ElyntOptimizer\backups\<time>-<store>`

## Undo

Every change is journaled with the exact prior state before it is made. The Undo history page lists every change
(time, tweak, state) and can undo any one of them or restore everything, newest first. Undo refuses to overwrite a
value that something else changed since, and says which one. Before changes the service also attempts a System
Restore point (at most once a day; Windows skips more anyway) and tells you when Windows skipped it.

Logs roll daily and keep 14 days.

## Drift after Windows updates

At each service start the app quickly checks that every change it made is still in place; the first check, and the
first after the Windows build changes, is a full check that also re-checks every change still fits this build and PC.
Changes Windows undid are listed for re-apply (all, or pick). Automatic re-apply is off by default, and even when on
it never re-applies destructive changes, changes that need a restart, or changes with a must-read warning (a tweak
with an apply warning): those always wait for you. The update pause is not drift when it ends.

## Security model and accepted risks

- **Who can drive the service.** The service runs as SYSTEM and listens on a named pipe that any interactively
  signed-in user may open (`InteractiveSid`, `src/Elynt.Service/EngineHost.cs`); network logons are denied. That is
  deliberate: the app window runs without admin rights, and the app is meant for a single-user gaming PC. It means
  any interactive user on the PC, admin or not, can ask the service for SYSTEM-level actions: tweaks, driver
  installs and cleaning (pnputil), PawnIO, CPU (SMU/MSR) and GPU tuning. Hard rules (never remove Edge, never block
  quality or security updates, destructive actions need confirmation) are enforced in the service, not the window,
  so they hold whoever connects. On a PC shared with people you don't trust with admin rights, don't install it.
- **Unsigned update channel.** Beta and stable builds come from the public releases repo (Velopack, [docs/releasing.md](docs/releasing.md)) over HTTPS and are not
  code-signed (signing costs money). Trust rests on GitHub's account security and HTTPS; anyone who can publish to the
  releases repo can ship an update. This is an accepted risk for a private app shared with friends.
- **Downloads the service runs as SYSTEM** (GPU and chipset drivers, DDU, PawnIO) are checked first: vendor hash
  where the vendor publishes one, and an Authenticode signature from the expected signer.
