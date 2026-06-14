# Contributing

Code is open source, assets are not. If you want to help with code, here's how it works.

## Before you start

For anything bigger than a bug fix, open an issue first so we can talk about the approach. Saves you from writing a big PR that goes in the wrong direction.

Small stuff (typos, obvious bugs, missing null checks) — just send the PR.

## Setup

You need Godot 4.6 with .NET support and the .NET 8 SDK. Windows only for now (D3D12).

```bash
git clone https://github.com/justin-bobr/vantix.git
cd vantix
dotnet tool restore
```

Open the project in Godot and hit Build (the hammer icon).

## Workflow

1. Fork, then branch off `main`.
2. Make your change.
3. Run the formatter and make sure it passes:
   ```bash
   dotnet csharpier --check codebase
   ```
   If it complains, run `dotnet csharpier codebase` to fix it.
4. Open a PR against `main`. Describe what you changed and why.

VSCode is set up to format on save, so most of the time CSharpier won't bother you.

## Code style

- CSharpier decides formatting. Don't fight it.
- Every method gets an English `/// <summary>` XML doc above it.
- No inline `//` comments inside method bodies — if something needs explaining, put it in the summary.

## AI

Use AI as a helper, not as the author. It's fine for things like writing doc comments, rough checks or small chores. But the code itself has to be written, understood and tested by you, by hand. You can run it through AI for a first-pass review if you want, but you still review and test it yourself before it goes in.

We don't take AI slop. Commits that are clearly just generated and dumped in, without a human actually writing and testing them, get rejected.

## Commits and PRs

- Keep commits focused. One logical change per commit where you can.
- Write commit messages that say what changed, not "fixed stuff".
- A PR doing one thing gets reviewed faster than one doing five.

## Assets

Every asset stays the property of its author. The Apache license only covers code-side commits — it does not apply to assets. Models, textures, audio, maps and the like are not open source. If you want to contribute assets they need explicit licensing — see [LICENSE.md](LICENSE.md). Without that we can't include them.

If you're submitting your own work (maps, weapons, characters and so on), you have to give your consent for it to be used. If needed, we agree a separate licensing or royalty deal first.

If you bought an asset from a third party and want to submit it, you need the proper license for it and have to hand that license in with the asset. The same goes if you hired someone else to make it — make sure the rights actually allow it to be used here.

If you find your work in the repo without credit, open an issue.

## Donations

This is a community-driven game, so if donations come in, the pot gets split fairly between the people who build it — code, maps, assets, audio. See [DONATIONS.md](DONATIONS.md) for how that works.

## Questions

Open an issue. That's the place for anything that isn't obvious from the README.
