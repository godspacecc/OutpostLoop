param(
    [int]$Port = 8000,
    [Parameter(Mandatory=$true)]
    [string]$Root
)

$Listener = $null

# 发布包由其完整服务器脚本同时提供资源、公告与 APK 更新端点。
if ((Split-Path $Root -Leaf) -eq 'ResourceFiles') {
    $publishedRoot = Split-Path ([System.IO.Path]::GetFullPath($Root)) -Parent
    $publishedScript = Join-Path $publishedRoot 'resource_server.ps1'
    if (-not (Test-Path -LiteralPath $publishedScript -PathType Leaf)) {
        throw "Published resource server script is missing: $publishedScript"
    }
    & $publishedScript -Port $Port -Root $Root `
        -NoticeFile (Join-Path $publishedRoot 'Notices/resource-update-notices.json') `
        -AppUpdateRoot (Join-Path $publishedRoot 'AppUpdates/Android')
    return
}

function Get-MimeType($Path) {
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    switch ($ext) {
        '.html' { 'text/html'; break }
        '.htm' { 'text/html'; break }
        '.css' { 'text/css'; break }
        '.js' { 'application/javascript'; break }
        '.json' { 'application/json'; break }
        '.xml' { 'application/xml'; break }
        '.txt' { 'text/plain'; break }
        '.version' { 'text/plain'; break }
        default { 'application/octet-stream'; break }
    }
}

function Send-Response($Stream, $StatusCode, $StatusText, $Headers, $Body) {
    if (-not $Headers) { $Headers = @{} }
    $Headers['Connection'] = 'close'
    $Header = "HTTP/1.1 $StatusCode $StatusText`r`n"
    foreach ($key in $Headers.Keys) {
        $Header += "${key}: $($Headers[$key])`r`n"
    }
    $Header += "`r`n"

    $HeaderBytes = [System.Text.Encoding]::ASCII.GetBytes($Header)
    $Stream.Write($HeaderBytes, 0, $HeaderBytes.Length)
    if ($Body -and $Body.Length -gt 0) {
        $Stream.Write($Body, 0, $Body.Length)
    }
}

try {
    if (-not (Test-Path $Root -PathType Container)) {
        throw "Root directory does not exist: $Root"
    }

    $Root = [System.IO.Path]::GetFullPath((Resolve-Path $Root).Path).TrimEnd('\', '/')
    $RootPrefix = $Root + [System.IO.Path]::DirectorySeparatorChar

    $VersionFile = Join-Path $Root 'OutpostLoop.version'
    if (-not (Test-Path $VersionFile -PathType Leaf)) {
        throw "Version file does not exist: $VersionFile"
    }
    $Version = (Get-Content -Raw $VersionFile).Trim()

    try {
        $Host.UI.RawUI.WindowTitle = "OutpostLoop YooAsset资源服 - $Port"
    } catch {
        Write-Warning "Unable to set console title: $_"
    }

    Write-Host "========================================"
    Write-Host "OutpostLoop YooAsset资源服"
    Write-Host "资源版本: $Version"
    Write-Host "资源目录: $Root"
    Write-Host "基础地址: http://127.0.0.1:$Port"
    Write-Host "版本地址: http://127.0.0.1:$Port/OutpostLoop.version"
    Write-Host "停止服务: 按 Ctrl+C 或关闭窗口"
    Write-Host "========================================"

    $Listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    $Listener.Start()

    while ($true) {
        $Client = $Listener.AcceptTcpClient()
        $Stream = $Client.GetStream()

        try {
            $Buffer = New-Object byte[] 4096
            $BytesRead = $Stream.Read($Buffer, 0, $Buffer.Length)
            if ($BytesRead -le 0) {
                Send-Response $Stream 400 "Bad Request" @{} $null
                $Client.Close()
                continue
            }

            $RequestText = [System.Text.Encoding]::ASCII.GetString($Buffer, 0, $BytesRead)
            $Lines = $RequestText -split "`r`n"
            $Parts = $Lines[0] -split ' '
            if ($Parts.Length -lt 3) {
                Send-Response $Stream 400 "Bad Request" @{} $null
                $Client.Close()
                continue
            }

            $Method = $Parts[0].ToUpperInvariant()
            $Url = [System.Uri]::UnescapeDataString($Parts[1])
            $QueryIndex = $Url.IndexOf('?')
            if ($QueryIndex -ge 0) { $Url = $Url.Substring(0, $QueryIndex) }
            $Url = $Url.TrimStart('/')

            $FullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $Url))
            $InsideRoot = $FullPath.Equals($Root, [System.StringComparison]::OrdinalIgnoreCase) -or
                          $FullPath.StartsWith($RootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
            if (-not $InsideRoot) {
                Send-Response $Stream 403 "Forbidden" @{} $null
                $Client.Close()
                continue
            }

            if ($Method -ne 'GET' -and $Method -ne 'HEAD') {
                Send-Response $Stream 405 "Method Not Allowed" @{} $null
                $Client.Close()
                continue
            }

            if (-not (Test-Path $FullPath -PathType Leaf)) {
                Send-Response $Stream 404 "Not Found" @{} $null
                $Client.Close()
                continue
            }

            $FileInfo = Get-Item $FullPath
            $Headers = @{
                'Content-Type' = Get-MimeType $FullPath
                'Content-Length' = $FileInfo.Length
            }

            if ($Method -eq 'HEAD') {
                Send-Response $Stream 200 "OK" $Headers $null
            } else {
                $FileBytes = [System.IO.File]::ReadAllBytes($FullPath)
                Send-Response $Stream 200 "OK" $Headers $FileBytes
            }
            $Client.Close()
        } catch {
            try {
                Send-Response $Stream 500 "Internal Server Error" @{} $null
                $Client.Close()
            } catch { }
        }
    }
} catch {
    Write-Error "[Fatal] $_"
    exit 1
} finally {
    if ($Listener) { $Listener.Stop() }
}
