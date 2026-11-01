# Architecture

This guide is for someone who knows some C#. It shows how the code is split into folders, how one key press becomes a
real `git` command, and where the safety rules live. For what graftui does, read the [README](README.md) first.

File and type names are real and can be found with a search. There are no line numbers, because they go stale.

## 1. What graftui is

graftui is a terminal UI over the git command line. It is written in C# on .NET 10, with Terminal.Gui v2 (2.5.0) for the
screen. The app is one executable in `src/graftui`. The tests are in `tests/graftui.Tests`. git is always a separate
process: graftui never links a git library, and it never edits `.git` files itself.

## 2. Folders

The main files in each folder:

| Folder | What is in it |
|---|---|
| `src/graftui/` | `Program.cs` (startup, teardown, exit code) and `CliArgs.cs` (the command line and `HelpText`) |
| `Git/` | `GitClient` is the only code that starts git. `RepoReader` runs read-only queries (`LoadSnapshot`, `LoadLayout`, `LoadChanges`, `LoadDiff`). `StatusParser` and `DiffParser` read git's output. `GitArgs` holds the git verbs and `GitCommandLine` renders a command as you would type it. `GitResult` is what one call returned. `Sandbox` builds `~/.graftui/sandbox`, and `SandboxEdits` is the hidden `--make-edits` flag |
| `Model/` | Plain records: `Branch`, `CommitInfo`, `RepoSnapshot`, `GraphLayout`, `UndoRecord` and `BranchLanes`. Each operation's request, check and outcome records are in `MergeOutcome`, `BranchModels`, `CommitModels`, `StageModels` and `StashModels` |
| `Operations/` | One class per git action, each implementing `IGitOperation`: `MergeOperation`, `StageOperation`, `CommitOperation`, `DiscardOperation`, `StashOperation`, `PopOperation`, `UndoOperation`, `SwitchOperation`, `CreateBranchOperation`, `RenameBranchOperation`, `DeleteBranchOperation` and `StashMergeOperation`. Helpers: `UndoLog`, `UndoJson`, `WorkState`, `RepoState`, `BranchGit`, `BranchNames`, `BranchRefusals`, `ChangeRefusals` and `CommitMessage` |
| `Ui/App/` | The glue between the views and git: `AppController`, the tabs (`BranchesTab`, `ChangesTab`), the animators (`Animator`, `BranchAnimator`, `ChangesAnimator`), `QuitGate` and `BranchListWidth` |
| `Ui/Scenes/` | Pure drawing. A scene takes a size and a time and paints an `ArtGrid`: `MergeScenes`, `MergeAnimation`, `BranchAnimation`, `AreasScenes`, `ChangesAnimation`, `StashAnimation`, `StashMergeAnimation`, `ArtGeometry` and `AreasLayout` |
| `Ui/Text/` | Pure text and layout: `CommandDisplay` (what the Commands pane shows), `CommitLists` (the commit lists, with the `CommitPanel` record), `DirectionLine`, `TextFit`, `ScreenLayout`, `KeyState`, `RefusalText`, `BranchInks` and `Style` (the `Ink` colour roles, `Style` and `Seg`) |
| `Ui/Views/` | The Terminal.Gui views: `StatusBarView`, `BranchesView`, `ChangesView`, `ArtPaneView`, `LogView`, `KeyBarView`, `TooSmallView` and `PaneView` (their base). Also `Paint`, `Palette` (the colours in use) and `Themes` (the colour sets) |
| `tests/graftui.Tests/` | xUnit tests, covered in section 10 |

The rule behind the split is simple. Only the views draw. Only `Operations`, `RepoReader` and `GitClient` run git.
`Scenes` and `Text` never start git and never touch a view. They use Terminal.Gui only for character widths (`Rune.GetColumns`),
so their tests run with plain strings. `Model` has no git calls and no drawing either. Some of its records carry a
`GitResult`, which is only the record of a call that already ran.

## 3. Startup

`Program.cs` runs these steps in order:

1. `CliArgs.Parse` reads the arguments. A bad argument prints the error and exits with 2. `--help` (and `-h`) and `--version`
   exit before the screen opens and before any repo is touched. The hidden `--make-edits` also exits early, but it edits the
   practice repo first.
2. `Themes.TryGet` finds the theme, and `Palette.Apply` sets the colours. This happens before any view exists, because
   every view reads the colours.
3. With `--sandbox`, `Sandbox.Rebuild` rebuilds `~/.graftui/sandbox`.
4. `new GitClient(path)` is made. `IsRepository` must be true. Then `RepoReader.LoadSnapshot(withCounts: false, ...)` reads
   the first snapshot. A broken repo fails here, with a plain message, before the screen opens.
5. `Application.Create()` returns an `IApplication`, and `app.Init()` starts Terminal.Gui. In Terminal.Gui v2 the
   application is an object you create and pass around, not a static class.
6. `new AppController(app, git, new GitClient(root), snapshot, repoDisplay)`. There are two git clients. `Git` works in the
   folder you gave. `RootGit` works in the top level of the repo. The Changes tab uses `RootGit`, because its paths are
   relative to the top level.
7. `gui.BuildWindow()` builds the views, and `app.Run(window)` runs the screen until you quit.
8. The `finally` block calls `gui.MarkQuitting()` and disposes the window and the app. That puts the terminal back.
9. `FinishMerge()` and `WaitForChanges()` wait for any git work still running, print what it did, and set the exit code.

## 4. Threads: one rule

- git runs on worker threads started with `Task.Run`. The UI thread never waits for git.
- Views change only on the UI thread. Background code calls `AppController.Ui(action)`, which hands the action to
  `App.Invoke`.
- `AppController.Busy` is true while a change runs. While it is true, new actions are refused, and the key bar shows it.
- `SnapshotSeq` counts snapshots. A background read that finishes after something newer is dropped, so an old answer
  never overwrites a new one.
- Animations tick with a timer. `BranchAnimator`, `ChangesAnimator` and `Animator` each call
  `App.AddTimeout(TimeSpan.FromMilliseconds(50), Tick)`. Terminal.Gui repeats the callback while it returns `true` and
  stops when it returns `false`.
- `SetNeedsDraw()` asks Terminal.Gui to draw the view again. Then Terminal.Gui calls its `OnDrawingContent`.

## 5. Worked example: a key press becomes a merge

This follows `m` on the Branches tab from the key press to the final frame.

1. `AppController.BuildWindow` subscribes `OnKeyDown` to `App.Keyboard.KeyDown`.
2. `AppController.OnKeyDown` gives open boxes and questions the key first. Then `v` toggles the Commands pane
   (`LogView.ToggleExact`), and `Tab` switches tabs. On the Branches tab, `m` calls `BranchesTab.StartMerge`:

   ```csharp
   else if (e == Key.M)
   {
       e.Handled = true;
       BranchesTab.StartMerge();
   }
   ```

3. `BranchesTab.StartMerge` runs on the UI thread. It sets `Busy`, cancels any preview still loading, and marks the key
   bar busy. Then it starts a worker with `Task.Run` and keeps the task in `AppController.MergeTask`. `Program` waits for
   that task on exit.
4. The worker loads the `GraphLayout` for the selected branch (`AppController.TryLoadLayout`, which calls
   `RepoReader.LoadLayout`, or uses the copy the preview already read). Then `BranchesTab.RunGated` does the work in order:
   `MergeOperation.Check`, then a refusal if the check failed, then the quit check (`QuitGate.IsQuitting`), then the
   start of the animation, then `MergeOperation.Run`. `StartMerge` passes three small functions to `RunGated`: one
   answers "is graftui quitting?", one shows the refusal (or offers the stash question when the tree is dirty), and one
   starts the animation. `RunGated` itself is a plain method:

   ```csharp
   var check = op.Check(request);
   if (!check.CanMerge)
   {
       refused(check);
       return null;
   }
   if (isQuitting())
       return null;
   starting(check);
   return op.Run(request, check);
   ```

5. `MergeOperation.Check` only reads: `symbolic-ref -q HEAD` (is HEAD detached?), a leading `-` in the name, the same
   branch as the current one, a merge already in progress (`MERGE_HEAD`), and `status --porcelain` (is the tree dirty?).
6. `MergeOperation.Run` reads HEAD and the branch's tip. It refuses a name that a tag also has. It checks `status`
   again, then runs `git merge --no-edit <branch>`. Then the local `Verify` classifies the result. It reads
   `rev-list --parents -n 1 HEAD`. If HEAD is the old HEAD, the merge was up to date. If HEAD is the branch's tip, it was
   a fast-forward. If HEAD has two parents and the first is the old HEAD, it was a merge commit. On failure, `diff
   --name-only --diff-filter=U` lists the conflicting files, and `Rollback` runs (section 8). A verified merge saves an
   undo step with `UndoLog.Push`. Every git call is kept in the outcome's `Commands`.
7. Back on the worker, `AppController.TryLoadSnapshot` reads the new state. For a merge commit it reads that commit too.
   Then `Animator.SetPending` runs on the UI thread and stores the outcome.
8. `Animator.BeginAnimation` ran earlier, on the UI thread. It sets `ArtPaneView.Scene` to a function of time that calls
   `MergeAnimation.Render`, and it starts the 50 ms timer. Each `Tick` gives the outcome to `MergeAnimation.SetOutcome`
   once it has arrived, asks for a redraw, and calls `FinishAnimation` when `MergeAnimation.IsDone` says so.
9. `Animator.FinishAnimation` freezes the last frame. It sets the commit lists (`CommitLists.After`) and logs every
   command through `AppController.LogCommands`. Each command goes through `CommandDisplay.Entry`, which turns it into a
   `LogEntry` holding its exact line and its simplified line. It logs the conflicting files, and it anchors the log on the `git merge` line. Then it applies the
   new snapshot (`ApplySnapshot` refreshes the status bar, the branch list and the key bar) and clears `Busy`.
10. Drawing: `ArtPaneView.OnDrawingContent` calls the scene for the pane's `ArtGeometry`. `MergeScenes.Merge`,
    `FastForward`, `UpToDate` and `Failed` paint into an `ArtGrid` and return a `SceneFrame`, which is the grid and a caption.

In short:

```
key press -> OnKeyDown -> StartMerge (Busy = true)
  worker:   LoadLayout -> RunGated: Check -> refused? (UI: ShowRefused)
                                    -> quit requested? stop
                                    -> UI: BeginAnimation (Tick every 50 ms)
                                    -> Run: merge, Verify or Rollback
            TryLoadSnapshot -> UI: SetPending
  UI:       Tick -> SetOutcome -> SetNeedsDraw -> IsDone? FinishAnimation
            OnDrawingContent -> scene -> MergeScenes -> ArtGrid
```

The Changes tab works the same way. `ChangesTab.StartChange` (in `ChangesTab.Stash.cs`) runs the gate through
`ChangesTab.RunGated`, and `ChangesAnimator` draws the three areas. Branch changes (switch, create, rename, delete) go
through `BranchesTab.StartBranchChange` (in `BranchesTab.Run.cs`), and `BranchAnimator` draws them.

## 6. IGitOperation

Every operation implements this interface, from `Operations/IGitOperation.cs`:

```csharp
public interface IGitOperation<in TRequest, TCheck, out TOutcome>
{
    TCheck Check(TRequest request);
    TOutcome Run(TRequest request, TCheck check);
}
```

The remarks in that file describe the shape of every operation:

- `Check` never writes. It returns a refusal or a go-ahead, and the git commands it ran.
- `Run` checks again anything that could have changed since `Check`, runs git, and then verifies the result.
- Verify works out the result from repo state: HEAD, its parents and `MERGE_HEAD`. It never reads git's text, because
  that text depends on the language.
- Rollback runs only on failure or interruption. It undoes only this operation's own changes, and it reports what it
  could not undo.
- `Run` never throws. Its outcome lists every command that ran, and it carries an `UndoRecord` when refs moved.

The interface has only `Check` and `Run`. Verify and Rollback are methods that each operation has, as private or local
functions. In `MergeOperation`, `Verify` is a local function inside `Run`, and `Rollback` is a private static method.

The table shows the main git command of each operation and the undo kind it saves (`UndoKind`, in `Model/UndoRecord.cs`).

| Operation | Main git commands | Undo kind |
|---|---|---|
| `MergeOperation` | `merge --no-edit <branch>` | `Merge` |
| `CommitOperation` | `commit` (from a message file); `commit --amend` | `Commit`, `Amend` |
| `StageOperation` | `add -- <path>`, `add -A`, `restore --staged`, `rm --cached` | `Unstage`, for hand-picked changes only |
| `DiscardOperation` | `commit-tree` and `stash store -m "graftui: discarded <path>"` save the change, then `restore` puts the file back | `Discard` |
| `StashOperation` | `stash push -u` (with `-m` when there is a message) | `Stash` |
| `PopOperation` | `stash pop`; `stash pop --index` for graftui's own entry | `Pop` |
| `SwitchOperation` | `switch --no-guess <target>` | none (not undoable) |
| `CreateBranchOperation` | `switch -c <name>` | `CreateBranch` |
| `RenameBranchOperation` | `branch -m <old> <new>` | `RenameBranch` |
| `DeleteBranchOperation` | `branch -d <name>` or `branch -D <name>` | `DeleteBranch` |
| `StashMergeOperation` | `stash push -u`, `merge`, `stash pop --index` | `StashMerge` |

## 7. Undo

- Each undoable step is an `UndoRecord`. Its `UndoKind` says what it is, and it has the refs that moved and the commits it
  needs.
- `UndoLog` stores each step as one commit under `refs/graftui/undo/NNNNNN`. The newest step has the highest number.
  The commit uses the empty tree (`mktree`) and is made with `commit-tree --no-gpg-sign`. A hand-picked unstage is the
  exception: its tree holds the saved index entries (`UnstageTree`), so the staged versions stay reachable. Its parents
  are the commits the step needs, so those commits stay reachable. Its message is a headline, `graftui undo: <what>`,
  then the record as one line of JSON (`UndoJson`).
- `UndoLog` has `Push` (save a step), `Top` (read the newest) and `Drop` (remove it). Only the last 50 steps are kept
  (`UndoLog.Max`).
- `UndoOperation.Check` reads the newest step. It refuses when undoing would overwrite something that changed since the
  step was saved.
- `UndoOperation.Run` reverses the step. For a merge it runs `git reset -q --keep <before>`. For a commit it runs
  `git reset -q --soft <before>`. Then it drops the step.
- `WorkState` saves tree and index snapshots for the steps that need them, such as the hand-picked unstage.
- The undo steps are commits, so `git log --all` and git GUIs show them. They do no harm.
- The Commands pane hides the bookkeeping. It shows `↶ saved an undo step (u)` instead (`CommandDisplay.UndoSavedNote`).

## 8. Safety rules in code

- **git never waits for a person.** `GitClient.RunCore` sets these variables and closes git's input:

  ```csharp
  psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
  psi.Environment["GIT_EDITOR"] = "true";
  psi.Environment["GIT_PAGER"] = "cat";
  psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
  ```

  ```csharp
  process.StandardInput.Close();
  ```

  Merges also use `--no-edit`.
- **The right repo.** `RunCore` removes `GIT_DIR`, `GIT_WORK_TREE` and `GIT_INDEX_FILE` from the environment. Every call
  runs in `GitClient.RepoPath`.
- **Status reads don't lock.** `GIT_OPTIONAL_LOCKS=0` means a status read never takes `index.lock`, so it can't get in the
  way of a merge that is starting.
- **Literal paths.** `RunLiteral` and `RunWithIndex` set `GIT_LITERAL_PATHSPECS=1`. A file called `*.txt` matches only
  that file.
- **Never left mid-merge.** `MergeOperation.Rollback` runs when a merge fails or is interrupted. Its first step is
  `AbortIfMerging`, which runs `git merge --abort` (or `git reset --merge` if that fails). Then it checks that the tree
  is clean and that `MERGE_HEAD` is gone. It only resets the tree when the merge started and HEAD is still where it began.
- **Results from repo state.** `MergeOperation.Run` decides the outcome from HEAD and its parents (`Verify`), never from
  git's text.
- **Quitting mid-change.** `QuitGate` stops a change that has not started yet. A change that has started is not stopped.
  `Program.FinishMerge` and `WaitForChanges` wait for it to finish, print the outcome, and leave Ctrl+C to git. Ctrl+C
  reaches git too, so a half-done merge is rolled back.
- **One door to git.** `GitClient` is the only code that starts a process. `Process.Start` appears nowhere else.

## 9. Branch colours

`Model/BranchLanes.cs` decides which colour slot ("lane") each branch has. There are six lanes (`BranchLanes.Count`).
Each theme gives each lane one colour (`Themes.cs`, the `Lanes` list, applied by `Palette`).

- `Assign(branches)` gives every branch a lane, in list order. It looks only at the names and their order. It never looks
  at which branch is current or selected, so a colour does not move when you switch.
- The trunk (`main`, or `master` when there is no `main`, from `TrunkName`) always gets lane 0. Lane 0 is blue in every theme.
- Every other branch starts from a preferred lane, which is `Preferred(name)`:

  ```csharp
  internal static int Preferred(string name)
  {
      return 1 + (int)(Fnv1a(name) % PoolSize);
  }
  ```

  `Fnv1a` is a 32-bit FNV-1a hash of the name's UTF-8 bytes. Unlike `string.GetHashCode`, it gives the same answer on every
  run and every machine.
- If the preferred lane is taken, `FirstFree` takes the next free lane. If all five are taken, `FirstUnlike` takes the
  first lane that differs from the previous branch in the list, so neighbours never share a colour.
- The users are `BranchesView` (the names), `GraphLayout.WithLanes` (which sets `CurrentLane` and `SelectedLane` for the
  graph), and `BranchInks`, which maps a lane to `Ink.Lane0` to `Ink.Lane5`. The merge commit's `◉` uses `Ink.Mrg`, which
  is gold, not a lane.

## 10. Tests

The test project is `tests/graftui.Tests`. The app makes its internals visible to it (`InternalsVisibleTo` in
`graftui.csproj`). There are three kinds of tests:

- **Git safety tests** (`*SafetyTests.cs`, for example `MergeSafetyTests`, `StashSafetyTests` and `UndoSafetyTests`).
  Each builds a throwaway repository with `Support/TempRepo.cs` (`TempRepo.Create`, `Diverged` and `FastForwardable`)
  under the system temp folder, and deletes it afterwards. Sandbox tests use `TempRepo.NewSandboxDir`.
- **Pure tests** for `Scenes` and `Text`, such as `MergeScenesTests`, `CommitListsTests`, `TextFitTests` and
  `CommandDisplayTests`. They need no git and no terminal.
- **View and parsing tests**, such as `BranchesViewTests`, `LogViewTests`, `KeyBarFitTests` and `CliArgsTests`.

The rule is that no test touches the graftui repo or `~/.graftui/sandbox`. Run the tests with `dotnet test graftui.slnx`.
CI runs the `build-and-test` job on every pull request to `main`.

## 11. Adding an operation

1. Add the request, check and outcome records in `Model/`.
2. Write a class in `Operations/` that implements `IGitOperation`. Every git call goes through `GitClient`.
3. Add a safety test in `tests/graftui.Tests` that uses `TempRepo`.
4. If the operation moves refs, add an `UndoKind` and save an `UndoRecord`.
5. Wire a key into `OnKeyDown` or `ChangesTab.HandleKey`. Then add it to the key bar (`KeyBarView`, and the `BranchesKeyState` or `ChangesKeyState` record in `KeyState.cs`),
   to `CliArgs.HelpText`, and to the README.
