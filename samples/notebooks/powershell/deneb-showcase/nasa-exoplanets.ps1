# NASA Exoplanet Archive: PSCompPars has one composite row per planet.
# https://exoplanetarchive.ipac.caltech.edu/docs/API_PS_columns.html
# Composite parameters can come from different references; this is not a habitability score.

function ConvertFrom-NasaExoplanets {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records)
    $seen = [Collections.Generic.HashSet[string]]::new()
    foreach ($record in $Records) {
        if (-not $record.pl_name -or -not $record.hostname -or -not $record.discoverymethod -or -not $seen.Add($record.pl_name)) {
            throw 'Invalid or duplicate NASA planet identity.'
        }
        $row = [ordered]@{ Name = $record.pl_name; Host = $record.hostname; Method = $record.discoverymethod
            Controversial = $record.pl_controv_flag; RadiusLimit = $record.pl_radelim
            PeriodLimit = $record.pl_orbperlim; TemperatureLimit = $record.st_tefflim
            Url = "https://exoplanetarchive.ipac.caltech.edu/overview/$([uri]::EscapeDataString($record.pl_name))" }
        $fields = [ordered]@{ RadiusEarth = 'pl_rade'; PeriodDays = 'pl_orbper'; DistancePc = 'sy_dist'
            StarTempK = 'st_teff'; DiscoveryYear = 'disc_year' }
        foreach ($field in $fields.GetEnumerator()) {
            if (-not $record.PSObject.Properties[$field.Value]) { throw "NASA response is missing column $($field.Value)." }
            $value = $null
            if ($null -ne $record.($field.Value)) {
                $number = 0.0
                if (-not [double]::TryParse([string]$record.($field.Value), [Globalization.NumberStyles]::Float,
                    [cultureinfo]::InvariantCulture, [ref]$number) -or -not [double]::IsFinite($number)) { throw 'Invalid NASA numeric value.' }
                $value = $number
            }
            $row[$field.Key] = $value
        }
        $row['HasAllDimensions'] = @($fields.Keys | Where-Object { $null -eq $row[$_] -or $row[$_] -le 0 }).Count -eq 0
        $row['HasLimits'] = @('RadiusLimit', 'PeriodLimit', 'TemperatureLimit' | Where-Object {
            $null -eq $row[$_] -or $row[$_] -ne 0
        }).Count -gt 0
        if ($null -ne $row.DiscoveryYear -and ($row.DiscoveryYear -ne [math]::Truncate($row.DiscoveryYear) -or
            $row.DiscoveryYear -lt 1900 -or $row.DiscoveryYear -gt (Get-Date).Year)) { throw 'Invalid NASA discovery year.' }
        [pscustomobject]$row
    }
}

function Get-NasaExoplanets {
    <# Downloads the current composite catalogue, retaining nulls and limit flags.
       The cap is checked, not silently used to truncate the catalogue. No API key. #>
    [CmdletBinding()]
    param([ValidateRange(10, 50000)][int]$MaxRecords = 20000)
    $query = "select top $($MaxRecords + 1) pl_name,hostname,discoverymethod,disc_year,pl_rade,pl_radelim,pl_orbper,pl_orbperlim,st_teff,st_tefflim,sy_dist,pl_controv_flag from pscomppars order by pl_name"
    $uri = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=$([uri]::EscapeDataString($query))&format=json"
    $records = @(Invoke-RestMethod -Uri $uri -TimeoutSec 120 -ErrorAction Stop)
    # Invoke-RestMethod may return an array as one pipeline object.
    if ($records.Count -eq 1 -and $records[0] -is [array]) { $records = @($records[0]) }
    if (-not $records.Count) { throw 'NASA returned an empty catalogue.' }
    if ($records.Count -gt $MaxRecords) { throw 'NASA catalogue exceeds MaxRecords; raise the cap explicitly.' }
    $rows = @(ConvertFrom-NasaExoplanets -Records $records)
    $eligible = @($rows | Where-Object { $_.HasAllDimensions -and -not $_.HasLimits -and $_.Controversial -eq 0 })
    [ordered]@{ Rows = $rows; DownloadedAt = [datetime]::UtcNow.ToString('o'); Query = $uri; Table = 'pscomppars'
        Summary = [pscustomobject]@{ Planets = $rows.Count; MissingOrNonPositive = @($rows | Where-Object { -not $_.HasAllDimensions }).Count
            WithLimitsOrUnknownFlags = @($rows | Where-Object HasLimits).Count; Eligible = $eligible.Count }
        SourcePage = 'https://exoplanetarchive.ipac.caltech.edu/docs/API_PS_columns.html'
        Note = 'One composite row per planet; parameters can combine references and derived values. Positive complete dimensions with zero limit flags and noncontroversial status only in the chart. Not a representative sample, not a habitability ranking.' }
}

function Set-NasaExoplanetCoordinates {
    <# Keeps Deneb's axis reordering, range brushes and reset. Only data-specific fields,
       scales, labels and tooltips change. Radius, period and distance use log scales. #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Catalogue,
        [ValidateRange(1900, 9998)][int]$MinDiscoveryYear = 2020,
        [ValidateRange(0.01, 100000)][double]$MaxDistancePc = 200,
        [ValidateRange(0.01, 1000)][double]$MaxRadiusEarth = 4,
        [string]$Method = 'All', [ValidateRange(2, 2000)][int]$MaxPlanets = 250)
    process {
        $eligible = @($Catalogue.Rows | Where-Object {
            $_.HasAllDimensions -and -not $_.HasLimits -and $_.Controversial -eq 0 -and
            $_.DiscoveryYear -ge $MinDiscoveryYear -and $_.DistancePc -le $MaxDistancePc -and
            $_.RadiusEarth -le $MaxRadiusEarth -and ($Method -eq 'All' -or $_.Method -eq $Method)
        } | Sort-Object @{ Expression = 'DiscoveryYear'; Descending = $true }, Name)
        $selected = @($eligible | Select-Object -First $MaxPlanets)
        if ($selected.Count -lt 2) { throw 'Fewer than two eligible planets. Broaden the year, radius, distance or method filters.' }
        $copy = $Spec | Set-DenebSpecData -DataName input -Data $selected | Set-DenebSpecSize -Width 780 -Height 380
        $input = @($copy.data | Where-Object name -eq 'input')[0]
        $input.Remove('format'); $input.Remove('transform') # Original cars date parser and horsepower filter.
        $dimensions = @(
            @{ name = 'RadiusEarth'; title = 'Radius (Earth)'; type = 'log' },
            @{ name = 'PeriodDays'; title = 'Orbit (days)'; type = 'log' },
            @{ name = 'DistancePc'; title = 'Distance (pc)'; type = 'log' },
            @{ name = 'StarTempK'; title = 'Star temp (K)'; type = 'linear' },
            @{ name = 'DiscoveryYear'; title = 'Discovery year'; type = 'linear' }
        )
        @($copy.data | Where-Object name -eq 'dimensions')[0].values = @($dimensions | ForEach-Object { @{ name = $_.name } })
        $calc = @($copy.data | Where-Object name -eq 'inputCalcs')[0]
        @($calc.transform | Where-Object type -eq 'aggregate')[0].groupby = @('id', 'Name', 'Method', 'Host', 'Url') + @($dimensions.name) + @('key', 'value')
        $copy.scales = @(@{ name = 'colour'; type = 'ordinal'; range = @{ scheme = 'tableau10' }
            domain = @{ data = 'input'; field = 'Method'; sort = $true } })
        $axisTemplate = Copy-DenebSpec $copy.axes[0]
        $copy.axes = @()
        foreach ($dimension in $dimensions) {
            $copy.scales += @{ name = $dimension.name; type = $dimension.type; range = 'height'; zero = $false; nice = $true
                domain = @{ data = 'input'; field = $dimension.name } }
            $axis = Copy-DenebSpec $axisTemplate
            $axis.scale = $dimension.name; $axis.title = $dimension.title
            $axis.tickCount = 5
            $axis.format = if ($dimension.name -eq 'DiscoveryYear') { 'd' } else { '~g' }
            if ($dimension.name -eq 'DiscoveryYear') { $axis.tickMinStep = 1 }
            if ($dimension.name -eq 'StarTempK') { $axis.format = ',.0f' }
            $copy.axes += $axis
        }
        $copy.config.axisY.titleY = 395
        $copy.legends = @(@{ stroke = 'colour'; title = 'Discovery method'; orient = 'bottom'; direction = 'horizontal'; columns = 3; offset = 65 })
        foreach ($group in @($copy.marks | Where-Object type -eq 'group')) {
            $line = $group.marks[0]
            if ($line.encode.update.stroke.scale) { $line.encode.update.stroke = @{ field = 'Method'; scale = 'colour' } }
            $line.encode.update.tooltip = @{ signal = "{'Planet':datum.Name,'Host':datum.Host,'Method':datum.Method,'Radius (Earth)':datum.RadiusEarth,'Orbit (days)':datum.PeriodDays,'Distance (pc)':datum.DistancePc,'Star temp (K)':datum.StarTempK,'Discovery year':datum.DiscoveryYear,'Source':datum.Url}" }
        }
        $copy.title = @{ text = 'Exoplanets | NASA Exoplanet Archive'; anchor = 'start'; interactive = $false; fontSize = 22
            subtitle = @("$($selected.Count) shown / $($eligible.Count) matching | discovered >= $MinDiscoveryYear | radius <= $MaxRadiusEarth Earth | distance <= $MaxDistancePc pc",
                'Drag axes to filter; drag titles to reorder; double-click to reset. Radius / orbit / distance: log scales.',
                'Complete composite parameters only; newest discoveries first. Not a representative sample or habitability score.')
            subtitleFontSize = 11; subtitleColor = '#595959' }
        $copy.marks += @{ type = 'text'; interactive = $false; encode = @{ update = @{
            x = @{ value = 0 }; y = @{ signal = 'height + 46' }; fontSize = @{ value = 10 }; fill = @{ value = '#595959' }
            text = @{ value = 'Source: NASA Exoplanet Archive (PSCompPars) | Dataviz: David Bacci' }
        } } }
        $copy.usermeta = [ordered]@{ Source = 'NASA Exoplanet Archive'; Table = $Catalogue.Table; Query = $Catalogue.Query
            DownloadedAt = $Catalogue.DownloadedAt; CatalogueSummary = $Catalogue.Summary; Matching = $eligible.Count; Shown = $selected.Count
            MinDiscoveryYear = $MinDiscoveryYear; MaxDistancePc = $MaxDistancePc; MaxRadiusEarth = $MaxRadiusEarth
            Method = $Method; MaxPlanets = $MaxPlanets; Note = $Catalogue.Note }
        $copy
    }
}
