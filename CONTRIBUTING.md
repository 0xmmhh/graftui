# Contributing to graftui

Thanks for taking the time to help. graftui runs real git commands on real repositories, so the bar for changes that touch git is high, and the bar for everything else is friendly.

## Before you start

- **Bugs:** open an issue with the steps, what you expected and what happened. Paste the Commands pane if it's relevant, and say which git version you have (`git --version`).
- **Features:** open an issue first for anything bigger than a small fix, so we can agree on the approach before you spend time on it. The "Status and plans" section of the README lists what is planned.
- **Small fixes** (typos, docs, an obvious bug): a pull request on its own is fine.

## Setup

You need git and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/0xmmhh/graftui.git
cd graftui
dotnet build graftui.slnx
dotnet test graftui.slnx
```

Try your change in the practice repository, never in the graftui repository itself or in a repo with work you care about:

```bash
dotnet run --project src/graftui -- --sandbox            # rebuild and open ~/.graftui/sandbox
dotnet run --project src/graftui -- ~/.graftui/sandbox   # reopen without rebuilding
```

## The safety rules

Every change must keep these. A pull request that weakens one won't be merged, however nice the feature.

1. **Never leave a repository mid-operation.** If a merge (or anything later) fails or conflicts, graftui aborts it and checks that the repo is back where it was: a clean tree, no `MERGE_HEAD`, `HEAD` where it started.
2. **Refuse before touching anything** when the working tree is dirty, `HEAD` is detached, or another operation is in progress.
3. **Never discard work graftui didn't create.** Rollbacks only undo graftui's own changes.
4. **git never waits for an editor, pager or prompt.** All git access goes through `GitClient`, which sets the environment for this. Never start git any other way.
5. **Show every git command that ran,** with its exit code.
6. **Work out results from repository state** (`HEAD`, parents, `MERGE_HEAD`), never by parsing git's human-readable output, which is localised.

New operations follow the shape in `src/graftui/Operations/IGitOperation.cs`: check, run, verify, roll back.

## Tests

- Any change to how graftui touches a repository needs a test in `tests/graftui.Tests` that runs real git in a temporary repository (see `Support/TempRepo.cs`). Tests must never touch `~/.graftui/sandbox` or any real repository.
- Drawing and text code (`Ui/Scenes`, `Ui/Text`) is pure and can be unit-tested without a terminal.
- For anything visible, also run it in the sandbox at a normal size and at the minimum size (64×22), and check that nothing overlaps or spills out of its pane.

## Pull requests

- One change per pull request, on a branch of its own.
- `dotnet build graftui.slnx` must have **no warnings** (warnings are errors), and `dotnet test graftui.slnx` must pass.
- Match the style of the code around you. Comments explain why, not what.
- Write commit messages that say what changed and why, in the imperative ("Add stash pop", not "Added stash pop").
- If the change is visible, describe what you see before and after, or attach a screenshot.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
