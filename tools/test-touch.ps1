# Essai automatique du glisser-déposer AU TOUCHER : injecte de vrais événements tactiles (InjectTouchInput de Windows)
# sur la page Essai tactile et prend des copies d'écran. Résultat : artifacts\touch\*.png
param([switch]$Keep)
$root = Split-Path $PSScriptRoot -Parent
$exe = "$root\src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe"
$out = "$root\artifacts\touch"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*.png" -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class T {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll", SetLastError = true)] public static extern bool InitializeTouchInjection(uint max, uint mode);
  [DllImport("user32.dll", SetLastError = true)] public static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINTER_INFO {
    public uint pointerType, pointerId, frameId, pointerFlags; public IntPtr sourceDevice, hwndTarget;
    public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
    public uint dwTime, historyCount; public int InputData; public uint dwKeyStates; public ulong PerformanceCount; public int ButtonChangeType; }
  [StructLayout(LayoutKind.Sequential)] public struct POINTER_TOUCH_INFO {
    public POINTER_INFO pointerInfo; public uint touchFlags, touchMask; public RECT rcContact, rcContactRaw; public uint orientation, pressure; }
  public static bool Touch(uint id, int x, int y, uint flags) {
    var c = new POINTER_TOUCH_INFO[1];
    c[0].pointerInfo.pointerType = 2; c[0].pointerInfo.pointerId = id; c[0].pointerInfo.pointerFlags = flags;
    c[0].pointerInfo.ptPixelLocation.X = x; c[0].pointerInfo.ptPixelLocation.Y = y;
    c[0].touchFlags = 0; c[0].touchMask = 7;
    c[0].rcContact.Left = x - 6; c[0].rcContact.Right = x + 6; c[0].rcContact.Top = y - 6; c[0].rcContact.Bottom = y + 6;
    c[0].orientation = 90; c[0].pressure = 32000;
    return InjectTouchInput(1, c);
  }
}
"@
[T]::SetProcessDPIAware() | Out-Null
if (-not [T]::InitializeTouchInjection(2, 3)) { "InitializeTouchInjection a échoué : $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"; exit 2 }

$p = Start-Process $exe -ArgumentList '--windowed', '--topmost', '--page', 'DragDropTest' -PassThru
Start-Sleep -Seconds 4
$p.Refresh(); $hwnd = $p.MainWindowHandle
[T]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800
Start-Sleep -Milliseconds 500
$pt = New-Object T+POINT; [T]::ClientToScreen($hwnd, [ref]$pt) | Out-Null
$rc = New-Object T+RECT; [T]::GetClientRect($hwnd, [ref]$rc) | Out-Null
$W = $rc.Right; $H = $rc.Bottom
$dpi = [T]::GetDpiForWindow($p.MainWindowHandle); if ($dpi -le 0) { $dpi = 144 }
$sx = $dpi / 96.0
$f = $W / (1920 * $sx)   # échelle de la fenêtre d essai (--topmost réduit la mise en page pour tenir sur l écran)
"client ($($pt.X),$($pt.Y)) ${W}x${H} scale=$sx"

function PX($v, $o) { [int]($o + $v * $sx) }
function PXX($v) { [int]($pt.X + $v * $sx * $f) }
function PXY($v) { [int]($pt.Y + ($v + 64) * $sx * $f) }
function Shot($name) {
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)
  $bmp.Save("$out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}
# DOWN=0x10000 UPDATE=0x20000 UP=0x40000 INRANGE=2 INCONTACT=4 FIRSTBUTTON=0x10 NEW=1
function TouchDrag($name, $x0, $y0, $x1, $y1, [int]$steps = 25, [int]$holdMs = 0, [int]$stepMs = 20) {
  $ax = PXX $x0; $ay = PXY $y0
  $cp = New-Object T+POINT; $cp.X = $ax; $cp.Y = $ay
  if ([T]::GetAncestor([T]::WindowFromPoint($cp), 2) -ne $hwnd) { Write-Host "ABANDON : le point n est pas au-dessus de la fenetre de l application, aucun toucher injecte."; $p.Kill(); exit 3 }
  $r = [T]::Touch(1, $ax, $ay, 0x10000 -bor 0x2 -bor 0x4); "$name down inject=$r err=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()) at $ax,$ay"
  if ($holdMs -gt 0) { Start-Sleep -Milliseconds $holdMs }
  for ($i = 1; $i -le $steps; $i++) {
    $x = PXX ($x0 + ($x1 - $x0) * $i / $steps); $y = PXY ($y0 + ($y1 - $y0) * $i / $steps)
    $ok = [T]::Touch(1, $x, $y, 0x20000 -bor 0x2 -bor 0x4); if (-not $ok) { "  move $i inject FAILED err=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    Start-Sleep -Milliseconds $stepMs
  }
  Start-Sleep -Milliseconds 300
  Shot "$name-pendant"
  $x = PXX $x1; $y = PXY $y1
  [T]::Touch(1, $x, $y, 0x40000) | Out-Null
  Start-Sleep -Milliseconds 600
  Shot "$name-apres"
}

# 1. Poignée : palette -> liste réordonnable (insertion avant « Prendre pièce »)
TouchDrag '1-poignee' 160 224 600 200 -holdMs 120
# 2. Corps de carte, glissement rapide : doit défiler (pas de glisser) dans la longue liste
TouchDrag '2-defilement-sur-corps' 1760 700 1760 300 -steps 12 -stepMs 12
# 3. Appui long (450 ms) sur le corps d'une carte puis glisser vers la branche 1
TouchDrag '3-appui-long' 300 336 980 226 -holdMs 450
# 4. Zone refusée
TouchDrag '4-refus' 160 504 1165 385

if (-not $Keep) { Stop-Process -Id $p.Id -Force }
Get-Content "$root\src\StdMachine.Hmi\bin\Debug\net48\crash.log" -Encoding UTF8 -ErrorAction SilentlyContinue | Select-Object -First 10
"Terminé : $out"
