# Ship builds to friends without SmartScreen and Smart App Control pain

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/21

Researched 2026-10-16, mostly against Microsoft Learn, the CA/Browser Forum, Velopack's docs and GitHub's docs. Each claim has a link. Labels:

- **(secondary)**: only a non-first-party source backs the claim.
- **(inference)**: my own reading, with no source saying it.
- **(unverified)**: I could not load the first-party page (support.microsoft.com refused the fetch), so the claim rests on memory or a secondary source.

## Answer

- **Free option: ship unsigned and have friends turn Smart App Control off.** SmartScreen shows "Windows protected your PC", and the user clicks *More info → Run anyway*. That's fine for a few friends. Smart App Control (SAC) is the real blocker, because it **blocks unsigned code outright**, and that includes our service EXE and any DLL it loads. Since KB5083769 (April 2026), users can turn SAC off and back on without reinstalling Windows, so asking friends to turn it off is no longer a one-way decision.
- **Self-signing doesn't help.** SmartScreen treats a self-signed build like an unsigned one, and SAC only trusts certificates that chain to the Microsoft Trusted Root Program.
- **Cheapest real certificate: Azure Artifact Signing (formerly Trusted Signing), from $9.99/month.** The key lives in the cloud, so you need no HSM or USB token. It works with GitHub Actions and Velopack (`--azureTrustedSignFile`). **The catch: Public Trust for individual developers is only available in the US and Canada.** Outside those, you need a registered organisation in a listed country, or a paid OV certificate kept on a cloud HSM (e.g. Certum SimplySign, from about €209).
- **Signing does not stop SmartScreen on day one.** Reputation builds over weeks and "hundreds of clean installs", and "EV certificates no longer bypass SmartScreen". A few friends will never reach that volume. What signing *does* buy is that **SAC lets a signed app run** even when it has no reputation.
- **Updates: Velopack + a separate *public* releases repo (or any static HTTPS host), not a private repo.** A private repo means putting a GitHub token in every copy of the app. Plain EXE via Velopack, not MSIX. MSIX needs a trusted certificate (self-signed means friends must import it into Trusted Root), and its service support needs restricted capabilities.
- **Recommended minimal-cost setup:**
  1. Now (€0): unsigned Velopack builds on a public `…-releases` repo. Give friends a one-page "Run anyway / turn SAC off" guide.
  2. If friends complain or SAC matters: add Artifact Signing ($9.99/mo, if eligible; otherwise Certum cloud OV) and sign *every* EXE/DLL, including the service, with the same identity.

## SmartScreen

- SmartScreen checks a downloaded file's **publisher reputation** (its certificate) and **file-hash reputation**. "A negative or unknown reputation for a file's hash or its publisher's certificate can cause warnings to show." https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
- For an unsigned file, reputation "must build for each new version… starting with zero reputation". Self-signed certificates behave the same way. Every release we ship unsigned resets the warning. (same source)
- Users can click *Run anyway*. Enterprise policy can remove that option, which shouldn't matter for friends' home PCs (inference). (same source)
- There is no threshold: "it can take several weeks and hundreds of clean installs from a wide audience". Consumers have no manual review. (same source)
- "EV certificates no longer bypass SmartScreen… Paying a premium for EV solely to avoid SmartScreen warnings is no longer justified." (same source)
- Velopack's signing page still says "EV certificates are granted instant SmartScreen reputation", and says the same of Artifact Signing. **This contradicts Microsoft's current page, and Microsoft's page wins.** https://docs.velopack.io/packaging/signing
- Artifact Signing's own FAQ promises nothing instant either: "SmartScreen reputation builds up automatically. The prompt stops appearing once the file hash has sufficient download history." https://learn.microsoft.com/en-us/azure/artifact-signing/faq
- With a stable signing identity, reputation carries over to later versions, so it eventually stops resetting each release. Velopack says renewing an OV certificate restarts the process. https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation, https://docs.velopack.io/packaging/signing

## Smart App Control

- "Malware, Potentially Unwanted Apps (PUA), and unknown, unsigned code are blocked by default." https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview
- If the cloud service can't make a prediction, SAC still allows an app "signed with a certificate issued by a certificate authority (CA) within the Trusted Root Program". A self-signed certificate is not in that program, so self-signed builds are blocked (inference from the same page).
- SAC applies to all executables, not just downloaded ones. https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
- SAC can block a *part* of an app: a "driver, app, or other file that isn't properly signed". The functionality that depends on it then fails. **So the service EXE, the GUI EXE and every native DLL we load all need signatures.** https://support.microsoft.com/en-US/Windows/Security/Threat-Malware-Protection/smart-app-control-has-blocked-part-of-this-app
- Neither Microsoft page describes a per-app allow or override. **(unverified)** As far as I know, the only user fix is turning SAC off.
- **Since KB5083769 (14 Apr 2026, builds 26100/26200.8246), SAC can be turned on or off in Windows Security → App & Browser Control without a clean install.** (secondary) https://www.bleepingcomputer.com/news/microsoft/windows-11-cumulative-updates-kb5083769-and-kb5082052-released/amp/, https://blog-en.topedia.com/2026/04/smart-app-control-in-windows-11-can-now-be-re-enabled-without-reinstalling/. I could not load Microsoft's KB page.
- SAC automatically turns itself off for users it judges unsuitable while it is in evaluation mode. Many friends' PCs may therefore already show it as Off. https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview

## Code-signing options

### Azure Artifact Signing (formerly Trusted Signing)

- The price starts at **$9.99/month**. You need no hardware token, and it plugs into GitHub Actions and Azure DevOps. https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
- Tiers are Basic (5,000 signatures/month) and Premium (100,000). The pricing page showed placeholders rather than amounts, so I have **not verified** the Premium price or the overage rate. https://azure.microsoft.com/pricing/details/artifact-signing/
- Billing starts when you create the account, and it is not prorated. You need a paid subscription: free, trial or sponsored subscriptions are not supported. https://learn.microsoft.com/en-us/azure/artifact-signing/faq
- **Who can get Public Trust:** "Individual developers must be located in the United States or Canada." Organisations qualify in the US, Canada, EU, UK, Australia, NZ, Japan, South Korea, Singapore, Switzerland, Norway and Israel. For an individual, the Azure billing account must be of type *Individual*, and its name and address must match a government ID. Validation takes 1 to 20 business days. https://learn.microsoft.com/azure/artifact-signing/quickstart
- Private Trust has no geographic limits, but SAC and SmartScreen don't trust it on consumer PCs (inference: it doesn't chain to the Trusted Root Program).
- An open Q&A asks whether an Irish sole trader qualifies, which suggests an EU individual developer cannot. https://learn.microsoft.com/en-us/answers/questions/5915697/can-an-irish-sole-trader-(non-incorporated)-qualif

### OV / EV certificates from a CA

- **Since 2023-06-01, the subscriber's private key must be generated and kept in a hardware crypto module** (FIPS 140-2 Level 2 or equivalent) for OV as well as EV. In practice that means a USB token or a cloud HSM, and no `.pfx` on disk. CA/B Forum Code Signing BR §6.2.7.4. https://cabforum.org/working-groups/code-signing/requirements/
- **Certificates issued on or after 2026-03-01 can be valid for at most 460 days**, so you will re-buy or renew roughly yearly. (same source, §6.3.2)
- An example of a cheap cloud-HSM OV for individuals is **Certum Standard Code Signing in the Cloud, from €209**, with the key kept in SimplySign (FIPS 140-2 L3 / CC EAL4+). The subject can be a natural person. https://shop.certum.eu/standard-code-signing-in-the-cloud.html. Its signing flow through a virtual smartcard is awkward in CI (inference).
- EV costs more and no longer buys SmartScreen reputation (see above), so it's not worth it here.

## Update delivery

### Velopack

- `vpk` signs app binaries, `Setup.exe` and the MSI, and timestamps each one. It skips files that are already signed. Its options are `--azureTrustedSignFile` (native Artifact Signing since Velopack 1.3), `--signParams` (signtool) and `--signTemplate` (e.g. AzureSignTool). https://docs.velopack.io/packaging/signing
- Update sources include `GithubSource`, `SimpleWebSource` (any static HTTPS location, including S3 and Azure Blob), `SimpleFileSource`, GitLab, Gitea and Velopack Flow. **For a private GitHub repo, `accessToken` is required.** Without a token, the limit is 60 requests/hour per IP. https://docs.velopack.io/integrating/update-sources
- Setup.exe installs **per-user** to `%LocalAppData%\{packId}` without elevation. A per-machine MSI (`--msi --instLocation PerMachine`) needs elevation. https://docs.velopack.io/packaging/operating-systems/windows
- **Velopack has no built-in support for Windows services** (its Windows and hooks pages don't mention them). Hooks must exit within 30 s (install/uninstall) or 15 s (update), and can't show UI. https://docs.velopack.io/integrating/hooks. Our app will need to register, stop and restart the elevated service itself, e.g. the GUI elevates once to (re)install the service from the new `current` folder (inference).

### Private repo vs public releases repo

- A fine-grained PAT can be limited to one repo with read-only Contents. Even so, GitHub says to "Treat your access tokens like passwords." https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- Any token built into the EXE can be extracted by anyone holding the binary (inference). Release assets alone would leak little, but if the token's scope isn't kept tight it also exposes source. Tokens also expire, which would break updates for everyone at once (inference).
- **So: keep source private, and publish only build artefacts to a separate public `…-releases` repo** (or a static host via `SimpleWebSource`). Clients then need no token (inference, consistent with the Velopack docs above).

### MSIX vs plain EXE

- Sideloading an MSIX requires a certificate the device trusts. For a self-signed package, each friend must import the certificate into **Local Machine → Trusted Root Certification Authorities** (as an admin), and the page says sideloading may need to be turned on. https://learn.microsoft.com/en-us/windows/application-management/sideload-apps-in-windows
- A packaged service (`desktop6:Service`) needs the `packagedServices` or `localSystemServices` **restricted capability**. https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-service
- MSIX adds certificate setup and restricted-capability friction, and it gives us no SmartScreen benefit outside the Store. **Plain EXE via Velopack is the simpler choice** (inference).
- The Microsoft Store is the one route with no SmartScreen download warning. https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation. It is out of scope for a private friends build.

## Open questions

- Whether SAC offers any per-app exception in current builds. **(unverified)** I couldn't load the Microsoft FAQ.
- Artifact Signing Premium price and overage rate. **(unverified)**
- Whether the owner is in the US or Canada, which decides Artifact Signing eligibility.
