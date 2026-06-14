# Donations & Revenue Sharing

> **Draft.** This is just a first idea of how donations could be shared. Nothing here is final and it still needs to be talked through before anything goes live.

This is a community game. People help out with code, maps, assets and audio, so if money comes in we don't want it to just vanish into one person's pocket. Here's the rough plan.

The main rule: keep it transparent. Money in and out is public.

## What donations are

Donations help keep the servers running and reward the people building the game.

They never buy an in-game advantage. No pay-to-win, no donor-only guns, no stat boosts. A cosmetic thank-you at most.

## Costs come first

All donations go into one pot, sorted out once a month. The servers get paid before anyone else, in this order:

1. **Running the game.** Game servers and matchmaking. This is a monthly bill and it's paid first. Whoever pays it gets it back, with receipts.
2. **Other infra.** Domain, CI, build and test stuff. Same thing, paid back with receipts.
3. **Buffer.** We keep back enough to cover next month's server costs too, so one bad month doesn't take the servers offline. After that, a small extra buffer for emergencies.

Whatever is left after that is what gets shared out.

If donations don't even cover the running costs one month, then there's nothing to share that month. The buffer covers the gap and the servers stay up.

## Who gets paid

Small contributions like bug fixes, cleanups or minor features are just normal open-source work. You get credit for them, the same as on most projects.

The pot goes to people who make something that sticks around, or who keep the project running. There are three ways:

- **Maps in the official pool** earn a share based on how much they get played. Other core content is agreed case by case.
- **Bounties** for bigger jobs the project wants done.
- **Core fund** for the few people doing the ongoing work.

Skins are separate. The author keeps 100% of those. More on that below.

## Content

This only covers content that gets taken into the official game, the official map pool and the fixed core assets. Community maps that aren't in the official pool are not part of this, and it's not about DLC content. You can still make and share community maps freely, they're just not paid out here.

**Maps** in the official pool are paid by how much they get played. The server knows which map a match runs on, so this is real, verifiable data, not a guess. A map can be months of work, and paying for it once wouldn't be fair, so instead it earns every month it's in the game. Each month the maps share gets split between the maps that shipped, by how often they actually get played. If people keep playing your map, you keep getting paid, even if you've stopped contributing otherwise. New maps get a small boost for the first month or two so they're not buried before anyone's had a chance to try them.

**Everything else** (characters, weapon models, audio, other core stuff) is agreed case by case. There's no automatic payout, because for most of it there's nothing reliable to measure. If you want to make something and be paid for it, talk to us first. We agree a share or a price only if the work is actually a benefit to the community. And as always, costs like the servers come first.

## Bounties

Some jobs are too big to expect for free, like a new system, a tricky netcode fix or a tool we need. Those get a bounty: a set amount put on the task ahead of time.

- Bounties are public. Take one, finish it, get paid the set amount.
- They're for real, defined work, not small fixes.
- Anyone can suggest something worth a bounty. The maintainer sets the amount.

This is how someone gets paid for serious code work without the project pretending it can pay everyone a salary.

## Core fund

A few people end up doing the steady work: reviewing PRs, running the servers, keeping things from breaking. That work never really stops, so a share of the pot goes to them.

Who's in it and what they get is decided in the open and shown in the monthly report. It's meant to be a small group, and it's about ongoing work, not a one-off contribution.

## Skins and DLC

Skins and any other DLC content are not part of the donation pot. They're yours.

Send in a skin or some other DLC, and if it's good enough we put it in the game. You keep 100% of what it sells for. The project takes nothing. You can also give it away for free if you want, that's up to you. (If it's sold, the payment provider like Stripe takes its own fee before it reaches you. That's them, not us.)

How it works:

- You send in a skin or DLC. If it fits, we add it.
- Players buy it, or get it for free if that's what you chose. Any money is yours.
- A database keeps track of who owns what, including items traded or passed between players, so it's always clear who owns it and your work stays credited to you.

Same rule as any asset: you have to own the rights to what you send (see [LICENSE.md](LICENSE.md)). We do the work of adding it and running the shop, the money is yours.

Since this pays you directly, it's not part of the content share. No double-dipping.

## Payout rules

- You only get paid for what you own. Assets need proper licensing to go in and to earn (see [LICENSE.md](LICENSE.md)).
- You have to opt in. Set up a payout method to get paid. No method, your share waits in the buffer.
- Small amounts roll over to next month instead of paying fees on a tiny transfer.
- You can give up your share if you want. It goes back into the pot for everyone else.

## The monthly report

Once a month we post the numbers: what came in, what the servers and infra cost, what's in the buffer, what each piece of content earned, what bounties were paid and what the core fund got. Skin sales are tracked in the ownership database. No private deals.

## Disagreements

Open an issue. The maintainer has the final say, but the reason is always public.

---

*These numbers and shares are just a starting point. If something seems off, open an issue and we'll talk about it.*
