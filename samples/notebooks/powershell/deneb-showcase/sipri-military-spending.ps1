# Read-only SIPRI Excel adapter for the original Deneb Marimekko specification.
# Uses .NET ZIP/XML APIs only; no Excel application or PowerShell module required.

function Read-SipriZipXml {
    param([Parameter(Mandatory)]$Archive, [Parameter(Mandatory)][string]$Path)
    $entry = $Archive.GetEntry($Path)
    if ($null -eq $entry) { throw "SIPRI workbook is missing $Path." }
    $stream = $entry.Open()
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($stream, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

function Get-SipriCellText {
    param($Cell, [AllowEmptyCollection()][string[]]$SharedStrings)
    if ($null -eq $Cell) { return '' }
    switch ($Cell.GetAttribute('t')) {
        's' {
            $index = [int]$Cell.v
            if ($index -lt 0 -or $index -ge $SharedStrings.Count) { throw 'Invalid XLSX shared-string reference.' }
            return $SharedStrings[$index]
        }
        'inlineStr' { return $Cell.is.InnerText }
        default { return [string]$Cell.v }
    }
}

function Resolve-SipriWorkbookUri {
    param([Parameter(Mandatory)][string]$Html)
    $links = @([regex]::Matches($Html, '(?i)href=["'']([^"'']*SIPRI[^"'']*milex[^"'']*\.xlsx)["'']') |
        ForEach-Object { [System.Net.WebUtility]::HtmlDecode($_.Groups[1].Value) } | Select-Object -Unique)
    if ($links.Count -ne 1) { throw 'Expected one current SIPRI Military Expenditure XLSX download link.' }
    $uri = [uri]::new([uri]'https://www.sipri.org/databases/milex', $links[0])
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'www.sipri.org') { throw 'The workbook download must remain on https://www.sipri.org.' }
    $uri.AbsoluteUri
}

function ConvertFrom-SipriWorkbook {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [ValidateRange(1949, 9998)][int]$StartYear = 2023,
        [ValidateRange(1949, 9998)][int]$ThroughYear = ((Get-Date).Year - 1)
    )
    $memory = [System.IO.MemoryStream]::new($Bytes, $false)
    $archive = $null
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($memory, [System.IO.Compression.ZipArchiveMode]::Read)
        $workbook = Read-SipriZipXml $archive 'xl/workbook.xml'
        $relationships = Read-SipriZipXml $archive 'xl/_rels/workbook.xml.rels'
        $sheets = @($workbook.workbook.sheets.sheet | Where-Object { $_.name -eq 'Current US$' })
        if ($sheets.Count -ne 1) { throw 'Expected the SIPRI "Current US$" worksheet (not constant dollars).' }
        $id = $sheets[0].GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $relationship = @($relationships.Relationships.Relationship | Where-Object { $_.Id -eq $id })
        if ($relationship.Count -ne 1 -or $relationship[0].TargetMode -eq 'External') { throw 'Invalid worksheet relationship.' }
        $target = [uri]::new([uri]'https://xlsx.invalid/xl/workbook.xml', [string]$relationship[0].Target).AbsolutePath.TrimStart('/')
        $sheet = Read-SipriZipXml $archive $target
        $stringsXml = Read-SipriZipXml $archive 'xl/sharedStrings.xml'
        # Keep empty entries: removing one shifts every subsequent shared-string index.
        $strings = [System.Collections.Generic.List[string]]::new()
        foreach ($item in $stringsXml.sst.si) { $strings.Add($item.InnerText) }
        $styles = Read-SipriZipXml $archive 'xl/styles.xml'
        $fonts = @($styles.styleSheet.fonts.font)
        $formats = @($styles.styleSheet.cellXfs.xf)
        $rows = @($sheet.worksheet.sheetData.row)
        $description = Get-SipriCellText ($rows[0].c | Where-Object r -eq A1) $strings.ToArray()
        if ($description -notmatch 'millions of US\$ at current prices') { throw 'Unexpected SIPRI units or price basis.' }
        $header = @($rows | Where-Object {
            (Get-SipriCellText ($_.c | Where-Object { $_.r -match '^A\d+$' }) $strings.ToArray()) -eq 'Country'
        })
        if ($header.Count -ne 1) { throw 'Expected one country/year header row.' }
        $columns = [ordered]@{}
        foreach ($cell in $header[0].c) {
            $text = Get-SipriCellText $cell $strings.ToArray()
            if ($text -match '^\d{4}$' -and [int]$text -ge $StartYear -and [int]$text -le $ThroughYear) {
                $year = $text
                if ($columns.Contains($year)) { throw "Duplicate year column $year." }
                $columns[$year] = $cell.r -replace '\d+', ''
            }
        }
        if ($columns.Count -eq 0) { throw "No published SIPRI years in $StartYear-$ThroughYear." }
        $lastYear = ($columns.Keys | Measure-Object -Maximum).Maximum
        foreach ($year in $StartYear..$lastYear) {
            if (-not $columns.Contains([string]$year)) { throw "Missing SIPRI year column $year." }
        }
        $regions = @('Africa', 'Americas', 'Asia & Oceania', 'Europe', 'Middle East')
        $subregions = @('North Africa', 'sub-Saharan Africa', 'Central America and the Caribbean',
            'North America', 'South America', 'Oceania', 'South Asia', 'East Asia', 'South East Asia',
            'Central Asia', 'Central Europe', 'Eastern Europe', 'Western Europe')
        $periods = @{}
        foreach ($year in $columns.Keys) {
            $periods[$year] = @{ Rows = [System.Collections.Generic.List[object]]::new(); Missing = [System.Collections.Generic.List[object]]::new(); NotApplicable = [System.Collections.Generic.List[object]]::new() }
        }
        $region = $null
        $seenCountries = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($row in $rows | Where-Object { [int]$_.r -gt [int]$header[0].r }) {
            $cells = @{}
            foreach ($cell in $row.c) { $cells[$cell.r -replace '\d+', ''] = $cell }
            $country = (Get-SipriCellText $cells['A'] $strings.ToArray()).Trim()
            if (-not $country) { continue }
            if ($country -in $regions) { $region = $country; continue }
            if ($country -in $subregions) { continue }
            if (-not $region) { throw "Country without a SIPRI region: $country." }
            if (-not $seenCountries.Add($country)) { throw "Duplicate SIPRI country/entity: $country." }
            $notes = Get-SipriCellText $cells['B'] $strings.ToArray()
            foreach ($year in $columns.Keys) {
                $cell = $cells[$columns[$year]]
                $text = (Get-SipriCellText $cell $strings.ToArray()).Trim()
                $address = "$($columns[$year])$($row.r)"
                $omitted = [pscustomobject]@{ Country = $country; Continent = $region; SourceCell = $address; Marker = $text }
                if ($text -eq 'xxx') { $periods[$year].NotApplicable.Add($omitted); continue }
                if ($text -eq '' -or $text -match '^\.[\s.]*$') { $periods[$year].Missing.Add($omitted); continue }
                $amount = 0.0
                if ($cell.GetAttribute('t') -notin @('', 'n') -or
                    -not [double]::TryParse($text, [System.Globalization.NumberStyles]::Float,
                        [System.Globalization.CultureInfo]::InvariantCulture, [ref]$amount) -or
                    -not [double]::IsFinite($amount) -or $amount -lt 0) {
                    throw "Invalid SIPRI amount for $country/$year ($address): '$text'."
                }
                $styleIndex = [int]$cell.GetAttribute('s')
                if ($styleIndex -ge $formats.Count) { throw "Invalid cell style at $address." }
                $font = $fonts[[int]$formats[$styleIndex].fontId]
                $color = $font.color
                $quality = if ($color.indexed -eq '12' -or $color.rgb -match '0000FF$') { 'SIPRI estimate' }
                    elseif ($color.indexed -eq '10' -or $color.rgb -match 'FF0000$') { 'Highly uncertain' }
                    else { 'Unflagged' }
                $periods[$year].Rows.Add([pscustomobject]@{
                    Continent = $region; Country = $country; Spend = $amount
                    Quality = $quality; Notes = $notes; SourceCell = $address
                })
            }
        }
        $years = foreach ($year in $columns.Keys) {
            $period = $periods[$year]
            if ($period.Rows.Count -eq 0) { throw "No numeric country data for $year." }
            foreach ($region in $regions) {
                if (@($period.Rows | Where-Object { $_.Continent -eq $region -and $_.Spend -gt 0 }).Count -eq 0) {
                    throw "No positive country spending for $region/$year."
                }
            }
            [pscustomobject]@{
                Year = [int]$year; Rows = $period.Rows.ToArray(); Missing = $period.Missing.ToArray()
                NotApplicable = $period.NotApplicable.ToArray(); EntityCount = $period.Rows.Count
                MissingCount = $period.Missing.Count; ZeroCount = @($period.Rows | Where-Object Spend -eq 0).Count
                EstimateCount = @($period.Rows | Where-Object Quality -eq 'SIPRI estimate').Count
                UncertainCount = @($period.Rows | Where-Object Quality -eq 'Highly uncertain').Count
                TotalMillions = ($period.Rows | Measure-Object Spend -Sum).Sum
            }
        }
        # Native dictionary remains intact across Verso's PowerShell cell boundary.
        [ordered]@{ Years = @($years); SourceSheet = 'Current US$'; Unit = 'USD millions, current prices'; SourceDescription = $description }
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $memory.Dispose()
    }
}

function Get-SipriMilitarySpending {
    <#
    .SYNOPSIS
    Downloads SIPRI's current workbook and reads completed calendar years since 2023.
    .DESCRIPTION
    Uses the official "Current US$" country/entity worksheet. Missing data and extinct
    countries are kept separately; no zero substitution, extrapolation or stale fallback.
    Historical figures may be revised with each SIPRI release. Estimates and highly
    uncertain data retain the source's blue/red font flags in the Quality field.
    #>
    [CmdletBinding()]
    param([ValidateRange(1949, 9998)][int]$StartYear = 2023)
    $ProgressPreference = 'SilentlyContinue'
    $sourcePage = 'https://www.sipri.org/databases/milex'
    $page = Invoke-WebRequest $sourcePage -TimeoutSec 60 -ErrorAction Stop
    $uri = Resolve-SipriWorkbookUri $page.Content
    $response = Invoke-WebRequest $uri -TimeoutSec 60 -ErrorAction Stop
    if ($response.Content -isnot [byte[]]) { throw 'Expected XLSX bytes from SIPRI, not an HTML response.' }
    $data = ConvertFrom-SipriWorkbook -Bytes $response.Content -StartYear $StartYear
    $data['SourcePage'] = $sourcePage
    $data['WorkbookUri'] = $uri
    $data['DownloadedAt'] = [datetime]::UtcNow.ToString('o')
    $data['Sha256'] = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($response.Content)).ToLowerInvariant()
    $data
}

function Set-SipriMekkoYear {
    <#
    .SYNOPSIS
    Binds a downloaded year to Deneb's original Marimekko or Mekko Bar specification.
    .DESCRIPTION
    Only the table values, title and source credit change. The chart's native region
    merging, small-country grouping, marks, dimensions and colors remain unchanged.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][System.Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Spending,
        [Parameter(Mandatory)][ValidateRange(1949, 9998)][int]$Year
    )
    process {
        $period = @($Spending.Years | Where-Object Year -eq $Year)
        if ($period.Count -ne 1) {
            throw "Expected one downloaded SIPRI year $Year. Available: $($Spending.Years.Year -join ', '). Run the download cell first."
        }
        $period = $period[0]
        if ($period.Rows.Count -eq 0 -or @($period.Rows | Where-Object { -not [double]::IsFinite($_.Spend) -or $_.Spend -lt 0 }).Count -gt 0) {
            throw 'The Mekko dataset must contain finite, non-negative spending amounts.'
        }
        $copy = $Spec | Set-DenebSpecData -DataName 'table' -Data $period.Rows
        $copy.title.text = "Global Military Spending $Year"
        $credit = @($copy.marks | Where-Object { ($_.encode.update.text.value -join ' ') -match 'Dataviz: David Bacci' })
        if ($credit.Count -ne 1) { throw 'Expected the original Mekko source/author credit.' }
        $credit[0].encode.update.text.value = @(
            "Source: SIPRI | $($Spending.SourcePage) | $([uri]::UnescapeDataString(([uri]$Spending.WorkbookUri).Segments[-1]))",
            "$Year calendar year; current US dollars; available countries/entities only; $($period.MissingCount) missing. Includes SIPRI estimates/uncertain data.",
            'Shares use the available-country sum, not SIPRI world estimates. Dataviz: David Bacci'
        )
        $copy
    }
}

function Set-SipriDonutYear {
    <#
    .SYNOPSIS
    Reuses a downloaded SIPRI year in Deneb's TopN Donut; no additional download.
    .DESCRIPTION
    TopN=0 shows all available countries/entities. Otherwise the original Vega ranking
    groups the remainder as Others. Shares use the same available-country/entity sum
    as Mekko. Missing values are excluded, zeros and source quality flags are retained.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][System.Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Spending,
        [Parameter(Mandatory)][ValidateRange(1949, 9998)][int]$Year,
        [ValidateRange(0, 1000)][int]$TopN = 10
    )
    process {
        $period = @($Spending.Years | Where-Object Year -eq $Year)
        if ($period.Count -ne 1) { throw "SIPRI year $Year is not downloaded. Available: $($Spending.Years.Year -join ', ')." }
        $period = $period[0]
        if ($period.Rows.Count -eq 0 -or @($period.Rows | Where-Object { -not [double]::IsFinite($_.Spend) -or $_.Spend -lt 0 }).Count) {
            throw 'The Donut dataset must contain finite, non-negative spending amounts.'
        }
        if (($period.Rows | Measure-Object Spend -Sum).Sum -le 0) { throw 'The Donut dataset needs positive total spending.' }
        if ($TopN -gt $period.Rows.Count) { throw "TopN exceeds the $($period.Rows.Count) available countries/entities." }
        $rows = @($period.Rows | ForEach-Object {
            [pscustomobject]@{ id = $_.Country; value = $_.Spend; Quality = $_.Quality; Notes = $_.Notes }
        })
        $copy = $Spec | Set-DenebSpecData -DataName table -Data $rows
        $signal = @($copy.signals | Where-Object name -eq configTopN)
        if ($signal.Count -ne 1) { throw 'Expected original TopN Donut configTopN signal.' }
        $signal[0]['init'] = [string]$TopN
        $signal[0].bind.max = $rows.Count
        $copy['title'] = @{
            text = "Military spending | $Year"; fontSize = 16
            subtitle = @('Current USD millions | Source: SIPRI',
                "$($period.MissingCount) missing; estimates included; available-entity shares", 'Dataviz: David Bacci')
            subtitleFontSize = 10
        }
        # Replace the upstream debug tooltip (an array of label sides) with real values.
        $arc = @($copy.marks | Where-Object type -eq arc)
        if ($arc.Count -ne 1) { throw 'Expected one original Donut arc mark.' }
        $arc[0].encode.update.tooltip.signal = "{'Country / group': datum.label, 'USD millions': format(datum.value, ',.1f'), 'Share': format(datum.value / datum.total, '.2%'), 'Countries / entities': length(datum.id)}"
        $copy['usermeta'] = @{ source = $Spending.SourcePage; year = $Year; topN = $TopN; missingCount = $period.MissingCount }
        $copy
    }
}
