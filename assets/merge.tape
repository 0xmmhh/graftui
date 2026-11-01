# Records assets/merge.gif in the practice repo (~/.graftui/sandbox), never in a real repo.
# Needs vhs and graftui on PATH (./dev install). Run from the repo root: vhs assets/merge.tape
Output assets/merge.gif
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
Show

Type "graftui --sandbox"
Sleep 1s
Hide
Enter
Sleep 4s
Show
Sleep 500ms
Down
Sleep 1.5s
Type "m"
Sleep 4s
Down
Sleep 500ms
Down
Sleep 1.5s
Type "m"
Sleep 4.5s
Hide
Escape
Sleep 500ms
