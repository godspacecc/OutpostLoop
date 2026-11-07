param(
    [ValidateRange(1, 65535)]
    [int]$Port = 8000,
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [string]$NoticeFile = '',
    [string]$AppUpdateRoot = '',
    [string]$WindowsAppUpdateRoot = ''
)

$ErrorActionPreference = 'Stop'
$MaxRequestHeaderBytes = 16 * 1024

# 仅展示当前网卡的 IPv4 候选地址，不修改网卡、客户端配置或防火墙。
function Show-NetworkEndpoints {
    param([int]$Value)

    Write-Host "本机客户端资源地址: http://127.0.0.1:${Value}"
    $addresses = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($networkInterface in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
            if ($networkInterface.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up -or
                $networkInterface.NetworkInterfaceType -eq [System.Net.NetworkInformation.NetworkInterfaceType]::Loopback) {
                continue
            }
            foreach ($unicast in $networkInterface.GetIPProperties().UnicastAddresses) {
                $address = $unicast.Address
                if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
                    [System.Net.IPAddress]::IsLoopback($address)) {
                    continue
                }
                $text = $address.ToString()
                if (-not $text.StartsWith('169.254.') -and -not $addresses.Contains($text)) {
                    $addresses.Add($text)
                }
            }
        }
    }
    catch {
        # 地址展示失败不应结束已成功监听的资源服务器。
        Write-Host "局域网地址读取失败。"
    }
    foreach ($address in ($addresses | Sort-Object)) {
        Write-Host "局域网客户端资源地址: http://${address}:${Value}"
        Write-Host "Version URL: http://${address}:${Value}/OutpostLoop.version"
    }
    if ($addresses.Count -eq 0) {
        Write-Host "局域网客户端资源地址: 未检测到"
    }
}

function Write-StatusLine {
    param(
        [System.IO.Stream]$Stream,
        [int]$StatusCode,
        [string]$Reason,
        [hashtable]$Headers
    )

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append("HTTP/1.1 $StatusCode $Reason`r`n")
    foreach ($key in $Headers.Keys) {
        [void]$builder.Append("$key`: $($Headers[$key])`r`n")
    }
    [void]$builder.Append("Connection: close`r`n")
    [void]$builder.Append("`r`n")
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($builder.ToString())
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Send-TextResponse {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [int]$StatusCode,
        [string]$Reason,
        [string]$Text,
        [bool]$HeadOnly = $false,
        [hashtable]$ExtraHeaders = @{}
    )

    $body = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $headers = @{
        "Content-Type" = "text/plain; charset=utf-8"
        "Content-Length" = $body.Length
    }
    foreach ($key in $ExtraHeaders.Keys) {
        $headers[$key] = $ExtraHeaders[$key]
    }

    $stream = $Client.GetStream()
    Write-StatusLine $stream $StatusCode $Reason $headers
    if (-not $HeadOnly) {
        $stream.Write($body, 0, $body.Length)
    }
}

function Read-HttpHeaderBlock {
    param(
        [System.IO.Stream]$Stream
    )

    $bytes = [System.Collections.Generic.List[byte]]::new()
    while ($true) {
        $value = $Stream.ReadByte()
        if ($value -lt 0) {
            throw [System.IO.InvalidDataException]::new('Incomplete HTTP request header.')
        }

        $bytes.Add([byte]$value)
        if ($bytes.Count -gt $MaxRequestHeaderBytes) {
            throw [System.IO.InvalidDataException]::new('HTTP request header is larger than 16 KiB.')
        }

        $count = $bytes.Count
        if ($count -ge 4 -and
            $bytes[$count - 4] -eq 13 -and
            $bytes[$count - 3] -eq 10 -and
            $bytes[$count - 2] -eq 13 -and
            $bytes[$count - 1] -eq 10) {
            return [System.Text.Encoding]::ASCII.GetString($bytes.ToArray())
        }
    }
}

function Parse-RequestLine {
    param(
        [string]$RequestLine
    )

    if ([string]::IsNullOrWhiteSpace($RequestLine)) {
        throw [System.IO.InvalidDataException]::new('Missing HTTP request line.')
    }

    $parts = $RequestLine.Split([char]' ')
    if ($parts.Length -ne 3 -or
        [string]::IsNullOrWhiteSpace($parts[0]) -or
        [string]::IsNullOrWhiteSpace($parts[1]) -or
        [string]::IsNullOrWhiteSpace($parts[2])) {
        throw [System.IO.InvalidDataException]::new('Malformed HTTP request line.')
    }

    if ($parts[2] -ne 'HTTP/1.0' -and $parts[2] -ne 'HTTP/1.1') {
        throw [System.IO.InvalidDataException]::new('Unsupported HTTP version.')
    }

    return @{
        Method = $parts[0].ToUpperInvariant()
        Target = $parts[1]
        Version = $parts[2]
    }
}

function Get-ContentType {
    param(
        [string]$Path
    )

    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        '.version' { return 'text/plain; charset=utf-8' }
        '.txt' { return 'text/plain; charset=utf-8' }
        '.json' { return 'application/json; charset=utf-8' }
        '.xml' { return 'application/xml; charset=utf-8' }
        '.apk' { return 'application/vnd.android.package-archive' }
        default { return 'application/octet-stream' }
    }
}

function Send-NoticeFile {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [string]$ResolvedNoticeFile,
        [string]$Method
    )

    if ([string]::IsNullOrWhiteSpace($ResolvedNoticeFile) -or
        -not (Test-Path -LiteralPath $ResolvedNoticeFile -PathType Leaf)) {
        Send-TextResponse $Client 404 'Not Found' 'Not Found' ($Method -eq 'HEAD')
        return
    }

    $stream = $Client.GetStream()
    try {
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $file = [System.IO.File]::Open($ResolvedNoticeFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        try {
            $headers = @{
                "Content-Type" = "application/json; charset=utf-8"
                "Content-Length" = $file.Length
                "Cache-Control" = "no-store, no-cache, must-revalidate"
                "Pragma" = "no-cache"
                "Expires" = "0"
            }
            Write-StatusLine $stream 200 'OK' $headers
            if ($Method -eq 'GET') {
                $buffer = New-Object byte[] 65536
                while (($read = $file.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $stream.Write($buffer, 0, $read)
                }
            }
        }
        finally {
            $file.Dispose()
        }
    }
    catch {
        Send-TextResponse $Client 500 'Internal Server Error' 'Internal Server Error' ($Method -eq 'HEAD')
    }
}

function Resolve-RequestPath {
    param(
        [string]$RequestTarget,
        [string]$ResolvedRoot
    )

    try {
        $pathOnly = ($RequestTarget -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathOnly) -or $pathOnly -eq '/') {
            return @{ Status = 404; Path = $null }
        }

        $decoded = [System.Uri]::UnescapeDataString($pathOnly).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $decoded = $decoded.TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($decoded.Contains('..')) {
            return @{ Status = 403; Path = $null }
        }

        $candidate = [System.IO.Path]::GetFullPath((Join-Path $ResolvedRoot $decoded))
        if (-not $candidate.StartsWith($ResolvedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            return @{ Status = 403; Path = $null }
        }

        return @{ Status = 200; Path = $candidate }
    }
    catch [System.UriFormatException] {
        return @{ Status = 400; Path = $null }
    }
    catch [System.IO.PathTooLongException] {
        return @{ Status = 400; Path = $null }
    }
    catch [System.ArgumentException] {
        return @{ Status = 400; Path = $null }
    }
    catch [System.NotSupportedException] {
        return @{ Status = 400; Path = $null }
    }
}

function Resolve-AppUpdatePath {
    param([string]$Target, [string]$Directory)
    $name = $Target.Substring('/app-updates/android/'.Length)
    if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.apk$') {
        return @{ Status = 403; Path = $null }
    }
    if ([string]::IsNullOrWhiteSpace($Directory)) { return @{ Status = 404; Path = $null } }
    return Resolve-RequestPath ('/' + $name) $Directory
}

# PC 安装包与 Android APK 分目录，只接受单一 EXE 名称。
function Resolve-WindowsAppUpdatePath {
    param([string]$Target, [string]$Directory)
    $name = $Target.Substring('/app-updates/windows/'.Length)
    if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.exe$') { return @{ Status = 403; Path = $null } }
    if ([string]::IsNullOrWhiteSpace($Directory)) { return @{ Status = 404; Path = $null } }
    return Resolve-RequestPath ('/' + $name) $Directory
}

function Test-ResourceRoot {
    param([string]$ResolvedRoot)

    if (-not (Test-Path -LiteralPath $ResolvedRoot -PathType Container)) {
        throw "ResourceFiles directory does not exist: $ResolvedRoot"
    }

    $versionFile = Join-Path $ResolvedRoot 'OutpostLoop.version'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "Missing version file: $versionFile"
    }

    $version = (Get-Content -Raw -LiteralPath $versionFile).Trim()
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Version file is empty: $versionFile"
    }

    foreach ($suffix in @('bytes', 'hash')) {
        $path = Join-Path $ResolvedRoot "OutpostLoop_$version.$suffix"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing package file: $path"
        }
        if ((Get-Item -LiteralPath $path).Length -le 0) {
            throw "Package file is empty: $path"
        }
    }

    $bundle = Get-ChildItem -LiteralPath $ResolvedRoot -File | Where-Object {
        $_.Name.EndsWith('.bundle', [System.StringComparison]::OrdinalIgnoreCase) -or
        ($_.Name.Length -eq 32 -and -not $_.Name.Contains('.'))
    } | Select-Object -First 1
    if ($null -eq $bundle) {
        throw "No bundle file found in: $ResolvedRoot"
    }

    return $version
}

function Handle-Client {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [string]$ResolvedRoot,
        [string]$ResolvedNoticeFile,
        [string]$ResolvedAppUpdateRoot,
        [string]$ResolvedWindowsAppUpdateRoot
    )

    try {
        $Client.ReceiveTimeout = 5000
        $Client.SendTimeout = 5000
        $stream = $Client.GetStream()
        $headerBlock = Read-HttpHeaderBlock $stream
        $headerLines = $headerBlock.Split([string[]]@("`r`n"), [System.StringSplitOptions]::None)
        $request = Parse-RequestLine $headerLines[0]
        $method = $request.Method

        if ($method -ne 'GET' -and $method -ne 'HEAD') {
            Send-TextResponse $Client 405 'Method Not Allowed' 'Method Not Allowed' $false @{ Allow = 'GET, HEAD' }
            return
        }

        $pathOnly = ($request.Target -split '\?', 2)[0]
        if ($pathOnly -eq '/resource-update-notices.json') {
            Send-NoticeFile $Client $ResolvedNoticeFile $method
            return
        }

        if ($pathOnly -eq '/app-update-windows.json') {
            $manifestPath = if ([string]::IsNullOrWhiteSpace($ResolvedWindowsAppUpdateRoot)) { '' } else { Join-Path $ResolvedWindowsAppUpdateRoot 'app-update-windows.json' }
            Send-NoticeFile $Client $manifestPath $method
            return
        }
        if ($pathOnly -eq '/app-update.json') {
            $manifestPath = if ([string]::IsNullOrWhiteSpace($ResolvedAppUpdateRoot)) { '' } else { Join-Path $ResolvedAppUpdateRoot 'app-update.json' }
            Send-NoticeFile $Client $manifestPath $method
            return
        }

        $resolved = if ($pathOnly.StartsWith('/app-updates/android/', [StringComparison]::Ordinal)) {
            Resolve-AppUpdatePath $pathOnly $ResolvedAppUpdateRoot
        } elseif ($pathOnly.StartsWith('/app-updates/windows/', [StringComparison]::Ordinal)) {
            Resolve-WindowsAppUpdatePath $pathOnly $ResolvedWindowsAppUpdateRoot
        } else { Resolve-RequestPath $request.Target $ResolvedRoot }
        if ($resolved.Status -eq 400) {
            Send-TextResponse $Client 400 'Bad Request' 'Bad Request' ($method -eq 'HEAD')
            return
        }

        if ($resolved.Status -eq 403) {
            Send-TextResponse $Client 403 'Forbidden' 'Forbidden' ($method -eq 'HEAD')
            return
        }

        $filePath = $resolved.Path
        if ($resolved.Status -eq 404 -or -not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            Send-TextResponse $Client 404 'Not Found' 'Not Found' ($method -eq 'HEAD')
            return
        }

        try {
            $file = [System.IO.File]::Open($filePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            try {
                $headers = @{
                    "Content-Type" = Get-ContentType $filePath
                    "Content-Length" = $file.Length
                }
                Write-StatusLine $stream 200 'OK' $headers
                if ($method -eq 'GET') {
                    $buffer = New-Object byte[] 65536
                    while (($read = $file.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $stream.Write($buffer, 0, $read)
                    }
                }
            }
            finally {
                $file.Dispose()
            }
        }
        catch {
            Send-TextResponse $Client 500 'Internal Server Error' "Internal Server Error: $($_.Exception.Message)" ($method -eq 'HEAD')
        }
    }
    catch [System.IO.InvalidDataException] {
        try {
            Send-TextResponse $Client 400 'Bad Request' 'Bad Request'
        }
        catch {
        }
    }
    catch [System.IO.IOException] {
        try {
            Send-TextResponse $Client 400 'Bad Request' 'Bad Request'
        }
        catch {
        }
    }
    finally {
        $Client.Close()
    }
}

$resolvedRoot = [System.IO.Path]::GetFullPath($Root)
$resolvedNoticeFile = if ([string]::IsNullOrWhiteSpace($NoticeFile)) { '' } else { [System.IO.Path]::GetFullPath($NoticeFile) }
$resolvedAppUpdateRoot = if ([string]::IsNullOrWhiteSpace($AppUpdateRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path (Split-Path $resolvedRoot -Parent) 'AppUpdates/Android'))
} else { [System.IO.Path]::GetFullPath($AppUpdateRoot) }
$resolvedWindowsAppUpdateRoot = if ([string]::IsNullOrWhiteSpace($WindowsAppUpdateRoot)) {
    [IO.Path]::GetFullPath((Join-Path (Split-Path $resolvedRoot -Parent) 'AppUpdates/Windows'))
} else { [IO.Path]::GetFullPath($WindowsAppUpdateRoot) }
$version = Test-ResourceRoot $resolvedRoot
$listener = $null
$currentClient = $null

try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
    $listener.Start()
    Write-Host "OutpostLoop Resource Server"
    Write-Host "Root: $resolvedRoot"
    if (-not [string]::IsNullOrWhiteSpace($resolvedNoticeFile)) {
        Write-Host "Notice: $resolvedNoticeFile"
    }
    Write-Host "Version: $version"
    Write-Host "Listening: http://0.0.0.0:$Port/"
    Show-NetworkEndpoints -Value $Port
    Write-Host "Stop: Ctrl+C or close this window."

    while ($true) {
        $currentClient = $listener.AcceptTcpClient()
        Handle-Client $currentClient $resolvedRoot $resolvedNoticeFile $resolvedAppUpdateRoot $resolvedWindowsAppUpdateRoot
        $currentClient = $null
    }
}
catch {
    Write-Error "Resource server failed on port $Port. $($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $currentClient) {
        $currentClient.Close()
    }
    if ($null -ne $listener) {
        $listener.Stop()
    }
}
