# Essai automatique du glisser-déposer : lance l'application en fenêtre sur la page Essai tactile, injecte de vrais
# événements souris (SetCursorPos / mouse_event) et prend des copies d'écran de la fenêtre pendant et après chaque glisser.
# Résultat : artifacts\dnd\*.png
param([switch]$Keep)
$root = Split-Path $PSScriptRoot -Parent
$exe = "$root\src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe"
$out = "$root\artifacts\dnd"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*.png" -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Win]::SetProcessDPIAware() | Out-Null

$p = Start-Process $exe -ArgumentList '--windowed', '--topmost', '--page', 'DragDropTest' -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$hwnd = $p.MainWindowHandle
[Win]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800
Start-Sleep -Milliseconds 500
$pt = New-Object Win+POINT
[Win]::ClientToScreen($hwnd, [ref]$pt) | Out-Null
$rc = New-Object Win+RECT
[Win]::GetClientRect($hwnd, [ref]$rc) | Out-Null
$W = $rc.Right; $H = $rc.Bottom
"Fenêtre : client à ($($pt.X),$($pt.Y)) taille ${W}x${H}"
$p.Refresh(); $dpi = [Win]::GetDpiForWindow($p.MainWindowHandle); if ($dpi -le 0) { $dpi = 144 }
$sx = $dpi / 96.0   # DIP -> pixels
$f = $W / (1920 * $sx)   # échelle de la fenêtre d essai (--topmost réduit la mise en page pour tenir sur l écran)
"dpi scale = $sx"

function Pos($x, $y) { @([int]($pt.X + $x * $sx * $f), [int]($pt.Y + ($y + 64) * $sx * $f)) }  # la barre du haut ajoute 64 px
function MouseTo($x, $y) { $a = Pos $x $y; [Win]::SetCursorPos($a[0], $a[1]) | Out-Null }
function BtnDown() {
  # Sécurité : n injecter un appui que si le pointeur est bien au-dessus de la fenêtre de l application (jamais dans une autre application)
  $c = New-Object Win+POINT; [Win]::GetCursorPos([ref]$c) | Out-Null
  $top = [Win]::GetAncestor([Win]::WindowFromPoint($c), 2)
  if ($top -ne $hwnd) { Write-Host "ABANDON : le pointeur ($($c.X),$($c.Y)) n est pas au-dessus de la fenetre de l application (top=$top h=$hwnd), aucun appui injecte."; $p.Kill(); exit 3 }
  [Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
}
function BtnUp() { [Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
function Shot($name) {
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)
  $bmp.Save("$out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}
function Drag($name, $x0, $y0, $x1, $y1, [int]$steps = 25, [int]$holdMs = 0, [switch]$NoRelease) {
  MouseTo $x0 $y0; Start-Sleep -Milliseconds 150
  BtnDown; Start-Sleep -Milliseconds 100
  if ($holdMs -gt 0) { Start-Sleep -Milliseconds $holdMs }
  for ($i = 1; $i -le $steps; $i++) {
    MouseTo ($x0 + ($x1 - $x0) * $i / $steps) ($y0 + ($y1 - $y0) * $i / $steps)
    Start-Sleep -Milliseconds 20
  }
  Start-Sleep -Milliseconds 300
  Shot "$name-pendant"
  if (-not $NoRelease) { BtnUp; Start-Sleep -Milliseconds 500; Shot "$name-apres" }
}

# 1. Palette -> liste réordonnable : insertion entre « Prendre pièce » et « Poser pièce »
Drag '1-palette-vers-liste' 160 392 600 258
# 2. Réordonner dans la liste : « Poser pièce » avant « Chercher pièce »
Drag '2-reordonner' 485 336 600 150
# 3. Palette -> branche 1 (Robot : accepté)
Drag '3-branche1' 160 280 980 226
# 4. Palette -> branche 2 (Robot : refusé, conflit)
Drag '4-branche2-refus' 160 336 1350 226
# 5. Palette -> zone refusée
Drag '5-zone-refusee' 160 504 1165 385
# 6. Défilement automatique : vers le bas de la longue liste
Drag '6-autoscroll' 160 448 1700 930 -steps 25 -NoRelease
Start-Sleep -Milliseconds 1500
Shot '6-autoscroll-apres-attente'
BtnUp; Start-Sleep -Milliseconds 400; Shot '6-autoscroll-relache'
# 7. Appui long (300 ms) sur le corps d'une carte, sans poignée
Drag '7-appui-long' 300 168 600 400 -holdMs 450

if (-not $Keep) { Stop-Process -Id $p.Id -Force }
Get-Content "$root\src\StdMachine.Hmi\bin\Debug\net48\crash.log" -Encoding UTF8 -ErrorAction SilentlyContinue | Select-Object -First 10
"Terminé : $out"
