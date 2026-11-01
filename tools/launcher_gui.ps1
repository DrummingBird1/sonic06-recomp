# A small window over launcher.ps1, for players who do not want a console.
# Each action runs the launcher in its own console, so the output stays visible.
# -SmokeTest builds the window and closes it, to check the script without a screen.
param([switch]$SmokeTest)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$tools = $PSScriptRoot
$launcher = Join-Path $tools 'launcher.ps1'

function Invoke-Launcher([string[]]$launcherArgs, [switch]$Wait) {
  $a = @('-NoProfile', '-NoExit', '-File', $launcher) + $launcherArgs
  if ($Wait) { $a = $a | Where-Object { $_ -ne '-NoExit' } }
  Start-Process pwsh -ArgumentList $a -Wait:$Wait
}

function Get-Lines {
  (& pwsh -NoProfile -File $launcher -Check | Out-String).Trim()
}

$form = New-Object Windows.Forms.Form
$form.Text = 'sonic06-recomp'
$form.ClientSize = New-Object Drawing.Size(640, 330)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false

$box = New-Object Windows.Forms.TextBox
$box.Multiline = $true
$box.ReadOnly = $true
$box.Font = New-Object Drawing.Font('Consolas', 9)
$box.SetBounds(10, 10, 620, 260)
$form.Controls.Add($box)

function Add-Button([string]$text, [int]$x, [scriptblock]$click) {
  $b = New-Object Windows.Forms.Button
  $b.Text = $text
  $b.SetBounds($x, 285, 145, 32)
  $b.Add_Click($click)
  $form.Controls.Add($b)
}

$refresh = { $box.Text = Get-Lines }

Add-Button 'Choose game ISO' 10 {
  $d = New-Object Windows.Forms.OpenFileDialog
  $d.Filter = 'Disc image (*.iso)|*.iso'
  if ($d.ShowDialog() -eq 'OK') { Invoke-Launcher @('-Iso', $d.FileName) -Wait; & $refresh }
}
Add-Button 'Choose FFmpeg folder' 165 {
  $d = New-Object Windows.Forms.FolderBrowserDialog
  if ($d.ShowDialog() -eq 'OK') { & pwsh -NoProfile -File $launcher -Check -FFmpeg $d.SelectedPath | Out-Null; & $refresh }
}
Add-Button 'Build' 320 { Invoke-Launcher @('-Build') -Wait; & $refresh }
Add-Button 'Play' 485 { Invoke-Launcher @() }

$form.Add_Shown($refresh)
if ($SmokeTest) {
  $form.Add_Shown({ $form.Close() })
  [void]$form.ShowDialog()
  'window ok: ' + $box.Text.Length + ' characters of status'
  return
}
[void]$form.ShowDialog()
