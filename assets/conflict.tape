# Records assets/conflict.gif in the practice repo (~/.graftui/sandbox), never in a real repo.
# Needs vhs and graftui on PATH (./dev install). Run from the repo root: vhs assets/conflict.tape
Output assets/conflict.gif
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
Show
Sleep 2s
Type "m"
Sleep 8s
Hide
Escape
Sleep 500ms
