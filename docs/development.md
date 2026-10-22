# Developing Synchromesh

## Build, test, run

Needs the .NET 10 SDK (`global.json` pins 10.0.100 with roll-forward).

    dotnet build Synchromesh.slnx
    dotnet test tests/Synchro.Tests
    dotnet run --project src/Synchro.Desktop

In Rider, open `Synchromesh.slnx` and run `Synchro.Desktop`. Older Rider versions that can't open `.slnx`: run
`dotnet new sln -n Synchromesh && dotnet sln add src/*/*.csproj tests/Synchro.Tests/Synchro.Tests.csproj` and open the
`.sln`. Notifications and opening files from Finder only work in the app bundle, not under `dotnet run`.

## Packages

From macOS or Linux:

    build/macos/make-app.sh [version]                        # artifacts/Synchromesh.app, registered with this Mac
    build/macos/package.sh <version> [osx-arm64]             # the .app and a .dmg, as CI builds it
    build/windows/package.sh <version> [win-x64|win-arm64]   # the NSIS setup .exe
    build/linux/package.sh <version> [linux-x64|linux-arm64] # the .tar.gz with install.sh

The macOS package is self-contained and ad-hoc signed: `codesign` and `hdiutil` on a Mac, `rcodesign`,
`mkfs.hfsplus` and libdmg-hfsplus on Linux (`build/ci/install-macos-packaging-tools.sh` installs them, pinned by
version and checksum). To check on a Mac what CI produces: `MACOS_SIGNER=rcodesign build/macos/package.sh 1.0.0`
(with rcodesign on the PATH); the script then verifies the seal with Apple's `codesign`. The Windows setup needs
`makensis` (NSIS 3). Publishing for a `win-*` runtime switches `Synchro.Desktop` to the Windows build of .NET
(`net10.0-windows10.0.19041.0`) and adds `Synchro.Platform.Windows`; other builds stay `net10.0`.

## Screenshots

The README's screenshots are rendered from the real windows, headless, against a made-up server with only Linux
installation images (and peers on documentation IP addresses), in light and dark:

    dotnet run --project tools/Synchro.Screenshots

This writes `docs/screenshots/<name>-light.png` and `<name>-dark.png`. Run it after a visible change and commit the
pictures. The demo data is in `tools/Synchro.Screenshots/DemoServer.cs`; what gets captured is in its `Program.cs`.

## Builds and releases

`.github/workflows/ci.yml` runs on GitHub's Linux runners:

- **Every push and pull request:** build and run the tests.
- **A version tag** (`v1.2.0`, or `v1.3.0-beta.1` for a pre-release): build, test, check the changelog, package
  macOS arm64, Windows x64 and ARM64, and Linux x64 and ARM64, and publish a GitHub release with the packages
  attached (`build/ci/github-release.sh`). The release notes (`build/ci/release-notes.sh`) are that version's section
  of [CHANGELOG.md](../CHANGELOG.md), followed by install instructions; a tag without a section fails before
  packaging. Releases from tags with a suffix (`-beta.1`) are marked as pre-releases. Nothing needs configuring: the
  release job gets `contents: write` for the built-in token.

To release:

1. In `CHANGELOG.md` ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)), rename `## [Unreleased]` to
   `## [1.2.0] - YYYY-MM-DD` and add an empty `## [Unreleased]` above it. A `-dev` tag needs no section of its own:
   it takes what's under Unreleased.
2. Commit, then tag and push:

       git tag v1.2.0
       git push origin v1.2.0

Adding a platform means one more entry in the `package` matrix, with a script that writes its installer to
`artifacts/`.

## Code layout

Dependencies point inward:

    Synchro.Desktop  ->  Synchro.ViewModels  ->  Synchro.AppServices  ->  Synchro.Core  <-  Synchro.Transmission
         |
         +-> Synchro.Platform.MacOS     Synchro.Desktop is the only composition root (Simple Injector)
         +-> Synchro.Platform.Windows   (Windows builds only)
         +-> Synchro.Platform.Linux

| Project | Role |
|---|---|
| Synchro.Core | Models and interfaces, plus `.torrent` reading (Bencode). No dependencies. |
| Synchro.Transmission | JSON-RPC transport, the 409 session-id handshake, the Transmission client, the connection test. |
| Synchro.AppServices | Connection state, polling, torrent actions, notifications, settings and the password file. |
| Synchro.ViewModels | CommunityToolkit.Mvvm view models. No Avalonia reference, so all unit-tested. |
| Synchro.Platform.MacOS | Keychain passwords, Notification Center, the app menu's Hide and Show All. |
| Synchro.Platform.Windows | Credential Manager passwords, toast notifications. |
| Synchro.Platform.Linux | Keyring passwords (Secret Service via libsecret), desktop notifications over D-Bus. |
| Synchro.Desktop | Avalonia views (Material theme), dialogs, single instance, DI wiring. |
| tools/Synchro.Screenshots | Renders the README screenshots. Not shipped. |

`CLAUDE.md` has the conventions, protocol details and lessons learned; `docs/licensing.md` the license inventory.
