# The elevated service makes every change; the window never runs as admin

The window runs without admin rights and sends every command over a named pipe to an elevated Windows Service, which
holds the undo history and is the only part that changes Windows. Most optimizers simply ask for admin when they
open, but per-game profiles have to see games start while nobody is at the window, changes a crash left behind are
undone when the service starts, and one process holding the undo history keeps it consistent across windows and
accounts.

## Consequences

- Velopack installs the app per user and can't manage a Windows service, so the app installs, updates and removes
  its own service, with one UAC prompt (see `docs/releasing.md`).
- Every command crosses the pipe, which mirrors the Engine's commands one to one. That is also why the Engine is the
  single test seam: tests drive the same commands the window sends.
