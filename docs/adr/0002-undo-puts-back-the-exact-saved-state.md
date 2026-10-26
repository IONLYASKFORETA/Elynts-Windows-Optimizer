# Undo puts back the exact saved state, never Windows' defaults

Before every change the service saves exactly what was there (a missing value as missing, registry values with their
type and raw bytes) and only then makes the change, so undo can put that back even after a crash. Undo refuses when
something else has changed the same setting since, rather than overwrite it.

## Considered options

- **Reset to Windows' defaults on undo.** Rejected: a PC someone had already set up their way would lose those
  settings, and many tweaks have no single documented default.
- **System Restore as the undo.** Rejected: Windows skips a new restore point within 24 hours of the last one, System
  Protection is often off on Windows 11, and a restore rolls back everything at once instead of one tweak. Restore
  points and whole-store exports (BCD, firewall, power plans, scheduled tasks, drivers, hosts) stay as a safety net
  under the saved state.

## Consequences

- The app only undoes its own changes. A tweak that was already set before the app changed anything can't be
  switched off from the app.
- A destructive change has nothing to put back, so it is never offered for undo.
