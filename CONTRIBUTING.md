# Contributing to Synchromesh

Thanks for helping. Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

[Open an issue](https://github.com/maxxie85/synchromesh/issues/new) and include:

- your system (macOS, Windows or Linux, and which version) and the Synchromesh version (Help > About);
- the Transmission version on the server (the status bar shows it while connected);
- what you did, what you expected, and what happened instead. A screenshot often says more than words.

If the window freezes or hesitates, turn on Help > Log UI stalls, repeat what you did, and attach the log
(Help > Show the UI stall log).

Please leave out passwords, server addresses and torrent names you'd rather keep private.

## Suggesting a feature

Open an issue that describes what you're trying to do. Synchromesh only talks to Transmission 4.1 or newer over its
RPC interface, so a feature has to be something Transmission's
[RPC specification](https://github.com/transmission/transmission/blob/main/docs/rpc-spec.md) supports.

## Pull requests

For anything larger than a small fix, open an issue first, so we can agree on the approach before you put time in.

1. [docs/development.md](docs/development.md) explains how to build, test and run the app, and how the code is
   organised. [CLAUDE.md](CLAUDE.md) has the conventions and the details that are easy to get wrong.
2. Keep the change focused, and add or update tests for what you change: `dotnet test tests/Synchro.Tests` must pass.
3. For a change users will notice, add a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), in plain
   language.
4. For a visible UI change, regenerate the screenshots with `dotnet run --project tools/Synchro.Screenshots`.
5. Write user-facing text in sentence case and plain language, with error messages that say what to do.

## License of contributions

Synchromesh is licensed under the [MIT license](LICENSE). By submitting a contribution, you agree that it is
licensed under the same MIT license, and that you have the right to submit it ("inbound = outbound"). There is no
separate agreement to sign.
