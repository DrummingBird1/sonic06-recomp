# Per-thread CPU of sonic06.exe over a window: which host threads are busy.
param([int]$Seconds = 5)
$p = Get-Process sonic06 -ErrorAction Stop
$a = @{}; foreach ($t in $p.Threads) { try { $a[$t.Id] = $t.TotalProcessorTime.TotalMilliseconds } catch {} }
Start-Sleep -Seconds $Seconds
$p = Get-Process sonic06 -ErrorAction Stop
$rows = foreach ($t in $p.Threads) {
    try { $ms = $t.TotalProcessorTime.TotalMilliseconds } catch { continue }
    if ($a.ContainsKey($t.Id)) { [pscustomobject]@{ tid = $t.Id; pct = [math]::Round(100 * ($ms - $a[$t.Id]) / ($Seconds * 1000), 1) } }
}
$rows | Sort-Object pct -Descending | Select-Object -First 12 | ForEach-Object { "{0,6} {1,6}%" -f $_.tid, $_.pct }
"total {0}% of one core" -f [math]::Round(($rows | Measure-Object pct -Sum).Sum, 1)
