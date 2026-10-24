# graft

An animated git TUI. Pick a branch, press `m`, and watch two limbs grow together while the real `git merge` runs underneath.

```
main                                    feature/merge-commit
●━━━━━━━━━━━━━━━━━━━━◉━━━━━━━━━━━━━━━━━━━━●
✔ Merge commit 41c083c created on main
```

graft is a thin, safety-first layer over the git CLI. It never reimplements git: every action is a real `git` command, and every command it runs is shown on screen with its exit code.

Built with C# / .NET 10 and [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2.

**Status: v1, merge only.**

## Requirements

- git on your `PATH`
- macOS or Linux terminal. Windows should work but is untested.
- To build: the .NET 10 SDK. To run a published binary: the .NET 10 runtime, or nothing at all with a self-contained build (see [Install](#install-as-a-single-command)).

## Install as a single command

Publish graft as one executable and put it on your `PATH`:

```sh
dotnet publish src/Graft -c Release -r osx-arm64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
  -o ~/.local/bin
mv ~/.local/bin/Graft ~/.local/bin/graft
```

Then, from inside any git repository:

```sh
graft
```

Make sure `~/.local/bin` is on your `PATH` (e.g. `export PATH="$HOME/.local/bin:$PATH"` in `~/.zshrc`).

Variants:

| Want | Change |
|---|---|
| No .NET runtime needed on the machine (≈90 MB instead of ≈11 MB) | `--self-contained true` |
| Intel Mac / Linux / Windows | `-r osx-x64`, `-r linux-x64`, `-r linux-arm64`, `-r win-x64` |

Re-run the same command after pulling changes to update.

## Usage

```
graft [path-to-repo]
graft --sandbox
graft --help
```

| Argument | Meaning |
|---|---|
| `path-to-repo` | The repository to work on. Optional; defaults to the current directory. |
| `--sandbox` | Rebuild the practice repo at `~/.graft/sandbox` and open it. Nothing else is touched. |
| `--help`, `-h` | Print the usage and exit. |

An unknown option, or more than one argument, prints an error and exits with code `2`.

### Keys

| Key | Action |
|---|---|
| `↑` / `↓` | Select a branch |
| `m` | Merge the selected branch into the current branch |
| `Esc` | Quit (if a merge is running, graft waits for it to finish first) |

### Screen

- **Status bar (top):** the repository, the current branch, and whether the working tree is clean. When merging is disabled, the bar says why.
- **Branches (left):** every local branch, with `*` on the current one and the highlighted row for the selection. `↑2` means the branch has 2 commits the current branch lacks; `↓1` means the current branch has 1 commit the branch lacks. The short hash is shown when there is room.
- **Preview and animation (right):** the direction line (`feature/x is 1 ahead, 1 behind main (current)`), the picture of the two branches, a caption, and the commits involved. Before you merge it shows the relationship; during the merge the two lines grow together; afterwards it shows the result.
- **Commands:** every git command graft ran, with its exit code and the output of `git merge`.
- **Key bar (bottom):** the keys that work right now.

The terminal must be at least 64×22. Smaller than that, graft shows the size it needs and ignores `m` until the window is big enough again. Names that don't fit are cut with `…`.

## What a merge can do

| Outcome | What you see | What happens in the repo |
|---|---|---|
| Fast-forward | `✔ Fast-forward: main is now at 1a2b3c4 (+1 commit, no merge commit)` | The current branch moves to the selected branch's tip |
| Merge commit | `✔ Merge commit 1a2b3c4 created on main` (gold `◉` at the join) | A merge commit is created with git's default message |
| Already up to date | `Already up to date: feature/x is already part of main` | Nothing |
| Conflict | `✕ CONFLICT in app.txt: merge aborted, main unchanged` (red `✕`) | graft runs `git merge --abort` and lists the conflicting files. The repo is back where it was |
| Failed | `✕ Merge failed: <reason>` (red `✕`) | git refused or something went wrong; graft reports what state the repo is in |

graft classifies outcomes from the commit graph (did `HEAD` move, to which commit, with how many parents), not by parsing git's localised output.

### When graft refuses to merge

graft refuses, without running `git merge`, when:

- the working tree has uncommitted changes (including untracked files)
- `HEAD` is detached
- a merge is already in progress (graft never aborts a merge it didn't start)
- you selected the current branch
- the current branch has no commits yet
- the branch name is shadowed by a tag or other ref of the same name
- the branch name starts with `-`

## Safety guarantees

- **Never left mid-merge.** On conflict, graft always runs `git merge --abort` (falling back to `git reset --merge`) and then verifies the tree is clean with no `MERGE_HEAD`.
- **Never resets your work.** The rollback only runs when graft's own merge actually started and `HEAD` hasn't moved. Changes graft didn't make are reported, never discarded.
- **Never waits on an editor, pager or prompt.** graft sets `GIT_EDITOR=true`, `GIT_PAGER=cat`, `GIT_TERMINAL_PROMPT=0` and `GIT_MERGE_AUTOEDIT=no`, uses `--no-edit`, and closes git's stdin.
- **Interrupted merges are rolled back.** If you press `Esc` during a merge, graft restores the terminal, prints `waiting for git merge to finish...`, then prints the outcome and every command. `Ctrl+C` at that point interrupts git, and graft rolls back the partial merge.
- **Slow git is visible.** If a merge takes more than about 5 seconds (a signing passphrase prompt or a slow hook), the caption says `git is still running`.
- **Ignores stray git environment variables.** `GIT_DIR`, `GIT_WORK_TREE` and `GIT_INDEX_FILE` are removed, so graft always works on the repo you gave it.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Normal exit, `--help`, or a merge that succeeded |
| `1` | Startup error (not a repository, folder not found, git not on `PATH`, `--sandbox` refused), a UI error, or a merge that finished after you quit and ended in a conflict or failure |
| `2` | Bad command line (unknown option, too many arguments) |

## Known limitations

- `m` merges immediately; there is no confirmation step.
- Your git config applies. `merge.ff=only` turns diverged merges into failures; `merge.ff=false` turns fast-forwards into merge commits. Commit signing with a passphrase-protected key and no agent will show a prompt over the TUI.
- Like plain `git merge`, a merge can overwrite *ignored* files (such as a local `.env`) if the incoming branch tracks them.
- The command log follows the latest output and can't be scrolled by hand.
- Non-ASCII file names in the conflict list are shown git-quoted.

## Roadmap

v1 is merge only. These are ideas for later versions, not commitments:

- **More operations, each with its own animation:** rebase (a limb being re-grafted), cherry-pick, branch create / delete / rename, switch, fetch and pull.
- **Conflict handling inside graft:** keep the merge open and resolve files one by one, instead of aborting.
- **Previews:** show the commits and files a merge would bring in before pressing `m`, and a dry-run mode.
- **Confirmation and options:** an optional confirm step, `--no-ff` / `--ff-only` toggles, a custom merge message.
- **Better views:** a commit graph, a scrollable and copyable command log, remote branches.
- **Distribution:** packaged releases (Homebrew, `dotnet tool install -g graft`), a `--version` flag.

## Development

```sh
dotnet build
dotnet test
dotnet run --project src/Graft -- --sandbox          # rebuild and open ~/.graft/sandbox
dotnet run --project src/Graft -- ~/.graft/sandbox   # reopen without rebuilding
```

Never run graft against the graft repo itself during development. The sandbox has one branch per outcome:

| Branch | Merging it into `main` gives |
|---|---|
| `feature/fast-forward` | Fast-forward |
| `feature/ff-to-merge` | Fast-forward onto a merge commit |
| `feature/merge-commit` | Merge commit |
| `feature/conflict` | Conflict (aborted) |
| `feature/picks` | Merge commit (diverged, 3 commits ahead; three independent commits for later cherry-pick work) |

`graft --sandbox` builds a practice repo at `~/.graft/sandbox` and opens it. It only deletes a folder that graft itself created (marked by `.git/graft-sandbox`); anything else there is left alone and graft refuses to start.

Layout:

- `src/Graft/Program.cs`: arguments, startup, terminal teardown, the exit code after a merge
- `src/Graft/Git/`: `GitClient` (all git CLI access), `RepoReader` (read-only queries for the screen), `Sandbox` (builds `~/.graft/sandbox`)
- `src/Graft/Model/`: the plain records (branches, commits, layouts, merge outcomes)
- `src/Graft/Operations/`: the shared operation shape (`IGitOperation`) and `MergeOperation` (checks, merge, rollback)
- `src/Graft/Ui/App/`: `AppController` (wires the views to git), `BranchesTab`, `Animator`, `QuitGate`
- `src/Graft/Ui/Scenes/` and `src/Graft/Ui/Text/`: the pure drawing, animation and text code (`MergeScenes`, `MergeAnimation`, `ArtGrid`, `CommitLists`, `DirectionLine`, `TextFit`)
- `src/Graft/Ui/Views/`: the Terminal.Gui views and `Palette`
- `tests/Graft.Tests/`: the xUnit tests, including the git safety tests (run in temp repos)

