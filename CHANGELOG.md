# Changelog

All notable changes to Synchromesh are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.1] - 2026-10-22

### Changed
- Updated readme
- Changed strings in various windows to reel less vibed.

## [1.0.0] - 2026-10-22

The first public release.

### Added

- Control a Transmission 4.1 or newer server (JSON-RPC 2.0, RPC version 6) from macOS (Apple Silicon), Windows
  (x64 and ARM64) and Linux (x64 and ARM64).
- Several servers, with HTTPS, a username and password, a custom RPC path and an optional client certificate for a
  proxy in front of Transmission.
- Torrent list with status and label filters, search, queue order, start, stop, verify, re-announce, move data,
  rename and remove.
- Adding torrents from files, magnet links, drag and drop and the clipboard, and from the operating system
  (double-clicked `.torrent` files and magnet links), choosing files before the download starts.
- Details panel with general information, trackers (add and remove), peers, files (wanted, priority, rename) and
  per-torrent options (speed limits, priority, seeding limits, sequential download).
- Server settings: download folders, queues, peers, encryption, a port check and the blocklist.
- Speed limits with the alternative-speed (turtle) toggle.
- Sidebar with a live speed graph, storage on the server's download disk and transfer statistics.
- Notifications for finished downloads and errors.
- Passwords in the Keychain, Windows Credential Manager or the Linux keyring, or in Synchromesh's own encrypted
  file, optionally protected with a master password. Where there's no keyring, such as on a Steam Deck, the app
  offers that file.
- Light, dark and system appearance.
- A warning when the server's RPC version is newer than the app was tested with.
- Help > Log UI stalls, to record moments the window stops responding.
- Linux package with `install.sh`, which also leaves an `uninstall.sh` in the app folder.
