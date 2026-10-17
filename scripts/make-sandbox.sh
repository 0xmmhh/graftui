#!/usr/bin/env bash
# Creates a throwaway repo with branches covering every merge outcome graft handles.
# Usage: scripts/make-sandbox.sh [path]   (default: ../graft-sandbox)
# Re-run any time to reset it — the old sandbox is deleted first.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR="${1:-$REPO_ROOT/../graft-sandbox}"
rm -rf "$DIR"
mkdir -p "$DIR"
DIR="$(cd "$DIR" && pwd)"
cd "$DIR"

git init -q -b main
git config user.name "graft sandbox"
git config user.email "sandbox@example.invalid"
git config commit.gpgsign false

commit() { echo "$2" >> "$1"; git add "$1"; git commit -q -m "$3"; }

commit README.md "# sandbox" "Initial commit"
commit app.txt   "line 1"    "Add app"

# conflict: edits the same line of app.txt as main will
git switch -q -c feature/conflict
sed -i.bak 's/line 1/line 1 (from conflict branch)/' app.txt && rm app.txt.bak
git commit -q -am "Change line 1 on branch"
git switch -q main

# main moves on, so the next two can't fast-forward
sed -i.bak 's/line 1/line 1 (from main)/' app.txt && rm app.txt.bak
git commit -q -am "Change line 1 on main"

# merge commit: diverged, but touches a different file
git switch -q -c feature/merge-commit HEAD~1
commit other.txt "other" "Add other file"
git switch -q main

# fast-forward: branches off main's final tip, so main is its ancestor
git switch -q -c feature/fast-forward
commit ff.txt "fast" "Add ff file"
git switch -q main

# fast-forward onto a merge commit: main is an ancestor of the tip, and the tip is itself a merge
git switch -q -c feature/ff-to-merge
commit ffm.txt "ffm" "Add ffm file"
git switch -q -c side-for-ffm
commit side.txt "side" "Add side file"
git switch -q feature/ff-to-merge
git merge -q --no-ff --no-edit side-for-ffm
git branch -q -d side-for-ffm
git switch -q main

# picks: three independent commits off main~1 (for cherry-pick later; merges as a merge commit)
git switch -q -c feature/picks main~1
printf '# Sandbox\n' > README.md && git commit -q -am "Fix typo in README"
commit log.txt "log" "Add logging"
commit VERSION "0.3" "Bump version to 0.3"
git switch -q main

echo "Sandbox ready at $DIR"
git log --oneline --graph --all
