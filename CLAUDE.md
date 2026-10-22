# Synchromesh

Desktop client for Transmission servers. C#, .NET 10, Avalonia 12.1.x with Material.Avalonia, MVVM (CommunityToolkit.Mvvm),
Simple Injector for DI. macOS (Apple Silicon), Windows (x64, ARM64) and Linux (x64, ARM64). Speaks only the modern protocol:
JSON-RPC 2.0 with snake_case names, Transmission 4.1.0+ (rpc_version_semver >= 6.0.0). Spec:
https://github.com/transmission/transmission/blob/main/docs/rpc-spec.md

## Commands

    dotnet build Synchromesh.slnx
    dotnet test tests/Synchro.Tests
    dotnet run --project src/Synchro.Desktop
    build/macos/make-app.sh            # artifacts/Synchromesh.app (local, registered with macOS)
    build/macos/package.sh <version>   # .app + .dmg, on macOS or Linux (what CI runs)
    build/windows/package.sh <version> [win-x64|win-arm64]   # NSIS setup .exe, on Linux or macOS
    build/linux/package.sh <version> [linux-x64|linux-arm64] # .tar.gz with install.sh, on Linux or macOS
    build/icon/make-icns.sh            # after editing build/icon/AppIcon.svg
    dotnet run --project tools/Synchro.Screenshots   # README screenshots (docs/screenshots, light and dark)

Always build and run the tests after a change. If your tooling does not open `.slnx`, use the csproj files.

## Architecture (dependencies point inward)

    Synchro.Desktop -> Synchro.ViewModels -> Synchro.AppServices -> Synchro.Core <- Synchro.Transmission
         |
         +-> Synchro.Platform.MacOS   (Keychain, Notification Center)
         +-> Synchro.Platform.Windows (Credential Manager, toasts; only when publishing for win-*)
         +-> Synchro.Platform.Linux   (Secret Service keyring, D-Bus notifications)

- Synchro.Core: models and interfaces, plus dependency-free domain code (`Metainfo`: Bencode and .torrent reading).
- Synchro.Transmission: JSON-RPC transport, 409 session-id handler, `TransmissionClient`. Internal types are visible to Synchro.Tests.
- Synchro.AppServices: connection state, polling, actions, the fallback password file. (Not named "Application": that namespace would shadow Avalonia's `Application`.)
- Synchro.ViewModels: no Avalonia reference. Everything here must stay unit-testable.
- Synchro.Desktop: views, dialog service, `CompositionRoot`. The only place that knows the DI container.
- Platform-specific code lives in Synchro.Platform.* behind Core interfaces (`ICredentialStore`, `INotifier`). Only
  `CompositionRoot` and `Program` know platforms: `Synchro.Platform.Windows` targets `net10.0-windows10.0.19041.0` (toasts
  via the Windows SDK projections), so `Synchro.Desktop` switches to that TFM and references it only for `win-*` runtime
  identifiers, and `CompositionRoot` registers it under `#if WINDOWS`. No `#if` or OS checks anywhere else.

## Conventions

- SOLID, small interfaces, constructor injection. Singletons for app-wide services, transient for dialog view models.
- View models use `[ObservableProperty]` / `[RelayCommand]`; validation via `ObservableValidator`.
- Compiled bindings are on by default: every XAML file needs `x:DataType`. Avalonia 12 APIs: drag and drop
  uses `e.DataTransfer` with `DataFormat.File`/`DataFormat.Text`, the clipboard `TryGetTextAsync`, text boxes
  `PlaceholderText` (not `Watermark`).
- Look: Material.Avalonia (`MaterialTheme`, `BaseTheme="Inherit"` follows macOS light/dark; Indigo/Teal) and
  Material.Icons.Avalonia. Shared styles and colour tokens are in `Synchro.Desktop/Styles/AppStyles.axaml`; use its
  classes (`bar` icon buttons, `nav` list, `status` progress/icon, `secondary`/`error` text, `actions` dialog row)
  and `DynamicResource` brushes (`Material*Brush`, `Status*Brush`) instead of hard-coded colours, in both variants.
  On/off options are `ToggleSwitch` (styled in AppStyles); keep `CheckBox` only where a mixed state is needed (file
  tree, Labels dialog). Dialogs: primary action is the default button, Cancel uses `MaterialFlatButton`, secondary actions
  `MaterialOutlineButton`; text fields are `Classes="outline"` with `assists:TextFieldAssist.Label`.
  Icons for view-model values come from `Synchro.Desktop/Converters` (view models stay free of Avalonia).
  Material updates brushes asynchronously after a theme change: headless renders must pump the dispatcher first.
- NuGet: central package management. Versions go in `Directory.Packages.props` as `<PackageVersion>`; csproj files
  use `<PackageReference>` without versions. Keep `Tmds.DBus.Protocol` at the version Avalonia.FreeDesktop depends on (0.x:
  a newer pin would also run Avalonia's own Linux D-Bus code on it).
- Licenses: every shipped dependency is permissive (docs/licensing.md). After any package change, update
  `THIRD-PARTY-NOTICES.txt` (embedded in the app, copied next to it by every package script; includes the
  SkiaSharp/HarfBuzzSharp native-library notices) and
  `AboutViewModel.Components`. The app itself is MIT (`LICENSE`, copyright Max Vinken), shown in About and shipped as LICENSE.txt.
- Tests: xunit + NSubstitute, one project (tests/Synchro.Tests), explicit `using Xunit;` per file. Network is faked with
  `FakeHttpHandler`. Add a test for every behaviour change.
- User-facing text: sentence case, plain language, actionable error messages.

## Protocol facts that matter

- Versions: `ConnectionTester` reads `rpc_version_semver` on every connect. Below 6.0.0 is refused; a major version
  above `RpcVersion.TestedMajor` (6) connects with a warning (status bar dot amber, text in the status bar and the
  server setup), because semver allows breaking changes there. The result travels as `ServerCapabilities` in
  `ConnectionTestResult.Success` to `ITransmissionClientFactory.Create` (the place to pick another protocol
  implementation) and `ConnectionStatus.Capabilities`. Everything used today is in 6.0.0; a feature from a later
  version gets a property on `ServerCapabilities` (`AtLeast(6, n)`) and is hidden when false. Unknown JSON fields are
  ignored, so additions in 6.x don't break anything. JSON-RPC "method not found" (-32601) gets a message naming the
  request. Servers before 4.1 speak a different (legacy) protocol: that would be a second transport and client.
- Every request may first return HTTP 409 with `X-Transmission-Session-Id`; resend with it (`SessionIdHandler`).
- Torrents are addressed by info-hash in requests; numeric ids are only stable while the daemon runs.
- Polling: one full `torrent_get`, then `ids: "recently_active"` deltas (plus a `removed` list) and a full refresh
  about every 30 ticks. After an action the next poll comes at once: `RequestRefresh(hashes)` re-reads just the
  touched torrents (`torrent_get` with those hashes) next to the delta; only remove and queue moves use the full
  `RequestRefresh()` (gone torrents, shifted positions). With 10,000 torrents a full list is ~4 MB.
- Responses are gzipped: the client sends `Accept-Encoding: gzip` (`TransmissionConnectionFactory.CreateNetworkHandler`)
  and Transmission's RPC server then compresses (5-11x smaller; confirmed on the owner's 4.1.3 with curl, 2026-10-20). Measured 2026-10-20: with 1,000 active torrents a
  2 s poll is ~380 KB raw, ~72 KB gzipped; the UI side stays at 3-8 ms per poll even with 10,000 torrents.
- Files tab: file names are fetched once (`DetailFiles.NamesAndProgress`), later polls only `file_stats`
  (`DetailFiles.ProgressOnly`), merged by index in `TorrentPoller`. Names are fetched again for another torrent, a
  different file count (a magnet's metadata arrived), after any refresh request (a rename) and with each full refresh.
- `System.Text.Json` escapes `+` and `&` (e.g. `\u002B`) in request bodies. That is valid JSON; tests should parse
  the JSON rather than search for raw substrings.
- `torrent_set` file lists (`files_wanted`, `priority_high`, …): an EMPTY array means ALL files. Never send one;
  the client returns early for an empty selection. (`labels` is different: an empty array removes all labels.)
- File indices must match the server's: libtransmission 4.1 lists the v1 `info.files` in order (prefers `path.utf-8`,
  keeps BEP 47 padding files), uses `info.length` for one file, and ignores a v2-only `file tree`. Hide padding files
  in the UI but never renumber.
- Speed limits are kB/s in the protocol; rates and sizes elsewhere are bytes.
- `tracker_list` (torrent_set) replaces ALL trackers: one announce URL per line, a blank line between tiers. Build it
  from the current `tracker_stats` (tier + announce) plus or minus the change.
- `torrent_rename_path` takes exactly one torrent and the path as the server lists it. The Files tab hides the
  torrent's own top folder, so folder rows add it back (`FilesViewModel.ServerPath`); the torrent itself is renamed
  with path = its name.
- Client certificates (HTTPS only, for a TLS proxy in front of Transmission): `ServerProfile` stores the paths;
  `ClientCertificateLoader` reads a PEM pair, a PEM with both, or a password-less .p12/.pfx, and re-imports PEM keys
  via PKCS#12 because Windows' TLS can't use in-memory keys. Problems surface as `ConnectionFailure.ClientCertificate`.
- Timeouts are per call in `JsonRpcTransport` (20 s; the HttpClient has none). `blocklist_update` downloads the list
  before answering and gets 5 minutes.

## README and screenshots

The README is for GitHub visitors (features with screenshots, download, requirements); build, packaging, CI and the
code layout are in `docs/development.md`. `tools/Synchro.Screenshots` renders the screenshots from the real windows,
headless (Avalonia.Headless + Skia), with `CompositionRoot.Create` and overridden registrations
(`AllowOverridingRegistrations`): `DemoServer` replaces the connection test and the client (NSubstitute), plus the
credential store and notifier, so nothing touches the real Keychain or a server. Culture is pinned to en-US.
`SpeedHistoryViewModel.Seed` (internal, visible to the tool) fills the graph. Waiting on a task there must pump the
dispatcher (`Await`), never `GetResult()`: the main thread has Avalonia's synchronization context and deadlocks.
Regenerate after visible UI changes.

## Lessons from earlier milestones (do not repeat)

- Do not name a test helper `Raise`: it shadows NSubstitute's `Raise.Event`. Use `Publish`.
- Do not name a test helper after a type it returns (`Torrent(...)`); use `MakeTorrent`.
- Fluent's `ProgressBar` has a large built-in MinWidth; set `MinWidth="0"` or it overlaps neighbouring columns.
- Text with `TextTrimming` never trims inside a horizontal `StackPanel` (it offers unlimited width), so it runs into the
  next column. Put icons and text in a `DockPanel` (`HorizontalSpacing`), text last, so it gets the width that's left.
- The list selection is owned by `TorrentListViewModel` (by hash). The ListBox fires selection events while rows are
  reordered; the view model suppresses them and asks the view to restore the selection.
- Poller and connection events are raised on the UI thread because the loop is started there and does not use
  `ConfigureAwait(false)`. Keep it that way.
- Raw interpolated JSON (`$$"""..."""`) breaks on runs of closing braces; build such test JSON with `.Replace`.
- Generic records can't be XAML data types: bind dropdown templates to the non-generic `LabeledChoice` base of
  `Choice<T>`.
- Hiding a Flyout from a button's Click detaches the button, which clears its command before it runs: post the hide
  with `Dispatcher.UIThread.Post`.

## CI and releases

- `.github/workflows/ci.yml` (GitHub Actions, repository maxxie85/synchromesh) on a Linux runner (`ubuntu-latest`):
  test on every push/PR; a `v*` tag checks the changelog, packages each platform in the matrix and publishes a
  GitHub release (`build/ci/github-release.sh` with `gh`, release job `contents: write`) whose notes come from
  `build/ci/release-notes.sh`. GitHub is the project's only home: no references to other hosts.
- macOS packaging is cross-built on Linux: `build/macos/package.sh` uses `codesign`/`hdiutil` on a Mac and
  `rcodesign`/`mkfs.hfsplus`/libdmg-hfsplus (`hfsplus`, `dmg`) on Linux. Tool versions and checksums are pinned in
  `build/ci/install-macos-packaging-tools.sh`.
- Use `actions/upload-artifact@v4`/`download-artifact@v4` (GitHub retired v3).
- `CHANGELOG.md` follows Keep a Changelog 1.1.0: user-facing changes go under `## [Unreleased]` with the change
  (Added / Changed / Deprecated / Removed / Fixed / Security), in plain language. A release renames that to
  `## [x.y.z] - YYYY-MM-DD`; `build/ci/changelog-notes.sh` prints a version's section (a `-dev` tag uses Unreleased),
  CI fails a tag without one, and the release uses it as its notes. `ChangelogTests` checks the format.
- The macOS publish is single-file (`PublishSingleFile`): rcodesign only seals Mach-O files in `Contents/MacOS`, and
  any other file there (.dll, .json) makes macOS call the downloaded app "damaged". `package.sh` fails if non-code
  lands in `Contents/MacOS`, and on a Mac verifies with `codesign --verify --deep --strict`. Scripts must run on
  macOS's Bash 3.2 (e.g. `case` patterns inside `$( )` need the `(pattern)` form).
- Tests must pass without ICU and in any culture (CI may run invariant): don't rely on
  `CompareOptions.NumericOrdering` or a decimal separator; use `NaturalStringComparer` and format via the formatters.

## Status

Everything planned is built; verified against a real Transmission 4.1.3. Below: what exists, with the facts needed to
change it, then what is still open.

### Features and how they work

- **Torrent list:** delta polling (see Protocol facts), filters by status combined with labels, search, queue order,
  start/stop/remove, verify and re-announce (Torrent and right-click menus).
- **Details panel** for the one selected torrent, via `ITorrentDetailsFeed.Watch(hash)`: one extra `torrent_get` per
  poll; `files`/`file_stats` only while the Files tab shows. Tabs: General; Trackers (Add trackers… gives each new
  address its own tier; remove asks first); Peers; Files (wanted, priority, right-click Rename…; no menu in the Add
  dialog); Options (limits, priority, peers, seeding by ratio/idle, sequential download; Apply/Revert so the 2 s
  details poll never overwrites edits).
- **Adding:** every entry point (dialog, drag and drop, Cmd/Ctrl+V, files and links opened from the OS) produces an
  `AddTorrentSource` and goes through `IAddTorrentFlow`. A multi-file .torrent shows its files at once (choices sent
  with `torrent_add`). With "Choose files before downloading" (default on) a link is added first: a torrent URL
  paused, but a magnet link running, because a stopped torrent contacts no peers and never gets its file list. The
  dialog polls `metadata_percent_complete` every 500 ms, stops a magnet (`HoldForChoiceAsync`) once the list is
  there, then `FinishAddAsync` applies the choices with `torrent_set` and starts it (or stops it for "don't start").
  Cancel or closing after the add calls `UndoAddAsync` (removes the torrent, never its data); a duplicate is never
  touched. Sequential download is sent with `torrent_add` only when ticked.
- **Opening from the OS:** `ActivationListener` (Desktop) subscribes to `IActivatableLifetime` (no generic
  `TryGetFeature<T>`; use `TryGetFeature(typeof(...))`) and reads command-line args; `ActivationSources` turns them
  into sources; `ExternalAddQueue` holds them until `MainWindowViewModel.InitializeAsync` finishes and the connection
  is not `Connecting`, then feeds `IAddTorrentFlow.AddAsync` one batch at a time.
- **Torrent actions:** Move data (`torrent_set_location`, move or only re-point), rename (one selected torrent),
  labels (Labels… dialog with mixed states, unchanged torrents not sent).
- **Server:** several servers (Settings > Servers: add, edit, remove with password deletion, connect; editing the
  connected server reconnects; removing it moves to another or disconnects; the toolbar's server menu switches, a
  Flyout closed in code-behind). Speed limits and the turtle toggle (`AltSpeedViewModel`, re-read every 30 s because
  the server's schedule can flip it). Settings > Transmission: server preferences (`session_set`, Apply/Revert),
  port check (`port_test` per IP version), blocklist (on/off and address with Apply/Revert, rule count, Update now,
  which waits until edits are applied). Sidebar: a speed graph (`SpeedHistoryViewModel`: one `session_stats` sample per poll,
  3 minutes, a 1/2/5 × 10ⁿ scale over the visible peak, cleared whenever the connection is not up; drawn by
  `Controls/SpeedGraph`, which trails the clock by one poll interval and eases the scale at 30 frames a second only
  while there is a line, colours from the `Status*Brush` resources; ScottPlot was assessed and not used: a second
  Linux SkiaSharp native package and far more than a sparkline needs), storage (`StorageViewModel`:
  `free_space` on the default download folder, refreshed on connect and every minute, red below 10% free, cleared
  when switching servers, last values kept through a dropped connection), then the `session_stats` totals.
- **Connection setup:** address, port, user and password (Keychain / Credential Manager), RPC path, HTTPS, optional
  client certificate and key (see Protocol facts).
- **Where passwords live:** `AppCredentialStore` (AppServices) is the app's `ICredentialStore` and `IPasswordStorage`,
  wrapping the platform store (registered by type in `CompositionRoot`) and `PasswordFile` (AES-256-GCM, key file,
  see Linux). Settings > Servers has "Keep passwords in Synchromesh's own encrypted file"
  (`AppSettings.PasswordStorage`, `PasswordsViewModel`): switching copies every server's password first, removes
  them from the old place only after all copies worked, then remembers the choice; with the file chosen the
  keyring is never called (no Keychain prompts). In keyring mode the file still serves as the no-keyring fallback
  once its key exists, file entries win on reading, and saving to a working keyring empties the file entry.
  Optional master password ("Protect the file with a master password", shown whenever the file is in use): the
  random data key in `passwords.key` is then stored wrapped (AES-256-GCM under PBKDF2-SHA256, 600,000 iterations,
  salt and count in the file), so changing the master password re-encrypts only the key. `PasswordFile` asks
  through `IMasterPasswordPrompt` (Desktop `MasterPasswordPrompt`, marshalled to the UI thread) the first time a
  saved password is needed, keeps the key for the run, retries on a wrong one (the GCM tag fails) and throws
  `PasswordFileLockedException` on cancel. Not recoverable: the unlock prompt says which two files to delete.
- **Notifications:** `TorrentNotifications` (AppServices: finished downloads and errors 2/3, baseline after each
  connect, grouped above three) through `INotifier` (`Initialize` asks for permission at start-up; macOS asks
  only once). There's no test command any more: removed 2026-10-22 after notifications were seen working on macOS,
  Windows and a Steam Deck. A user who declined on macOS turns them on in System Settings > Notifications.
- **Appearance and settings:** System / Light / Dark via `IThemeService` → `RequestedThemeVariant` (Material follows
  with BaseTheme="Inherit"). `settings.json` (`IAppSettingsStore`, read-modify-write under a lock) keeps the theme and
  the last server; start-up reconnects to that server and applies the theme before the first window opens.
- **UI stall log** (Help > Log UI stalls, off by default, kept in `settings.json` so it runs from start-up):
  `StallDetector` (AppServices) posts a probe to the UI thread every 50 ms from a background thread and reports waits
  over 100 ms; `UiStallMonitor` (Desktop) adds the last click or key press (class handlers on `TopLevel`,
  handled events too) and `StallLog` writes `<data folder>/logs/ui-stalls.log` off the UI thread (moved to `.1`
  past 1 MB). Help > Show the UI stall log opens the folder. It found the freeze below; look at it before optimising
  anything else (2026-10-20: polls of 1,500 torrents take under 10 ms; ReadyToRun gained little).
- **Dialogs never wait for the server before opening:** the Add dialog and Speed limits open at once and fill in
  when the server answers (`SpeedLimitsViewModel.LoadAsync`, Save off until loaded, a load error inside the dialog).
- **About:** icon, name, version, copyright, MIT, the open-source components (`AboutViewModel.Components`) and
  License texts… (LICENSE + THIRD-PARTY-NOTICES.txt, embedded). `IDialogService.PromptAsync` with a `TextPrompt` is
  the shared one-field dialog (validation callback, optional multi-line).

### macOS

- `build/macos/make-app.sh` builds a self-contained, ad-hoc signed `artifacts/Synchromesh.app` (osx-arm64)
  from `build/macos/Info.plist` (`.torrent` document type as `Alternate`, `magnet` URL scheme) and registers it with
  LaunchServices.
- Menu bar: app menu in `App.axaml` with our own Hide/Hide Others/Show All/Quit (Avalonia's default items are off in
  `Program`, so there is no Services submenu; `MacApplicationMenu` calls NSApplication); Torrent/View/Tools/Help on
  `MainWindow`. No shortcuts that text fields use: native menu shortcuts win over the focused control.
- Rendering is OpenGL (`Program`: `AvaloniaNativePlatformOptions.RenderingMode` = OpenGl, Software). With Avalonia
  12.1's default Metal, opening a menu, flyout or dialog from a real click froze the UI for about 1 s in most clicks:
  the main thread waited in `-[CAMetalLayer nextDrawable]` until its 1 s timeout. Code-triggered opens don't show it,
  only real mouse events; reproduce by posting NSEvents to the app itself (`[NSApp postEvent:atStart:]`) while
  running `/usr/bin/sample <pid>` from outside. `SYNCHROMESH_RENDERER=metal` brings Metal back to test a newer Avalonia. Confirmed by the owner in daily use (2026-10-20): no
  stalls with OpenGL. Retested on Avalonia 12.1.4: Metal still stalls on every click, so OpenGL stays.
- Single instance comes from LaunchServices: a magnet opened while the bundle runs goes to that process (checked: no
  second process). Not handled for `dotnet run` builds, nor `open -n`.
- `MacNotifier` uses UserNotifications via the Objective-C runtime and only works in the .app bundle (no-op under
  `dotnet run`).
- App icon (since 2026-10-19): a brass synchro ring with a red download arrow on a light body.
  `build/icon/AppIcon.svg` is generated by `build/icon/make-svg.py` (teeth and rings are computed; edit the script).
  `build/icon/make-icns.sh` renders it with AppKit (`render-svg.swift`: transparent background, icon drop shadow; Quick Look would flatten onto white and AppKit
  ignores SVG filters) into `build/icon/AppIcon.icns`, `build/icon/AppIcon.ico` (Windows: cropped to the body, no
  shadow) and `src/Synchro.Desktop/Assets/AppIcon.png`. Commit those outputs.

### Windows

- Credential Manager passwords, toasts (the app registers its own AppUserModelId under HKCU, so no installer step),
  an in-window menu (`NativeMenuBar`; hidden on macOS), Ctrl shortcuts (`{OnPlatform Ctrl+X, macOS=Cmd+X}`), About in
  Help, Settings… (Ctrl+,) in Tools and Exit at the bottom of Torrent (Ctrl+Q on Linux; Windows has Alt+F4), single instance via a named mutex + pipe (`SingleInstance`, short hashed name: Unix socket paths max ~104
  chars).
- Per-user NSIS setup for x64 and ARM64 (`build/windows/installer.nsi`), built in CI. makensis needs a UTF-8 locale
  (it crashes with std::bad_alloc otherwise); package.sh sets one. The .exe has no embedded icon; the Start menu
  shortcut and the uninstall entry use AppIcon.ico, the window its Avalonia icon.
- Verified on a Windows VM: install, upgrade over an older version, toasts. Don't read `ToastNotifier.Setting`: it
  throws for this unpackaged app (the shortcut has no AppUserModelID), although toasts show.

### Linux

- Not run on a real Linux desktop yet: built and packaged, but this Mac has no VM or container. Verify there first.
- `SecretServiceCredentialStore`: libsecret (`libsecret-1.so.0`, `libglib-2.0.so.0`) through its non-variadic
  `*v_sync` functions with GHashTable attributes (`service`=Synchromesh, `profile`=id, schema
  `nl.vinch.synchromesh.Password`); never call variadic C functions from .NET. Runs on the thread pool
  (unlock prompts block). A missing library or keyring becomes an InvalidOperationException with a plain message.
- No keyring (a Steam Deck has none: SteamOS ships no KWallet settings, so nothing owns `org.freedesktop.secrets`):
  the store throws `KeyringUnavailableException` for a missing libsecret or the D-Bus "no such service" errors
  (`SecretInterop.GError.MeansNoKeyring`; a locked keyring or a dismissed prompt stays an ordinary error). The server
  dialog then offers "Keep it in an encrypted file": `PasswordFile` (AppServices) keeps AES-256-GCM entries in
  `passwords.json` (profile id as associated data) under a random key in `passwords.key`, both mode 600. The owner
  chose a key file over a master password (2026-10-22): it protects a copied file, not against programs running as
  the user.
- `DesktopNotifier`: `org.freedesktop.Notifications.Notify` on `DBusConnection.Session` (Tmds.DBus.Protocol, pinned
  to the version Avalonia.FreeDesktop uses); icon and `desktop-entry` hint = the app id. Permission = whether
  `GetServerInformation` answers.
- `build/linux/package.sh`: self-contained folder publish + `install.sh` + `.desktop` template + icon + licenses in
  a .tar.gz. `install.sh` (POSIX sh) installs per user into `~/.local/share/nl.vinch.synchromesh`, writes
  `~/.local/share/applications/nl.vinch.synchromesh.desktop` (Exec quoted, `%U`, Icon as the full path to a copy in the app folder because KDE on a Steam Deck
  didn't find it by name in the user's hicolor folder, MimeType for
  `application/x-bittorrent` and `x-scheme-handler/magnet`, `StartupWMClass` = app id = X11 `WmClass` set in
  `Program`), sets the defaults with `xdg-mime` only when unset, and copies itself into the app folder as `uninstall.sh` (run under
  that name, or with `--uninstall`, it removes the app; settings and passwords stay). The executable is `Synchromesh` and is not put on
  the PATH.
- Single instance as on Windows (`SingleInstance`, a Unix socket); `%U` hands over `file://` URIs, which
  `ActivationSources` turns into paths. Menus inside the window (`NativeMenuBar`), Ctrl shortcuts.

### Identity and decisions

- Name Synchromesh and app identifier `nl.vinch.synchromesh` (since 2026-10-19; the domain vinch.nl is the owner's,
  as Flathub requires). Earlier test builds were called Transmission Remote; only the owner's Mac and a VM had them,
  so there is no migration code (2026-10-19). Don't change the identifier again: macOS keys the notification
  permission and default-app choices on it. It lives in `build/macos/Info.plist`, `build/windows/installer.nsi`,
  `Program.AppId`, `WindowsNotifier.AppId`, `DesktopNotifier.DesktopEntry`, `build/linux/install.sh` and the
  `.desktop` file name; `AppIdentifierTests` keeps them equal. The executable is `Synchromesh` (`AssemblyName`), so
  resources are `avares://Synchromesh/…`. Projects, folders and namespaces are `Synchro.*` (renamed from `Tm.*`,
  2026-10-20).
- License MIT, copyright and publisher Max Vinken (2026-10-18). There is no company.
- Local-only, ad-hoc signed builds for now (2026-10-18); Developer ID signing, notarization and Windows code signing
  later.

### Open

- To verify by hand: the Linux build on a real desktop (start, keyring save and read, notification, opening a
  `.torrent` from the file manager and a magnet link from the browser, a second launch handing over, the dock
  icon); the four power-user features and the blocklist against the real server; the Add dialog for a
  Finder-opened `.torrent` and a browser magnet click, on a cold start and while running (the default app has to be
  chosen again after the identifier change); on Windows, a fresh install as Synchromesh with Tools > Settings… (Ctrl+,) and
  Torrent > Exit; a
  client certificate against a proxy that asks for one; the app menu (no Services, Hide/Quit work).
- Published on GitHub (maxxie85/synchromesh, 2026-10-22), starting from one squashed commit (the earlier history isn't
  published); contributions inbound = outbound MIT (`CONTRIBUTING.md`).
- Ideas, not planned: trusting a self-signed server certificate (by fingerprint); password-protected .p12 and
  encrypted keys; a per-server proxy setting (the system proxy already applies; assessed 2026-10-18, not needed now).
