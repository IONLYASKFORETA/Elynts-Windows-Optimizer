namespace Elynt.Core.Tests;

/// <summary>
/// A journal file exactly as an earlier version wrote it (before whole-PC changes were shared, ADR 0007): one change
/// of every kind, all made from Aden's account (S-1-5-21-aden). It must load unchanged, with no migration for anyone
/// to run. A game profile's two changes (closing Discord, the Ultimate Performance plan) are still temporary, as a
/// crash mid-game leaves them.
/// </summary>
internal static class EarlierJournal
{
    public const string Json =
        """
        [
          {
            "Id": "00000000-0000-0000-0000-000000000001",
            "TweakId": "test.machine-wide",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:01:00+00:00",
            "Steps": [
              {
                "kind": "registryValue",
                "Location": {
                  "Root": "LocalMachine",
                  "KeyPath": "SOFTWARE\\ElyntTest",
                  "ValueName": "MachineWide"
                },
                "Before": null,
                "After": {
                  "Kind": "DWord",
                  "Data": "AQAAAA=="
                },
                "CreatedKeys": [
                  "SOFTWARE\\ElyntTest"
                ]
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000002",
            "TweakId": "test.own-settings",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:02:00+00:00",
            "Steps": [
              {
                "kind": "registryValue",
                "Location": {
                  "Root": "CurrentUser",
                  "KeyPath": "Software\\ElyntTest",
                  "ValueName": "OwnSettings"
                },
                "Before": {
                  "Kind": "DWord",
                  "Data": "AAAAAA=="
                },
                "After": {
                  "Kind": "DWord",
                  "Data": "AQAAAA=="
                },
                "CreatedKeys": []
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000003",
            "TweakId": "service:Spooler",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:03:00+00:00",
            "Steps": [
              {
                "kind": "serviceConfig",
                "ServiceName": "Spooler",
                "Before": {
                  "Start": "Automatic",
                  "DelayedAutoStart": false,
                  "ErrorControl": 1,
                  "BinaryPath": "",
                  "Account": "",
                  "LoadOrderGroup": "",
                  "Dependencies": [],
                  "TriggerStart": false
                },
                "After": {
                  "Start": "Manual",
                  "DelayedAutoStart": false,
                  "ErrorControl": 1,
                  "BinaryPath": "",
                  "Account": "",
                  "LoadOrderGroup": "",
                  "Dependencies": [],
                  "TriggerStart": false
                }
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000004",
            "TweakId": "startup:task:\\Vendor\\Updater",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:04:00+00:00",
            "Steps": [
              {
                "kind": "scheduledTask",
                "TaskPath": "\\Vendor\\Updater",
                "WasEnabled": true,
                "SetEnabled": false
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000005",
            "TweakId": "gaming.fullscreen-optimizations-off|C:\\Games\\Valorant\\VALORANT.exe",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:05:00+00:00",
            "Steps": [
              {
                "kind": "registryListItem",
                "Location": {
                  "Root": "CurrentUser",
                  "KeyPath": "Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers",
                  "ValueName": "C:\\Games\\Valorant\\VALORANT.exe"
                },
                "Format": "CompatibilityLayers",
                "Item": "DISABLEDXMAXIMIZEDWINDOWEDMODE",
                "Before": null,
                "After": {
                  "Kind": "String",
                  "Data": "fgAgAEQASQBTAEEAQgBMAEUARABYAE0AQQBYAEkATQBJAFoARQBEAFcASQBOAEQATwBXAEUARABNAE8ARABFAAAA"
                },
                "ItemExisted": false,
                "ItemValueBefore": null,
                "CreatedKeys": []
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000006",
            "TweakId": "gaming.placebo.nagle-off",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:06:00+00:00",
            "Steps": [
              {
                "kind": "registryEachKey",
                "Values": [
                  {
                    "Location": {
                      "Root": "LocalMachine",
                      "KeyPath": "SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\{one}",
                      "ValueName": "TcpAckFrequency"
                    },
                    "Before": null,
                    "After": {
                      "Kind": "DWord",
                      "Data": "AQAAAA=="
                    },
                    "CreatedKeys": []
                  }
                ]
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000007",
            "TweakId": "gaming.placebo.dynamic-tick-off",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:07:00+00:00",
            "Steps": [
              {
                "kind": "bootSetting",
                "Element": "disabledynamictick",
                "Before": null,
                "After": "Yes"
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000008",
            "TweakId": "gaming.high-performance-plan",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:08:00+00:00",
            "Steps": [
              {
                "kind": "powerPlan",
                "Before": "381b4222-f694-41f0-9685-ff5bb260df2e",
                "Plan": "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                "Created": false
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000009",
            "TweakId": "debloat.remove-copilot",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:09:00+00:00",
            "Steps": [
              {
                "kind": "appRemoval",
                "App": {
                  "Name": "Copilot",
                  "PackageName": "Microsoft.Copilot",
                  "Kind": "Appx",
                  "ReinstallFrom": "MicrosoftStore",
                  "ReinstallId": "9NHT9RB2F4HD"
                },
                "WasInstalled": true
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000010",
            "TweakId": "debloat.deprovision-preinstalled",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:10:00+00:00",
            "Steps": [
              {
                "kind": "appDeprovision",
                "App": {
                  "Name": "Copilot",
                  "PackageName": "Microsoft.Copilot",
                  "Kind": "Appx",
                  "ReinstallFrom": "MicrosoftStore",
                  "ReinstallId": "9NHT9RB2F4HD"
                },
                "WasProvisioned": true
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000011",
            "TweakId": "privacy.remove-recall",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:11:00+00:00",
            "Steps": [
              {
                "kind": "optionalFeature",
                "Feature": "Recall",
                "Before": true,
                "After": false
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000012",
            "TweakId": "profile.close-app|C:\\Program Files\\Discord\\Discord.exe",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:12:00+00:00",
            "Steps": [
              {
                "kind": "closedApp",
                "ExePath": "C:\\Program Files\\Discord\\Discord.exe",
                "User": "S-1-5-21-aden"
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": true
          },
          {
            "Id": "00000000-0000-0000-0000-000000000013",
            "TweakId": "gaming.placebo.core-parking-off",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:13:00+00:00",
            "Steps": [
              {
                "kind": "powerSetting",
                "Plan": "381b4222-f694-41f0-9685-ff5bb260df2e",
                "Subgroup": "54533251-82be-4824-96c1-47b60b740d00",
                "Setting": "0cc5b647-c1df-4637-891a-dec35c318583",
                "SettingName": "core parking min cores (CPMINCORES)",
                "BeforeAc": 10,
                "BeforeDc": 5,
                "After": 100
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000014",
            "TweakId": "gaming.placebo.hpet-off",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:14:00+00:00",
            "Steps": [
              {
                "kind": "deviceEnabled",
                "DeviceName": "High precision event timer",
                "Before": [
                  {
                    "InstanceId": "ACPI\\PNP0103\\0",
                    "Enabled": true
                  }
                ],
                "After": false
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000015",
            "TweakId": "security.game-folder-scan-exclusion|C:\\Games\\Valorant\\VALORANT.exe",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:15:00+00:00",
            "Steps": [
              {
                "kind": "scanExclusion",
                "Folder": "C:\\Games\\Valorant",
                "WasExcluded": false
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000016",
            "TweakId": "test.both",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:16:00+00:00",
            "Steps": [
              {
                "kind": "registryValue",
                "Location": {
                  "Root": "LocalMachine",
                  "KeyPath": "SOFTWARE\\ElyntTest",
                  "ValueName": "MachineWide"
                },
                "Before": null,
                "After": {
                  "Kind": "DWord",
                  "Data": "AQAAAA=="
                },
                "CreatedKeys": [
                  "SOFTWARE\\ElyntTest"
                ]
              },
              {
                "kind": "registryValue",
                "Location": {
                  "Root": "CurrentUser",
                  "KeyPath": "Software\\ElyntTest",
                  "ValueName": "OwnSettings"
                },
                "Before": {
                  "Kind": "DWord",
                  "Data": "AAAAAA=="
                },
                "After": {
                  "Kind": "DWord",
                  "Data": "AQAAAA=="
                },
                "CreatedKeys": []
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": false
          },
          {
            "Id": "00000000-0000-0000-0000-000000000017",
            "TweakId": "gaming.ultimate-performance-plan",
            "User": "S-1-5-21-aden",
            "OsBuild": "26100.1",
            "AppliedAt": "2026-09-01T18:17:00+00:00",
            "Steps": [
              {
                "kind": "powerPlan",
                "Before": "381b4222-f694-41f0-9685-ff5bb260df2e",
                "Plan": "4c1e7a3b-6d2f-4e8a-9b5c-e1a7d0c3b201",
                "Created": true
              }
            ],
            "Status": "Applied",
            "ClosedAt": null,
            "Temporary": true
          }
        ]
        """;
}
