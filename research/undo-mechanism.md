# Work out how undo works

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/7

## Answer

**System Restore can't be the undo mechanism.** It has a throttle (by default, at most one new restore point every 24 hours, so a second one is silently skipped), it may be switched off, and it reverts the whole system at once rather than one tweak. Use these three layers instead:

1. **Per-change journal, written before the change.** Before the app changes anything, it records the exact prior state to disk: whether a value or key existed, its type and raw data, the security descriptor, the service start type, the task XML, the package identity, the feature state and so on. Undo applies the inverse of each entry, newest first. It refuses to overwrite a value that someone else has changed since.
2. **Native snapshots for things that have a whole-store export.** These are `bcdedit /export`, `netsh advfirewall export`, `powercfg /export`, `Export-ScheduledTask`, `pnputil /export-driver` and a copy of the hosts file. They are taken before the first change of each kind, so a corrupted journal can still be recovered from them.
3. **A System Restore point before each apply batch, as a last-resort safety net.** If it's skipped because of the throttle or because protection is off, the app tells the user instead of failing silently.

**Every tweak gets one of three reversibility labels:**
- **Reversible:** registry, services, tasks, BCD, power, hosts, firewall.
- **Reversible but needs a download or source:** optional features with payload removed, capabilities, Store apps.
- **Destructive, opt-in only:** provisioned app removal, deleting inbox payloads, deleting a third-party driver that wasn't exported, and anything on TrustedInstaller-owned or protected objects.

**Feature updates can silently undo tweaks.** So each journal entry also stores a "verify" probe, and the app re-checks after an OS build change.

> **Source caveat:** in this research session, direct page fetches of learn.microsoft.com were blocked by the network proxy. Every URL below was confirmed to exist via web search and is the canonical Microsoft doc for that API or tool. The specific claims come from those docs as previously known, not from text re-read today. Items marked *(verify)* are less certain or describe observed behaviour rather than documented behaviour. Re-check them on a Windows 11 test VM before relying on them.

---

## 1. Registry values

**Capturing prior state**
- **Value present or absent.** Read with `RegQueryValueEx` / `RegGetValue`. A missing value returns `ERROR_FILE_NOT_FOUND`, and the journal must store `exists=false` rather than a default. Then undo means *delete the value*, not "set it to 0". See https://learn.microsoft.com/en-us/windows/win32/sysinfo/retrieving-data-from-the-registry and https://learn.microsoft.com/en-us/windows/win32/sysinfo/registry-functions
- **Record the type and the raw bytes.** Store `REG_SZ` vs `REG_EXPAND_SZ`, `REG_DWORD` vs `REG_QWORD` and so on, not a parsed value. Rewriting a value with the wrong type is a classic bug that "undo" introduces. https://learn.microsoft.com/en-us/windows/win32/sysinfo/registry-functions
- **Record key existence too.** If the tweak creates a key such as `...\Policies\Microsoft\Windows\DataCollection`, record which keys were created. Undo deletes only those, and only if they're empty of foreign values.
- **Whole-subtree snapshot (optional, coarse).** `RegSaveKeyEx` saves a key to a hive file and needs `SeBackupPrivilege`. `RegRestoreKey` puts it back and needs `SeRestorePrivilege`. Restoring overwrites the whole subtree, so use this only as a fallback, never for per-tweak undo. https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regsavekeyexa and https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regrestorekeya
- **Don't rely on registry transactions (KTM/TxR).** `RegCreateKeyTransacted` exists, but Microsoft steers developers away from the KTM transactional APIs: TxF is formally deprecated and "may not be available in future versions". Use the journal for atomicity instead. https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regcreatekeytransactedw and https://learn.microsoft.com/en-us/windows/win32/fileio/deprecation-of-txf

**Permissions and TrustedInstaller-owned keys**
- Keys carry security descriptors (owner and DACL) and access rights such as `KEY_SET_VALUE` and `WRITE_DAC`. https://learn.microsoft.com/en-us/windows/win32/sysinfo/registry-key-security-and-access-rights
- Read the current descriptor with `RegGetKeySecurity` and write one with `RegSetKeySecurity`. .NET offers `RegistryKey.GetAccessControl` and `SetAccessControl`. https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-reggetkeysecurity and https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.getaccesscontrol?view=net-6.0
- Windows Resource Protection (WRP) protects critical files, folders and registry keys. Only TrustedInstaller may replace them, and administrators get read-only access. https://learn.microsoft.com/en-us/windows/win32/wfp/about-windows-file-protection and https://learn.microsoft.com/en-us/windows/win32/wfp/protected-file-list
- **Rule for the app:** if a write gets access-denied, don't take ownership automatically. Taking ownership and editing the DACL is itself a change that must be journaled (original owner plus SDDL) and restored on undo, and it weakens WRP. Label any such tweak as **destructive/advanced**. If the app does it anyway, the journal needs the old owner SID and the full SDDL, and undo has to restore both after restoring the value.
- **Defender settings.** Tamper Protection blocks or ignores changes to Defender settings made through the registry or policy. A "disable Defender" tweak will appear to apply and then do nothing, so treat it as unsupported. https://learn.microsoft.com/en-us/defender-endpoint/prevent-changes-to-security-settings-with-tamper-protection and https://learn.microsoft.com/en-us/defender-endpoint/faqs-on-tamper-protection

## 2. Services

- **Capture these before any change:**
  - from `QueryServiceConfig`: start type (`dwStartType`), binary path and account;
  - from `QueryServiceConfig2`: delayed-auto-start, trigger info (trigger-start services start even when set to Manual), failure actions.

  Restore with `ChangeServiceConfig` / `ChangeServiceConfig2`. https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfigw, https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2a and https://learn.microsoft.com/en-us/windows/win32/services/changing-a-service-configuration
- **Protected services.**
  - Services launched as protected (antimalware PPL, `SERVICE_LAUNCH_PROTECTED_*`) can't be reconfigured by an administrator. https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_launch_protected_info
  - Each service also has its own DACL. When `SERVICE_CHANGE_CONFIG` isn't granted to Administrators, `ChangeServiceConfig` fails with access-denied. https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights
  - Treat access-denied as "not tweakable", not as a reason to edit the registry `Start` value directly.
- **Self-healing services *(verify)*.** Windows Update components, through the Update Medic service, are observed to reset their own start types. Defender services are protected by Tamper Protection (see §1). Tweaks to these won't last, so either exclude them or mark them "may be reverted by Windows".
- **Per-user services *(verify)*.** These have names like `Foo_1a2b3`. Change the template service, record that, and expect new instances to follow it.
- **Persist the original state.** Store the start type as a number and also capture the service's full `HKLM\SYSTEM\CurrentControlSet\Services\<name>` values as a backup record.

## 3. Scheduled tasks

- **Snapshot:** `Export-ScheduledTask` gives the full task XML. https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/export-scheduledtask?view=windowsserver2025-ps
- **Prefer disabling over deleting.** `Disable-ScheduledTask` is undone with `Enable-ScheduledTask`. If a task was deleted, re-create it with `Register-ScheduledTask -Xml <saved>`, using the original task path and principal. https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/disable-scheduledtask?view=windowsserver2025-ps, https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/register-scheduledtask?view=windowsserver2025-ps and https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/?view=windowsserver2025-ps
- Re-registering a task that runs as SYSTEM or with S4U requires elevation, and the password-based principals can't be round-tripped. That's another reason to only disable. https://learn.microsoft.com/en-us/answers/questions/184857/import-scheduled-task-with-powershell-and-s4u
- *(verify)* Some `\Microsoft\Windows\...` tasks are ACL'd to SYSTEM or TrustedInstaller and return access-denied. Cumulative and feature updates also re-create or re-enable some tasks, so the post-update drift check (§11) must recheck them.

## 4. Appx/MSIX packages and provisioned packages

- **There are two different removals:**
  - `Remove-AppxPackage` removes the app for the current user, or for all users with `-AllUsers`. It deletes that user's app data. https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage?view=windowsserver2025-ps
  - `Remove-AppxProvisionedPackage -Online` removes the package from the image, so *new* user profiles don't get it. It doesn't uninstall the app from existing users. https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage?view=windowsserver2025-ps and https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-app-package--appx-or-appxbundle--servicing-command-line-options?view=windows-11
- **Capture these before removal:**
  - from `Get-AppxPackage -AllUsers`: `PackageFullName`, `PackageFamilyName`, `InstallLocation`, `NonRemovable`, `SignatureKind`, and which users have it;
  - from `Get-AppxProvisionedPackage -Online`: whether it's provisioned;
  - a mapping to its Store product ID.

  https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage?view=windowsserver2025-ps
- **Can it be reinstalled?**
  - **Removed for the current user only, still installed for another user or staged:** re-register from disk with `Add-AppxPackage -Register "<InstallLocation>\AppxManifest.xml" -DisableDevelopmentMode`. https://learn.microsoft.com/en-us/powershell/module/appx/?view=windowsserver2025-ps and https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/modern-inbox-store-apps-troubleshooting-guidance
  - **Removed everywhere:** reinstall from the Microsoft Store, or with `winget install --id <StoreProductId> --source msstore`. This needs network access, a working Store/App Installer, and the app still being published. https://learn.microsoft.com/en-us/windows/package-manager/winget/install
  - **Deprovisioned:** re-provisioning needs the original package file plus its license, through `Add-AppxProvisionedPackage -PackagePath ... -LicensePath ...` or DISM. Consumers don't have these files, so **in practice deprovisioning can't be undone locally**. Microsoft's own forum answers point people to the Store or an in-place repair install instead. https://learn.microsoft.com/en-us/windows/msix/desktop/deploy-preinstalled-apps and https://learn.microsoft.com/en-us/archive/msdn-technet-forums/71340a30-e5ee-431e-96ee-90bb2f6ea979
  - **Some inbox apps (for example the Store itself) are hard or impossible to restore** once fully removed. https://learn.microsoft.com/en-us/answers/questions/4028551/how-to-reinstall-microsoft-store-after-being-unins
  - **App data is lost.** `-PreserveApplicationData` only applies to apps under development.
- **Rules for the app:**
  - Per-user removal is "reversible with download".
  - Deprovisioning and `-AllUsers` removal are **destructive, opt-in**.
  - `NonRemovable` packages, the Store, App Installer and the framework/runtime packages are never offered for removal.

## 5. Windows optional features and capabilities

- **Capture:** `Get-WindowsOptionalFeature -Online` gives each feature's state (Enabled, Disabled, or DisabledWithPayloadRemoved). `Get-WindowsCapability -Online` gives each capability's state (Installed or NotPresent). https://learn.microsoft.com/en-us/powershell/module/dism/get-windowscapability?view=windowsserver2025-ps
- **Undo:**
  - `Enable-` / `Disable-WindowsOptionalFeature`. `Disable-WindowsOptionalFeature -Remove` deletes the payload, after which re-enabling needs a source (Windows Update or `-Source`). Never use `-Remove` by default. https://learn.microsoft.com/en-us/powershell/module/dism/disable-windowsoptionalfeature?view=windowsserver2025-ps and https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism?view=windows-11
  - `Remove-WindowsCapability` is undone with `Add-WindowsCapability`. Features on Demand are downloaded from Windows Update, or from FoD media, so undo can fail offline or where WSUS or policy blocks it. https://learn.microsoft.com/en-us/powershell/module/dism/remove-windowscapability?view=windowsserver2025-ps, https://learn.microsoft.com/en-us/powershell/module/dism/add-windowscapability?view=windowsserver2025-ps, https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-v2--capabilities?view=windows-11 and https://learn.microsoft.com/en-us/windows/deployment/update/optional-content
- Both often require a restart. The journal entry should stay "pending reboot" until a probe confirms the new state.

## 6. BCD (bcdedit)

- **Whole-store snapshot:** run `bcdedit /export <file>` before the first BCD change. `bcdedit /import <file>` restores the whole store. https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdedit-command-line-options?view=windows-11 and https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/bcdedit
- **Per-setting:** record the output of `bcdedit /enum {current} /v` for the element. If it was absent, undo is `bcdedit /deletevalue {current} <element>`. Otherwise undo is `/set` to the old value. https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--deletevalue, https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set and https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcd-boot-options-reference
- **Risks:**
  - Changing boot configuration can trigger BitLocker recovery at next boot. Check BitLocker status and suspend it for one reboot (`Suspend-BitLocker -RebootCount 1`), or refuse the change. https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/operations-guide and https://learn.microsoft.com/en-us/troubleshoot/windows-client/windows-security/bitlocker-recovery-known-issues
  - `bcdedit /set` docs note that some options, such as testsigning and nointegritychecks, are ignored or blocked under Secure Boot. https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set
  - Feature updates rewrite the boot entries. https://learn.microsoft.com/en-us/answers/questions/2186053/duplicate-boot-entries-after-windows-11-24h2-updat?forum=windowsclient-all
- **Label all BCD tweaks advanced.**

## 7. Power plans and powercfg

- **Capture:**
  - the active scheme: `powercfg /getactivescheme`;
  - each touched setting: `powercfg /query <scheme> <subgroup> <setting>`, keeping the AC and DC indices separately;
  - a full export: `powercfg /export <file> <GUID>`.

  https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options
- **Undo:**
  - `/setacvalueindex` and `/setdcvalueindex` back to the old values;
  - `/setactive <old GUID>`;
  - for a plan the app created (`/duplicatescheme`, for example from Ultimate Performance), switch back and then `/delete` it.

  Don't use `/restoredefaultschemes` as undo, because it wipes every user-created plan. Source as above.
- `powercfg /hibernate off` deletes `hiberfil.sys` and also disables Fast Startup. It can be undone with `/hibernate on`, but journal it as two effects. Source as above.
- On Modern Standby (S0) systems usually only Balanced is exposed *(verify per device)*. https://learn.microsoft.com/en-us/answers/questions/5643715/i-cant-create-or-find-best-high-ultra-performance

## 8. Group Policy vs registry policy keys

- Administrative Template settings are stored in `Registry.pol` files under `%SystemRoot%\System32\GroupPolicy\{Machine,User}`. The Group Policy engine writes them into `HKLM|HKCU\Software\Policies\...` and related keys. https://learn.microsoft.com/en-us/previous-versions/windows/desktop/Policy/extensions-to-the-group-policy-object-editor and https://learn.microsoft.com/en-us/answers/questions/957202/registry-pol-file
- **What this means for undo:**
  - Writing a `...\Policies\...` value directly takes effect for most settings. It isn't shown in gpedit, and the next policy refresh may overwrite it if `Registry.pol` or a domain or MDM policy sets the same value.
  - Conversely, a value left behind after a GPO is removed ("tattooing") persists until someone deletes it. https://learn.microsoft.com/en-us/answers/questions/425339/registry-keys-created-by-gpo-still-exist-after-gpo and https://learn.microsoft.com/en-gb/answers/questions/2238010/windows-update-settings-in-registry-not-reflecting
- **Recommendation:**
  - Write policy values directly and journal them like any registry value, recording absent-vs-present.
  - Don't edit `Registry.pol`.
  - Detect domain join or MDM enrollment and refuse to change policy keys on managed devices.
  - Note in the UI that some policies are only honoured on certain editions (for example, some telemetry levels only on Enterprise or Education).

## 9. Hosts file and firewall rules

- **Hosts:**
  - The file is at `%SystemRoot%\System32\drivers\etc\hosts`. https://learn.microsoft.com/en-us/answers/questions/2639953/editing-the-hosts-file
  - Before editing, copy the whole file into the journal with a hash.
  - Add entries only inside a fenced block (`# BEGIN Elynt-Optimizer` ... `# END`), so undo removes only the app's own lines and keeps any user edits made since.
  - *(verify)* Defender flags hosts files that block Microsoft telemetry domains as a settings-modifier threat and may revert them.
- **Firewall:**
  - Put every rule the app creates in a dedicated `-Group "Elynt Optimizer"`. Undo is `Remove-NetFirewallRule -Group ...`.
  - For edits to existing rules, journal the rule's full properties (`Get-NetFirewallRule` plus its address, port and application filters) and restore them with `Set-NetFirewallRule`.
  - As a safety net, take `netsh advfirewall export <file>.wfw`. `import` replaces the *entire* policy, so it's a last resort only.

  https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/configure-with-command-line?tabs=powershell, https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-advfirewall and https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/rules

## 10. Driver installs and removals (pnputil and the driver store)

- **Capture:**
  - `pnputil /enum-drivers` lists the published `oemNN.inf` packages in the driver store;
  - `pnputil /enum-devices /drivers` gives the device-to-driver bindings;
  - before deleting any driver package, `pnputil /export-driver oemNN.inf <dir>`.

  https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax and https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-examples
- **Undo:**
  - **Driver the app installed:** `pnputil /delete-driver oemNN.inf /uninstall`. Record the published name the install produced, because it isn't predictable in advance.
  - **Driver the app removed:** `pnputil /add-driver <exported>.inf /install`.
  - Without an export, removal is **destructive**.
  - Inbox drivers can't be deleted from the store.
  - Windows Update may reinstall a removed driver.

  https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-device-manager-to-uninstall-devices-and-driver-packages and https://learn.microsoft.com/en-us/windows-hardware/drivers/driversecurity/create-a-driver-inventory
- **Recommendation:** keep driver changes out of v1, or ship them as opt-in destructive tweaks only.

## 11. System Restore on Windows 11

- **What it covers:**
  - A restore point snapshots system files, the registry, installed programs and drivers, using Volume Shadow Copy.
  - It doesn't restore or delete personal data files, so it is not a backup.
  - It only monitors certain file types and locations.

  https://learn.microsoft.com/en-us/windows/win32/sr/about-system-restore, https://learn.microsoft.com/en-us/windows/win32/sr/restore-points and https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-protection
- **Creating one:**
  - `SRSetRestorePoint` with `BEGIN_SYSTEM_CHANGE` / `END_SYSTEM_CHANGE`, type `MODIFY_SETTINGS` or `APPLICATION_INSTALL`. https://learn.microsoft.com/en-us/windows/win32/api/srrestoreptapi/nf-srrestoreptapi-srsetrestorepointa and https://learn.microsoft.com/en-us/windows/win32/sr/calling-srsetrestorepoint
  - Or `Checkpoint-Computer`, which is Windows PowerShell 5.1 only and absent from PowerShell 7. https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/checkpoint-computer?view=powershell-5.1 and https://learn.microsoft.com/en-us/powershell/scripting/whats-new/differences-from-windows-powershell?view=powershell-5.1
- **The 24-hour throttle:**
  - Since Windows 8, a new restore point isn't created if one was created in the last 24 hours, and the call still "succeeds".
  - The DWORD `SystemRestorePointCreationFrequency` (in minutes) under `HKLM\Software\Microsoft\Windows NT\CurrentVersion\SystemRestore` overrides it. Setting it to 0 means no throttle.
  - The `Checkpoint-Computer` doc states the one-per-24-hours limit directly.

  https://learn.microsoft.com/en-us/windows/win32/sr/creating-a-restore-point *(found by search; fetch blocked; this is the page documenting the value)*, https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/checkpoint-computer?view=powershell-5.1 and https://learn.microsoft.com/en-us/answers/questions/463351/why-creating-a-system-restore-point-is-only-succes

  **App rule:** compare `Get-ComputerRestorePoint` before and after the call to detect a skipped point. https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/get-computerrestorepoint?view=powershell-5.1

  If the app lowers the frequency temporarily, that's itself a registry tweak: journal it and restore it right away.
- **Is it off by default?** It often is. System Protection for C: is frequently off on Windows 11 installs, and whether it's on by default isn't something to rely on *(verify per OEM image)*.
  - Detect it by listing restore points, or by querying the `SystemRestore` WMI class or the policy keys. https://learn.microsoft.com/en-us/answers/questions/172860/command-line-to-check-if-system-restore-is-turned and https://learn.microsoft.com/en-us/answers/questions/2573346/getting-an-error-system-protection-is-turned-off-w?forum=windows-all
  - Turn it on only with the user's consent, using `Enable-ComputerRestore -Drive C:\`, and journal that. https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/enable-computerrestore?view=powershell-5.1
- **Reliability:**
  - Restore points are deleted when the shadow-storage quota fills, and can fail with VSS errors (for example 0x81000202). https://learn.microsoft.com/en-us/answers/questions/2417907/error-vol-shadow-copy-not-working-(0x81000202)-whe?forum=windows-windows_vista-system and https://learn.microsoft.com/en-us/answers/questions/2147056/system-protection-turned-on-but-wont-save-restore
  - *(verify)* Points are generally gone after a feature update, and restores are known to leave Store apps broken.
  - `Restore-Computer` restores everything at once and reboots. https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/restore-computer?view=powershell-5.1
  - So it's a safety net, never the undo path.

## 12. How Windows feature updates revert tweaks

- A feature update is an in-place upgrade. It migrates user data, apps and *most* settings, but it re-lays the OS image, so some system state is reset. https://learn.microsoft.com/en-us/windows/deployment/upgrade/windows-upgrade-and-migration-considerations
- **Seen in practice *(verify on the next 24H2 to 25H2 upgrade)*:**
  - some service start types and scheduled tasks return to defaults;
  - inbox apps can reappear for existing users;
  - power and BCD entries are rewritten;
  - restore points disappear.
- **Design response:**
  - Every journal entry stores a `verify` probe (expected `after` state) and the OS build (`CurrentBuild.UBR`) it was applied on.
  - When the app starts, if the build changed, it runs every probe. Each tweak is then reported as *still applied*, *reverted by Windows* or *changed by someone else*.
  - The user can re-apply a reverted tweak, and doing so writes a new journal entry with a fresh before-state.
  - Undo of a reverted tweak is a no-op, and the entry is marked closed.

## 13. Recommended undo architecture

1. **Single elevated engine.** All changes go through one elevated helper, which is the only writer. Every tweak is declared as a list of typed **operations**: `RegValue`, `RegKey`, `RegSecurity`, `ServiceConfig`, `TaskState`, `TaskDefinition`, `AppxUser`, `AppxProvisioned`, `OptionalFeature`, `Capability`, `BcdElement`, `PowerSetting`, `PowerScheme`, `HostsBlock`, `FirewallRuleGroup`, `DriverPackage`. Each operation type implements `Capture()`, `Apply()`, `Verify()` and `Revert(before)`.
2. **Write-ahead journal.**
   - Store it in SQLite or JSON under `%ProgramData%\ElyntOptimizer\journal`, ACL'd to SYSTEM and Administrators.
   - For each operation, persist `{id, tweakId, opType, target, before, intendedAfter, osBuild, timestamp, status}` and flush it to disk *before* `Apply()`. Then set the status to applied, failed or pending-reboot.
   - If the app crashes mid-batch, entries left in "capturing" or "applying" state are reconciled on the next launch by running `Verify()`.
3. **Undo order and conflict checks.**
   - Undo runs per tweak, reverting that tweak's operations in reverse order.
   - Before reverting, compare the current state with `intendedAfter`. If they differ, something else changed the target, so warn the user rather than overwrite it.
   - "Undo all" reverts tweaks in reverse order of application.
4. **Reversibility class is computed, not hand-written.** Each operation reports its class: `Reversible`, `NeedsSource` (download, Store or FoD media) or `Destructive`. A tweak takes its worst operation's class, and the UI requires explicit opt-in for `Destructive`.
5. **Coarse snapshots.** On the first change of each kind per session, take the native export (BCD, firewall, power, task XML, driver export, hosts copy) and link it from the journal entries.
6. **Restore point.**
   - Before each apply batch, call `SRSetRestorePoint` or `Checkpoint-Computer`.
   - Detect a skip caused by the throttle or by protection being off, and show it to the user. Offer to enable protection, which is journaled.
   - Never block a reversible tweak on a missing restore point. Do require a restore point, or explicit user acknowledgement, before `Destructive` tweaks.
7. **Hard exclusions, never offered:**
   - WRP- or TrustedInstaller-protected keys and files, unless the tweak is labeled Destructive;
   - PPL/protected services;
   - Tamper-Protected Defender settings;
   - NonRemovable or framework Appx packages, and the Store and App Installer;
   - policy keys on domain-joined or MDM-managed devices.
8. **Drift check** after OS build changes (§12).
