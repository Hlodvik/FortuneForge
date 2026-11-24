param([Parameter(Mandatory = $true)][string]$ReportPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable
if (!$report.options.pairedStakes -or $report.options.wagerPolicy -ne 'fixed') {
    throw 'This check requires a native fixed-wager report with --paired-stakes=true.'
}

function Assert-Same($Actual, $Expected, [string]$Label) {
    if ($Actual -ne $Expected) { throw "$Label differs: $Actual versus $Expected." }
}
function Assert-Scaled($Actual, $Expected, [long]$Stake, [long]$ReferenceStake, [string]$Label) {
    if ([decimal]$Actual * $ReferenceStake -ne [decimal]$Expected * $Stake) {
        throw "$Label is not proportional at stake $Stake."
    }
}

$expectedStakes = @()
for ($wager = [long]$report.wagering.minimumWagerPoints;
    $wager -le [long]$report.wagering.maximumWagerPoints;
    $wager += [long]$report.wagering.wagerIncrementPoints) { $expectedStakes += $wager }
$profiles = @($report.profiles | Sort-Object { [long]$_.stakePoints })
Assert-Same $profiles.Count $expectedStakes.Count 'Full legal wager-grid coverage'
$reference = $profiles[0]
$referenceStake = [long]$reference.stakePoints
$financialTotals = @('paidWagerPoints', 'paidWagerCents', 'paidReelBasePayoutPoints',
    'paidReelPayoutPoints', 'paidReelPayoutCents', 'paidEnergyExtraPoints',
    'earnedReelBasePayoutPoints', 'earnedReelPayoutPoints', 'earnedReelPayoutCents',
    'earnedEnergyExtraPoints', 'paidPityTriggeredWagerPoints', 'paidEnergyMultiplierWagerPoints')
$financialTomb = @('basePoints', 'roomPoints', 'relicExtraPoints', 'finalePoints',
    'payoutPoints', 'payoutCents', 'fundedWagerPoints')

for ($index = 0; $index -lt $profiles.Count; $index++) {
    $profile = $profiles[$index]
    $stake = [long]$profile.stakePoints
    Assert-Same $stake $expectedStakes[$index] 'Ordered legal wager grid'
    Assert-Same $profile.wagerPolicy 'fixed' "Wager policy at $stake"
    foreach ($name in $reference.totals.Keys) {
        if (!$profile.totals.ContainsKey($name)) { throw "Missing total $name at $stake." }
        if ($name -eq 'minimumBetPaidSpins') {
            $expectedMinimumSpins = if ($stake -eq $report.wagering.minimumWagerPoints) { $profile.totals.paidSpins } else { 0 }
            Assert-Same $profile.totals[$name] $expectedMinimumSpins "$name at $stake"
        } elseif ($name -in $financialTotals) {
            Assert-Scaled $profile.totals[$name] $reference.totals[$name] $stake $referenceStake $name
        } else {
            Assert-Same $profile.totals[$name] $reference.totals[$name] "$name at $stake"
        }
    }
    Assert-Same (($profile.strategies.Keys | Sort-Object) -join ',') (($reference.strategies.Keys | Sort-Object) -join ',') 'Strategy coverage'
    foreach ($strategy in $reference.strategies.Keys) {
        $actual = $profile.strategies[$strategy]
        $expected = $reference.strategies[$strategy]
        foreach ($name in $expected.tomb.Keys) {
            if (!$actual.tomb.ContainsKey($name)) { throw "Missing tomb total $name at $stake." }
            if ($name -in $financialTomb) {
                Assert-Scaled $actual.tomb[$name] $expected.tomb[$name] $stake $referenceStake "$strategy tomb $name"
            } else {
                Assert-Same $actual.tomb[$name] $expected.tomb[$name] "$strategy tomb $name at $stake"
            }
        }
        foreach ($name in @('observedRtp', 'paidReelRtp', 'earnedReelRtp', 'tombRtp')) {
            if ([Math]::Abs([double]$actual[$name] - [double]$expected[$name]) -gt 0.000000000001) {
                throw "$strategy $name differs at stake $stake."
            }
        }
    }
    $actualResidual = $profile.residualResources
    $expectedResidual = $reference.residualResources
    foreach ($name in @('energy', 'pity', 'nativeRemaining', 'legacyRemaining', 'specialPoints', 'unredeemedTickets')) {
        Assert-Same $actualResidual[$name] $expectedResidual[$name] "Residual $name at $stake"
    }
    if ($actualResidual.carry -ne $null) { throw "Undrained carry at stake $stake." }
    foreach ($track in $expectedResidual.sealCounts.Keys) {
        Assert-Same $actualResidual.sealCounts[$track] $expectedResidual.sealCounts[$track] "$track seals at $stake"
        Assert-Scaled $actualResidual.sealWagerPoints[$track] $expectedResidual.sealWagerPoints[$track] $stake $referenceStake "$track funding"
    }
    Assert-Scaled $actualResidual.sigilWagerPointTotal $expectedResidual.sigilWagerPointTotal $stake $referenceStake 'Sigil funding'
    foreach ($name in @('sigils', 'requiredSigils', 'minimumScarabs')) {
        Assert-Same $actualResidual.tombFunding[$name] $expectedResidual.tombFunding[$name] "Tomb funding $name at $stake"
    }
    Assert-Scaled $actualResidual.tombFunding.averageWagerPoints $expectedResidual.tombFunding.averageWagerPoints $stake $referenceStake 'Partial ticket funded average'
}

[pscustomobject]@{
    passed = $true
    engineVersion = $report.engineVersion
    legalStakes = $profiles.Count
    paidCyclesPerStake = $reference.totals.paidSpins
    totalPaidCycles = $profiles.Count * $reference.totals.paidSpins
    seed = $report.options.seed
    pairedStakes = $true
    scope = 'Exact scaled financial totals, outcome/progression counters, tomb strategies and terminal resources; no new game requests.'
} | ConvertTo-Json
