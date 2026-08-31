# FDIC BankFind failures API. No key, modules or inflation estimates required.
# Field definitions: https://api.fdic.gov/banks/docs/failure_properties.yaml

function ConvertFrom-FdicFailure {
    param([Parameter(Mandatory)]$Record)
    if ($Record.RESTYPE -ne 'FAILURE') { throw 'Expected an FDIC FAILURE, not an assistance transaction.' }
    if ([string]::IsNullOrWhiteSpace($Record.NAME) -or [string]::IsNullOrWhiteSpace($Record.ID)) {
        throw 'FDIC record is missing its name or unique ID.'
    }
    $date = [datetime]::MinValue
    if (-not [datetime]::TryParseExact([string]$Record.FAILDATE, [string[]]@('M/d/yyyy', 'yyyy-MM-dd'),
        [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$date)) {
        throw "Invalid FDIC failure date: $($Record.FAILDATE)."
    }
    $assets = 0.0
    if (-not [double]::TryParse([string]$Record.QBFASSET, [Globalization.NumberStyles]::Float,
        [cultureinfo]::InvariantCulture, [ref]$assets) -or -not [double]::IsFinite($assets * 1000) -or $assets -le 0) {
        throw "Missing or invalid FDIC assets for $($Record.NAME)."
    }
    [pscustomobject]@{
        Id = [string]$Record.ID; Certificate = [string]$Record.CERT; Bank = [string]$Record.NAME
        Date = $date.ToString('dd/MM/yyyy', [cultureinfo]::InvariantCulture)
        FailureDate = $date.ToString('yyyy-MM-dd'); Year = $date.Year
        # QBFASSET is thousands of USD; the original Absolute spec expects dollars.
        Assets = $assets * 1000; AssetsThousands = $assets
    }
}

function Get-FdicBankFailures {
    <#
    .SYNOPSIS
    Downloads all FDIC bank failures in a date range, starting in 2001 by default.
    .DESCRIPTION
    Only RESTYPE=FAILURE, not assistance. Assets are the last financial report filed
    before failure, in nominal US dollars, not depositor losses or inflation-adjusted
    values. Paging is checked for missing/duplicate records and source-index changes.
    No fallback to the Showcase's old data. Returns a native cross-cell dictionary.
    #>
    [CmdletBinding()]
    param(
        [ValidateRange(1934, 9998)][int]$StartYear = 2001,
        [datetime]$ThroughDate = (Get-Date).Date,
        [ValidateRange(1, 10000)][int]$PageSize = 1000
    )
    $ProgressPreference = 'SilentlyContinue'
    $start = [datetime]::new($StartYear, 1, 1)
    $end = $ThroughDate.Date
    if ($end -lt $start -or $end -gt (Get-Date).Date) { throw 'FDIC date range must end between its start and today.' }
    $filter = [uri]::EscapeDataString("FAILDATE:[$($start.ToString('yyyy-MM-dd')) TO $($end.ToString('yyyy-MM-dd'))] AND RESTYPE:FAILURE")
    $rows = [Collections.Generic.List[object]]::new()
    $queries = [Collections.Generic.List[string]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new()
    $total = $null
    $index = $null
    do {
        $uri = "https://api.fdic.gov/banks/failures?filters=$filter&fields=ID,NAME,FAILDATE,QBFASSET,CERT,RESTYPE&sort_by=FAILDATE&sort_order=ASC&limit=$PageSize&offset=$($rows.Count)&format=json"
        $page = Invoke-RestMethod $uri -TimeoutSec 60 -ErrorAction Stop
        if ($null -eq $page.meta.total -or $null -eq $page.data -or -not $page.meta.index.name) {
            throw 'Unexpected FDIC API response; expected records, total and source index.'
        }
        if ($null -eq $total) { $total = [int]$page.meta.total; $index = [string]$page.meta.index.name }
        if ($total -ne [int]$page.meta.total -or $index -ne $page.meta.index.name) {
            throw 'FDIC data changed during pagination. Re-run the download.'
        }
        $queries.Add($uri)
        if (@($page.data).Count -eq 0 -and $rows.Count -lt $total) { throw 'FDIC returned an incomplete page.' }
        foreach ($item in $page.data) {
            $row = ConvertFrom-FdicFailure $item.data
            if (-not $ids.Add($row.Id)) { throw "Duplicate FDIC record $($row.Id) across pages. Re-run the download." }
            $date = [datetime]::ParseExact($row.FailureDate, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)
            if ($date -lt $start -or $date -gt $end) { throw 'FDIC returned a failure outside the requested range.' }
            $rows.Add($row)
        }
    } while ($rows.Count -lt $total)
    if ($rows.Count -ne $total) { throw 'FDIC record count does not match the API total.' }
    $years = foreach ($year in $StartYear..$end.Year) {
        $selected = @($rows | Where-Object Year -eq $year)
        [pscustomobject]@{ Year = $year; Failures = $selected.Count; AssetsBillions = ($selected | Measure-Object Assets -Sum).Sum / 1e9 }
    }
    [ordered]@{
        Rows = $rows.ToArray(); Years = @($years); StartYear = $StartYear; ThroughDate = $end.ToString('yyyy-MM-dd')
        SourcePage = 'https://banks.data.fdic.gov/explore/failures'; Queries = $queries.ToArray()
        SourceIndex = $index; DownloadedAt = [datetime]::UtcNow.ToString('o'); Unit = 'USD, nominal assets before failure'
    }
}

function Set-FdicBankFailurePeriod {
    <#
    .SYNOPSIS
    Selects downloaded years for the original Bank Failure Bubble Chart (Absolute).
    .DESCRIPTION
    Changes the input, date-axis bounds, title and source note only. Force animation,
    bubble-size formula, dimensions, colors and author credit stay original.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Failures,
        [Parameter(Mandatory)][ValidateRange(1934, 9998)][int]$StartYear,
        [Parameter(Mandatory)][ValidateRange(1934, 9998)][int]$EndYear
    )
    process {
        $through = [datetime]::ParseExact($Failures.ThroughDate, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)
        if ($StartYear -gt $EndYear -or $StartYear -lt $Failures.StartYear -or $EndYear -gt $through.Year) {
            throw "Select years within downloaded FDIC coverage: $($Failures.StartYear)-$($through.Year)."
        }
        $rows = @($Failures.Rows | Where-Object { $_.Year -ge $StartYear -and $_.Year -le $EndYear })
        if ($rows.Count -eq 0) { throw "No bank failures in $StartYear-$EndYear; no bubbles to display." }
        if (@($rows | Where-Object { -not [double]::IsFinite($_.Assets) -or $_.Assets -le 0 }).Count) {
            throw 'FDIC bubble sizes require positive finite assets.'
        }
        if (($Spec | ConvertTo-Json -Depth 100 -Compress) -match 'InflationAdjustedAssets') {
            throw 'Use the Absolute FDIC spec; this adapter does not calculate inflation-adjusted assets.'
        }
        $copy = $Spec | Set-DenebSpecData -DataName table -Data $rows
        $axis = @($copy.scales | Where-Object name -eq x)
        $title = @($copy.marks | Where-Object { $_.encode.enter.text.value -contains 'Bank Failures' })
        $subtitle = @($copy.marks | Where-Object { $_.encode.enter.text.value -match '^Reported by' })
        if ($axis.Count -ne 1 -or $title.Count -ne 1 -or $subtitle.Count -ne 1) { throw 'Expected original Bank Failure Absolute spec structure.' }
        # Replace the original hard-coded 2025 limit; local datetime matches Vega's date parser.
        $axis[0]['domainMin'] = @{ signal = "datetime($StartYear, 0, 1)" }
        $axis[0]['domainMax'] = @{ signal = "datetime($($EndYear + 1), 0, 1)" }
        $title[0].encode.enter.text.value = @('Bank Failures', "$StartYear-$EndYear")
        $subtitle[0].encode.enter.text.value = "Reported by FDIC | nominal assets before failure | data through $($Failures.ThroughDate)"
        $copy['usermeta'] = @{ source = $Failures.SourcePage; throughDate = $Failures.ThroughDate; startYear = $StartYear; endYear = $EndYear }
        $copy
    }
}
