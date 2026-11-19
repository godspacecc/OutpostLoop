param([string]$PackagePath)
$ErrorActionPreference = 'Stop'

# 从当前用户已有的 Unity Asset Store 缓存还原已确认的原始资源。
if (-not $PackagePath) {
    $cache = Join-Path $env:APPDATA 'Unity/Asset Store-5.x/Pxltiger'
    $candidate = Get-ChildItem -LiteralPath $cache -Filter 'Zombie.unitypackage' -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($candidate) { $PackagePath = $candidate.FullName }
}
if (-not $PackagePath) { throw '请先通过 Unity My Assets 下载 Pxltiger Zombie Draft 1.2。' }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$expected = '42B165851CC7649D3371E7E341D7F149848BCEB2C22E93C4288AA3894C1085C5'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) {
    throw '包装哈希与已验证的 Zombie Draft 1.2 不一致。'
}

$root = Join-Path $PSScriptRoot '../Client/Assets/Game/Art/RiverValleyInfected'
$assets = Join-Path $root 'Resources/RiverValleyInfected'
$staging = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ("outpost-zombie-" + [guid]::NewGuid()))).FullName
try {
    & tar.exe -xzf $package -C $staging
    if ($LASTEXITCODE -ne 0) { throw '无法解包 Zombie。' }
    foreach ($entry in (Get-ChildItem -LiteralPath $staging -Recurse -Filter 'pathname' -File)) {
        $name = (Get-Content -LiteralPath $entry.FullName -TotalCount 1).Trim().Trim([char]0)
        if ($name -match '^Assets/Zombie/(?<part>FBXs/Zombie[123]\.FBX|Materials/Zombie\.mat|Textures/(Zombie\.tga|Zombie_ao\.png|Zombie_nm\.png|zombie_emission\.tga|Zombie_metallic\.tga))$') {
            $dest = Join-Path "$root/Source" $Matches.part
        }
        elseif ($name -match '^Assets/Zombie/(Prefabs|Animations)/(?<file>Zombie[23]\.prefab|Z_(Idle|Walk_InPlace|Run_InPlace)\.anim)$') {
            $dest = Join-Path $assets $Matches.file
        }
        else { continue }

        $null = New-Item -ItemType Directory -Force (Split-Path $dest)
        $asset = Join-Path $entry.DirectoryName 'asset'
        if ($dest.EndsWith('.prefab')) {
            $yaml = [IO.File]::ReadAllText($asset) -replace 'm_Controller: \{[^\r\n]+\}', 'm_Controller: {fileID: 0}'
            [IO.File]::WriteAllText([IO.Path]::GetFullPath($dest), $yaml, [Text.UTF8Encoding]::new($false))
        }
        else { Copy-Item -LiteralPath $asset -Destination $dest -Force }
        Copy-Item -LiteralPath (Join-Path $entry.DirectoryName 'asset.meta') -Destination ($dest + '.meta') -Force
    }
}
finally {
    $verifiedStage = (Resolve-Path -LiteralPath $staging).Path
    if ($verifiedStage -eq $staging -and (Split-Path $verifiedStage -Leaf) -like 'outpost-zombie-*' -and
        (Split-Path $verifiedStage -Parent) -eq [IO.Path]::GetTempPath().TrimEnd('\','/')) {
        Remove-Item -LiteralPath $verifiedStage -Recurse -Force
    }
}
Write-Output '河谷镇感染者资源已还原。'
