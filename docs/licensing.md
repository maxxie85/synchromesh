# Licensing: going open source

Status (2026-10-18): the app is licensed under **MIT** (`LICENSE`, copyright Max Vinken). This note lists what it
includes, which licenses apply, and what still needs doing before publishing. It is an engineering inventory,
not legal advice.

## What the app ships

Everything in a release build, from `dotnet list package --include-transitive` and the package metadata, checked
against the upstream license files. Full texts: `THIRD-PARTY-NOTICES.txt` (repository root, inside the app under
About > License texts, and next to the app in both installers).

| Component | License | Notes |
|---|---|---|
| Avalonia 12.1.4 (+ Native, Skia, HarfBuzz, Win32, X11, FreeDesktop, Remote.Protocol) | MIT | |
| Material.Avalonia 3.21.0 | MIT | Embeds the **Roboto** font: Apache 2.0 |
| Material.Icons / Material.Icons.Avalonia 3.0.2 | MIT | Icon artwork from Material Design Icons (Pictogrammers): Apache 2.0 |
| CommunityToolkit.Mvvm 8.4.2 | MIT | |
| Simple Injector 5.6.0 | MIT | |
| SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3 | MIT | Native Skia: BSD-3-Clause; native HarfBuzz: "Old MIT" |
| MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1 | MIT | Linux/macOS plumbing pulled in by Avalonia.Desktop |
| Native libraries inside libSkiaSharp and libHarfBuzzSharp (FreeType, libpng, libjpeg-turbo, libwebp, expat, zlib, …) | FreeType License, zlib, BSD, MIT and others | All builds; their notices are in SkiaSharp's THIRD-PARTY-NOTICES, included in ours |
| ANGLE (Avalonia.Angle.Windows.Natives) | BSD-3-Clause | Windows builds only |
| Microsoft.Windows.SDK.NET projection | Windows SDK license | Windows builds only; redistributable with apps, not open source |
| .NET 10 runtime (self-contained) | MIT | Plus the runtime's own third-party notices (zlib, etc.), included |

Build and test only (not shipped): xunit (Apache 2.0), NSubstitute (BSD-3-Clause), Microsoft.NET.Test.Sdk (MIT),
Avalonia.Headless (MIT, for the README screenshots), NSIS (zlib; its installer stub has no notice requirement), rcodesign (MPL 2.0), libdmg-hfsplus and hfsprogs (GPL).
Using GPL tools to build does not put the app under the GPL.

Linux builds also use the system's libsecret and GLib (LGPL) and the D-Bus session bus. They are loaded from the
user's system, not shipped, so they put no obligation on the app.

Transmission itself (GPL 2/3) is not included: the app only talks to it over the documented RPC protocol. No
Transmission code, text or artwork is copied.

**Conclusion: every shipped dependency is permissive (MIT, BSD, Apache 2.0).** Any license works for the app,
including MIT, Apache 2.0 or GPL. The only duty they impose is attribution, which the notices file now covers.

## Pain points

1. **License: done (MIT).** `LICENSE`, About, `Info.plist` (`NSHumanReadableCopyright`), the installer's
   LegalCopyright, and a LICENSE.txt next to the app in both packages. Copyright holder and installer publisher are
   both Max Vinken (there is no company). The app identifier is `nl.vinch.synchromesh`.
2. **The name: done.** The app is now Synchromesh (`nl.vinch.synchromesh`), no longer "Transmission Remote", which
   clashed with Transmission's own `transmission-remote` tool. The README says it is an independent client.
3. **Repository history: done.** GitHub starts from one squashed commit (2026-10-22); the development history before
   it stays private. Commits use max@vinch.nl.
4. **AI-assisted code.** Many commits are co-authored with Claude. Anthropic's terms assign to you whatever rights
   Anthropic has in that output, so it does not block a license; some projects state such use in CONTRIBUTING. Mention it if you like.
5. **Notices must stay current.** Updating a package (for example Avalonia) can change its license or add
   dependencies. When `Directory.Packages.props` changes, rerun the inventory and update `THIRD-PARTY-NOTICES.txt`
   and `AboutViewModel.Components` (a test checks that every listed component is in the notices file).
6. **Build telemetry.** Avalonia's build package (`Avalonia.BuildServices`) sends anonymous build statistics. It
   doesn't affect the license, but contributors may want to know: set `AVALONIA_TELEMETRY_OPTOUT=1` to turn it off.
7. **Contributions: done.** `CONTRIBUTING.md` (2026-10-22): "inbound = outbound", contributions come in under MIT;
   no CLA and no DCO sign-off. A CLA would only be needed to relicense later.

## Which license (decided: MIT)

- **MIT (recommended):** short, matches every dependency, and the Avalonia/.NET world mostly uses it. Anyone may reuse
  the code, also in closed products.
- **Apache 2.0:** like MIT, plus an explicit patent grant and a NOTICE convention. A bit more paperwork.
- **GPL 3:** keeps every derived app open (as Transmission does), but stops others from building closed products on
  it, and it is sometimes avoided by app stores (the Mac App Store's terms are widely seen as incompatible with GPL 3).

MIT was chosen on 2026-10-18: short, matches every dependency, and keeps every app store open.
