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

To use your build as your everyday `graftui`, run `./dev install` (see below).

### Developer shortcuts

`./dev` wraps these commands. It needs bash, so it works on macOS and Linux. Run `./dev` on its own to list them.

| Command | What it does |
|---|---|
| `./dev install` | publish a Release build to `~/.local/share/graftui` and put a `graftui` launcher in `~/.local/bin` |
| `./dev sandbox` | rebuild the practice repo at `~/.graftui/sandbox` and open it |
| `./dev open` | reopen the practice repo without rebuilding it |
| `./dev edits` | make unstaged changes in the practice repo (see below) |
| `./dev test` | run the tests |

If `~/.local/bin` is not on your `PATH`, `./dev install` prints the line to add to your shell profile. On Windows, use the `dotnet` commands above, or Git Bash or WSL.

### Test edits (hidden flag)

`graftui --make-edits` (or `./dev edits`) makes three unstaged changes in `~/.graftui/sandbox`: it modifies `README.md`, deletes `app.txt` and creates an untracked `notes.txt`. It changes nothing else, prints what it did and exits. Open the sandbox with `./dev open` to see the changes on the Changes tab.

It refuses any other repository, a missing sandbox, and a sandbox that already has changes. The flag is left out of `--help` on purpose.

## The safety rules

Every change must keep these. A pull request that weakens one won't be merged, however nice the feature.

1. **Never leave a repository mid-operation.** If a merge (or anything later) fails or conflicts, graftui aborts it and checks that the repo is back where it was: a clean tree, no `MERGE_HEAD`, `HEAD` where it started.
2. **Refuse before touching anything** when the operation can't run safely. For a merge that means uncommitted changes (unless the user chooses stash-merge-pop), a detached `HEAD`, or another operation in progress.
3. **Never discard work graftui didn't create.** Rollbacks only undo graftui's own changes.
4. **git never waits for an editor, pager or prompt.** All git access goes through `GitClient`, which sets the environment for this. Never start git any other way.
5. **Show every git command that ran** in the Commands pane (the exact list, `v`, has every call and its exit code).
6. **Work out results from repository state** (`HEAD`, parents, `MERGE_HEAD`), never by parsing git's human-readable output, which is localised.

New operations follow the shape in `src/graftui/Operations/IGitOperation.cs`: check, run, verify, roll back.

## How the code fits together

[ARCHITECTURE.md](ARCHITECTURE.md) walks through the folders, the path from a key press to a git command, and where the safety rules live in code. Read it before a change to `GitClient`, the operations or the screen.

## Tests

- Any change to how graftui touches a repository needs a test in `tests/graftui.Tests` that runs real git in a temporary repository (see `Support/TempRepo.cs`). Tests must never touch `~/.graftui/sandbox` or any real repository.
- Drawing and text code (`Ui/Scenes`, `Ui/Text`) is pure and can be unit-tested without a terminal.
- For anything visible, also run it in the sandbox at a normal size and at the minimum size (64×22), and check that nothing overlaps or spills out of its pane.

## Recording the GIFs

The GIFs in the README are recorded with [VHS](https://github.com/charmbracelet/vhs) from the tapes in `assets/`. To record one again, from the repo root:

```bash
brew install vhs          # macOS; see the VHS docs for other systems
./dev install             # the tapes run the installed graftui
vhs assets/merge.tape     # likewise conflict, changes, undo and switch
```

Each tape runs graftui with `--sandbox` or opens `~/.graftui/sandbox`, so it only ever records the practice repo. The `--sandbox` rebuild prints the full path before the screen opens, so the tapes hide that part. Keep each GIF under about 800 KB (1 MB at most), and check the frames before you commit: no username, hostname or home path other than `~/.graftui/sandbox`.

## Pull requests

- One change per pull request, on a branch of its own.
- `dotnet build graftui.slnx` must have **no warnings** (warnings are errors), and `dotnet test graftui.slnx` must pass. CI runs both on every pull request to `main`, and the pull request can't be merged until the `build-and-test` check is green.
- Match the style of the code around you. Comments explain why, not what.
- Write commit messages that say what changed and why, in the imperative ("Add stash pop", not "Added stash pop").
- If the change is visible, describe what you see before and after, or attach a screenshot.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
