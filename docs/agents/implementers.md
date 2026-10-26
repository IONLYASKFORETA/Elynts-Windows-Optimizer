# Building tickets with implementer agents

How a spec's tickets get built by implementer subagents, each in its own worktree. The first part is for the Claude
running the build (the `implement-spec` skill); the second is for each implementer. The run's own notes (which spec,
which tickets, the task graph, the integration branch) go in a notes folder in the session's scratchpad, and point
here rather than copy this.

## Running the build

- **Map the files first.** Before the first wave, one exploration subagent reads the tickets and writes a **file map**
  per ticket into the run's notes: the files the ticket changes, the tests beside them, and the seams it names. Each
  implementer's prompt points at its map. Four implementers each exploring alone used 165k to 310k tokens apiece,
  mostly reading the same Engine, journal and view model files.
- **Two implementers at a time.** A usage limit stops every running agent at once: four at once hit it 18 minutes
  into a run and stood still for 3 hours. Start the next ticket on the frontier as one finishes.
- **You merge and push.** Implementers keep their branches local. Merge each into the integration branch (a normal
  merge, git's own message), build and test, and push.
- **Read CI from its annotations.** Job logs don't download in a cloud session. The Windows job's Test step leaves
  hung, failed and skipped tests as annotations (`build/report_tests.py`):

  ```
  gh api repos/IONLYASKFORETA/Elynts-Windows-Optimizer/commits/<sha>/check-runs --jq '.check_runs[] | [.id, .name, .conclusion] | @tsv'
  gh api repos/IONLYASKFORETA/Elynts-Windows-Optimizer/check-runs/<windows id>/annotations --jq '.[] | [.annotation_level, .title, .message] | @tsv'
  ```

## Building a ticket

1. **Start from the integration branch.** Your worktree starts from `main`, so your first command is
   `git reset --hard <integration branch>`. Change files with Edit and Write, and run one plain git command per Bash
   call: the worktree guard refuses heredoc scripts, shell loops and `git -C`. A long commit message goes in a file
   (`git commit -F <file>`).
2. **Read the ticket and your file map.** In a cloud session read the ticket with
   `gh api repos/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/<n>`: `gh issue` needs GraphQL, which is blocked
   there. Read `CODING_STANDARDS.md`, which review checks, and `docs/ui.md` before changing `src/Elynt.App/`.
3. **Build it test first with the `tdd` skill.** The seams are already agreed: the ones the ticket names, at the
   seams `CODING_STANDARDS.md` lists. Go straight from the seams to the first test, since nobody is there to confirm
   them.
4. **Build, then test.** `dotnet build ElyntOptimizer.slnx`, then
   `dotnet test --solution ElyntOptimizer.slnx --max-parallel-test-modules 2` (xunit.v3 on Microsoft.Testing.Platform,
   so VSTest options don't apply). While iterating, run your area alone, such as
   `dotnet test --project tests/Elynt.Core.Tests --filter-class <class>`, and one build at a time: implementers
   share the machine's cores. Windows-only tests skip on Linux; Windows CI runs them after the merge.
5. **Stay within the ticket.** Where it can't be done as written, do the closest thing and say so in your report.
6. **Bring in the integration branch.** `git merge <integration branch>` into your branch, then build and test again.
7. **Report in under 200 words:** your branch and head commit, what you built, the tests you added, anything not done
   or done differently, and what needs Windows CI or a real PC to prove.
