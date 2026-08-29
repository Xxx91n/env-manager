# Env Manager CLI demo — recorded with vhs (charmbracelet/vhs).
# Regenerate: vhs docs/assets/demo.tape
# Read-only commands only: this tape never writes to the registry.

Output docs/assets/demo.gif

Set Shell "powershell"
Set FontSize 20
Set Width 1100
Set Height 560
Set Padding 24
Set PlaybackSpeed 1.0
Set LoopOffset 0

Set Theme {
  "name": "env-manager",
  "black": "#111318",
  "red": "#F59E0B",
  "green": "#10B981",
  "yellow": "#F59E0B",
  "blue": "#06B6D4",
  "magenta": "#8B5CF6",
  "cyan": "#06B6D4",
  "white": "#F2F4F7",
  "brightBlack": "#4B5563",
  "brightRed": "#F59E0B",
  "brightGreen": "#34D399",
  "brightYellow": "#FBBF24",
  "brightBlue": "#22D3EE",
  "brightMagenta": "#A78BFA",
  "brightCyan": "#67E8F9",
  "brightWhite": "#FFFFFF",
  "background": "#111318",
  "foreground": "#F2F4F7",
  "selectionBackground": "#1F2937",
  "cursorColor": "#10B981"
}

# Alias the in-repo release CLI without showing the absolute path.
Hide
Type "Set-Alias em D:\Aworker\env-manager\bin\Release\net10.0-windows\env-manager-cli.exe"
Enter
Type "Clear-Host"
Enter
Show

Sleep 500ms
Type "em agents --summary"
Sleep 900ms
Enter
Sleep 1800ms

Type "em path health"
Sleep 1100ms
Enter
Sleep 2500ms

Type "em get PATH"
Sleep 900ms
Enter
Sleep 2000ms

Type "em agents --json"
Sleep 900ms
Enter
Sleep 2500ms

Sleep 1500ms
