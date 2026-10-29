<div align="center">

<h1>graftui</h1>

<p><b>Watch your branches grow together.</b><br/>
An animated git TUI: pick a branch, press <code>m</code>, and see the real <code>git merge</code> happen.</p>

<p>
  <img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="License: MIT"/>&nbsp;&nbsp;
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" alt=".NET 10"/>&nbsp;&nbsp;
  <img src="https://img.shields.io/badge/Terminal.Gui-v2-2ea44f?style=flat-square" alt="Terminal.Gui v2"/>&nbsp;&nbsp;
  <img src="https://img.shields.io/badge/platform-macOS%20%7C%20Linux-lightgrey?style=flat-square" alt="Platform: macOS | Linux"/>
</p>

<p>
  <a href="#install">Install</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#usage">Usage</a> ·
  <a href="#safety">Safety</a> ·
  <a href="#contributing">Contributing</a>
</p>

<img src="assets/demo.gif" alt="graftui demo: a merge commit, then a conflict that is aborted automatically" width="760"/>

</div>

<br/>

graftui is a thin, safety-first layer over the git CLI. It never reimplements git: every action is a real `git` command, and every command it runs is shown on screen with its exit code. You see what git actually did, not a guess.

## What it does

- **Animated merges.** Every outcome has its own animation: fast-forward, merge commit, already up to date, conflict and refused
- **Branch overview.** Local branches with ahead/behind counts and a preview of how each one relates to the current branch
- **Nothing hidden.** A live log of the git commands that ran, each one as the command you would type. Press `v` for the exact list, with exit codes and git's output
- **Safe by default.** Refuses to merge over uncommitted work or a detached HEAD, and aborts conflicts automatically so the repo is never left mid-merge
- **Practice sandbox.** `graftui --sandbox` builds a throwaway repo with one branch per merge outcome
- **Tested against real git.** Every safety rule has a test that runs git in a throwaway repository

## Install

There are no packaged releases yet; graftui builds into a single executable. You need git and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

1. Clone it:
   ```bash
   git clone https://github.com/0xmmhh/graftui.git && cd graftui
   ```
2. Publish it into a folder on your `PATH`:
   ```bash
   dotnet publish src/graftui -c Release -r osx-arm64 --self-contained false \
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
     -o ~/.local/bin
   ```
3. Check that `~/.local/bin` is on your `PATH` (e.g. `export PATH="$HOME/.local/bin:$PATH"` in `~/.zshrc`), then run `graftui --help`.

To update, pull and run step 2 again. On another platform, change the runtime: `-r osx-x64`, `-r linux-x64`, `-r linux-arm64` or `-r win-x64` (Windows is untested). Add `--self-contained true` for a binary that doesn't need .NET installed (≈90 MB instead of ≈11 MB).

## Quick start

Try it on the practice repo first:

```bash
graftui --sandbox
```

This builds a repository at `~/.graftui/sandbox` and opens it. Each branch gives a different outcome when merged into `main`:

| Branch | Merging it gives |
|---|---|
| `feature/fast-forward` | Fast-forward |
| `feature/ff-to-merge` | Fast-forward onto a merge commit |
| `feature/merge-commit` | Merge commit |
| `feature/conflict` | Conflict (aborted automatically) |
| `feature/picks` | Merge commit (three commits ahead) |

Run `--sandbox` again any time to reset it. graftui only ever deletes a folder it created itself (marked by `.git/graftui-sandbox`).

Then, in any repository of your own:

```bash
graftui                 # the repo in the current directory
graftui path/to/repo    # or a specific one
```

## Usage

```
graftui [options] [path-to-repo]
graftui --sandbox [options]
graftui --help
graftui --version
```

| Option | Meaning |
|---|---|
| `--theme <name>` | Colours: `default`, `lifted`, `muted`, `high-contrast` or `light`. It combines with `--sandbox` and a path (`--theme=<name>` works too) |
| `--version` | Print the version and exit |
| `--help` | Print the usage and the keys, then exit |

| Key | Action |
|---|---|
| `Tab` | Switch between Branches and Changes |
| `v` | Switch the Commands pane between the simplified commands and the exact list |
| `↑` / `↓` | Select a branch (Branches) or a file (Changes) |
| `m` | Merge the selected branch into the current branch (Branches) |
| `Space` | Stage or unstage the selected file (Changes) |
| `a` / `U` | Stage all / unstage all (Changes) |
| `c` / `A` | Commit what is staged / amend the last commit (Changes) |
| `x` | Discard the unstaged change to the selected file, after a question (Changes) |
| `z` / `Z` | Stash everything / pop the newest stash entry (Changes) |
| `u` | Undo the last step, on either tab |
| `Esc` | Quit (if a merge, a commit or another change is running, graftui waits for it to finish first) |

<details>
<summary>The Changes tab</summary>

- **Changes (left):** the staged files, then the unstaged ones (`M` modified, `A` added, `D` deleted, `R` renamed, `?` untracked, `U` conflicted). The right pane shows the selected file's diff, with each hunk marked as `── line N ──` (the line where it starts in the file), and under it one line with HEAD and the stash count. During a change the three areas animate: the working tree, the staging area and the repository.
- **Staging** moves a file between the working tree and the staging area. Each move is animated and logged with its `git add`, `git restore --staged` or `git rm --cached`. Nothing is ever discarded: the edit stays in your working tree.
- **Committing** opens a message box in the art pane. Type a subject, press `Tab` to reach the body, and press `Enter` to commit (Enter in the body adds a line). `Ctrl+Enter` or `Ctrl+J` saves from either field, if your terminal sends them. `Esc` cancels.
- **Amending** starts from the last commit's message. If that commit is already on a remote branch, graftui asks three escalating questions first: "Are you sure?" (answer `y`), "Are you really sure?" (answer `1`) and "Last chance, are you still sure?" (answer `2`). Any other key is ignored; `n` or `Esc` cancels at any step. graftui never force-pushes; after an amend you run `git push --force-with-lease` yourself.
- **Unstaging hand-picked changes.** A staged file that also differs in the working tree (you staged one version, then edited again) has a staged version that exists only in the index. Unstaging it asks first: "<file> has hand-picked staged changes; unstage them all?" (`y` or `n`). `U` asks once for all of them. `u` restores the exact staged version.
- **Discard** (`x`) asks first. graftui saves the unstaged change as a stash entry named `graftui: discarded <file>`, then puts the file back. A partly staged file keeps its staged part in the index, and a new file is removed. `git stash list` shows the entry, and `u` brings the change back.
- **Stash** (`z`) takes every change, untracked files included (`git stash push -u`). A one-line box asks for a message; Enter with it empty uses git's default message.
- **Pop** (`Z`) runs a plain `git stash pop`: the changes come back unstaged. If git cannot apply the entry, graftui puts the working tree and index back exactly as they were, keeps the stash entry, and lists the conflicting files. The repository is never left with conflict markers.
- **Undo** (`u`) reverses the newest graftui step, one step per press, newest first: commit, amend, merge, discard, stash, pop and the hand-picked unstage. It refuses when undoing would overwrite something that changed since (the refusal names the file or branch). Undo steps are commits under `refs/graftui/undo/`, so `git log --all`, gitk and other git GUIs show them (as "graftui undo: …"). They are harmless, and only the last 50 are kept.
- **Refusals** are shown in the caption and logged: nothing staged, an empty subject, a conflicted file, a nested repository, an unfinished merge, rebase, cherry-pick or revert, a submodule, or a detached HEAD.

</details>

<details>
<summary>The screen</summary>

- **Status bar (top):** the repository, the current branch, and whether the working tree is clean. When merging is disabled, the bar says why.
- **Branches (left):** every local branch, with `*` on the current one. `↑2` means the branch has 2 commits the current branch lacks; `↓1` means the current branch has 1 commit the branch lacks.
- **Preview and animation (right):** how the selected branch relates to the current one, a picture of the two branches, a caption and the commits involved. The commits are listed side by side: the selected branch's on the left, `main`'s on the right, with their headers. Narrower terminals (under 70 columns) stack them. During a merge the two lines grow together; afterwards it shows the result.
- **Commands:** the git commands graftui ran, each as the command you would type (`$ git merge feature/x  ✔`), with git's output. A failed command is shown exactly, with its exit code. Press `v` to see the exact list instead: every git call, with its exit code and the reads graftui hides by default.
- **Key bar (bottom):** the keys that work right now.

The terminal must be at least 64×22. Names that don't fit are cut with `…`.

</details>

<details>
<summary>What a merge can do</summary>

| Outcome | What you see | What happens in the repo |
|---|---|---|
| Fast-forward | `✔ Fast-forward: main is now at 1a2b3c4 (+1 commit, no merge commit)` | The current branch moves to the selected branch's tip |
| Merge commit | `✔ Merge commit 1a2b3c4 created on main` (gold `◉` at the join) | A merge commit is created with git's default message |
| Already up to date | `Already up to date: feature/x is already part of main` | Nothing |
| Conflict | `✕ CONFLICT in app.txt: merge aborted, main unchanged` | graftui runs `git merge --abort` and lists the conflicting files. The repo is back where it was |
| Failed | `✕ Merge failed: <reason>` | git refused or something went wrong; graftui reports what state the repo is in |

graftui works out the outcome from the commit graph (did `HEAD` move, to which commit, with how many parents), not by parsing git's localised output.

</details>

<details>
<summary>When graftui refuses to merge</summary>

graftui refuses, without running `git merge`, when:

- the working tree has uncommitted changes (including untracked files)
- `HEAD` is detached
- a merge is already in progress (graftui never aborts a merge it didn't start)
- you selected the current branch
- the current branch has no commits yet
- the branch name is shadowed by a tag or other ref of the same name
- the branch name starts with `-`

</details>

<details>
<summary>Exit codes</summary>

| Code | Meaning |
|---|---|
| `0` | Normal exit, `--help`, or a merge that succeeded |
| `1` | Startup error (not a repository, folder not found, git not on `PATH`, `--sandbox` refused), a UI error, or a merge that finished after you quit and ended in a conflict or failure |
| `2` | Bad command line (unknown option, too many arguments, unknown theme) |

</details>

## Safety

- **Never left mid-merge.** On conflict, graftui always runs `git merge --abort` (falling back to `git reset --merge`) and then checks that the tree is clean with no `MERGE_HEAD`.
- **Never resets your work.** The rollback only runs when graftui's own merge actually started and `HEAD` hasn't moved. Changes graftui didn't make are reported, never discarded.
- **Staging and committing never discard work.** Stage and unstage only move entries in the index, and the work tree is never touched. A failed commit leaves `HEAD`, the index and your files as they were. graftui never force-pushes.
- **Discard is recoverable.** Before a file's unstaged change is removed, it is saved as a stash entry (`graftui: discarded <file>`) and `u` brings it back. The discard is refused when the change can't be saved safely (a submodule, a nested repository, an intent-to-add file or a conflict).
- **Undo never overwrites.** `u` checks that the state a step left behind is still there, and refuses otherwise. Nothing is dropped silently: a refused step stays on top until you deal with it.
- **Never waits on an editor, pager or prompt.** graftui sets `GIT_EDITOR=true`, `GIT_PAGER=cat`, `GIT_TERMINAL_PROMPT=0` and `GIT_MERGE_AUTOEDIT=no`, uses `--no-edit`, and closes git's stdin.
- **Interrupted merges are rolled back.** Quit during a merge and graftui waits for git, then prints the outcome and every command. `Ctrl+C` at that point interrupts git, and graftui rolls back the partial merge.
- **Slow git is visible.** If a merge takes more than about 5 seconds (a signing prompt or a slow hook), the caption says `git is still running`.
- **Ignores stray git environment variables.** `GIT_DIR`, `GIT_WORK_TREE` and `GIT_INDEX_FILE` are removed, so graftui always works on the repo you gave it.

<details>
<summary>Known limitations</summary>

- `m` merges immediately; there is no confirmation step.
- Your git config applies. `merge.ff=only` turns diverged merges into failures; `merge.ff=false` turns fast-forwards into merge commits. Commit signing with a passphrase-protected key and no gpg-agent running will show a prompt over the TUI.
- Like plain `git merge`, a merge can overwrite *ignored* files (such as a local `.env`) if the incoming branch tracks them.
- The command log follows the latest output and can't be scrolled by hand.
- A signing prompt (pinentry) or a hook that opens the terminal can draw over the TUI during a commit or a merge. graftui waits for git either way, and the caption says `git is still running` after about 5 seconds.
- Non-ASCII file names in the conflict list are shown git-quoted.

</details>

## Status and plans

Early, and deliberately narrow: **graftui does merges, and does them safely.** It also covers everyday local work: the Changes tab stages, unstages, commits and amends, discards (always recoverable), stashes and pops, and `u` undoes the last steps one at a time. Planned next, roughly in this order (ideas, not promises):

1. **Branches** - switch, create, rename and delete branches, each with its own animation, and "stash, merge, pop" as one guided step
2. **Remotes** - fetch, pull (fast-forward first) and push (never forced), with ahead/behind against `origin`
3. **History tools** - cherry-pick, rebase, and a choice of merge types (no-ff, ff-only, squash, linear)
4. **The smart layer** - a forecast of what each key would do, resolving conflicts inside graftui, interactive rebase, a whole-repo graph view, and plain-English explanations of each command

## Development

```bash
dotnet build graftui.slnx
dotnet test graftui.slnx
dotnet run --project src/graftui -- --sandbox            # rebuild and open ~/.graftui/sandbox
dotnet run --project src/graftui -- ~/.graftui/sandbox   # reopen without rebuilding
```

Develop against the sandbox, never against the graftui repository itself. Warnings are errors and nullable reference types are on for every project. The demo GIF is recorded with [vhs](https://github.com/charmbracelet/vhs): `vhs assets/demo.tape`.

<details>
<summary>Project layout</summary>

- `src/graftui/Program.cs` - arguments, startup, terminal teardown, the exit code after a merge
- `src/graftui/Git/` - `GitClient` (all git CLI access), `RepoReader` (read-only queries for the screen), `Sandbox` (builds `~/.graftui/sandbox`)
- `src/graftui/Model/` - plain records (branches, commits, layouts, merge outcomes, undo records)
- `src/graftui/Operations/` - the shared operation shape (`IGitOperation`: check, run, verify, roll back), `MergeOperation`, `StageOperation`, `CommitOperation`, `DiscardOperation`, `StashOperation`, `PopOperation` and `UndoOperation`, with the undo history in `UndoLog` and the saved working-tree states in `WorkState`
- `src/graftui/Ui/App/` - `AppController` (wires the views to git), `BranchesTab` (with its undo), `Animator`, `QuitGate`, `ChangesTab` (the Changes tab, in five files: browsing, commit, stage, ask and stash) and `ChangesAnimator`
- `src/graftui/Ui/Scenes/` and `src/graftui/Ui/Text/` - pure drawing, animation and text code, unit-tested without a terminal
- `src/graftui/Ui/Views/` - the Terminal.Gui views and `Palette`
- `tests/graftui.Tests/` - xUnit tests, including the git safety tests (run in temp repositories)

</details>

## Contributing

Contributions are welcome: bug reports, ideas and pull requests.

- **Found a bug?** [Open an issue](https://github.com/0xmmhh/graftui/issues) with what you did, what you expected and what happened. Include the Commands pane output if it's relevant: press `v` for the exact list of every git command graftui ran.
- **Have an idea?** Open an issue before starting anything big, so we can agree on the approach first. The plans above are a good place to start.
- **Sending a pull request?** Keep it focused on one change, make sure `dotnet build` has no warnings and `dotnet test` passes, and add a test for anything that changes how graftui touches a repository.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the safety rules every change must keep, and how to test.

## License

[MIT](LICENSE)
