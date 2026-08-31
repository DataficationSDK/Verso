# World Bank WDI SP.POP.TOTL; annual population, not a live population counter.
# https://datahelpdesk.worldbank.org/knowledgebase/articles/898581-api-basic-call-structures

function Get-WorldBankPages {
    param([Parameter(Mandatory)][string]$Uri)
    $rows = [Collections.Generic.List[object]]::new()
    $queries = [Collections.Generic.List[string]]::new()
    $pages = 1; $total = -1; $updated = $null
    for ($page = 1; $page -le $pages; $page++) {
        $url = "$Uri&page=$page"
        $response = Invoke-RestMethod -Uri $url -TimeoutSec 90 -ErrorAction Stop
        if (@($response).Count -ne 2 -or $null -eq $response[0].total -or
            [int]$response[0].page -ne $page -or [int]$response[0].pages -lt 1 -or [int]$response[0].pages -gt 100) {
            throw "Invalid World Bank paginated response at $url. Metadata: $($response[0] | ConvertTo-Json -Depth 5 -Compress)"
        }
        if ($page -eq 1) {
            $pages = [int]$response[0].pages; $total = [int]$response[0].total; $updated = $response[0].lastupdated
        }
        elseif ([int]$response[0].total -ne $total -or [int]$response[0].pages -ne $pages -or $response[0].lastupdated -ne $updated) {
            throw 'World Bank pagination changed during download. Re-run to get a consistent snapshot.'
        }
        foreach ($row in @($response[1])) { if ($null -ne $row) { $rows.Add($row) } }
        $queries.Add($url)
    }
    if ($rows.Count -ne $total) { throw 'World Bank pagination returned an incomplete result.' }
    [ordered]@{ Rows = $rows.ToArray(); Queries = $queries.ToArray(); LastUpdated = $updated }
}

function ConvertFrom-WorldBankPopulation {
    param([Parameter(Mandatory)]$CountryRows, [Parameter(Mandatory)]$PopulationRows,
        [Parameter(Mandatory)][int]$StartYear, [Parameter(Mandatory)][int]$EndYear)
    $countries = @{}
    foreach ($country in $CountryRows) {
        if (-not $country.id -or -not $country.name -or -not $country.region.id -or $countries.ContainsKey($country.id)) {
            throw 'Invalid or duplicate World Bank country reference.'
        }
        $countries[$country.id] = $country
    }
    $rows = [Collections.Generic.List[object]]::new(); $seen = [Collections.Generic.HashSet[string]]::new()
    foreach ($record in $PopulationRows) {
        $id = [string]$record.countryiso3code
        # WDI leaves ISO3 blank for some income aggregates; resolve using its own ISO2 reference.
        if (-not $id) {
            $matches = @($CountryRows | Where-Object { $_.iso2Code -and $_.iso2Code -eq $record.country.id })
            if ($matches.Count -eq 1 -and $matches[0].name -eq $record.country.value) { $id = $matches[0].id }
        }
        if ($record.indicator.id -ne 'SP.POP.TOTL' -or -not $countries.ContainsKey($id) -or
            [string]$record.date -notmatch '^\d{4}$') { throw 'Unexpected World Bank population record or unknown country.' }
        $year = [int]$record.date
        if ($year -lt $StartYear -or $year -gt $EndYear -or -not $seen.Add("$id/$year")) { throw 'Out-of-range or duplicate World Bank population record.' }
        if ($id -ne 'WLD' -and $countries[$id].region.id -eq 'NA') { continue }
        $value = $null
        if ($null -ne $record.value) {
            $number = 0.0
            if (-not [double]::TryParse([string]$record.value, [Globalization.NumberStyles]::Float,
                [cultureinfo]::InvariantCulture, [ref]$number) -or -not [double]::IsFinite($number) -or
                $number -lt 0 -or $number -ne [math]::Truncate($number)) { throw 'Invalid World Bank population value.' }
            # WDI can mark forecasts. Do not silently present them as observed/estimated history.
            if ($record.obs_status -ne 'F') { $value = $number }
        }
        $rows.Add([pscustomobject]@{ Code = $id; Entity = $(if ($id -eq 'WLD') { 'World' } else { $countries[$id].name })
            Continent = $countries[$id].region.value.Trim(); RegionId = $countries[$id].region.id
            Year = $year; Population = $value; ObservationStatus = [string]$record.obs_status })
    }
    $years = foreach ($year in $StartYear..$EndYear) {
        $available = @($rows | Where-Object { $_.Year -eq $year -and $_.Code -ne 'WLD' -and $null -ne $_.Population })
        $world = @($rows | Where-Object { $_.Year -eq $year -and $_.Code -eq 'WLD' -and $null -ne $_.Population })
        if ($available.Count -and $world.Count -eq 1) {
            [pscustomobject]@{ Year = $year; AvailableEconomies = $available.Count; WorldPopulation = $world[0].Population }
        }
    }
    if (@($years).Count -eq 0) { throw 'No published population years with country and world data.' }
    [ordered]@{ Rows = $rows.ToArray(); Years = @($years); StartYear = $StartYear; RequestedEndYear = $EndYear
        LatestYear = @($years)[-1].Year; Indicator = 'SP.POP.TOTL'
        SourcePage = 'https://data.worldbank.org/indicator/SP.POP.TOTL'
        Note = 'Countries/economies, excluding aggregates except the separately reported WLD total. Colors use current World Bank regions, not historical classifications. Missing/forecast values remain null.' }
}

function Get-WorldBankPopulation {
    <# Downloads annual WDI population for all economies, with checked pagination and no API key. #>
    [CmdletBinding()]
    param([ValidateRange(1960, 9998)][int]$StartYear = 1960,
        [ValidateRange(1960, 9998)][int]$EndYear = ((Get-Date).Year - 1))
    if ($StartYear -gt $EndYear -or $EndYear -ge (Get-Date).Year) { throw 'Select completed calendar years, StartYear <= EndYear.' }
    $countries = Get-WorldBankPages 'https://api.worldbank.org/v2/country?format=json&per_page=400'
    $population = Get-WorldBankPages "https://api.worldbank.org/v2/country/all/indicator/SP.POP.TOTL?format=json&source=2&date=$StartYear`:$EndYear&per_page=10000"
    $result = ConvertFrom-WorldBankPopulation $countries.Rows $population.Rows $StartYear $EndYear
    $result['Queries'] = @($countries.Queries) + @($population.Queries)
    $result['LastUpdated'] = $population.LastUpdated
    $result['DownloadedAt'] = [datetime]::UtcNow.ToString('o')
    $result
}

function Set-WorldBankPopulationRace {
    <# Preserves the original race layout. Keeps only complete annual series within the chosen period.
       The world total is the source's WLD series, not the sum of selected economies or Top N.
       Between-year tweening is visual interpolation, not additional observations. #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Population,
        [ValidateRange(1960, 9998)][int]$StartYear = 1960,
        [ValidateRange(1960, 9998)][int]$EndYear = $Population.LatestYear,
        [string]$Region = 'All', [ValidateRange(3, 20)][int]$TopN = 12,
        [ValidateRange(0.1, 5)][double]$SecondsPerYear = 0.6, [bool]$AutoPlay = $false)
    process {
        if ($StartYear -ge $EndYear -or $StartYear -lt $Population.StartYear -or $EndYear -gt $Population.LatestYear) {
            throw 'Population race needs at least two downloaded years within the published range.'
        }
        $period = @($Population.Rows | Where-Object { $_.Year -ge $StartYear -and $_.Year -le $EndYear })
        $world = @($period | Where-Object { $_.Code -eq 'WLD' -and $null -ne $_.Population })
        if ($world.Count -ne $EndYear - $StartYear + 1) { throw 'Population world series has missing years.' }
        $candidate = @($period | Where-Object { $_.Code -ne 'WLD' -and ($Region -eq 'All' -or $_.RegionId -eq $Region) })
        if (-not $candidate.Count) { throw "No economies for World Bank region '$Region'. Use All or a downloaded RegionId." }
        $selected = [Collections.Generic.List[object]]::new(); $excluded = [Collections.Generic.List[string]]::new()
        foreach ($group in $candidate | Group-Object Code) {
            $valid = @($group.Group | Where-Object { $null -ne $_.Population })
            if ($valid.Count -ne $EndYear - $StartYear + 1 -or @($valid.Year | Select-Object -Unique).Count -ne $valid.Count) {
                $excluded.Add($group.Name); continue
            }
            foreach ($row in $valid) { $selected.Add($row) }
        }
        $count = @($selected.Code | Select-Object -Unique).Count
        if ($count -lt 2) { throw 'Too few complete population series. Shorten the period or choose another region.' }
        $copy = $Spec | Set-DenebSpecData -DataName input -Data (@($selected.ToArray()) + $world) |
            Set-DenebSpecSize -Width 650 -Height 360
        foreach ($signal in $copy.signals) {
            switch ($signal.name) {
                topn { $signal.value = [math]::Min($TopN, $count) }
                duration { $signal.value = $SecondsPerYear }
                run { $signal.value = $AutoPlay; $signal.on += @{ events = @{ signal = 'year' }; update = 'year >= extent[1] ? false : run' } }
                year { $signal.init = 'extent[0]' }
                t { $signal.on[0].update = 'clamp((timer-deltaTime-start)/(duration*1000),0,1)' }
            }
        }
        $copy.title.text = 'Population by country/economy | World Bank'
        $copy.title.fontSize = 24; $copy.title.subtitleFontSize = 12
        $copy.title.subtitle = @("$StartYear-$EndYear | Top $([math]::Min($TopN,$count)) of $count complete series | Region: $Region",
            "$($excluded.Count) incomplete series excluded. Click chart to play/pause; restart at bottom right.")
        $copy.legends[0].orient = 'bottom'; $copy.legends[0].direction = 'horizontal'
        $copy.legends[0].columns = 2; $copy.legends[0].labelLimit = 280; $copy.legends[0].offset = 15
        $copy.legends[0].title = 'Current World Bank region'
        @($copy.scales | Where-Object name -eq 'color1')[0].range += @('#B76B00', '#6447A6', '#727272')
        @($copy.scales | Where-Object name -eq 'color2')[0].range += @('#FFD279', '#B9A1F3', '#CCCCCC')
        foreach ($mark in $copy.marks[0].marks) {
            if ($mark.name -eq 'raceBars') {
                $mark.encode.update.tooltip = @{ signal = "{'Country/economy':datum.Entity,'Year':datum.Year,'Population (annual)':format(datum.Population,','),'Region':datum.Continent}" }
            }
            if ($mark.encode.update.fontSize.value -eq 90) { $mark.encode.update.fontSize.value = 64 }
            if ($mark.encode.update.fontSize.value -eq 28) { $mark.encode.update.fontSize.value = 22 }
        }
        $copy.marks[-1].marks[0].encode.update.text.value = 'Source: World Bank WDI | Between-year animation interpolated | Dataviz: David Bacci'
        $copy.description = 'Original Population Bar Chart Race by David Bacci; population data: World Bank WDI SP.POP.TOTL.'
        $copy.usermeta = [ordered]@{ Source = 'World Bank'; Indicator = 'SP.POP.TOTL'; StartYear = $StartYear; EndYear = $EndYear
            Region = $Region; CompleteEconomies = $count; ExcludedEconomies = $excluded.ToArray(); LastUpdated = $Population.LastUpdated
            DownloadedAt = $Population.DownloadedAt; Queries = $Population.Queries; Note = $Population.Note }
        $copy
    }
}
