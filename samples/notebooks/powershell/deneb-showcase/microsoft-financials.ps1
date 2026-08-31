# Microsoft-specific data adapter. Load helper.ps1 before rendering a spec.
# Values are as reported at each earnings release, in USD millions (not restated).

function ConvertFrom-MicrosoftFinancialTable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Html,
        [Parameter(Mandatory)][int]$Year,
        [Parameter(Mandatory)][ValidateRange(1, 4)][int]$Quarter
    )

    # Match Microsoft's tagged financial table, not navigation or a positional column.
    $tables = @([regex]::Matches($Html, '(?is)<table\b[^>]*>.*?</table>') |
        Where-Object { $_.Value -match 'us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax' })
    if ($tables.Count -ne 1) { throw "Expected one Microsoft financial table for FY$Year Q$Quarter." }
    $table = $tables[0].Value
    if ($table -notmatch '(?i)In millions') { throw 'Unexpected financial statement units.' }
    $calendarYear = if ($Quarter -le 2) { $Year - 1 } else { $Year }
    $expectedEnd = @('', 'September 30', 'December 31', 'March 31', 'June 30')[$Quarter]
    $result = @{ Quarter = @{}; Ytd = @{} }
    $columns = @{ Quarter = 'subcategoryCurYearQuarter'; Ytd = 'subcategoryCurYearYTD' }
    foreach ($period in $columns.Keys) {
        $column = $columns[$period]
        $header = [regex]::Match($table, "(?is)<th\b(?=[^>]*\bid=[`"']$column[`"'])[^>]*>(.*?)</th>")
        if ($header.Success) {
            $headerText = [regex]::Replace($header.Groups[1].Value, '<[^>]+>', '').Trim()
            if ($headerText -ne "$calendarYear") { throw "Unexpected year in $column`: $headerText." }
        }
        elseif ($period -eq 'Quarter' -and $Quarter -ne 4) {
            throw 'Missing current three-month column; refusing to substitute YTD or prior-year data.'
        }
        if ($header.Success) {
            $groupId = if ($period -eq 'Quarter') { 'subcategoryQuarter' } else { 'subcategoryYTD' }
            $group = [regex]::Match($table, "(?is)<th\b(?=[^>]*\bid=[`"']$groupId[`"'])[^>]*>(.*?)</th>")
            $groupText = [System.Net.WebUtility]::HtmlDecode([regex]::Replace($group.Groups[1].Value, '<[^>]+>', ' ')) -replace '\s+', ' '
            $duration = if ($period -eq 'Quarter') { 'Three Months' } else { @('', 'Three Months', 'Six Months', 'Nine Months', 'Year')[$Quarter] }
            if ($groupText -notmatch "$duration Ended $expectedEnd") {
                throw "Unexpected reporting period in $groupId`: $groupText."
            }
        }
    }

    foreach ($row in [regex]::Matches($table, '(?is)<tr\b[^>]*>.*?</tr>')) {
        $tag = [regex]::Match($row.Value, '(?is)<span\b[^>]*class=["''][^"'']*\bc-tooltip\b[^"'']*["''][^>]*>(.*?)</span>')
        if (-not $tag.Success) { continue }
        $tags = @([regex]::Matches([System.Net.WebUtility]::HtmlDecode($tag.Groups[1].Value), '(?:us-gaap|msft):\w+') |
            ForEach-Object { $_.Value })
        if ($tags.Count -eq 0) { continue }
        $key = $tags -join '|'
        foreach ($cell in [regex]::Matches($row.Value, '(?is)<td\b[^>]*>.*?</td>')) {
            $headers = [regex]::Match($cell.Value, '(?i)\bheaders=["'']([^"'']+)["'']').Groups[1].Value -split '\s+'
            foreach ($period in $columns.Keys) {
                if ($columns[$period] -notin $headers) { continue }
                $price = [regex]::Match($cell.Value, '(?is)<span\b[^>]*\bitemprop=["'']price["''][^>]*>(.*?)</span>')
                $number = [System.Net.WebUtility]::HtmlDecode($price.Groups[1].Value).Trim()
                if (-not $price.Success -or $number -notmatch '^\(?-?\d[\d,]*\)?$') {
                    # EPS and weighted share rows are irrelevant; required monetary rows are checked below.
                    continue
                }
                if ($result[$period].ContainsKey($key)) { throw "Duplicate financial fact: $period / $key." }
                $result[$period][$key] = [decimal]::Parse($number,
                    [System.Globalization.NumberStyles]::Number -bor [System.Globalization.NumberStyles]::AllowParentheses,
                    [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }
    $result
}

function ConvertTo-MicrosoftQuarter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Year,
        [Parameter(Mandatory)][ValidateRange(1, 4)][int]$Quarter,
        [Parameter(Mandatory)][hashtable]$Income,
        [Parameter(Mandatory)][hashtable]$Segments,
        [hashtable]$NineMonthIncome,
        [hashtable]$NineMonthSegments
    )

    $revenueTag = 'us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax'
    $costTag = 'us-gaap:CostOfGoodsAndServicesSold'
    $fields = [ordered]@{
        Revenue = $revenueTag
        ProductRevenue = "$revenueTag|us-gaap:ProductMember"
        ServiceRevenue = "$revenueTag|us-gaap:ServiceOtherMember"
        CostOfRevenue = $costTag
        ProductCosts = "$costTag|us-gaap:ProductMember"
        ServiceCosts = "$costTag|us-gaap:ServiceOtherMember"
        GrossProfit = 'us-gaap:GrossProfit'
        ResearchAndDevelopment = 'us-gaap:ResearchAndDevelopmentExpense'
        SalesAndMarketing = 'us-gaap:SellingAndMarketingExpense'
        GeneralAndAdministrative = 'us-gaap:GeneralAndAdministrativeExpense'
        OperatingIncome = 'us-gaap:OperatingIncomeLoss'
        OtherIncomeExpense = 'us-gaap:NonoperatingIncomeExpense'
        PretaxIncome = 'us-gaap:IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest'
        IncomeTax = 'us-gaap:IncomeTaxExpenseBenefit'
        NetIncome = 'us-gaap:NetIncomeLoss'
        IntelligentCloud = "$revenueTag|msft:IntelligentCloudMember"
        Productivity = "$revenueTag|msft:ProductivityAndBusinessProcessesMember"
        PersonalComputing = "$revenueTag|msft:MorePersonalComputingMember"
    }
    $end = [datetime]::new($Year - 1, 7, 1).AddMonths(3 * $Quarter).AddDays(-1)
    $baseUri = "https://www.microsoft.com/en-us/Investor/earnings/FY-$Year-Q$Quarter"
    $record = [ordered]@{
        Year = $Year; Quarter = $Quarter; PeriodEnd = $end.ToString('yyyy-MM-dd')
        Unit = 'USD millions'; IncomeSource = "$baseUri/income-statements"
        SegmentSource = "$baseUri/segment-revenues"; DerivedFromAnnual = @()
    }
    foreach ($name in $fields.Keys) {
        $isSegment = $name -in @('IntelligentCloud', 'Productivity', 'PersonalComputing')
        $table = if ($isSegment) { $Segments } else { $Income }
        $nineMonths = if ($isSegment) { $NineMonthSegments } else { $NineMonthIncome }
        $key = $fields[$name]
        if ($table.Quarter.ContainsKey($key)) {
            $record[$name] = $table.Quarter[$key]
        }
        elseif ($Quarter -eq 4 -and $table.Ytd.ContainsKey($key) -and $null -ne $nineMonths -and $nineMonths.Ytd.ContainsKey($key)) {
            $record[$name] = $table.Ytd[$key] - $nineMonths.Ytd[$key]
            $record.DerivedFromAnnual += $name
        }
        else { throw "Missing current-period fact for FY$Year Q$Quarter`: $name ($key)." }
    }
    $record['OperatingExpenses'] = $record.ResearchAndDevelopment + $record.SalesAndMarketing + $record.GeneralAndAdministrative
    if ($Quarter -eq 4 -and $record.DerivedFromAnnual.Count -gt 0) {
        $record['NineMonthIncomeSource'] = $record.IncomeSource.Replace('-Q4/', '-Q3/')
        $record['NineMonthSegmentSource'] = $record.SegmentSource.Replace('-Q4/', '-Q3/')
    }
    $value = [pscustomobject]$record
    Assert-MicrosoftQuarter $value
    $value
}

function Assert-MicrosoftQuarter {
    param([Parameter(Mandatory)]$Value)
    $balances = @{
        'segment revenue' = $Value.IntelligentCloud + $Value.Productivity + $Value.PersonalComputing - $Value.Revenue
        'product/service revenue' = $Value.ProductRevenue + $Value.ServiceRevenue - $Value.Revenue
        'cost of revenue' = $Value.ProductCosts + $Value.ServiceCosts - $Value.CostOfRevenue
        'gross profit' = $Value.Revenue - $Value.CostOfRevenue - $Value.GrossProfit
        'operating income' = $Value.GrossProfit - $Value.OperatingExpenses - $Value.OperatingIncome
        'pretax income' = $Value.OperatingIncome + $Value.OtherIncomeExpense - $Value.PretaxIncome
        'net income' = $Value.PretaxIncome - $Value.IncomeTax - $Value.NetIncome
    }
    foreach ($balance in $balances.GetEnumerator()) {
        if ($balance.Value -ne 0) { throw "Unbalanced FY$($Value.Year) Q$($Value.Quarter): $($balance.Key)." }
    }
    foreach ($name in @('Revenue', 'IntelligentCloud', 'Productivity', 'PersonalComputing', 'GrossProfit',
        'CostOfRevenue', 'ProductCosts', 'ServiceCosts', 'OperatingIncome', 'OperatingExpenses', 'PretaxIncome',
        'NetIncome', 'IncomeTax', 'ResearchAndDevelopment', 'SalesAndMarketing', 'GeneralAndAdministrative')) {
        if ($null -eq $Value.$name -or $Value.$name -le 0) {
            throw "This Sankey adapter requires positive $name; FY$($Value.Year) Q$($Value.Quarter) is unsupported."
        }
    }
}

function Get-MicrosoftQuarterlyFinancials {
    <#
    .SYNOPSIS
    Downloads all available, completed Microsoft fiscal quarters from StartYear.
    .DESCRIPTION
    FY2023 Q1 ends September 2022. Current-release values are not retrospectively
    restated. Q4 uses annual minus Q3 YTD where the release has no quarterly column.
    HTTP 404 periods are listed explicitly; network, parsing and balance errors fail
    the download instead of silently introducing gaps or substituting sample data.
    #>
    [CmdletBinding()]
    param(
        [ValidateRange(2023, 9998)][int]$StartYear = 2023,
        [datetime]$ThroughDate = (Get-Date)
    )

    $ProgressPreference = 'SilentlyContinue'
    $quarters = [System.Collections.Generic.List[object]]::new()
    $unavailable = [System.Collections.Generic.List[object]]::new()
    $lastYear = $ThroughDate.Year + [int]($ThroughDate.Month -ge 7)
    for ($year = $StartYear; $year -le $lastYear; $year++) {
        $nineMonthIncome = $null
        $nineMonthSegments = $null
        for ($quarter = 1; $quarter -le 4; $quarter++) {
            $end = [datetime]::new($year - 1, 7, 1).AddMonths(3 * $quarter).AddDays(-1)
            if ($end -gt $ThroughDate.Date) { continue }
            $baseUri = "https://www.microsoft.com/en-us/Investor/earnings/FY-$year-Q$quarter"
            Write-Verbose "Downloading FY$year Q$quarter (ended $($end.ToString('yyyy-MM-dd')))."
            try {
                $incomeResponse = Invoke-WebRequest "$baseUri/income-statements" -TimeoutSec 30 -MaximumRetryCount 2 -RetryIntervalSec 1 -ErrorAction Stop
            }
            catch {
                if ([int]$_.Exception.Response.StatusCode -eq 404) {
                    $unavailable.Add([pscustomobject]@{ Year = $year; Quarter = $quarter; Url = "$baseUri/income-statements"; Status = 'HTTP 404' })
                    Write-Warning "FY$year Q$quarter is unavailable at Microsoft IR (HTTP 404); no data substituted."
                    continue
                }
                throw
            }
            $segmentResponse = Invoke-WebRequest "$baseUri/segment-revenues" -TimeoutSec 30 -MaximumRetryCount 2 -RetryIntervalSec 1 -ErrorAction Stop
            $income = ConvertFrom-MicrosoftFinancialTable -Html $incomeResponse.Content -Year $year -Quarter $quarter
            $segments = ConvertFrom-MicrosoftFinancialTable -Html $segmentResponse.Content -Year $year -Quarter $quarter
            $value = ConvertTo-MicrosoftQuarter -Year $year -Quarter $quarter -Income $income -Segments $segments `
                -NineMonthIncome $nineMonthIncome -NineMonthSegments $nineMonthSegments
            $quarters.Add($value)
            if ($quarter -eq 3) { $nineMonthIncome = $income; $nineMonthSegments = $segments }
        }
    }
    if ($quarters.Count -eq 0) { throw 'No completed Microsoft fiscal quarters could be loaded for the requested range.' }
    # A native dictionary survives the current Verso cross-cell variable bridge;
    # a top-level PSCustomObject loses its ETS note properties when unwrapped.
    [ordered]@{
        DownloadedAt = [datetime]::UtcNow.ToString('o')
        ThroughDate = $ThroughDate.ToString('yyyy-MM-dd')
        Quarters = $quarters.ToArray()
        Unavailable = $unavailable.ToArray()
    }
}

function Set-MicrosoftSankeyPeriod {
    <#
    .SYNOPSIS
    Binds a fiscal year/quarter to the original Deneb Sankey spec.
    .DESCRIPTION
    Changes only the input dataset, title and source credit. The original transforms,
    scales, signals, dimensions and drawing instructions are preserved. Other income
    and expense are separate flows so that every intermediate node balances.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][System.Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Financials,
        [Parameter(Mandatory)][ValidateRange(2023, 9998)][int]$Year,
        [Parameter(Mandatory)][ValidateRange(1, 4)][int]$Quarter
    )
    process {
        $matches = @($Financials.Quarters | Where-Object { $_.Year -eq $Year -and $_.Quarter -eq $Quarter })
        if ($matches.Count -ne 1) {
            $available = ($Financials.Quarters | ForEach-Object { "FY$($_.Year) Q$($_.Quarter)" }) -join ', '
            throw "Expected one downloaded record for FY$Year Q$Quarter. Available: $available. Run the download cell first."
        }
        $value = $matches[0]
        Assert-MicrosoftQuarter $value
        $rows = [System.Collections.Generic.List[object]]::new()
        # Keep the original six-column grammar; segment totals replace product estimates.
        $nodes = @(
            @('Intelligent Cloud', 1, 1), @('Productivity', 1, 2), @('Personal Computing', 1, 3),
            @('Revenue', 2, 1), @('Gross Profit', 3, 1), @('Cost of Revenue', 3, 2),
            @('Operating Profit', 4, 1), @('Operating Expenses', 4, 3), @('Product Costs', 4, 4), @('Service Costs', 4, 5),
            @('Pretax Income', 5, 1), @('R&D', 5, 3), @('S&M', 5, 4), @('G&A', 5, 5),
            @('Net Profit', 6, 1), @('Tax', 6, 2)
        )
        $edges = @(
            @('Intelligent Cloud', 'Revenue', $value.IntelligentCloud),
            @('Productivity', 'Revenue', $value.Productivity),
            @('Personal Computing', 'Revenue', $value.PersonalComputing),
            @('Revenue', 'Gross Profit', $value.GrossProfit), @('Revenue', 'Cost of Revenue', $value.CostOfRevenue),
            @('Gross Profit', 'Operating Profit', $value.OperatingIncome),
            @('Gross Profit', 'Operating Expenses', $value.OperatingExpenses),
            @('Cost of Revenue', 'Product Costs', $value.ProductCosts), @('Cost of Revenue', 'Service Costs', $value.ServiceCosts),
            @('Operating Expenses', 'R&D', $value.ResearchAndDevelopment),
            @('Operating Expenses', 'S&M', $value.SalesAndMarketing),
            @('Operating Expenses', 'G&A', $value.GeneralAndAdministrative),
            @('Pretax Income', 'Net Profit', $value.NetIncome), @('Pretax Income', 'Tax', $value.IncomeTax)
        )
        if ($value.OtherIncomeExpense -lt 0) {
            $nodes += ,@('Other Expense', 5, 2)
            $edges += ,@('Operating Profit', 'Other Expense', -$value.OtherIncomeExpense)
            $edges += ,@('Operating Profit', 'Pretax Income', $value.PretaxIncome)
        }
        else {
            $edges += ,@('Operating Profit', 'Pretax Income', $value.OperatingIncome)
            if ($value.OtherIncomeExpense -gt 0) {
                $nodes += ,@('Other Income', 4, 2)
                $edges += ,@('Other Income', 'Pretax Income', $value.OtherIncomeExpense)
            }
        }
        foreach ($node in $nodes) {
            $rows.Add(@{ category = $node[0]; stack = $node[1]; sort = $node[2]; gap = 10; labels = 'right' })
        }
        foreach ($edge in $edges) {
            $rows.Add(@{ source = $edge[0]; destination = $edge[1]; value = [decimal]$edge[2] / 1000 })
        }
        $copy = $Spec | Set-DenebSpecData -DataName 'input' -Data $rows.ToArray()
        $copy.title.text = "Microsoft's FY$Year Q$Quarter Income Statement"
        # Preserve the author's attribution and footer position, replacing only the stale source.
        $credit = @($copy.marks | Where-Object { ($_.encode.update.text.value -join ' ') -match 'Dataviz: David Bacci' })
        if ($credit.Count -ne 1) { throw 'Expected the original Sankey author/source credit.' }
        $credit[0].encode.update.text.value = @(
            "Source: $($value.IncomeSource) + segment-revenues (as reported)",
            "Quarter ended $($value.PeriodEnd); USD billions; three revenue segments; Dataviz: David Bacci"
        )
        $copy
    }
}
