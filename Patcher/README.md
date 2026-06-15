# Vantix Launcher

Installer and updater for Vantix, built on [Velopack](https://velopack.io/). Players run the
installer once; afterwards the launcher pulls delta updates from Cloudflare R2 and starts the game.
Builds for Windows and Linux.

## Setup

1. Create an R2 bucket `vantix-patches`, enable public access, and create an API token.
2. Set `FeedUrl` in `Updater.cs` to the bucket's public URL (not the S3 endpoint).
3. Add repository secrets: `R2_ENDPOINT`, `R2_BUCKET`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`.

## Releasing

Tag a version and push:

```bash
git tag v0.0.1
git push origin v0.0.1
```

The CI exports the game, builds the launcher, uploads delta patches to R2, and attaches the
installers to a GitHub release. To test without publishing, run the workflow manually with
`dry_run` enabled — the installers show up as run artifacts.

## Running locally

```bash
dotnet run --project Patcher
```

Without an installed copy the update check is skipped; the launcher looks for a `game/` folder
next to the executable and shows the changelog from `CHANGELOG.md`.
