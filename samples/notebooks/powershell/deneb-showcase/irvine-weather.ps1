# Daily historical weather for Deneb's Calendar Heatmap.
# https://open-meteo.com/en/docs/historical-weather-api (reanalysis, not forecasts).

function ConvertFrom-OpenMeteoDaily {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][datetime]$StartDate,
        [Parameter(Mandatory)][datetime]$EndDate)
    $fields = @('temperature_2m_mean', 'precipitation_sum')
    if ($Response.daily_units.time -ne 'iso8601' -or $Response.daily_units.temperature_2m_mean -ne '°C' -or
        $Response.daily_units.precipitation_sum -ne 'mm') { throw 'Unexpected Open-Meteo daily units.' }
    $count = ($EndDate.Date - $StartDate.Date).Days + 1
    if ($count -lt 1 -or @($Response.daily.time).Count -ne $count) { throw 'Open-Meteo date coverage is incomplete.' }
    foreach ($field in $fields) {
        if (@($Response.daily.$field).Count -ne $count) { throw "Open-Meteo $field array length does not match dates." }
    }
    for ($i = 0; $i -lt $count; $i++) {
        $date = $StartDate.Date.AddDays($i)
        if ($Response.daily.time[$i] -ne $date.ToString('yyyy-MM-dd')) { throw 'Open-Meteo dates are missing, duplicated or out of order.' }
        $record = [ordered]@{ Date = $date.ToString('yyyy-MM-dd'); Year = $date.Year }
        foreach ($field in $fields) {
            $raw = $Response.daily.$field[$i]
            $value = $null
            if ($null -ne $raw) {
                $number = 0.0
                if (-not [double]::TryParse([string]$raw, [Globalization.NumberStyles]::Float,
                    [cultureinfo]::InvariantCulture, [ref]$number) -or -not [double]::IsFinite($number) -or
                    ($field -eq 'precipitation_sum' -and $number -lt 0)) { throw "Invalid Open-Meteo $field on $($record.Date)." }
                $value = $number
            }
            $record[$field] = $value
        }
        [pscustomobject]$record
    }
}

function Get-IrvineDailyWeather {
    <#
    .SYNOPSIS
    Downloads daily mean temperature and precipitation since 2023, through yesterday.
    .DESCRIPTION
    Open-Meteo's historical best-match reanalysis for Irvine by default. Requests use
    local calendar days in America/Los_Angeles. A location can be overridden with its
    label, coordinates and timezone. Missing readings stay null, never zero. The native
    dictionary keeps all downloaded years available across Verso cells.
    #>
    [CmdletBinding()]
    param(
        [ValidateRange(1940, 9998)][int]$StartYear = 2023,
        [ValidateRange(-90, 90)][double]$Latitude = 33.6846,
        [ValidateRange(-180, 180)][double]$Longitude = -117.8265,
        [ValidateNotNullOrEmpty()][string]$Location = 'Irvine, CA',
        [ValidateNotNullOrEmpty()][string]$TimeZone = 'America/Los_Angeles'
    )
    $ProgressPreference = 'SilentlyContinue'
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZone)
    $end = [TimeZoneInfo]::ConvertTimeFromUtc([datetime]::UtcNow, $zone).Date.AddDays(-1)
    if ($StartYear -gt $end.Year) { throw 'Weather start year must include at least one completed local day.' }
    $rows = [Collections.Generic.List[object]]::new()
    $queries = [Collections.Generic.List[string]]::new()
    $grids = [Collections.Generic.List[object]]::new()
    $culture = [cultureinfo]::InvariantCulture
    foreach ($year in $StartYear..$end.Year) {
        $start = [datetime]::new($year, 1, 1)
        $stop = if ($year -eq $end.Year) { $end } else { [datetime]::new($year, 12, 31) }
        $uri = "https://archive-api.open-meteo.com/v1/archive?latitude=$($Latitude.ToString($culture))&longitude=$($Longitude.ToString($culture))&start_date=$($start.ToString('yyyy-MM-dd'))&end_date=$($stop.ToString('yyyy-MM-dd'))&daily=temperature_2m_mean,precipitation_sum&temperature_unit=celsius&precipitation_unit=mm&timezone=$([uri]::EscapeDataString($TimeZone))"
        $response = Invoke-RestMethod $uri -TimeoutSec 60 -ErrorAction Stop
        if ($response.timezone -ne $TimeZone) { throw 'Open-Meteo returned a different timezone.' }
        foreach ($row in @(ConvertFrom-OpenMeteoDaily $response $start $stop)) { $rows.Add($row) }
        $queries.Add($uri)
        $grids.Add([pscustomobject]@{ Year = $year; Latitude = $response.latitude; Longitude = $response.longitude; Elevation = $response.elevation })
    }
    $years = foreach ($year in $StartYear..$end.Year) {
        $selected = @($rows | Where-Object Year -eq $year)
        [pscustomobject]@{
            Year = $year; Days = $selected.Count; ThroughDate = $selected[-1].Date
            MissingTemperature = @($selected | Where-Object { $null -eq $_.temperature_2m_mean }).Count
            MissingPrecipitation = @($selected | Where-Object { $null -eq $_.precipitation_sum }).Count
        }
    }
    [ordered]@{
        Rows = $rows.ToArray(); Years = @($years); Location = $Location; TimeZone = $TimeZone
        Latitude = $Latitude; Longitude = $Longitude; ReturnedGrids = $grids.ToArray()
        StartYear = $StartYear; ThroughDate = $end.ToString('yyyy-MM-dd'); Queries = $queries.ToArray()
        SourcePage = 'https://open-meteo.com/en/docs/historical-weather-api'; DownloadedAt = [datetime]::UtcNow.ToString('o')
    }
}

function Set-WeatherCalendarYear {
    <#
    .SYNOPSIS
    Selects a downloaded year and Temperature or Precipitation for the Calendar Heatmap.
    .DESCRIPTION
    Preserves original month/week/day layout. Uses explicit ISO local-date parsing and
    full years, keeping leap days and all weeks. Adds a metric legend, tooltips and source
    title. Both metrics use blue for low values and red for high values. Future days
    are absent; unavailable historical readings remain null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Weather,
        [Parameter(Mandatory)][ValidateRange(1940, 9998)][int]$Year,
        [ValidateSet('Temperature', 'Precipitation')][string]$Metric = 'Temperature'
    )
    process {
        $period = @($Weather.Years | Where-Object Year -eq $Year)
        if ($period.Count -ne 1) { throw "Weather year $Year is not downloaded. Available: $($Weather.Years.Year -join ', ')." }
        $field = if ($Metric -eq 'Temperature') { 'temperature_2m_mean' } else { 'precipitation_sum' }
        $label = if ($Metric -eq 'Temperature') { 'Daily mean temperature (°C)' } else { 'Daily precipitation (mm)' }
        $rows = @($Weather.Rows | Where-Object Year -eq $Year | ForEach-Object {
            [pscustomobject]@{ Date = $_.Date; Amount = $_.$field }
        })
        $missing = @($rows | Where-Object { $null -eq $_.Amount }).Count
        if (-not $rows.Count -or $missing -eq $rows.Count) { throw "No available $Metric observations for $Year." }
        $copy = $Spec | Set-DenebSpecData -Data $rows
        $copy.data.format.parse.Date = "date:'%Y-%m-%d'"
        $yearTransform = @($copy.transform | Where-Object { $_.as -eq 'Year' })
        if ($yearTransform.Count -ne 1) { throw 'Expected the original calendar Year transform.' }
        $yearTransform[0].calculate = 'year(datum.Date)'
        $copy['title'] = @{
            text = "$($Weather.Location) | $Year | $label"; color = '#dedede'; anchor = 'start'; fontSize = 18
            subtitle = @("Open-Meteo historical reanalysis | through $($period[0].ThroughDate) | $missing missing days",
                'Dataviz: David Bacci | Weather data: Open-Meteo (CC BY 4.0)')
            subtitleColor = '#bfc1cc'; subtitleFontSize = 11
        }
        $copy.encoding.color['title'] = $label
        # Replace the upstream reversed turbo palette: low -> blue, midpoint -> light, high -> red.
        $copy.encoding.color.scale = @{ range = @('#2166ac', '#f7f7f7', '#b2182b') }
        # A horizontal auto-length legend references a facet-only datum in Vega-Lite 6.4.3.
        # Explicit length preserves independent month widths without that runtime error.
        $copy.encoding.color.legend = @{ orient = 'bottom'; gradientLength = 200; titleColor = '#bfc1cc'; labelColor = '#bfc1cc' }
        $copy.encoding['tooltip'] = @(
            @{ field = 'Date'; type = 'temporal'; title = 'Date'; format = '%Y-%m-%d' }
            @{ field = 'Amount'; type = 'quantitative'; title = $label; format = '.1f' }
        )
        $copy['usermeta'] = @{ source = $Weather.SourcePage; throughDate = $period[0].ThroughDate; year = $Year; metric = $Metric; missingDays = $missing }
        $copy
    }
}
