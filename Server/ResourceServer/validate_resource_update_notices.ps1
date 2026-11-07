param(
    [string]$Path = ''
)

$ErrorActionPreference = 'Stop'
$MaxBytes = 256 * 1024

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $PSScriptRoot 'Notices\resource-update-notices.json'
}

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Count-CodePoints([string]$Value) {
    $count = 0
    for ($i = 0; $i -lt $Value.Length; $i++) {
        if ([char]::IsHighSurrogate($Value[$i]) -and
            $i + 1 -lt $Value.Length -and
            [char]::IsLowSurrogate($Value[$i + 1])) {
            $i++
        }
        $count++
    }
    return $count
}

function Test-PlainText([string]$Value, [int]$MaxCodePoints, [string]$Stage) {
    if ($Value.Contains([char]0xFEFF)) {
        Fail "$Stage contains BOM."
    }
    foreach ($ch in $Value.ToCharArray()) {
        if ([char]::IsControl($ch)) {
            Fail "$Stage contains a control character."
        }
    }
    if ($Value -match '<[^>\r\n]+>') {
        Fail "$Stage contains rich text tags."
    }
    if ((Count-CodePoints $Value) -gt $MaxCodePoints) {
        Fail "$Stage is too long."
    }
}

function Get-JsonProperty($Object, [string]$Name) {
    foreach ($property in $Object.PSObject.Properties) {
        if ($property.Name -eq $Name) {
            return $property
        }
    }
    return $null
}

try {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail "Notice file does not exist: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -gt $MaxBytes) {
        Fail 'Notice file is larger than 256 KiB.'
    }
    $json = [System.IO.File]::ReadAllText($item.FullName, [System.Text.UTF8Encoding]::new($false))
    if ($json.Length -gt 0 -and $json[0] -eq [char]0xFEFF) {
        Fail 'Notice file contains BOM.'
    }
    $doc = $json | ConvertFrom-Json
    if ($doc.schemaVersion -ne 1) {
        Fail 'schemaVersion must be 1.'
    }
    if ([string]::IsNullOrWhiteSpace($doc.defaultLanguage)) {
        Fail 'defaultLanguage is required.'
    }
    Test-PlainText $doc.defaultLanguage 64 'defaultLanguage'
    if ($json -notmatch '"notices"\s*:\s*\[') {
        Fail 'notices must be an array.'
    }
    if ($json -match '"texts"\s*:\s*\[' -or $json -match '"texts"\s*:\s*"') {
        Fail 'texts must be an object.'
    }
    if ($json -match '"items"\s*:\s*(?!\[)') {
        Fail 'items must be an array.'
    }
    if ($json -match '"items"\s*:\s*\[\s*(?!\]|")') {
        Fail 'item must be a string.'
    }
    if ($json -match '"title"\s*:\s*(?!")') {
        Fail 'title must be a string.'
    }
    if ($json -match '"summary"\s*:\s*(?!")') {
        Fail 'summary must be a string.'
    }
    if ($json -match '<[^>\r\n]+>') {
        Fail 'rich text tags are not allowed.'
    }
    if ($json -match '\\r|\\n|\\t') {
        Fail 'control characters are not allowed.'
    }
    if ($json -match '\\u00(0[0-9a-fA-F]|1[0-9a-fA-F])') {
        Fail 'control characters are not allowed.'
    }
    $noticeList = if ($null -eq $doc.notices) { @() } elseif ($doc.notices -is [array]) { $doc.notices } else { @($doc.notices) }
    if ($noticeList.Count -gt 100) {
        Fail 'notices count is larger than 100.'
    }
    $versions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    for ($i = 0; $i -lt $noticeList.Count; $i++) {
        $notice = $noticeList[$i]
        if ($notice -isnot [pscustomobject]) {
            Fail "notice[$i] must be an object."
        }
        if ([string]::IsNullOrWhiteSpace($notice.version) -or $notice.version -notmatch '^\d{12}$') {
            Fail "notice[$i].version must be yyyyMMddHHmm."
        }
        $parsed = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($notice.version, 'yyyyMMddHHmm', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
            Fail "notice[$i].version is invalid."
        }
        if (-not $versions.Add([string]$notice.version)) {
            Fail "notice[$i].version is duplicated."
        }
        if ($notice.enabled -isnot [bool]) {
            Fail "notice[$i].enabled must be boolean."
        }
        if ($null -eq $notice.texts -or $notice.texts -isnot [pscustomobject]) {
            Fail "notice[$i].texts must be an object."
        }
        $displayableDefault = $false
        foreach ($property in $notice.texts.PSObject.Properties) {
            Test-PlainText $property.Name 64 "notice[$i].language"
            $text = $property.Value
            if ($text -isnot [pscustomobject]) {
                Fail "notice[$i].$($property.Name) text must be an object."
            }
            $titleProperty = Get-JsonProperty $text 'title'
            $summaryProperty = Get-JsonProperty $text 'summary'
            if ($null -ne $titleProperty -and $titleProperty.Value -isnot [string]) {
                Fail "notice[$i].$($property.Name).title must be a string."
            }
            if ($null -ne $summaryProperty -and $summaryProperty.Value -isnot [string]) {
                Fail "notice[$i].$($property.Name).summary must be a string."
            }
            $title = if ($null -eq $titleProperty) { '' } else { $titleProperty.Value }
            $summary = if ($null -eq $summaryProperty) { '' } else { $summaryProperty.Value }
            Test-PlainText $title 100 "notice[$i].$($property.Name).title"
            Test-PlainText $summary 500 "notice[$i].$($property.Name).summary"
            $itemCount = 0
            $itemsProperty = Get-JsonProperty $text 'items'
            if ($null -ne $itemsProperty) {
                if ($itemsProperty.Value -isnot [array]) {
                    Fail "notice[$i].$($property.Name).items must be an array."
                }
                if ($itemsProperty.Value.Count -gt 20) {
                    Fail "notice[$i].$($property.Name).items has more than 20 entries."
                }
                foreach ($entry in $itemsProperty.Value) {
                    if ($entry -isnot [string]) {
                        Fail "notice[$i].$($property.Name).item must be a string."
                    }
                    Test-PlainText $entry 200 "notice[$i].$($property.Name).item"
                    if (-not [string]::IsNullOrWhiteSpace($entry)) {
                        $itemCount++
                    }
                }
            }
            if ($property.Name -eq $doc.defaultLanguage -and
                (-not [string]::IsNullOrWhiteSpace($title) -or
                 -not [string]::IsNullOrWhiteSpace($summary) -or
                 $itemCount -gt 0)) {
                $displayableDefault = $true
            }
        }
        if ($notice.enabled -and -not $displayableDefault) {
            Fail "notice[$i] enabled record requires displayable default language text."
        }
    }
    Write-Host "Notice file is valid: $($item.FullName)"
    exit 0
}
catch {
    Fail "JSON validation failed: $($_.Exception.Message)"
}
