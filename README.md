# graft

An animated git TUI. Pick a branch, press `m`, and watch two limbs grow together while the real `git merge` runs underneath.

```
main                                    feature/merge-commit
●━━━━━━━━━━━━━━━━━━━━━◆━━━━━━━━━━━━━━━━━━━━●
Merge commit 41c083c created on main
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
```

| Argument | Meaning |
|---|---|
| `path-to-repo` | The repository to work on. Optional; defaults to the current directory. |

There are no other options yet.

### Keys

| Key | Action |
|---|---|
| `↑` / `↓` | Select a branch |
| `m` | Merge the selected branch into the current branch |
| `Esc` | Quit (if a merge is running, graft waits for it to finish first) |

### Screen

- **Status line:** the current branch and whether the working tree is clean.
- **Branch list:** all local branches; `*` marks the current one. It grows with the terminal and scrolls.
- **Animation:** the two branches growing together, then the outcome.
- **Commands:** every git command graft ran, with its exit code and the output of `git merge`.

## What a merge can do

| Outcome | What you see | What happens in the repo |
|---|---|---|
| Fast-forward | `Fast-forward: main is now at 1a2b3c4` (joined ◆) | The current branch moves to the selected branch's tip |
| Merge commit | `Merge commit 1a2b3c4 created on main` (joined ◆) | A merge commit is created with git's default message |
| Already up to date | `Already up to date` | Nothing |
| Conflict | `CONFLICT in app.txt: merge aborted, main unchanged` (broken ╳) | graft runs `git merge --abort` and lists the conflicting files. The repo is back where it was |
| Failed | `Merge failed: <reason>` (broken ╳) | git refused or something went wrong; graft reports what state the repo is in |

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
| `0` | Normal exit |
| `1` | Startup error (not a repository, folder not found, git not on `PATH`), a UI error, or a merge that finished after you quit and ended in a conflict or failure |

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
- **Distribution:** packaged releases (Homebrew, `dotnet tool install -g graft`), command-line flags such as `--version` and `--help`.

## Development

```sh
scripts/make-sandbox.sh                          # (re)build ../graft-sandbox
dotnet run --project src/Graft -- ../graft-sandbox
```

Never run graft against the graft repo itself during development. The sandbox has one branch per outcome:

| Branch | Merging it into `main` gives |
|---|---|
| `feature/fast-forward` | Fast-forward |
| `feature/ff-to-merge` | Fast-forward onto a merge commit |
| `feature/merge-commit` | Merge commit |
| `feature/conflict` | Conflict (aborted) |

Layout:

- `src/Graft/Program.cs`: entry point, UI wiring, merge driver
- `src/Graft/Git/`: `GitClient` (all git CLI access), `Merger` (checks, merge, rollback) and their records
- `src/Graft/Ui/MergeArt.cs`: the animation frames, as plain strings
- `scripts/make-sandbox.sh`: builds the sandbox repo


