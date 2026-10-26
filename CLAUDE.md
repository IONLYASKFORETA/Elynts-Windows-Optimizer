# Elynt's Windows Optimizer

## Repo skills

Skills live in `.claude/skills/<name>/SKILL.md`. Cloud project threads don't load them, so when the Skill tool doesn't
know a skill the user or another skill names, read that SKILL.md and follow it by hand. A skill named in this repo is
always this repo's skill, even where a built-in has a similar name.

## Changing the window

Read `docs/ui.md` before changing anything under `src/Elynt.App/`: the theme, the sizes, and the screenshot tour that
fails on binding errors. `CODING_STANDARDS.md` is what reviews check.

## Commits and pull requests

- Commit trailers go on ordinary commits. Merge commits on an integration branch keep git's own message; the squash
  merge into main carries the trailers.
- Set a long PR body from a file with `gh api -X PATCH repos/IONLYASKFORETA/Elynts-Windows-Optimizer/pulls/<n> -F body=@<file>`
  (REST works in cloud sessions), then read the PR back. The GitHub MCP's `update_pull_request` turned image links into
  code in long bodies.

## Building tickets with implementer agents

Read `docs/agents/implementers.md` before running `implement-spec`, and as an implementer before building a ticket
in `.claude/worktrees/`.

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`gh` locally; GitHub MCP tools or `gh api` in cloud sessions). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root, created lazily. See `docs/agents/domain.md`.
