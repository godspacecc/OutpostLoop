param([string]$Compiler = '')
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OutpostLoopInstallerTests_' + [Guid]::NewGuid().ToString('N'))
function Check([bool]$value, [string]$message) { if (-not $value) { throw $message } }
try {
    $player = Join-Path $testRoot 'Player'
    $data = Join-Path $player 'OutpostLoop_Data'
    [IO.Directory]::CreateDirectory((Join-Path $data 'StreamingAssets')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $data 'yoo')) | Out-Null
    foreach ($name in @('OutpostLoop.exe','UnityPlayer.dll','GameAssembly.dll','secret.txt')) {
        Set-Content -LiteralPath (Join-Path $player $name) -Value 'fixture'
    }
    Set-Content -LiteralPath (Join-Path $data 'yoo/cache.bundle') -Value 'old cache'
    Set-Content -LiteralPath (Join-Path $data 'ScriptingAssemblies.json') -Value '{"names":["Boot.dll","Game.dll"],"types":[16,16]}'
    $identityPath = Join-Path $data 'StreamingAssets/windows-release.json'
    Set-Content -LiteralPath $identityPath -Value '{"schemaVersion":1,"productId":"com.outpostloop.game","platform":"Windows","architecture":"x64","versionName":"0.1.0","releaseNumber":3}'
    $output = Join-Path $testRoot 'Output'
    & (Join-Path $PSScriptRoot 'build_installer.ps1') -PlayerDirectory $player -Compiler 'unused' -OutputDirectory $output -PrepareOnly
    Check (Test-Path -LiteralPath (Join-Path $output 'payload/GameAssembly.dll')) 'Missing runtime DLL'
    Check (-not (Test-Path -LiteralPath (Join-Path $output 'payload/OutpostLoop_Data/yoo'))) 'Included stale host cache'
    Check (-not (Test-Path -LiteralPath (Join-Path $output 'payload/secret.txt'))) 'Included unmanaged user file'
    $list = Get-Content -LiteralPath (Join-Path $output 'payload/windows-managed-files.txt')
    Check ($list -contains 'windows-managed-files.txt' -and $list -contains 'windows-install.ini') 'Recovery list omitted identity files'
    Check ((Get-Content -Raw -LiteralPath (Join-Path $output 'payload/windows-install.ini')) -match 'Number=3') 'Installer release changed'
    if ($Compiler) {
        $payloadBytes = (Get-ChildItem -LiteralPath (Join-Path $output 'payload') -File -Recurse | Measure-Object Length -Sum).Sum
        & $Compiler '/Q' "/DPayloadBytes=$([long]$payloadBytes)" '/DRelease=3' '/DVersion=0.1.0' '/DOutputName=Fixture-Setup-3' (Join-Path $output 'OutpostLoop.iss')
        Check ($LASTEXITCODE -eq 0) 'Installer template did not compile'
        Check (Test-Path -LiteralPath (Join-Path $output 'Fixture-Setup-3.exe')) 'Compiled installer missing'
        Write-Output 'Installer template compiled; fixture installer was not executed.'
    }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'build_installer.ps1') -PlayerDirectory $player -Compiler 'unused' -OutputDirectory (Join-Path $player 'BadOutput') -PrepareOnly } catch { $rejected = $true }
    Check $rejected 'Output inside source was accepted'
    Remove-Item -LiteralPath $identityPath
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'build_installer.ps1') -PlayerDirectory $player -Compiler 'unused' -OutputDirectory (Join-Path $testRoot 'NoIdentity') -PrepareOnly } catch { $rejected = $true }
    Check $rejected 'Old portable Player without PC identity was accepted'
    Write-Output 'Installer input checks passed; no Unity build or game installation executed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('OutpostLoopInstallerTests_')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
