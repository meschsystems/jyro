<#
.SYNOPSIS
    Benchmarks raw .jyro scripts vs compiled .jyrx binaries.

.DESCRIPTION
    Discovers all test triplets and runs each test twice: once with the raw
    .jyro source and once with the compiled .jyrx binary from tests/binaries/.
    Reports per-test and aggregate timing differences.

.PARAMETER BinaryA
    Path to the Jyro binary.  Overrides config.json.

.PARAMETER Config
    Path to configuration file.  Defaults to config.json in the script directory.

.PARAMETER Timeout
    Per-test timeout in seconds.  Overrides config.json.  Default: 30.

.PARAMETER Category
    Run only tests in the specified folder name (e.g. "for", "literals").

.PARAMETER Iterations
    Number of measured iterations per test.  Default: 10.

.PARAMETER Warmup
    Number of warmup iterations (discarded) before measured runs.  Default: 2.

.EXAMPLE
    .\Run-Benchmark.ps1

.EXAMPLE
    .\Run-Benchmark.ps1 -Category "for" -Iterations 10 -Warmup 3
#>
[CmdletBinding()]
param(
    [string]$BinaryA,
    [string]$Config,
    [int]$Timeout,
    [string]$Category,
    [int]$Iterations = 10,
    [int]$Warmup = 2
)

$ErrorActionPreference = "Stop"
$SuiteRoot = if ($PSScriptRoot) { $PSScriptRoot } else { $PWD.Path }
if (-not $Config) { $Config = Join-Path $SuiteRoot "config.json" }

# -- Configuration ----------------------------------------------------------------

$cfg = @{ BinaryA = ""; TimeoutSeconds = 30 }

if (Test-Path $Config) {
    $json = Get-Content $Config -Raw | ConvertFrom-Json
    if ($json.BinaryA)        { $cfg.BinaryA        = $json.BinaryA }
    if ($json.TimeoutSeconds) { $cfg.TimeoutSeconds  = $json.TimeoutSeconds }
}

if ($BinaryA) { $cfg.BinaryA = $BinaryA }
if ($PSBoundParameters.ContainsKey('Timeout')) { $cfg.TimeoutSeconds = $Timeout }

if (-not $cfg.BinaryA) {
    Write-Host "ERROR: No binary configured." -ForegroundColor Red
    Write-Host "Supply -BinaryA or populate config.json."
    exit 1
}

$binary = $cfg.BinaryA
if (-not [System.IO.Path]::IsPathRooted($binary)) {
    $binary = Join-Path $SuiteRoot $binary
}
if (-not (Test-Path $binary)) {
    Write-Host "ERROR: Binary not found: $binary" -ForegroundColor Red
    exit 1
}
$binary = (Resolve-Path $binary).Path

$timeoutMs     = $cfg.TimeoutSeconds * 1000
$binariesDir   = Join-Path $SuiteRoot "binaries"
$plaintextDir  = Join-Path $SuiteRoot "plaintext"

if (-not (Test-Path $plaintextDir)) {
    Write-Host "ERROR: Plaintext directory not found: $plaintextDir" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $binariesDir)) {
    Write-Host "ERROR: Binaries directory not found: $binariesDir" -ForegroundColor Red
    Write-Host "Run Compile-All.ps1 first to compile .jyro files to .jyrx."
    exit 1
}

# -- Test Discovery ----------------------------------------------------------------

$tests = @()

Get-ChildItem $plaintextDir -Recurse -Filter "*.jyro" | ForEach-Object {
    $folder   = $_.DirectoryName
    $baseName = $_.BaseName
    $group    = Split-Path $folder -Leaf

    # Category filter
    if ($Category -and $group -ne $Category) { return }

    $dataFile     = Join-Path $folder "D-$baseName.json"
    $outFile      = Join-Path $folder "O-$baseName.json"
    $exitCodeFile = Join-Path $folder "E-$baseName.txt"

    if (-not (Test-Path $outFile)) {
        Write-Warning "Skipping $group/$baseName - missing O-$baseName.json"
        return
    }

    # Find corresponding .jyrx in binaries/
    $rel = $_.DirectoryName.Substring($plaintextDir.Length).TrimStart('\/')

    $jyrxFile = Join-Path $binariesDir (Join-Path $rel ($baseName + ".jyrx"))
    if (-not (Test-Path $jyrxFile)) {
        Write-Warning "Skipping $group/$baseName - missing compiled $jyrxFile"
        return
    }

    $hasData = Test-Path $dataFile
    $expectedExitCode = 0
    if (Test-Path $exitCodeFile) {
        $expectedExitCode = [int](Get-Content $exitCodeFile -Raw).Trim()
    }

    $tests += [PSCustomObject]@{
        Group            = $group
        Name             = $baseName
        Script           = $_.FullName
        Compiled         = $jyrxFile
        Data             = if ($hasData) { $dataFile } else { $null }
        Expected         = $outFile
        ExpectedExitCode = $expectedExitCode
    }
}

$tests = @($tests | Sort-Object Group, Name)

if ($tests.Count -eq 0) {
    Write-Host "No test triplets found under $SuiteRoot" -ForegroundColor Red
    if ($Category) { Write-Host "  (filtered by category: $Category)" }
    exit 1
}

# -- Helper: Run a single test and return elapsed ms ---------------------------------

function Invoke-TestRun {
    param(
        [string]$Binary,
        [string]$InputFile,
        [string]$ExpectedFile,
        [string]$DataFile,
        [int]$TimeoutMs,
        [int]$ExpectedExitCode
    )

    $procArgs = "test -i `"$InputFile`" -o `"$ExpectedFile`""
    if ($DataFile) {
        $procArgs += " -d `"$DataFile`""
    }

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Binary
    $psi.Arguments = $procArgs
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()

    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $exited = $proc.WaitForExit($TimeoutMs)
    $exitCode = if ($exited) { $proc.ExitCode } else { -1 }
    $proc.Dispose()

    $sw.Stop()

    $passed = $false
    if (-not $exited) {
        # timeout
    } elseif ($exitCode -eq $ExpectedExitCode) {
        $passed = $true
    }

    return @{
        ElapsedMs = $sw.ElapsedMilliseconds
        Passed    = $passed
        ExitCode  = $exitCode
    }
}

# -- Helpers -----------------------------------------------------------------------

function Get-Median {
    param([double[]]$Values)
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 0) { return 0 }
    if ($n % 2 -eq 1) {
        return $sorted[([math]::Floor($n / 2))]
    } else {
        return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2.0
    }
}

# -- Execution ---------------------------------------------------------------------

$runTimestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$logDir       = Join-Path $SuiteRoot "logs"

$results      = @()
$currentGroup = ""

Write-Host ""
Write-Host "  Jyro Benchmark: Source vs Compiled" -ForegroundColor Cyan
Write-Host ("  " + "=" * 58) -ForegroundColor Cyan
Write-Host "  Binary:     $binary"
Write-Host "  Tests:      $($tests.Count)"
Write-Host "  Iterations: $Iterations (+ $Warmup warmup)"
Write-Host "  Statistic:  median"
Write-Host "  Timeout:    $($cfg.TimeoutSeconds)s"
if ($Category) { Write-Host "  Category:   $Category" }
Write-Host ""
Write-Host ("    " + "Test".PadRight(36) + "  .jyro    .jyrx    Diff     Speedup") -ForegroundColor DarkGray
Write-Host ("    " + "".PadRight(36) + " (median) (median)") -ForegroundColor DarkGray
Write-Host ("    " + ("-" * 72)) -ForegroundColor DarkGray

foreach ($test in $tests) {
    if ($test.Group -ne $currentGroup) {
        $currentGroup = $test.Group
        Write-Host ""
        Write-Host "  $currentGroup" -ForegroundColor Yellow
    }

    Write-Host -NoNewline ("    " + $test.Name.PadRight(36))

    # Warmup runs (discarded) - alternate src/cmp to warm both paths equally
    for ($i = 0; $i -lt $Warmup; $i++) {
        Invoke-TestRun -Binary $binary -InputFile $test.Script -ExpectedFile $test.Expected `
            -DataFile $test.Data -TimeoutMs $timeoutMs -ExpectedExitCode $test.ExpectedExitCode | Out-Null
        Invoke-TestRun -Binary $binary -InputFile $test.Compiled -ExpectedFile $test.Expected `
            -DataFile $test.Data -TimeoutMs $timeoutMs -ExpectedExitCode $test.ExpectedExitCode | Out-Null
    }

    # Run .jyro source N times
    $srcTimes = @()
    $srcPassed = $true
    for ($i = 0; $i -lt $Iterations; $i++) {
        $r = Invoke-TestRun -Binary $binary -InputFile $test.Script -ExpectedFile $test.Expected `
             -DataFile $test.Data -TimeoutMs $timeoutMs -ExpectedExitCode $test.ExpectedExitCode
        $srcTimes += $r.ElapsedMs
        if (-not $r.Passed) { $srcPassed = $false }
    }

    # Run .jyrx compiled N times
    $cmpTimes = @()
    $cmpPassed = $true
    for ($i = 0; $i -lt $Iterations; $i++) {
        $r = Invoke-TestRun -Binary $binary -InputFile $test.Compiled -ExpectedFile $test.Expected `
             -DataFile $test.Data -TimeoutMs $timeoutMs -ExpectedExitCode $test.ExpectedExitCode
        $cmpTimes += $r.ElapsedMs
        if (-not $r.Passed) { $cmpPassed = $false }
    }

    # Calculate medians
    $srcMedian = [math]::Round((Get-Median $srcTimes), 1)
    $cmpMedian = [math]::Round((Get-Median $cmpTimes), 1)
    $diff      = [math]::Round($srcMedian - $cmpMedian, 1)

    # Speedup ratio
    $speedup = if ($cmpMedian -gt 0) { [math]::Round($srcMedian / $cmpMedian, 2) } else { 0 }

    # Format output
    $srcStr = "${srcMedian}ms".PadLeft(8)
    $cmpStr = "${cmpMedian}ms".PadLeft(8)
    $diffSign = if ($diff -ge 0) { "+" } else { "" }
    $diffStr = "${diffSign}${diff}ms".PadLeft(8)
    $speedupStr = "${speedup}x".PadLeft(8)

    $srcColor = if ($srcPassed) { "White" } else { "Red" }
    $cmpColor = if ($cmpPassed) { "White" } else { "Red" }
    $diffColor = if ($diff -gt 0) { "Green" } elseif ($diff -lt 0) { "Red" } else { "Gray" }

    Write-Host -NoNewline $srcStr -ForegroundColor $srcColor
    Write-Host -NoNewline $cmpStr -ForegroundColor $cmpColor
    Write-Host -NoNewline $diffStr -ForegroundColor $diffColor
    Write-Host $speedupStr -ForegroundColor $(if ($speedup -ge 1) { "Green" } else { "Red" })

    $results += [PSCustomObject]@{
        Group        = $test.Group
        Name         = $test.Name
        SrcMedianMs  = $srcMedian
        CmpMedianMs  = $cmpMedian
        DiffMs       = $diff
        Speedup      = $speedup
        SrcPassed    = $srcPassed
        CmpPassed    = $cmpPassed
        SrcTimes     = $srcTimes
        CmpTimes     = $cmpTimes
    }
}

# -- Summary -----------------------------------------------------------------------

Write-Host ""
Write-Host ("  " + "=" * 58) -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan

$totalSrcMs  = [math]::Round(($results | ForEach-Object { $_.SrcMedianMs } | Measure-Object -Sum).Sum, 1)
$totalCmpMs  = [math]::Round(($results | ForEach-Object { $_.CmpMedianMs } | Measure-Object -Sum).Sum, 1)
$totalDiff   = [math]::Round($totalSrcMs - $totalCmpMs, 1)
$avgSpeedup  = if ($totalCmpMs -gt 0) { [math]::Round($totalSrcMs / $totalCmpMs, 2) } else { 0 }

Write-Host "  Total .jyro median: ${totalSrcMs}ms"
Write-Host "  Total .jyrx median: ${totalCmpMs}ms"

$diffSign = if ($totalDiff -ge 0) { "+" } else { "" }
$diffColor = if ($totalDiff -gt 0) { "Green" } elseif ($totalDiff -lt 0) { "Red" } else { "Gray" }
Write-Host "  Time saved:       ${diffSign}${totalDiff}ms" -ForegroundColor $diffColor
Write-Host "  Overall speedup:  ${avgSpeedup}x" -ForegroundColor $(if ($avgSpeedup -ge 1) { "Green" } else { "Red" })

# Per-group breakdown
Write-Host ""
Write-Host "  Per-category:" -ForegroundColor DarkGray
$groups = $results | Group-Object -Property Group
foreach ($g in $groups) {
    $gSrc = [math]::Round(($g.Group | ForEach-Object { $_.SrcMedianMs } | Measure-Object -Sum).Sum, 1)
    $gCmp = [math]::Round(($g.Group | ForEach-Object { $_.CmpMedianMs } | Measure-Object -Sum).Sum, 1)
    $gSpd = if ($gCmp -gt 0) { [math]::Round($gSrc / $gCmp, 2) } else { 0 }
    $gColor = if ($gSpd -ge 1) { "Green" } else { "Red" }
    Write-Host ("    " + $g.Name.PadRight(24) + "${gSrc}ms -> ${gCmp}ms  (${gSpd}x)") -ForegroundColor $gColor
}

# Top 5 biggest speedups
$sorted = @($results | Sort-Object Speedup -Descending)
if ($sorted.Count -gt 0) {
    Write-Host ""
    Write-Host "  Biggest speedups:" -ForegroundColor DarkGray
    $top = @($sorted | Select-Object -First 5)
    foreach ($t in $top) {
        Write-Host ("    " + "$($t.Group)/$($t.Name)".PadRight(44) + "$($t.Speedup)x") -ForegroundColor Green
    }
}

# Any regressions (compiled slower)
$regressions = @($results | Where-Object { $_.Speedup -lt 1 })
if ($regressions.Count -gt 0) {
    Write-Host ""
    Write-Host "  Regressions (compiled slower):" -ForegroundColor Red
    foreach ($t in $regressions) {
        Write-Host ("    " + "$($t.Group)/$($t.Name)".PadRight(44) + "$($t.Speedup)x") -ForegroundColor Red
    }
}

Write-Host ("  " + "=" * 58) -ForegroundColor Cyan

# -- Log ---------------------------------------------------------------------------

if (-not (Test-Path $logDir)) { New-Item $logDir -ItemType Directory -Force | Out-Null }

$logEntries = @($results | ForEach-Object {
    [ordered]@{
        group       = $_.Group
        name        = $_.Name
        srcMedianMs = $_.SrcMedianMs
        cmpMedianMs = $_.CmpMedianMs
        diffMs      = $_.DiffMs
        speedup     = $_.Speedup
        srcTimes    = $_.SrcTimes
        cmpTimes    = $_.CmpTimes
    }
})

$logGroupSummary = @($groups | ForEach-Object {
    $gSrc = [math]::Round(($_.Group | ForEach-Object { $_.SrcMedianMs } | Measure-Object -Sum).Sum, 1)
    $gCmp = [math]::Round(($_.Group | ForEach-Object { $_.CmpMedianMs } | Measure-Object -Sum).Sum, 1)
    [ordered]@{
        category = $_.Name
        srcMs    = $gSrc
        cmpMs    = $gCmp
        speedup  = if ($gCmp -gt 0) { [math]::Round($gSrc / $gCmp, 2) } else { 0 }
    }
})

$log = [PSCustomObject][ordered]@{
    timestamp      = (Get-Date -Format "o")
    binary         = $binary
    iterations     = $Iterations
    warmup         = $Warmup
    statistic      = "median"
    timeoutSeconds = $cfg.TimeoutSeconds
    testCount      = $results.Count
    totalSrcMs     = $totalSrcMs
    totalCmpMs     = $totalCmpMs
    totalDiffMs    = $totalDiff
    overallSpeedup = $avgSpeedup
    categories     = $logGroupSummary
    tests          = $logEntries
}

$logFile = Join-Path $logDir "benchmark-$runTimestamp.json"
$log | ConvertTo-Json -Depth 100 | Set-Content $logFile -Encoding UTF8

Write-Host "  Log: $logFile" -ForegroundColor DarkGray
Write-Host ""
