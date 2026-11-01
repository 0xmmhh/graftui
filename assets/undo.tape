# Records assets/undo.gif in the practice repo (~/.graftui/sandbox), never in a real repo.
# Needs vhs and graftui on PATH (./dev install). Run from the repo root: vhs assets/undo.tape
Output assets/undo.gif
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
Sleep 500ms
Down
Sleep 400ms
Down
Sleep 400ms
Down
Sleep 1.2s
Type "m"
Sleep 4.5s
Type "u"
Sleep 4s
Hide
Escape
Sleep 500ms
