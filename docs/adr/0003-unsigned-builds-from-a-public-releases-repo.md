# Builds ship unsigned from a public releases repo

Nothing in this project may cost money (Aden, 2026-10-16), so builds aren't code-signed: friends click "Run anyway" on
SmartScreen and turn Smart App Control off, since it blocks unsigned code outright. Velopack builds are published to a
separate public repo that holds only release files, so the app updates itself without a token and the source repo
stays private.

## Considered options

- **Azure Artifact Signing** (from $9.99 a month, open to individuals in the US and Canada) is the known fix if a
  friend ever needs Smart App Control left on. Signing alone wouldn't remove the SmartScreen warning for an app this
  few people run.
- **Updating from the private source repo** would need a GitHub token built into the app.
