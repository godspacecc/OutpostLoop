param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$Manifest,
    [string]$Bundle = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
# 只发布已有安装器；同一发布序号不能换字节，清单切换必须在复制校验完成之后。
$source = [IO.Path]::GetFullPath($Installer)
$bundlePath = [IO.Path]::GetFullPath($Bundle)
if (-not (Test-Path -LiteralPath (Join-Path $bundlePath 'ResourceFiles/OutpostLoop.version') -PathType Leaf)) { throw 'Invalid resource server directory.' }
if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or [IO.Path]::GetExtension($source) -ine '.exe') { throw 'Installer file missing.' }
if ((Get-Item -LiteralPath $Manifest).Length -gt 65536) { throw 'Manifest too large.' }
$m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
if ($m.schemaVersion -ne 1 -or $m.productId -cne 'com.outpostloop.game' -or $m.platform -cne 'Windows' -or $m.architecture -cne 'x64' -or
    $m.releaseNumber -lt 1 -or $m.releaseNumber -gt 2147483647 -or $m.versionName -cnotmatch '^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$' -or
    $m.installerFileName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.exe$' -or $m.sha256 -cnotmatch '^[0-9a-fA-F]{64}$' -or
    $m.sizeBytes -le 0 -or $m.sizeBytes -gt 32GB) { throw 'Invalid Windows update manifest.' }
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $m.sha256 -or (Get-Item -LiteralPath $source).Length -ne $m.sizeBytes -or
    (Split-Path $source -Leaf) -cne $m.installerFileName) { throw 'Installer does not match manifest.' }
$folder = Join-Path $bundlePath 'AppUpdates/Windows'
[IO.Directory]::CreateDirectory($folder) | Out-Null
# 文件共享锁只用于串行发布，不影响服务器读取已发布文件。
$lock = [IO.File]::Open((Join-Path $folder '.publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$pending = Join-Path $folder ('.pending-' + [Guid]::NewGuid().ToString('N'))
$pendingManifest = $pending + '.json'
$backup = $pending + '.backup'
try {
    $latest = Join-Path $folder 'app-update-windows.json'
    if (Test-Path -LiteralPath $latest) {
        $old = Get-Content -Raw -LiteralPath $latest | ConvertFrom-Json
        if ($old.releaseNumber -gt $m.releaseNumber -or
            ($old.releaseNumber -eq $m.releaseNumber -and ($old.sha256 -ne $hash -or $old.versionName -cne $m.versionName -or $old.installerFileName -cne $m.installerFileName))) {
            throw 'Release number must increase; cannot replace published release bytes or identity.'
        }
    }
    $destination = Join-Path $folder $m.installerFileName
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $hash) { throw 'Published installer is immutable.' }
    } else {
        Copy-Item -LiteralPath $source -Destination $pending
        if ((Get-FileHash -LiteralPath $pending).Hash.ToLowerInvariant() -ne $hash -or (Get-Item -LiteralPath $pending).Length -ne $m.sizeBytes) { throw 'Source changed during copy.' }
        [IO.File]::Move($pending, $destination)
    }
    [IO.File]::WriteAllText($pendingManifest, ($m | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $latest) { [IO.File]::Replace($pendingManifest, $latest, $backup) }
    else { [IO.File]::Move($pendingManifest, $latest) }
    Write-Output "Published PC release $($m.releaseNumber): $destination"
} finally {
    foreach ($path in @($pending, $pendingManifest, $backup)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
    $lock.Dispose()
}
