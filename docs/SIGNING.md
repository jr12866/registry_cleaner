# Code Signing — Options, Costs, and Recommendation

Resolves open item #1 in `PLAN.md` §10. Written 2026-11-20. Prices checked against vendor pages on
that date; every figure below is a vendor list price before tax.

---

## 1. Why this matters more for this project than usual

A registry cleaner is a **textbook AV heuristic trigger**. It asks for administrator rights,
rewrites `HKLM`, and is behaviorally identical to the crack tools and "optimizers" that signature-
based detection targets. So the usual reasons for signing apply, plus one more:

| Without a valid signature | With a valid signature |
|--------------------------|-----------------------|
| SmartScreen: "Windows protected your PC — Unknown publisher" | SmartScreen shows the verified publisher name |
| Some enterprise policies **block unsigned binaries outright** | Passes those policies |
| AV vendors weight "unsigned + asks for admin + touches HKLM" heavily | The Authenticode signature is a strong, cheap trust signal |
| Users cannot tell your build from a trojanized one | Verifiable publisher, verifiable provenance |

It does **not** solve heuristic detection. A signed registry cleaner can still be flagged on
behavior. Signing improves reputation and identity; it does not make the tool look benign.

## 2. The 2026 validity change (and the thing everyone gets wrong)

The widely-reported "45-day certificates" rule is about **TLS**, not code signing.

| Certificate type | Rule | Effect from | Source |
|---|---|---|---|
| **TLS server** | CA/B Forum Ballot SC-081v3 — 200 days, then 100 days (Mar 2027), then 47 days (Mar 2029) | March 15, 2026 | [encryptionconsulting.com](https://www.encryptionconsulting.com/shorter-code-signing-certificates-lifespan/) |
| **Code signing** | CA/B Forum Ballot **CSC-31**, adopted Nov 17, 2025, Code Signing BR v3.10.0 — max **460 days** | March 1, 2026 | [encryptionconsulting.com](https://www.encryptionconsulting.com/shorter-code-signing-certificates-lifespan/), [SSL.com eSigner guide](https://www.ssl.com/guide/esigner-pricing-for-code-signing/) |

Vendors implemented the code-signing cap slightly differently — SSL.com enforced a **458-day** cap
from February 27, 2026; Certum states **459 days** from the same date. Treat **~460 days** as the
practical ceiling and confirm the exact figure with your chosen CA.

**Practical consequences for us:**
- Multi-year product *subscriptions* still exist — the CA reissues the certificate under the hood
  at the new limit, and your build pipeline does not change.
- **Always timestamp.** Authenticode signatures must carry an RFC 3161 timestamp or they die with
  the certificate. Use `signtool /tr <timestamp-url> /td SHA256`.
- Release cadence is unchanged. Sign every release; nothing more complicated.

## 3. The other 2023 rule that drives the real cost

Since **June 1, 2023**, the CA/Browser Forum requires that **every** new OV/IV code signing private
key be held in a **FIPS 140-2 hardware token or a cloud HSM**. The key can no longer be downloaded
as a `.pfx`.

So the certificate price is only half the bill:

| Key storage | Cost | Notes |
|---|---|---|
| **Cloud HSM** (SSL.com eSigner, DigiCert KeyLocker, Certum SimplySign) | $15–$99.99/month | Recommended — no hardware to lose, CI-friendly |
| **USB token** (YubiKey FIPS) | $379 one-time | Cheap if you sign rarely and by hand |

This is why a "$129 certificate" is rarely a $129 purchase.

## 4. The options, ranked

### Tier 0 — Free

| Option | Cost | Who | Publisher shown | Catch |
|---|---|---|---|---|
| **Microsoft Store (MSIX)** | **$0** | Anyone with a free Partner Center account | You, via the Store | ⚠️ **Not viable for this project — see below** |
| **SignPath Foundation** | **$0** | Qualifying open-source projects | "SignPath Foundation" | Application is discretionary and geared toward projects with public traction |

#### ⚠️ The Microsoft Store route is closed to us

This looked like the obvious $0 answer, and it is not. Microsoft states plainly:

> "Your application needs to work while running as the interactive user. Users who install your
> application may not be system administrators, so requiring your application to run elevated means
> that it won't run correctly for standard users. **If you plan on publishing your app to the
> Microsoft Store, apps that require elevation for any part of their functionality won't be
> accepted into the Store.**"
> — [Prepare to package a desktop application (MSIX)](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)

The `allowElevation` restricted capability exists, but it is explicitly restricted to "apps
developed by Microsoft partners or enterprise organizations," is subject to approval under strict
criteria, and — in the words of a Microsoft Q&A answer — "it is generally unlikely that an app
forcing elevation on launch will pass the standard certification."

**A registry cleaner that rewrites `HKLM` requires elevation by definition.** There is no version of
this tool that can do its job as a standard user. The Store path would require a significant
redesign: split the app into a user-mode UI plus an elevated helper process communicating over a
named pipe, with IPC authentication and process-lifecycle handling. Even then, certification is
reviewed case by case under Store Policy 10.2 (Security), and the outcome is not guaranteed.

**Decision: we do not pursue the Microsoft Store.** Recorded as [ADR-021](DECISIONS.md#adr-021).
We ship a portable signed EXE.

**Microsoft Store details, for reference.** Microsoft re-signs MSIX packages after certification,
so no CA-trusted certificate is needed at all. The Store does **not** re-sign MSI/EXE installers —
that path still requires your own cert. ([Get started with Microsoft Store](https://learn.microsoft.com/en-us/windows/apps/publish/get-started))

**SignPath Foundation details.** Free OV-level signing with the key in their HSM, built for CI
([signpath.org](https://signpath.org/), [about.signpath.io/product/open-source](https://about.signpath.io/product/open-source)). Conditions ([terms](https://signpath.org/terms.html)): OSI license, no proprietary components, actively maintained, already released, documented, MFA on GitHub and SignPath for everyone in signing roles, per-release manual approval, verifiable build origin, and the required "Code signing policy" note in the README. The certificate is issued to *SignPath Foundation*, so the publisher shows their name, not yours. Apply at [signpath.org/apply](https://signpath.org/apply.html).

**Expect this to be hard at first.** Two real 2026 projects documented being declined or
discouraged for lacking public signs of trust (stars, forks, contributors, articles) — the
Foundation tends to approve projects that are already known. Realistically: publish the repo, get
some traction, then apply. Apply early anyway — it costs nothing.

### Tier 1 — Cheap and individual-friendly

| Option | Cost | Publisher | Key storage | Best for |
|---|---|---|---|---|
| **Certum Open Source Code Signing** | **from €49/yr** (cloud/SimplySign) or €69 (card + reader) | Your name, as "Open Source Developer, NAME" | SimplySign cloud included | Individuals, open-source, EU-based |
| **SSL.com IV Code Signing** | **$129/yr** + eSigner tier 1 ($180/yr annual, or $15/mo) | Your verified personal name | eSigner cloud or $379 YubiKey | Individuals anywhere in the world |
| **Azure Artifact Signing** | **$9.99/mo** = **$119.88/yr** | Your validated name | Microsoft-managed HSM, no hardware | Individuals **in the US/Canada**; orgs in US/CA/EU/UK |

**Certum Open Source.** Issued to an individual without a registered company — rare and valuable.
Cloud signing from €49. Weakness: SimplySign requires a phone-generated one-time code per signing,
so unattended CI means storing a TOTP seed (undesirable) or signing on an attended machine.
Source: [codenote.net](https://codenote.net/en/posts/ov-ev-code-signing-certificate-pricing-japan-2026/), plus a maintainer's 2026 field report.

**SSL.com IV.** IV = Individual Validation; no business registration required. $129 for one year
($96.75/yr on a 5-year plan). Identity validation needs a government-issued ID plus a photo of you
holding it. Private keys cannot be exported and cannot be issued as a downloadable `.pfx`.
Source: [ssl.com IV product page](https://www.ssl.com/products/software-integrity/code-signing/iv/).

**Azure Artifact Signing** (formerly Trusted Signing; renamed and GA January 2026). Basic
$9.99/mo = 5,000 signatures, Premium $99.99/mo = 100,000, overage $0.005. Certificates rotate
daily and are valid 72 hours, so **every signature needs an RFC 3161 timestamp** — Microsoft runs a
timestamp server. Identity validation takes 1–7 (up to 20) business days, and **billing starts when
the account is created**, not when you first sign. Free/trial Azure subscriptions are not accepted.
Source: [azure.microsoft.com/products/artifact-signing](https://azure.microsoft.com/en-us/products/artifact-signing), [learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

**The geographic catch:** Microsoft's own table lists Artifact Signing availability as
**organizations** in USA, Canada, EU, UK; **individuals in USA and Canada only**. Some
third-party pages list a wider organization country set (adding Australia, New Zealand, Japan,
South Korea, Singapore, Switzerland, Norway, and others). **If you are an individual outside the
US/Canada, Azure is off the table** — SSL.com IV or Certum Open Source are your routes.

### Tier 2 — Conventional OV/EV

| Vendor | OV | EV | Notes |
|---|---|---|---|
| **SSL.com** | $129/yr | $349/yr | Cheapest mainstream. IV also $129 |
| **Sectigo / Comodo** | from $219/yr (reseller) / ~$536/yr (direct, 5-yr plan) | from $287/yr (reseller) | FIPS token included; reseller pricing varies widely |
| **GlobalSign** | from $529/yr | from $669/yr | 1-year terms only since Dec 2025; own-HSM option $289 (OV) / $410 (EV) |
| **DigiCert** | $696/yr | $972/yr | Premium tier; KeyLocker cloud HSM available |
| **Certum** | from €169 (card+reader) / €209 (cloud) | from €359/€379 | EU-based, cheapest EU route |

### The EV question — answer: **don't**

Microsoft's own comparison table now states that EV certificates have had **no SmartScreen
advantage over OV since 2024** and are "no longer recommended specifically for SmartScreen bypass,"
at **$400+/year** versus OV's $150–300 ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)).
Some third-party blogs still claim EV grants "instant reputation." The vendor documentation is the
more authoritative source, and it contradicts them.

**Buy OV (or IV).** Paying 3–4× for EV buys no measurable user-visible benefit for a
consumer-distributed utility.

## 5. Recommendation for this project

**Assumed context:** a solo developer in the United States, publishing as an individual, under the
MIT License as an open source project (ADR-022).

### Short answer

**Do not buy anything yet.** Then, when you are ready to distribute:

| If you want… | Buy this | Cost |
|---|---|---|
| **Your name on the binary, cheapest option** | **Certum Open Source Code Signing** | **~€49/yr** (~$55) |
| **Zero-friction CI signing, Microsoft's own service** | **Azure Artifact Signing** | **$119.88/yr** |
| **$0, your name not shown** | **SignPath Foundation** (apply when the repo has traction) | **$0** |
| **Never** | EV certificates | — |

### Why "not yet" is the right first move

1. **There is no code.** Signing is an M5 concern; nothing before M5 needs a certificate.
2. **A hobby project's audience tolerates "Unknown publisher."** Most indie Windows utilities show
   it. The people who install a registry cleaner are power users who read the release notes.
3. **Azure bills from day one.** The Artifact Signing account charges $9.99/month from the moment
   you create it — not prorated, and not from your first signature. If validation drags or fails,
   you are paying for an idle service.
4. **Identity validation is a multi-day gate with real failure modes.** It requires a government
   photo ID and a live selfie-style check through Microsoft Authenticator, takes 1–20 business
   days, cannot be expedited, and allows only three document-upload attempts. If it fails, your
   next attempt may need a new request.
5. **Certs are ~460 days.** You can buy one the week you have a release to distribute and get
   nearly a full year of coverage. There is no advantage to buying early.
6. **The rules are still moving.** The 460-day cap only took effect in March 2026. Pricing and
   eligibility will shift again before you need it.

### When you are ready — the routes, honestly assessed

**Route A — Certum Open Source Code Signing (~€49/yr). Best value.**
- Issued to an **individual** with no registered company — rare, and the main reason it's cheap.
- Publisher shows **your name** ("Open Source Developer, NAME").
- Cloud signing (SimplySign) included in the price.
- Requires: an OSI-licensed public repository, and signing on an attended machine (SimplySign
  asks for a phone-generated one-time code). For a hobbyist releasing a version every few weeks,
  attended signing is completely fine.
- Weaknesses: priced in euros; one SKU was reported out of stock in October 2026; the ~459-day
  validity cap means you re-sign roughly twice a year.

**Route B — Azure Artifact Signing ($119.88/yr). Best if you want CI signing and no vendor juggling.**
- You are eligible: individuals in the US and Canada, with an Azure subscription whose billing
  account type is **Individual**, legal name and sold-to address matching your government ID.
- No hardware, keys never leave Microsoft's HSM, daily cert rotation (72-hour validity),
  RFC 3161 timestamping included, and an official GitHub Action for CI.
- The friction: paid Azure subscription (no free/trial/sponsored), an Entra tenant, identity
  validation via a third-party verifier, and billing that starts when the account is created.
- Microsoft warns reputation builds over time — expect initial SmartScreen warnings even though
  the publisher name is correct.

**Route C — SignPath Foundation ($0). Apply later, keep it open.**
- Free, key in their HSM, designed for CI, and it removes the installation warnings.
- The publisher shows **"SignPath Foundation"**, not you.
- Acceptance is discretionary and favors projects with visible public traction (stars, forks,
  contributors, articles). Two 2026 projects documented being declined or discouraged for exactly
  that reason. Plan on this being a year-two option, not a launch-week one.
- Applying is free and takes minutes — worth submitting the application early anyway.
- **The MIT license already satisfies the OSI-license requirement.** What the project must add:
  - The acknowledgement line in `CONTRIBUTING.md` — *"Free code signing provided by SignPath.io,
    certificate by SignPath Foundation."* ✅ done
  - A `SECURITY.md` with a private reporting channel ✅ done
  - A `CODE_OF_CONDUCT.md` ✅ done
  - No proprietary code in the repository — all dependencies must be MIT/Apache/BSD-compatible.
    `CommunityToolkit.Mvvm` (MIT) and `System.CommandLine` (MIT) qualify.
  - **A trusted build system linked to GitHub**, with binaries built from source in a verifiable
    way. This is why CI lands in M0 rather than M5.
  - MFA enabled on GitHub and SignPath for everyone in a signing role, and **per-release manual
    approval** of signing requests.
  - Product name and version metadata set identically on every build.

**Route D — SSL.com IV ($129 + ~$180/yr eSigner = ~$309/yr).**
- Correct choice only if Azure is unavailable *and* you want CI signing. Otherwise it is the most
  expensive individual route for the same outcome.

### The Certum Open Source specifics (Route A, confirmed current)

| Item | Detail |
|------|--------|
| Product | *Open Source Code Signing in the Cloud* — [certum.store](https://certum.store/open-source-code-signing-on-simplysign.html) |
| Price | **€49.00** (or **$58.00** from the USD storefront) |
| Certificate subject | Natural-person data prefixed with **"Open Source Developer"** |
| Trust | Trusted by Microsoft; supports building SmartScreen reputation |
| Signature limit | 5,000 signatures per month |
| Validity | Capped at **459 days** from 2026-02-27; multi-year products include free reissues |
| Requirements | SimplySign mobile app for access codes, SimplySign desktop app, internet access |
| Financial warranty | €60,000 |

The "Open Source Developer" prefix is a real trade-off: the publisher reads as
**"Open Source Developer, Joe Smith"** rather than just "Joe Smith". That is normal for OSS
signing and most users read it as a positive signal.

### Practical notes specific to this project

- **Sign the GUI EXE only.** Ship the CLI unsigned — it is a developer tool, and `winget` and
  GitHub Releases work fine without Authenticode. The GUI is what users double-click and what
  triggers SmartScreen.
- **Timestamp everything:** `signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256`.
- **Publish SHA-256 checksums** with each release. For a tool that asks for admin rights, a
  published hash is a meaningful trust signal independent of any certificate.
- **Ship to `winget`** as well as GitHub Releases. Microsoft's own table lists winget manifests as
  "cert recommended," and it adds real discoverability for exactly this audience.
- **Do not use the Store.** Covered above — elevation is disqualifying.

### The honest bottom line

A hobbyist's registry cleaner does not need a code-signing certificate to be useful, and it does
not need one to be trusted by the small audience that will actually install it. It needs a
**transparent, evidence-first tool with a working undo** — which is what the rest of this plan is
about. Signing is a polish step for when there is real usage, and by then you will know whether
this project is worth $50 or $120 a year.

## 6. Open items this does not resolve

| Item | Note |
|------|------|
| **Public or closed source** | Determines whether SignPath Foundation and Certum Open Source are on the table. Certum Open Source requires an OSI-licensed public repository |
| **Store certification risk** | Resolved — the Store path is closed to us (ADR-021) because elevation is disqualifying |
| **EV for driver signing** | Not needed — we're not shipping a kernel driver. If that ever changes, the requirements are different (and stricter) |
| **Azure subscription hygiene** | If the Azure route is chosen: use a separate resource group, set a budget alert, and delete the Artifact Signing account when idle — billing continues until you do |

## 7. Sources

- [Code signing options for Windows app developers — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) — official cost/availability/SmartScreen comparison table
- [Get started with Microsoft Store — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/publish/get-started) — MSIX free re-signing
- [Azure Artifact Signing — Microsoft Azure](https://azure.microsoft.com/en-us/products/artifact-signing) — pricing and rebrand
- [Artifact Signing pricing — Microsoft Azure](https://azure.microsoft.com/en-us/pricing/details/trusted-signing) — tier and quota detail
- [Choose a distribution path for your Windows app — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path) — signing requirement per distribution channel
- [Sign your MSIX package — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide) — individuals limited to US/Canada; OV fallback guidance
- [Shorter code signing certificate lifespans — Encryption Consulting](https://www.encryptionconsulting.com/shorter-code-signing-certificates-lifespan/) — Ballot CSC-31, 460-day cap
- [Prepare to package a desktop application (MSIX) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare) — elevation disqualifies Store submission
- [App capability declarations — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations) — `allowElevation` is a restricted capability under strict review
- [Run msix packaged app as admin — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5907620/run-msix-packaged-app-as-admin) — Store acceptance unlikely for apps forcing elevation; split-process alternative
- [Quickstart: Set up Artifact Signing — Microsoft Learn](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) — individual validation via Entra Verified ID, billing-account requirements
- [SSL.com IV Code Signing](https://www.ssl.com/products/software-integrity/code-signing/iv/) — $129/yr, hardware requirement, validation process
- [SSL.com eSigner pricing for code signing](https://www.ssl.com/guide/esigner-pricing-for-code-signing/) — cloud HSM tiers, 458-day enforcement
- [SSL.com code signing cloud signing options](https://www.ssl.com/guide/code-signing-certificates-cloud-signing-options-and-signing-operations-integration/) — FIPS hardware rule, no exportable PFX
- [DigiCert — compare code signing certificates](https://www.digicert.com/signing/compare-code-signing-certificates) — DigiCert pricing
- [SignPath Foundation](https://signpath.org/) and [conditions](https://signpath.org/terms.html) and [apply](https://signpath.org/apply.html) — free OSS signing and eligibility
- [Certum / OV-EV pricing and eligibility overview — codenote.net](https://codenote.net/en/posts/ov-ev-code-signing-certificate-pricing-japan-2026/) — Certum cloud pricing, Azure individual restriction
- [tracedraw code-signing.md](https://github.com/tracedraw/tracedraw/blob/main/docs/code-signing.md) and [saiph CODE_SIGNING.md](https://github.com/Abelik1/saiph/blob/main/docs/CODE_SIGNING.md) — 2026 field reports on SignPath acceptance difficulty and Certum Open Source in practice
