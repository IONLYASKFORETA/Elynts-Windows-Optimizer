# Only administrator accounts use the app, and whole-PC changes belong to the PC

The elevated service serves only accounts in the Administrators group, and a change that affects every account (a
machine-wide setting, a service, a boot setting, a power plan) belongs to the PC: it shows in every administrator
account's Undo history and any of them can undo it. Changes to one account's own settings stay with that account.

A change counts as whole-PC only when every part of it is. One that also touches an account's own settings stays with
that account, since undoing it has to write to that account's settings.

Without the first rule, any account signed in at the PC, a standard one included, could use the elevated service to
turn off Defender or change boot settings, which Windows itself would stop that account doing. Without the second, a
whole-PC change made from one account shows as "already set" in every other, can't be undone from them, and can't be
undone at all once that account is deleted.

## Consequences

- Restore everything in one administrator account also undoes whole-PC changes made from another.
- An administrator account's window still runs without admin rights (see ADR 0001); the service checks the account's
  group membership, not whether the window is elevated.
