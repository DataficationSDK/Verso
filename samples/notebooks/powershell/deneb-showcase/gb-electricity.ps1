# Great Britain generation mix, including imports; not a UK-wide or carbon-intensity chart.
# NESO Carbon Intensity API (beta generation endpoint), CC BY 4.0.
# https://carbon-intensity.github.io/api-definitions/#generation-mix

function Get-GbGenerationFuels {
    [ordered]@{ wind = 'Wind'; solar = 'Solar'; hydro = 'Hydro'; nuclear = 'Nuclear';
        gas = 'Gas'; biomass = 'Biomass'; imports = 'Imports'; coal = 'Coal'; other = 'Other' }
}

function ConvertTo-GbHalfHour {
    param([Parameter(Mandatory)][string]$Value)
    $time = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParseExact($Value, "yyyy-MM-dd'T'HH:mm'Z'", [cultureinfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$time) -or $time.Minute % 30 -ne 0) {
        throw "Expected a UTC half-hour boundary, e.g. 2026-08-31T12:30Z. Received: $Value"
    }
    $time
}

function ConvertFrom-GbGeneration {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][datetimeoffset]$From,
        [Parameter(Mandatory)][datetimeoffset]$To)
    if ($Response.error -or $null -eq $Response.data) { throw 'Carbon Intensity API returned an error or no data.' }
    $fuels = Get-GbGenerationFuels
    $seen = [Collections.Generic.HashSet[string]]::new()
    $result = [Collections.Generic.List[object]]::new()
    foreach ($interval in @($Response.data)) {
        $start = ConvertTo-GbHalfHour ([string]$interval.from)
        $end = ConvertTo-GbHalfHour ([string]$interval.to)
        if (($end - $start).TotalMinutes -ne 30) { throw 'Generation interval is not 30 minutes.' }
        # The range endpoint may include a boundary interval preceding the requested start.
        if ($start -lt $From -or $end -gt $To) { continue }
        if (-not $seen.Add($interval.from)) { throw 'Duplicate generation interval.' }
        $values = @{}; $total = 0.0
        foreach ($entry in @($interval.generationmix)) {
            if (-not $fuels.Contains([string]$entry.fuel) -or $values.ContainsKey($entry.fuel)) {
                throw 'Unknown or duplicate generation fuel; check the API schema.'
            }
            $number = 0.0
            if ($null -eq $entry.perc -or -not [double]::TryParse([string]$entry.perc,
                [Globalization.NumberStyles]::Float, [cultureinfo]::InvariantCulture, [ref]$number) -or
                -not [double]::IsFinite($number) -or $number -lt 0 -or $number -gt 100) {
                throw 'Invalid or missing generation percentage; missing is not zero.'
            }
            $values[$entry.fuel] = $number; $total += $number
        }
        if ($values.Count -ne $fuels.Count) { throw 'Generation mix is missing a fuel; missing is not zero.' }
        # Nine independently rounded one-decimal shares can differ from 100 by up to 0.45 pp.
        if ([math]::Abs($total - 100) -gt 0.500001) { throw "Generation shares do not total approximately 100%: $total." }
        $rows = foreach ($fuel in $fuels.Keys) {
            [pscustomobject]@{ Fuel = $fuel; Origin = $fuels[$fuel]; PercentExact = $values[$fuel]
                FromUtc = $start.ToString("yyyy-MM-dd'T'HH:mm'Z'"); ToUtc = $end.ToString("yyyy-MM-dd'T'HH:mm'Z'") }
        }
        $result.Add([pscustomobject]@{ FromUtc = $start.ToString("yyyy-MM-dd'T'HH:mm'Z'")
            ToUtc = $end.ToString("yyyy-MM-dd'T'HH:mm'Z'"); TotalPercent = [math]::Round($total, 6); Fuels = @($rows) })
    }
    if ($result.Count -eq 0) { throw 'No completed generation intervals in the requested range.' }
    $result.ToArray() | Sort-Object FromUtc
}

function Get-GbGenerationMix {
    <# Downloads recent completed UTC half hours. Gaps are reported, never synthesized.
       Re-run to refresh; display selection makes no further API request. No API key. #>
    [CmdletBinding()]
    param([ValidateRange(1, 168)][int]$Hours = 48)
    $now = [datetimeoffset]::UtcNow
    $end = [datetimeoffset]::new($now.Year, $now.Month, $now.Day, $now.Hour,
        ([math]::Floor($now.Minute / 30) * 30), 0, [timespan]::Zero)
    $start = $end.AddHours(-$Hours)
    $startText = $start.ToString("yyyy-MM-dd'T'HH:mm'Z'"); $endText = $end.ToString("yyyy-MM-dd'T'HH:mm'Z'")
    $uri = "https://api.carbonintensity.org.uk/generation/$startText/$endText"
    $response = Invoke-RestMethod -Uri $uri -TimeoutSec 60 -ErrorAction Stop
    $intervals = @(ConvertFrom-GbGeneration $response $start $end)
    $available = [Collections.Generic.HashSet[string]]::new([string[]]$intervals.FromUtc)
    $missing = for ($time = $start; $time -lt $end; $time = $time.AddMinutes(30)) {
        $text = $time.ToString("yyyy-MM-dd'T'HH:mm'Z'")
        if (-not $available.Contains($text)) { $text }
    }
    [ordered]@{ Intervals = $intervals; LatestFromUtc = $intervals[-1].FromUtc
        RequestedFromUtc = $startText; RequestedToUtc = $endText; ExpectedIntervals = $Hours * 2
        MissingFromUtc = @($missing); DownloadedAt = [datetimeoffset]::UtcNow.ToString('o'); Query = $uri
        SourcePage = 'https://carbon-intensity.github.io/api-definitions/#generation-mix'; License = 'CC BY 4.0'
        Note = 'NESO Carbon Intensity API generation mix for Great Britain, including imports. API values may be revised. Not UK-wide, not carbon intensity, not an unweighted daily average.' }
}

function Set-GbGenerationWaffle {
    <# Reuses Deneb's faceted 100-dot layout: one facet per fuel, not one shared 100-dot pie.
       Labels/tooltips show original shares. Dot counts round each share independently;
       they need not sum to 100 across facets. No renormalization of API percentages. #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)]$Mix, [string]$FromUtc = $Mix.LatestFromUtc)
    process {
        $null = ConvertTo-GbHalfHour $FromUtc
        $selected = @($Mix.Intervals | Where-Object FromUtc -eq $FromUtc)
        if ($selected.Count -ne 1) { throw "Generation interval $FromUtc is not downloaded. Choose FromUtc from Mix.Intervals." }
        $period = $selected[0]
        $copy = $Spec | Set-DenebSpecData -Data @($period.Fuels)
        if (@($copy.transform).Count -ne 9 -or $copy.transform[4].calculate -ne 'sequence(1,101)') {
            throw 'Expected the pinned Deneb faceted Waffle Charts template.'
        }
        # Replace car-count aggregation with the API's percentages; retain all five grid transforms.
        $copy.transform = @(@{ calculate = 'round(datum.PercentExact)'; as = 'Percent' }) + @($copy.transform | Select-Object -Skip 4)
        $labels = @((Get-GbGenerationFuels).Values)
        $scale = @{ domain = $labels; range = @('#218c74','#e5ac18','#2980b9','#8064a2','#d76a38','#6b8e23','#3b6f9c','#555555','#a27c6b') }
        $copy.facet = @{ field = 'Origin'; type = 'nominal'; sort = $labels
            header = @{ title = $null; labelOrient = 'bottom'; labelFontSize = 13; labelPadding = 7 } }
        $copy.columns = 3; $copy.spacing = 22
        $copy.spec.width = 140; $copy.spec.height = 140
        # Segoe UI is not installed on every Viewer host (e.g. macOS).
        $copy.config.font = 'Segoe UI, Arial, sans-serif'
        $copy.config.text.font = $copy.config.font
        $copy.config.header.labelFont = $copy.config.font
        $points = $copy.spec.layer[0]
        $points.encoding.size.value = 90
        $points.encoding.color.scale = $scale
        $points.encoding.tooltip = @(
            @{ field = 'Origin'; type = 'nominal'; title = 'Source' },
            @{ field = 'PercentExact'; type = 'quantitative'; title = 'API share (%)'; format = '.1f' },
            @{ field = 'Percent'; type = 'quantitative'; title = 'Filled dots (rounded)' },
            @{ field = 'FromUtc'; type = 'nominal'; title = 'From (UTC)' },
            @{ field = 'ToUtc'; type = 'nominal'; title = 'To (UTC)' }
        )
        $label = $copy.spec.layer[1]
        $label.mark.fontSize = 23; $label.encoding.y.value = -13
        $label.encoding.text.condition.value.expr = "format(datum.PercentExact, '.1f') + '%'"
        $label.encoding.color = @{ field = 'Origin'; type = 'nominal'; scale = $scale; legend = $null }
        $copy.title = @{ text = 'Electricity generation mix | Great Britain'; anchor = 'start'; fontSize = 22
            subtitle = @("$($period.FromUtc) - $($period.ToUtc) | UTC | API total: $($period.TotalPercent)%",
                'Each panel: 100 dots; filled dots rounded to 1 percentage point. Labels show API shares.',
                'Source: NESO Carbon Intensity API (CC BY 4.0) | Dataviz: David Bacci')
            subtitleFontSize = 11; subtitleColor = '#595959'; offset = 24 }
        $copy.usermeta = [ordered]@{ Source = 'NESO Carbon Intensity API'; FromUtc = $period.FromUtc; ToUtc = $period.ToUtc
            TotalPercent = $period.TotalPercent; Query = $Mix.Query; DownloadedAt = $Mix.DownloadedAt; License = $Mix.License
            ExpectedIntervals = $Mix.ExpectedIntervals; MissingIntervals = @($Mix.MissingFromUtc).Count; Note = $Mix.Note }
        $copy
    }
}
