# Coding standards

What a review checks this repo's code against, on top of matching the code around a change. Tooling already enforces
warnings (TreatWarningsAsErrors) and nullable references; this file holds what it can't.

## Behaviour

- **Every change to Windows goes through the Engine** (`IEngine`), which journals what was there first so undo puts it
  back exactly. Pages and the service only send Engine commands.
- **Tests drive the Engine's commands against the fakes** in `tests/Elynt.Core.Tests`, the single test seam (README).
  A new command or behaviour comes with one. Windows-only adapters get a round trip in `tests/Elynt.Windows.Tests`.
- **Destructive stays opt-in.** A change undo can't fully reverse is labelled destructive and needs its own
  confirmation, enforced in the Engine, not just in the page.

## Words

- **What the user reads is plain and whole:** full sentences that say what happened and what to do next, with no jargon
  a gamer wouldn't know. A failure says whether anything was changed.
- **Comments say why**, in the same plain words: a `/// <summary>` on each type and on members whose name doesn't say
  it all, and a one-line comment at the top of each XAML file saying what the page or panel shows.

## The window (XAML)

Background and the full list of styles: `docs/ui.md`.

- **Reuse before you add.** Use a named style from `Theme/Theme.xaml`, or extend one with `BasedOn`. A style that a
  second page could use goes in `Theme.xaml`, never copied into a page's resources. Page resources hold only what is
  specific to that page's view model.
- **Colours come from the `Theme.*` tokens.** A colour anywhere outside `src/Elynt.App/Theme/` fails CI.
- **The accent wave is `{DynamicResource Theme.AccentWave}`**, on accents only.
- **Every binding path matches its view model.** A binding error fails the screenshot tour.
- **A new list or page state gets an example on the sample PC** (`tests/Elynt.Core.Tests/SamplePc.cs`) so the tour
  draws it.
