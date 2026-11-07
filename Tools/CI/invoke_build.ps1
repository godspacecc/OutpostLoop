[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Config,
    [Parameter(Mandatory = $true)][string]$ResultPath,
    [ValidateRange(1, 1440)][int]$TimeoutMinutes = 120,
    [switch]$ValidateInputOnly
)
$ErrorActionPreference = 'Stop'
$request = $null
$gate = $null
$process = $null
$snapshotLease = $null
$launched = $false
$completed = $false
$canWriteFailure = $false
$exitCode = 1
$stage = 'LauncherInputValidation'
$startedUtc = [DateTime]::UtcNow.ToString('O')

function Get-AbsolutePath([string]$Value, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Value) -or -not [IO.Path]::IsPathRooted($Value) -or $Value.Contains('"') -or $Value.Contains("`n") -or $Value.Contains("`r")) {
        throw "$Name must be an explicit absolute path."
    }
    $absolute = [IO.Path]::GetFullPath($Value)
    if ($absolute.Length -gt [IO.Path]::GetPathRoot($absolute).Length) { $absolute = $absolute.TrimEnd([char[]]'\/') }
    return $absolute
}
function Test-Within([string]$Path, [string]$Root) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd([char[]]'\/')
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    return $resolvedPath.Equals($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoLinks([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($null -ne $current) {
        if (Test-Path -LiteralPath $current) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'CI paths cannot use reparse points.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
function Quote-Argument([string]$Value) {
    if ($Value.Contains('"') -or $Value.EndsWith('\')) { throw 'Unsupported quoted or trailing-backslash process argument.' }
    return '"' + $Value + '"'
}
function Write-NewJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 12))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}

try {
    $ResultPath = Get-AbsolutePath $ResultPath 'ResultPath'
    $Config = Get-AbsolutePath $Config 'Config'
    Assert-NoLinks $ResultPath
    Assert-NoLinks $Config
    if ([IO.Path]::GetExtension($ResultPath) -ne '.json' -or $ResultPath -eq $Config -or (Test-Path -LiteralPath $ResultPath)) { throw 'Use a new JSON result path, separate from the request.' }
    $scriptCheckout = [IO.Directory]::GetParent([IO.Directory]::GetParent($PSScriptRoot).FullName).FullName
    if ((Test-Within $ResultPath (Join-Path $scriptCheckout 'Client')) -or (Test-Within $ResultPath (Join-Path $scriptCheckout '.git'))) { throw 'ResultPath must be outside Unity and Git metadata.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ResultPath)) | Out-Null
    $canWriteFailure = $true
    $requestBytes = [IO.File]::ReadAllBytes($Config)
    $request = ([Text.Encoding]::UTF8.GetString($requestBytes)).TrimStart([char]0xfeff) | ConvertFrom-Json
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $configHash = [BitConverter]::ToString($hash.ComputeHash($requestBytes)).Replace('-', '').ToLowerInvariant() } finally { $hash.Dispose() }
    $client = Get-AbsolutePath $request.ProjectPath 'ProjectPath'
    if (-not $client.Equals((Join-Path $scriptCheckout 'Client'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Use the launcher belonging to the selected checkout.' }
    Assert-NoLinks $client
    $checkout = [IO.Directory]::GetParent($client).FullName
    if ((Test-Within $ResultPath $client) -or (Test-Within $ResultPath (Join-Path $checkout '.git'))) { throw 'ResultPath must be outside the Unity project and Git metadata.' }
    $dataRoot = Get-AbsolutePath $request.PersistentDataRoot 'PersistentDataRoot'
    if ((Test-Within $dataRoot $checkout) -or (Test-Within $checkout $dataRoot) -or (Test-Within $ResultPath $dataRoot)) { throw 'Persistent data, checkout and result paths must not overlap.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ResultPath)) | Out-Null
    $canWriteFailure = $true
    if ($request.SchemaVersion -ne 1 -or $request.ProjectId -ne 'com.outpostloop.game' -or $request.RequestId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,100}$') { throw 'Invalid schema, project or request ID.' }
    if ($request.Platform -notin @('PC', 'Android') -or $request.BuildType -notin @('FirstPackage', 'HostUpdate')) { throw 'Unsupported platform or build type.' }
    if ($request.ExpectedCommit -notmatch '^[a-fA-F0-9]{40}([a-fA-F0-9]{24})?$') { throw 'ExpectedCommit must be a full fixed commit.' }
    if (-not (Test-Path -LiteralPath (Join-Path $client 'ProjectSettings\ProjectVersion.txt') -PathType Leaf)) { throw 'Unity project is missing ProjectVersion.txt.' }
    $unity = Get-AbsolutePath $request.UnityEditor 'UnityEditor'
    if ([IO.Path]::GetFileName($unity) -ne 'Unity.exe' -or [IO.Path]::GetFileName($request.GitExecutable) -ne 'git.exe' -or
        [IO.Path]::GetFileName($request.PowerShell) -notin @('powershell.exe', 'pwsh.exe')) { throw 'Unexpected executable names in tool configuration.' }
    foreach ($tool in @($unity, $request.GitExecutable, $request.PowerShell)) {
        $toolPath = Get-AbsolutePath $tool 'Tool'
        Assert-NoLinks $toolPath
        if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) { throw "Missing tool: $toolPath" }
    }
    if ($request.BuildType -eq 'HostUpdate' -and $request.BaselineId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,100}$') { throw 'HostUpdate requires an explicit baseline ID.' }
    if ($request.Platform -eq 'PC' -and $request.BuildType -eq 'FirstPackage') {
        $compiler = Get-AbsolutePath $request.InnoCompiler 'InnoCompiler'
        if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Missing Inno Setup compiler.' }
    }
    foreach ($marker in @('.outpostloop-ci-active.json', '.outpostloop-ci-needs-recovery.json')) {
        if (Test-Path -LiteralPath (Join-Path $client $marker)) { throw 'Workspace has an interrupted-run recovery marker.' }
    }
    $stage = 'LauncherLock'
    $gatePath = Join-Path $client '.outpostloop-ci.lock'
    Assert-NoLinks $gatePath
    $gate = [IO.FileStream]::new($gatePath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if ($ValidateInputOnly) {
        Write-NewJson $ResultPath ([ordered]@{ SchemaVersion=1; ProjectId='com.outpostloop.game'; RequestId=$request.RequestId; Status='LauncherValidated'; Stage='LauncherInputValidation'; ExitCode=0; BuildStatus='NotStarted'; PublishStatus='NotRequested'; StartedUtc=$startedUtc; CompletedUtc=[DateTime]::UtcNow.ToString('O') })
        $exitCode = 0
    } else {
        $stage = 'UnityStartup'
        $outputFolder = [IO.Path]::GetDirectoryName($ResultPath)
        $runName = [IO.Path]::GetFileNameWithoutExtension($ResultPath)
        foreach ($suffix in @('.unity.log', '.stdout.log', '.stderr.log')) {
            $logFile = Join-Path $outputFolder ($runName + $suffix)
            Assert-NoLinks $logFile
            if (Test-Path -LiteralPath $logFile) { throw 'Use a fresh result name; an associated log file already exists.' }
        }
        $snapshot = Join-Path $outputFolder ($runName + '.request.json')
        Assert-NoLinks $snapshot
        $snapshotWriter = [IO.FileStream]::new($snapshot, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $snapshotWriter.Write($requestBytes, 0, $requestBytes.Length); $snapshotWriter.Flush($true) } finally { $snapshotWriter.Dispose() }
        $snapshotLease = [IO.FileStream]::new($snapshot, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $snapshotHash = [Security.Cryptography.SHA256]::Create()
        try { $actualHash = [BitConverter]::ToString($snapshotHash.ComputeHash($snapshotLease)).Replace('-', '').ToLowerInvariant() }
        finally { $snapshotHash.Dispose(); $snapshotLease.Position = 0 }
        if ($actualHash -ne $configHash) { throw 'Request snapshot changed before Unity startup.' }
        $logPath = Join-Path $outputFolder ($runName + '.unity.log')
        $target = if ($request.Platform -eq 'PC') { 'Win64' } else { 'Android' }
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $unity
        $info.WorkingDirectory = $client
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.Arguments = '-batchmode -quit -projectPath ' + (Quote-Argument $client) + ' -buildTarget ' + $target + ' -executeMethod Game.Editor.CiBuildEntry.Run -ciConfig ' + (Quote-Argument $snapshot) + ' -ciResult ' + (Quote-Argument $ResultPath) + ' -logFile ' + (Quote-Argument $logPath)
        $info.EnvironmentVariables['OUTPOSTLOOP_BUILD_DATA_ROOT'] = $dataRoot
        $process = [Diagnostics.Process]::Start($info)
        $launched = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMinutes * 60000)) {
            $stage = 'UnityTimeout'
            $process.Kill() # Only the exact Unity process started by this invocation.
            $process.WaitForExit(10000) | Out-Null
            throw 'Unity timed out; inspect owned child processes and restore the workspace before retrying.'
        }
        [IO.File]::WriteAllText((Join-Path $outputFolder ($runName + '.stdout.log')), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $outputFolder ($runName + '.stderr.log')), $stderr.GetAwaiter().GetResult())
        $stage = 'ValidateUnityResult'
        if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) { throw "Unity exited $($process.ExitCode) without a result report; inspect unity.log." }
        $report = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $expectedStatus = if ($request.ValidateOnly) { 'Validated' } else { 'Succeeded' }
        $completed = ($report.RequestId -eq $request.RequestId -and $report.Status -in @('Succeeded', 'Failed', 'Validated')) -or
            ($report.Status -eq 'Failed' -and $report.BuildStatus -eq 'NotStarted' -and $report.CleanupStatus -eq 'NotNeeded')
        if ($process.ExitCode -ne 0 -or $report.ExitCode -ne 0 -or $report.RequestId -ne $request.RequestId -or $report.Status -ne $expectedStatus -or
            $report.ConfigSha256 -ne $configHash -or
            ((-not $request.ValidateOnly) -and $report.BuildStatus -ne 'Succeeded') -or ($request.Publish -and $report.PublishStatus -ne 'Succeeded')) {
            throw 'Unity process exit code or result report does not confirm this request succeeded.'
        }
        $exitCode = 0
    }
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    if ($launched -and -not $completed) {
        $marker = Join-Path $client '.outpostloop-ci-needs-recovery.json'
        if (-not (Test-Path -LiteralPath $marker)) { Write-NewJson $marker ([ordered]@{ RequestId=$request.RequestId; Stage=$stage; Utc=[DateTime]::UtcNow.ToString('O'); Error=$_.Exception.Message }) }
    }
    if ($canWriteFailure -and -not (Test-Path -LiteralPath $ResultPath)) {
        Write-NewJson $ResultPath ([ordered]@{ SchemaVersion=1; ProjectId='com.outpostloop.game'; RequestId=$request.RequestId; Status='Failed'; Stage=$stage; ExitCode=1; BuildStatus='NotStarted'; PublishStatus='NotRequested'; Error=$_.Exception.Message; StartedUtc=$startedUtc; CompletedUtc=[DateTime]::UtcNow.ToString('O') })
    }
} finally {
    if ($null -ne $process) { $process.Dispose() }
    if ($null -ne $snapshotLease) { $snapshotLease.Dispose() }
    if ($null -ne $gate) { $gate.Dispose() }
}
exit $exitCode
