param([string]$PackagePath)
$ErrorActionPreference = 'Stop'

# Restore the audited portal prefabs and their dependencies from the owned package.
if (-not $PackagePath) {
    $PackagePath = Join-Path $env:APPDATA 'Unity/Asset Store-5.x/Archanor VFX/Particle Systems/Action RPG FX.unitypackage'
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'Download Archanor VFX Action RPG FX 2.02 through Unity My Assets first.'
}
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$expected = '677807DE8667E4C2C566811B9700B7CCAF7799ED33E8D696674706221AA69C3A'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) {
    throw 'The Action RPG FX archive differs from the verified package.'
}

$selected = @{
    'a25b3d9a8119bf047b7609dba7f57302' = 'Resources/RiverValleyPortal/PortalBlueIdle.prefab'
    'ade1f0bfc938f694ab4f3bc05fc5c55b' = 'Resources/RiverValleyPortal/PortalBlueOpen.prefab'
    '1be59f1cbeaab354f9ca4f2b66abda33' = 'Resources/RiverValleyPortal/PortalBlueClose.prefab'
    '3d073b4507d5c09438f1b628bb3c0313' = 'Animation/LightFadeIn.controller'
    'b1adec5ea60953f46a1c3cd2e80cc6d0' = 'Animation/LightFadeIn.anim'
    '7143fe40663184d40997f88a20403589' = 'Animation/LightFadeOut.controller'
    '7ba038f0a3d04b94f92eb65ebfaf4552' = 'Animation/LightFadeOut.anim'
    '6ce0b77f1ce62ba4ebc86b1f87eb2ba3' = 'Materials/portal_blue.mat'
    'bb53538a58209614794a88113c6a4a4b' = 'Materials/sparkle_atlas.mat'
    '3d6a31b75392daf4f93ca706bd2b1f12' = 'Materials/portalrays.mat'
    '2ca5ac61052f7264e847fad5284dd1fe' = 'Textures/portal.png'
    'f585c20cc8b5fdc488751986fc18f994' = 'Textures/sparkle_atlas.png'
    '8d29d9056384ce24f9ff1d4f5e4a5061' = 'Textures/portal_rays.png'
    '338e9578d1797374b858ead231938d29' = 'Scripts/ARPGFXRotation.cs'
    '22e815ce54ff10c4cb16c17eb2c27586' = 'Scripts/ARPGFXLightFlicker.cs'
}
$root = Join-Path $PSScriptRoot '../Client/Assets/ThirdParty/Archanor VFX/Action RPG FX'
$legacy = Join-Path $PSScriptRoot '../Client/Assets/Game/Art/RiverValleyPortal'
if (Test-Path -LiteralPath $legacy -PathType Container) {
    throw '旧传送门目录仍存在。先在 Unity 将原目录移动到 Assets/ThirdParty/Archanor VFX/Action RPG FX，以免产生重复 GUID。'
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$staging = (New-Item -ItemType Directory -Path (Join-Path $tempRoot ('outpost-portal-' + [guid]::NewGuid()))).FullName
try {
    $members = foreach ($key in $selected.Keys) { "$key/asset"; "$key/asset.meta" }
    & tar.exe -xzf $package -C $staging @members
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the selected portal assets.' }
    foreach ($key in $selected.Keys) {
        $destination = Join-Path $root $selected[$key]
        $null = New-Item -ItemType Directory -Force (Split-Path $destination)
        Copy-Item -LiteralPath (Join-Path $staging "$key/asset") -Destination $destination -Force
        Copy-Item -LiteralPath (Join-Path $staging "$key/asset.meta") -Destination "$destination.meta" -Force
    }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $staging).Path
    if ($resolved -eq $staging -and (Split-Path $resolved -Parent) -eq $tempRoot -and
        (Split-Path $resolved -Leaf) -like 'outpost-portal-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
Write-Output "Restored $($selected.Count) River Valley portal assets."
