param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [Parameter(Mandatory = $true)][string]$VersionName,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$VersionCode,
    [string]$Bundle = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

# 只发布用户已有的安装包，不调用 Unity 构建或修改资源清单。
if ($VersionName -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'VersionName must be major.minor.patch.' }
foreach ($part in $VersionName.Split('.')) { $null = [int]::Parse($part, [Globalization.CultureInfo]::InvariantCulture) }
$sourceApk = [IO.Path]::GetFullPath($Apk)
$bundleDirectory = [IO.Path]::GetFullPath($Bundle)
if (-not (Test-Path -LiteralPath (Join-Path $bundleDirectory 'ResourceFiles/OutpostLoop.version') -PathType Leaf)) {
    throw 'Bundle must be a published resource server directory containing ResourceFiles/OutpostLoop.version.'
}
if (-not (Test-Path -LiteralPath $sourceApk -PathType Leaf) -or [IO.Path]::GetExtension($sourceApk) -ine '.apk') { throw 'APK file missing or invalid extension.' }
$apkLength = (Get-Item -LiteralPath $sourceApk).Length
if ($apkLength -le 0 -or $apkLength -gt 8GB) { throw 'APK size must be between 1 byte and 8 GiB.' }
$folder = Join-Path $bundleDirectory 'AppUpdates/Android'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$hash = (Get-FileHash -LiteralPath $sourceApk -Algorithm SHA256).Hash.ToLowerInvariant()
$name = "OutpostLoop-$VersionName-$VersionCode.apk"
$destination = Join-Path $folder $name
$manifestPath = Join-Path $folder 'app-update.json'
$pendingApk = Join-Path $folder ('.apk-' + [Guid]::NewGuid().ToString('N') + '.pending')
$pendingManifest = Join-Path $folder ('.manifest-' + [Guid]::NewGuid().ToString('N') + '.pending')
$manifestBackup = $pendingManifest + '.backup'
try {
    # 已发布文件不可原位替换，防止正在下载的旧客户端拿到混合内容。
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
            throw 'This version already has different APK bytes. Publish a new application version instead.'
        }
    } else {
        Copy-Item -LiteralPath $sourceApk -Destination $pendingApk
        if ((Get-FileHash -LiteralPath $pendingApk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'APK changed during copy.' }
        [IO.File]::Move($pendingApk, $destination)
    }
    $manifest = [ordered]@{
        schemaVersion = 1; platform = 'Android'; packageName = 'com.godspace.outpostloop'
        versionName = $VersionName; versionCode = $VersionCode; apkFileName = $name
        sizeBytes = $apkLength; sha256 = $hash
    }
    [IO.File]::WriteAllText($pendingManifest, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $manifestPath) { [IO.File]::Replace($pendingManifest, $manifestPath, $manifestBackup) }
    else { [IO.File]::Move($pendingManifest, $manifestPath) }
    Write-Output "Published $VersionName (Android code $VersionCode): $destination"
} finally {
    foreach ($pending in @($pendingApk, $pendingManifest, $manifestBackup)) {
        if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending -Force }
    }
}
