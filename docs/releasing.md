# Releasing

Builds are packed with [Velopack](https://docs.velopack.io) and published to a separate **public** repo that holds
only release files: `IONLYASKFORETA/Elynts-Windows-Optimizer-releases`. The source repo stays private. Because the
releases repo is public, the app updates from it with no token, so nothing secret is ever built into the app.

## Channels

| Channel | Who | How a build gets there | GitHub release |
|---|---|---|---|
| beta | Aden | Automatically, on every push to the default branch that passes CI | Pre-release `v1.0.N-beta` |
| stable | Friends | By hand: the **Promote beta to stable** workflow | Release `v1.0.N` |

Versions are `1.0.<CI run number>`. Promotion rebuilds the beta's exact commit (tagged `beta/1.0.N` in this repo) with
the same version on the stable channel.

The app follows the channel it was installed from; its **Updates** box switches channel. Switching from beta to
stable may install an older build, which is allowed on a channel switch.

## One-time setup

1. Create the public repo `IONLYASKFORETA/Elynts-Windows-Optimizer-releases` (an empty README is enough; paste
   [install-guide.md](install-guide.md) into it so friends land on the guide).
2. Create a fine-grained personal access token limited to that one repo, with **Contents: Read and write**.
3. In this repo, add it as the Actions secret `RELEASES_TOKEN`.

Until the secret exists, CI still packs every build and keeps `Setup.exe` as a run artifact, but doesn't publish.

## Promote a beta to stable

Actions > **Promote beta to stable** > Run workflow, and enter the beta's version (e.g. `1.0.42`).

## How the background service is installed and updated

Velopack installs the app per user (`%LocalAppData%\ElyntOptimizer`) and doesn't manage Windows services. The
package carries the service in a `service\` subfolder. When the app starts and finds the service missing, stopped or
a different version from its own, it starts a copy of itself elevated (`--setup-service`, one UAC prompt) that stops
the service, copies it to `%ProgramFiles%\ElyntOptimizer\service`, registers it (automatic start, restart on failure)
and starts it. The uninstall hook runs `--remove-service` the same way. The journal and logs in
`%ProgramData%\ElyntOptimizer` are always kept.

## Code signing

Builds are unsigned (signing costs money). See `research/friend-distribution.md` on the research branch for the
cheapest options if Smart App Control becomes a problem; Velopack can sign during `vpk pack`.
