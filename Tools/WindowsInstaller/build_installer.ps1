param(
    [Parameter(Mandatory=$true)][string]$PlayerDirectory,
    [Parameter(Mandatory=$true)][string]$Compiler,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
# 只消费已有正式 Player，不调用 Unity；PrepareOnly 供输入校验测试使用。
$player = [IO.Path]::GetFullPath($PlayerDirectory).TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($output -eq $player -or $output.StartsWith($player + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside Player.' }
if (Test-Path -LiteralPath $output) { throw 'Output directory already exists; choose a new directory.' }
foreach ($file in @('OutpostLoop.exe','UnityPlayer.dll','GameAssembly.dll','OutpostLoop_Data/StreamingAssets/windows-release.json','OutpostLoop_Data/ScriptingAssemblies.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $player $file) -PathType Leaf)) { throw "Missing Player file: $file" }
}
$identity = Get-Content -Raw -LiteralPath (Join-Path $player 'OutpostLoop_Data/StreamingAssets/windows-release.json') | ConvertFrom-Json
if ($identity.schemaVersion -ne 1 -or $identity.productId -cne 'com.outpostloop.game' -or $identity.platform -cne 'Windows' -or
    $identity.architecture -cne 'x64' -or $identity.releaseNumber -lt 1 -or $identity.releaseNumber -gt 2147483647 -or
    $identity.versionName -cnotmatch '^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$') { throw 'Invalid embedded PC release identity; rebuild a new Player.' }
$assemblies = Get-Content -Raw -LiteralPath (Join-Path $player 'OutpostLoop_Data/ScriptingAssemblies.json') | ConvertFrom-Json
$gameIndices = @(0..($assemblies.names.Count - 1) | Where-Object { $assemblies.names[$_] -ceq 'Game.dll' })
if ($gameIndices.Count -ne 1 -or $assemblies.types.Count -ne $assemblies.names.Count -or $assemblies.types[$gameIndices[0]] -ne 16) { throw 'Player is missing valid Game.dll registration.' }
# 不跟随符号链接，不把运行缓存、调试资料或用户额外文件打入安装器。
$all = @(Get-ChildItem -LiteralPath $player -Recurse -Force)
if ((Get-Item -LiteralPath $player).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    @($all | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw 'Player contains reparse points.' }
$files = @($all | Where-Object { -not $_.PSIsContainer } | Where-Object {
    $relative = $_.FullName.Substring($player.Length + 1)
    ($relative -eq 'OutpostLoop.exe' -or $relative -match '^[A-Za-z0-9._-]+\.dll$' -or $relative.StartsWith('OutpostLoop_Data\')) -and
    -not $relative.StartsWith('OutpostLoop_Data\yoo\') -and
    -not ($relative -match '(?i)(DoNotShip|DontShip|\.pdb$|\.mdb$)')
})
[IO.Directory]::CreateDirectory($output) | Out-Null
$payload = Join-Path $output 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
$managed = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $relative = $file.FullName.Substring($player.Length + 1)
    if ($relative.Contains('"') -or $relative.Contains(';') -or $relative.Contains('|') -or $relative.Contains("`n")) { throw 'Unsupported payload filename.' }
    $target = Join-Path $payload $relative
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $managed.Add($relative)
}
$managed.Add('windows-managed-files.txt')
$managed.Add('windows-payload-sha256.txt')
$managed.Add('windows-install.ini')
[IO.File]::WriteAllText((Join-Path $payload 'windows-install.ini'), "[Release]`r`nNumber=$($identity.releaseNumber)`r`n", [Text.Encoding]::ASCII)
# Pascal LoadStringsFromFile 读取 UTF-8 BOM，保留中文资源路径。
[IO.File]::WriteAllLines((Join-Path $payload 'windows-managed-files.txt'), $managed, [Text.UTF8Encoding]::new($true))
$checksums = @($managed | Where-Object { $_ -ne 'windows-payload-sha256.txt' } | ForEach-Object {
    (Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '|' + $_
})
[IO.File]::WriteAllLines((Join-Path $payload 'windows-payload-sha256.txt'), $checksums, [Text.UTF8Encoding]::new($true))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OutpostLoop.iss') -Destination (Join-Path $output 'OutpostLoop.iss')
$name = "OutpostLoop-Setup-$($identity.releaseNumber)"
if ($PrepareOnly) { Write-Output "Validated installer input: $output"; return }
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) { throw 'Configure the full path of Inno Setup 6 ISCC.exe.' }
$payloadBytes = (Get-ChildItem -LiteralPath $payload -File -Recurse | Measure-Object -Property Length -Sum).Sum
& $Compiler "/DPayloadBytes=$([long]$payloadBytes)" "/DRelease=$($identity.releaseNumber)" "/DVersion=$($identity.versionName)" "/DOutputName=$name" (Join-Path $output 'OutpostLoop.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
$installer = Join-Path $output "$name.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Compiler did not produce installer.' }
$manifest = [ordered]@{ schemaVersion=1; productId='com.outpostloop.game'; platform='Windows'; architecture='x64';
    versionName=$identity.versionName; releaseNumber=[int]$identity.releaseNumber; installerFileName="$name.exe";
    sizeBytes=(Get-Item -LiteralPath $installer).Length; sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() }
[IO.File]::WriteAllText((Join-Path $output 'app-update-windows.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Installer ready: $installer"
