$base = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\analysis\fleet_sweep_2026-05-19"
$keys = @(
    "StatThroughputOrdersPerHour",
    "StatRobotUtilizationProductive",
    "StatRobotUtilizationEffective",
    "StatTimeIdleSec",
    "StatTimeRestSec",
    "StatQueueingAtStationTimeSec",
    "StatEnergyTotalKJ",
    "StatEnergyTotalWithSupportKJ",
    "StatEnergyPerOrderWithSupportKJ",
    "StatOverallOrdersHandled",
    "StatPodVisitOrdersServed_mean",
    "StatPodVisitOrdersServed_p50",
    "StatDecisionAvailableStationSlots_mean",
    "StatDecisionAvailableStationSlots_p50",
    "StatDecisionBotsInRest_count",
    "StatDecisionBotsInRest_mean",
    "StatDecisionBotsInRest_p25",
    "StatDecisionBotsInRest_p50",
    "StatDecisionBotsInRest_p75",
    "StatDecisionBotsInRest_p95",
    "StatDecisionBotsInRest_max",
    "StatDecisionBotsIdleOrRest_mean",
    "StatDecisionBotsIdleOrRest_p50",
    "StatDecisionBotsIdleOrRest_p95"
)
$rows = @()
foreach ($n in 30,40,45,50,60) {
    $log = "$base\1-6-12-$n-0.89-large-large_7200_300-hadgs-whcan-p-1\output.log"
    if (-not (Test-Path $log)) { Write-Output "MISSING: $log"; continue }
    $row = [ordered]@{n_bots = $n}
    foreach ($k in $keys) {
        $line = Select-String -Path $log -Pattern "^$([regex]::Escape($k)):\s*(.+)$" | Select-Object -First 1
        if ($line) { $row[$k] = $line.Matches[0].Groups[1].Value.Trim() } else { $row[$k] = "" }
    }
    # Derived: bot-seconds total, rest share, queue share
    $simTime = 7200.0
    $totalBotSec = $simTime * $n
    if ($row.StatTimeRestSec) {
        $row["RestShare"] = "{0:N4}" -f ([double]$row.StatTimeRestSec / $totalBotSec)
    }
    if ($row.StatQueueingAtStationTimeSec) {
        $row["QueueShare"] = "{0:N4}" -f ([double]$row.StatQueueingAtStationTimeSec / $totalBotSec)
    }
    $rows += [pscustomobject]$row
}
$rows | Format-Table -AutoSize
$rows | Export-Csv "$base\sweep_summary.csv" -NoTypeInformation
Write-Output "Saved $base\sweep_summary.csv"
