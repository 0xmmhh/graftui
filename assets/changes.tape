# Records assets/changes.gif in the practice repo (~/.graftui/sandbox), never in a real repo.
# Needs vhs and graftui on PATH (./dev install). Run from the repo root: vhs assets/changes.tape
Output assets/changes.gif
Require graftui

Set Shell "zsh"
Set FontSize 15
Set Width 1000
Set Height 700
Set Padding 12
Set Theme "Catppuccin Mocha"
Set TypingSpeed 50ms
Set Framerate 12
Set CursorBlink false

Hide
Type "cd ~ && clear"
Enter
Sleep 500ms
Type "graftui --sandbox"
Enter
Sleep 4s
Escape
Sleep 1s
Type "graftui --make-edits > /dev/null && clear"
Enter
Sleep 2s
Type "graftui ~/.graftui/sandbox"
Enter
Sleep 4s
Show
Sleep 500ms
Tab
Sleep 1.5s
Space
Sleep 2.5s
Type "a"
Sleep 2.5s
Type "c"
Sleep 1s
Type "Tidy up the sandbox"
Sleep 600ms
Enter
Sleep 3.5s
Hide
Escape
Sleep 500ms
