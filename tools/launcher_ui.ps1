# Clicks through the launcher with UI Automation, as a player would, and says
# what each click did: the pages, a graphics preset and Reset, the background
# shader toggle, the launcher language, the bug report, the update, the logs
# folder, Play (the game starts, runs 12 s and is closed), and the window's own
# buttons. Writes results.txt and screenshots to -Out. The settings are put
# back at the end, and the bug report is moved into -Out.
#
#   dotnet build launcher -c Release
#   pwsh tools/launcher_ui.ps1 [-Out work/ui-test] [-Lang he]
#
# -Lang is the launcher's language, which names its buttons: by default the
# one in launcher.local.json, else the system's. Update pulls from origin; with
# new commits it rebuilds everything, as the button does.
param(
  [string]$Exe = '',
  [string]$Out = '',
  [string]$Lang = ''
)
$Ws = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $Ws 'launcher\bin\Release\net48\Launcher.exe' }
if (-not $Out) { $Out = Join-Path $Ws 'work\ui-test' }
if (-not $Lang) {
  $f = Join-Path $Ws 'launcher.local.json'
  if (Test-Path $f) { $Lang = (Get-Content $f -Raw | ConvertFrom-Json).uiLanguage }
  if (-not $Lang) {
    $Lang = [Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName
    if ($Lang -eq 'pt') { $Lang = 'pt-BR' }
    if (-not (Test-Path (Join-Path $Ws "launcher\Strings\$Lang.json"))) { $Lang = 'en' }
  }
}
$S = Get-Content (Join-Path $Ws "launcher\Strings\$Lang.json") -Raw -Encoding utf8 | ConvertFrom-Json
function T($key) { $S.$key }
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
}
"@
[W]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
New-Item -ItemType Directory -Force $Out | Out-Null
$results = New-Object System.Collections.Generic.List[string]
function Note($ok, $what) { $line = "[$(if ($ok) {'ok'} else {'FAIL'})] $what"; $results.Add($line); Write-Host $line }

$cfgFile = Join-Path $Ws 'launcher.local.json'
$cfgBackup = Join-Path $Out 'launcher.local.json.bak'
$hadCfg = Test-Path $cfgFile
if ($hadCfg) { Copy-Item $cfgFile $cfgBackup -Force }
function Cfg { if (Test-Path $cfgFile) { Get-Content $cfgFile -Raw } else { '' } }

function ByName($root, $name) {
  $c = New-Object System.Windows.Automation.PropertyCondition ($A::NameProperty), $name
  $root.FindFirst($Scope::Descendants, $c)
}
function WaitName($root, $name, $sec = 10) {
  for ($i = 0; $i -lt $sec * 5; $i++) { $e = ByName $root $name; if ($e) { return $e }; Start-Sleep -Milliseconds 200 }
  $null
}
# The name may sit on the text inside a control: climb to what can be acted on.
function Act($root, $name, [switch]$Toggle) {
  $el = ByName $root $name
  if (-not $el) { throw "no element named '$name'" }
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  $pats = @([System.Windows.Automation.SelectionItemPattern]::Pattern, [System.Windows.Automation.TogglePattern]::Pattern,
            [System.Windows.Automation.InvokePattern]::Pattern)
  while ($el) {
    foreach ($p in $pats) {
      $o = $null
      if ($el.TryGetCurrentPattern($p, [ref]$o)) {
        if ($o -is [System.Windows.Automation.SelectionItemPattern]) { $o.Select() }
        elseif ($o -is [System.Windows.Automation.TogglePattern]) { $o.Toggle() }
        else { $o.Invoke() }
        Start-Sleep -Milliseconds 600
        return $el
      }
    }
    $el = $walker.GetParent($el)
  }
  throw "nothing to click for '$name'"
}
function Nav($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition ($A::AutomationIdProperty), $id
  $root.FindFirst($Scope::Descendants, $c).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 600
}
# A button by its text, when the same text also names a nav item or a title.
function Press($root, $name) {
  $c = New-Object System.Windows.Automation.PropertyCondition ($A::NameProperty), $name
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  foreach ($el in $root.FindAll($Scope::Descendants, $c)) {
    for ($e = $el; $e; $e = $walker.GetParent($e)) {
      $o = $null
      if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$o)) { break }
      if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { $o.Invoke(); Start-Sleep -Milliseconds 600; return }
    }
  }
  throw "no button '$name'"
}
# The toggle that sits in the same row as a label.
function ToggleNear($root, $label) {
  $c1 = New-Object System.Windows.Automation.PropertyCondition ($A::ControlTypeProperty), ([System.Windows.Automation.ControlType]::CheckBox)
  $c2 = New-Object System.Windows.Automation.PropertyCondition ($A::NameProperty), $label
  $cb = $root.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new($c1, $c2))
  if ($cb) { return $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) }
  $el = ByName $root $label
  if (-not $el) { throw "no label '$label'" }
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  for ($row = $walker.GetParent($el); $row; $row = $walker.GetParent($row)) {
    $c = New-Object System.Windows.Automation.PropertyCondition ($A::IsTogglePatternAvailableProperty), $true
    $t = $row.FindFirst($Scope::Descendants, $c)
    if ($t) { return $t.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) }
  }
  throw "no toggle near '$label'"
}
function Shot($h, $name) {
  $r = New-Object W+R; [W]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ([Math]::Max(1, $r.Ri - $r.L)), ([Math]::Max(1, $r.B - $r.T))
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [W]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}
function ExplorerWindows {
  $c = New-Object System.Windows.Automation.PropertyCondition ($A::ClassNameProperty), 'CabinetWClass'
  @($A::RootElement.FindAll($Scope::Children, $c) | ForEach-Object { $_ })
}

$before = @(ExplorerWindows | ForEach-Object { $_.Current.NativeWindowHandle })
$p = Start-Process $Exe -PassThru
try {
  $h = [IntPtr]::Zero
  for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh(); $h = $p.MainWindowHandle }
  if ($h -eq [IntPtr]::Zero) { Note $false 'the window opened'; exit 1 }
  Start-Sleep -Milliseconds 1200
  $root = $A::FromHandle($h)
  Note $true 'the window opened'

  # The pages, each by a text only it shows.
  foreach ($nav in @(@('NavSetup', 'setup_actions'), @('NavGraphics', 'gfx_sharpen'), @('NavSettings', 'settings_ui_language'),
                     @('NavSupport', 'support_report'), @('NavPlay', 'play_quick'))) {
    Nav $root $nav[0]
    Note ([bool](WaitName $root (T $nav[1]) 5)) "page $($nav[0]) shows $($nav[1])"
    Shot $h "page-$($nav[0])"
  }

  # Graphics: a preset, the reset, and the background-shader toggle.
  Nav $root 'NavGraphics'
  $c0 = Cfg
  Press $root (T 'gfx_preset_performance'); Start-Sleep -Milliseconds 400
  $c1 = Cfg
  Note ($c1 -ne $c0) "Performance preset saved settings"
  Shot $h 'preset-performance'
  Press $root (T 'gfx_reset'); Start-Sleep -Milliseconds 400
  Note ((Cfg) -ne $c1) "Reset saved settings"
  $t = ToggleNear $root (T 'gfx_async')
  $s0 = $t.Current.ToggleState
  $t.Toggle(); Start-Sleep -Milliseconds 400
  $on = Cfg
  Note (($t.Current.ToggleState -ne $s0) -and ($on -match '"shaderAsync"\s*:\s*true')) "background shaders on, saved: $($on -match 'shaderAsync')"
  $t.Toggle(); Start-Sleep -Milliseconds 400
  Note ($t.Current.ToggleState -eq $s0) 'background shaders back off'

  # Settings: the launcher language, to another and back.
  Nav $root 'NavSettings'
  $cb = (ByName $root (T 'settings_ui_language'))
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $combo = $null
  for ($row = $walker.GetParent($cb); $row -and -not $combo; $row = $walker.GetParent($row)) {
    $c = New-Object System.Windows.Automation.PropertyCondition ($A::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ComboBox)
    $combo = $row.FindFirst($Scope::Descendants, $c)
  }
  $walker2 = [System.Windows.Automation.TreeWalker]::RawViewWalker
  # The items carry no names of their own; they come in the order of Loc.Languages.
  $order = @('en', 'he', 'es', 'fr', 'de', 'it', 'pt-BR', 'ja', 'ru', 'ar')
  function Pick($code) {
    $c = New-Object System.Windows.Automation.PropertyCondition ($A::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)
    for ($try = 0; $try -lt 10; $try++) {
      $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
      $items = $combo.FindAll($Scope::Descendants, $c)
      if ($items.Count -ge $order.Count) { break }
    }
    $items[[array]::IndexOf($order, $code)].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    $items.Count
  }
  $otherCode = if ($Lang -eq 'en') { 'de' } else { 'en' }
  $O = Get-Content (Join-Path $Ws "launcher\Strings\$otherCode.json") -Raw -Encoding utf8 | ConvertFrom-Json
  $n = Pick $otherCode
  Note ([bool](WaitName $root $O.settings_ui_language 3)) "launcher language $otherCode ($n in the list): the label reads '$($O.settings_ui_language)'"
  Note ((Cfg) -match "`"uiLanguage`"\s*:\s*`"$otherCode`"") "the language is saved"
  Shot $h "settings-$otherCode"
  Pick $Lang | Out-Null
  Note ([bool](WaitName $root (T 'settings_ui_language') 3)) "launcher language back to $Lang"

  # Support: the report, the update and the logs folder.
  Nav $root 'NavSupport'
  $desk = [Environment]::GetFolderPath('Desktop')
  $old = @(Get-ChildItem $desk -Filter 'sonic06-report-*.zip' -ErrorAction SilentlyContinue | ForEach-Object FullName)
  Press $root (T 'support_report')
  Start-Sleep -Seconds 3
  $new = @(Get-ChildItem $desk -Filter 'sonic06-report-*.zip' | Where-Object { $old -notcontains $_.FullName })
  Note ($new.Count -eq 1) "bug report: $($new.Count) new zip on the desktop"
  foreach ($z in $new) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zz = [System.IO.Compression.ZipFile]::OpenRead($z.FullName)
    $names = ($zz.Entries | ForEach-Object FullName) -join ', '
    $home1 = $env:USERPROFILE
    $leak = $false
    foreach ($en1 in $zz.Entries) { $sr = New-Object System.IO.StreamReader($en1.Open()); if ($sr.ReadToEnd().Contains($home1)) { $leak = $true }; $sr.Close() }
    $zz.Dispose()
    Note (-not $leak) "the report holds $names; user folder hidden: $(-not $leak)"
    Move-Item $z.FullName (Join-Path $Out $z.Name)
  }
  Press $root (T 'support_update')
  # What it did shows in the activity log, on the Setup page.
  Nav $root 'NavSetup'
  $c = New-Object System.Windows.Automation.PropertyCondition ($A::AutomationIdProperty), 'LogBox'
  $u = $null
  for ($i = 0; $i -lt 60 -and -not $u; $i++) {
    Start-Sleep -Milliseconds 500
    $box = $root.FindFirst($Scope::Descendants, $c)
    $text = if ($box) { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { '' }
    $u = ($text -split "`r?`n") | Where-Object { $_ -like '*up to date*' } | Select-Object -Last 1
  }
  Note ([bool]$u) "update: '$u'"
  Nav $root 'NavSupport'
  Press $root (T 'support_logs')
  Start-Sleep -Seconds 2
  $opened = @(ExplorerWindows | Where-Object { $before -notcontains $_.Current.NativeWindowHandle })
  Note ($opened.Count -ge 2) "explorer windows opened by the report and the logs button: $(($opened | ForEach-Object { $_.Current.Name }) -join ' | ')"
  foreach ($w in $opened) { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }

  # Play: the game starts from the button, then is closed again.
  Nav $root 'NavPlay'
  $games0 = @(Get-Process sonic06 -ErrorAction SilentlyContinue | ForEach-Object Id)
  Press $root (T 'play_button')
  $g = $null
  for ($i = 0; $i -lt 40 -and -not $g; $i++) { Start-Sleep -Milliseconds 500; $g = Get-Process sonic06 -ErrorAction SilentlyContinue | Where-Object { $games0 -notcontains $_.Id } | Select-Object -First 1 }
  Note ([bool]$g) "Play started the game (pid $(if ($g) { $g.Id }))"
  if ($g) {
    Start-Sleep -Seconds 12
    Note ([bool](WaitName $root (T 'play_running') 3)) "the launcher says play_running"
    Shot $h 'play-running'
    $g.Refresh(); Note (-not $g.HasExited) 'the game still runs after 12 s'
    if ($g.MainWindowHandle -ne [IntPtr]::Zero) { Shot $g.MainWindowHandle 'game' }
    $g.Kill(); $g.WaitForExit(5000) | Out-Null
    Start-Sleep -Seconds 2
    Note ([bool](WaitName $root (T 'play_ready') 5)) 'after the game closed, the launcher says play_ready'
  }

  # The window's own buttons. Their glyphs are icon-font characters.
  Act $root ([string][char]0xE922) | Out-Null; Start-Sleep -Milliseconds 500
  Note ([W]::IsZoomed($h)) 'maximize'
  Act $root ([string][char]0xE922) | Out-Null; Start-Sleep -Milliseconds 500
  Note (-not [W]::IsZoomed($h)) 'restore'
  Act $root ([string][char]0xE921) | Out-Null; Start-Sleep -Milliseconds 500
  Note ([W]::IsIconic($h)) 'minimize'
  $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Normal'); Start-Sleep -Milliseconds 500
  Act $root ([string][char]0xE8BB) | Out-Null
  Note ($p.WaitForExit(5000)) 'the close button closes the launcher'
} catch {
  Note $false "stopped: $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
} finally {
  if (-not $p.HasExited) { $p.Kill() }
  if ($hadCfg) { Copy-Item $cfgBackup $cfgFile -Force } elseif (Test-Path $cfgFile) { Remove-Item $cfgFile }
  $results | Set-Content (Join-Path $Out 'results.txt') -Encoding utf8
}
