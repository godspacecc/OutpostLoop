# 双端均成功并校验产物后才发布；保留阶段产物以供排查。
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $workspace ('Backup/Artifacts/Foundation-20261107/Luban-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($target in @('client', 'server')) {
    $code = Join-Path $stage "$target/code"
    $data = Join-Path $stage "$target/data"
    & "$PSScriptRoot/Luban/Luban.exe" -t $target -c cs-bin -d bin --validationFailAsError --conf "$PSScriptRoot/luban.conf" -x "outputCodeDir=$code" -x "outputDataDir=$data"
    if ($LASTEXITCODE -ne 0) { throw "$target generation failed; stage retained: $stage" }
    foreach ($name in @('Tables.cs', 'Rule.cs', 'TbRule.cs')) {
        if (!(Test-Path "$code/$name") -or (Get-Item "$code/$name").Length -eq 0) { throw "Missing generated code: $target/$name" }
    }
    $tables = @(Get-ChildItem $data -Filter '*.bytes')
    if ($tables.Count -ne 1 -or $tables[0].Name -ne 'tbrule.bytes' -or $tables[0].Length -eq 0) { throw "Invalid generated table set: $target" }
}
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes("$stage/client/data/tbrule.bytes")) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes("$stage/server/data/tbrule.bytes"))) { throw 'Client/server data differ' }
# 只覆盖已校验的生成文件，不删除目录或 Unity meta。
$pairs = @(
    @('client/code', 'Client/Assets/Foundation/Config/Generated'),
    @('client/data', 'Client/Assets/Game/Config'),
    @('server/code', 'Server/Generated/Config'),
    @('server/data', 'Server/Config')
)
foreach ($pair in $pairs) {
    $destination = Join-Path $workspace $pair[1]
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem (Join-Path $stage $pair[0]) -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Force }
}
Write-Host "Both targets validated and published. Stage: $stage"
