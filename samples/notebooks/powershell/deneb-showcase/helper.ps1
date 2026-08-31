$script:DenebShowcaseRepository = 'PBI-David/Deneb-Showcase'
$script:DenebShowcaseRef = 'eded225be500aa9bdec51b72fbb94063e3a92af0'

function ConvertTo-DenebRawPath {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    ($Value.Replace('\', '/').Split('/') |
        ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
}

function Copy-DenebSpec {
    param(
        [Parameter(Mandatory)]
        $Spec
    )

    $Spec |
        ConvertTo-Json -Depth 100 -Compress |
        ConvertFrom-Json -AsHashtable -Depth 100
}

function Set-DenebDataSourceValues {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Source,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Values
    )

    # Inline records no longer need a file reader, but still need field parsing.
    $format = $Source['format']
    foreach ($property in @('url', 'format', 'source')) {
        $Source.Remove($property)
    }

    if ($format -is [System.Collections.IDictionary] -and $format.Contains('parse')) {
        $Source['format'] = @{ parse = $format['parse'] }
    }

    $Source['values'] = @($Values)
}

function Get-DenebSpecMode {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Spec
    )

    $schema = [string]$Spec['$schema']
    if ($schema -match '/vega-lite/') {
        return 'vega-lite'
    }
    if ($schema -match '/vega/') {
        return 'vega'
    }
    if ($Spec.Contains('mark') -or $Spec.Contains('encoding') -or
        $Spec.Contains('layer') -or $Spec.Contains('hconcat') -or
        $Spec.Contains('vconcat')) {
        return 'vega-lite'
    }
    if ($Spec.Contains('marks') -or $Spec.Contains('signals')) {
        return 'vega'
    }

    throw 'The specification does not identify itself as Vega or Vega-Lite.'
}

function Get-DenebShowcaseSpec {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Path,

        [string]$Ref = $script:DenebShowcaseRef
    )

    $encodedRef = ConvertTo-DenebRawPath $Ref
    $encodedPath = ConvertTo-DenebRawPath $Path
    $uri = "https://raw.githubusercontent.com/$script:DenebShowcaseRepository/$encodedRef/$encodedPath"

    try {
        $ProgressPreference = 'SilentlyContinue'
        $response = Invoke-WebRequest -Uri $uri -ErrorAction Stop
        $response.Content | ConvertFrom-Json -AsHashtable -Depth 100
    }
    catch {
        throw "Could not load Deneb Showcase specification '$Path' at ref '$Ref': $($_.Exception.Message)"
    }
}

function Get-DenebShowcaseData {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Path,

        [string]$Ref = $script:DenebShowcaseRef
    )

    $encodedRef = ConvertTo-DenebRawPath $Ref
    $encodedPath = ConvertTo-DenebRawPath $Path
    $uri = "https://raw.githubusercontent.com/$script:DenebShowcaseRepository/$encodedRef/$encodedPath"

    try {
        $ProgressPreference = 'SilentlyContinue'
        $response = Invoke-WebRequest -Uri $uri -ErrorAction Stop
        $response.Content | ConvertFrom-Json -Depth 100 -NoEnumerate
    }
    catch {
        throw "Could not load Deneb Showcase data '$Path' at ref '$Ref': $($_.Exception.Message)"
    }
}

function Set-DenebSpecData {
    [CmdletBinding(DefaultParameterSetName = 'SingleSource')]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        $Spec,

        [Parameter(Mandatory, ParameterSetName = 'SingleSource')]
        [AllowEmptyCollection()]
        [object[]]$Data,

        [Parameter(ParameterSetName = 'SingleSource')]
        [string]$DataName,

        [Parameter(Mandatory, ParameterSetName = 'NamedSources')]
        [System.Collections.IDictionary]$DataByName
    )

    process {
        $copy = Copy-DenebSpec $Spec
        $sources = $copy['data']

        if ($null -eq $sources) {
            throw 'The specification has no top-level data source.'
        }

        if ($PSCmdlet.ParameterSetName -eq 'NamedSources') {
            if ($sources -isnot [System.Collections.IList]) {
                throw '-DataByName requires a Vega specification with an array of named data sources.'
            }

            foreach ($entry in $DataByName.GetEnumerator()) {
                $matches = @($sources | Where-Object { $_['name'] -eq [string]$entry.Key })
                if ($matches.Count -eq 0) {
                    throw "The specification has no top-level data source named '$($entry.Key)'."
                }

                foreach ($match in $matches) {
                    Set-DenebDataSourceValues -Source $match -Values @($entry.Value)
                }
            }
        }
        elseif ($sources -is [System.Collections.IDictionary]) {
            if ($PSBoundParameters.ContainsKey('DataName') -and
                $sources.Contains('name') -and
                $sources['name'] -ne $DataName) {
                throw "The top-level data source is named '$($sources['name'])', not '$DataName'."
            }

            Set-DenebDataSourceValues -Source $sources -Values $Data
        }
        else {
            $targetName = if ($PSBoundParameters.ContainsKey('DataName')) { $DataName } else { 'dataset' }
            $matches = @($sources | Where-Object { $_['name'] -eq $targetName })

            if ($matches.Count -eq 0 -and
                -not $PSBoundParameters.ContainsKey('DataName') -and
                $sources.Count -eq 1) {
                $matches = @($sources[0])
            }
            if ($matches.Count -eq 0) {
                throw "The specification has no top-level data source named '$targetName'."
            }

            foreach ($match in $matches) {
                Set-DenebDataSourceValues -Source $match -Values $Data
            }
        }

        $copy
    }
}

function Set-DenebSpecSize {
    <#
    .SYNOPSIS
    Sets explicit top-level plot dimensions on a copy of a specification.
    .DESCRIPTION
    Specify Width, Height, or both in pixels. Other dimensions, autosize,
    padding, marks, and nested views are unchanged. An explicitly supplied
    dimension replaces any existing step/container/signal-based value.
    Legends and out-of-bounds marks may still enlarge the rendered output.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        $Spec,

        [ValidateRange(1, 2147483647)]
        [int]$Width,

        [ValidateRange(1, 2147483647)]
        [int]$Height
    )

    begin {
        if (-not $PSBoundParameters.ContainsKey('Width') -and
            -not $PSBoundParameters.ContainsKey('Height')) {
            throw 'Specify -Width, -Height, or both.'
        }
    }

    process {
        $copy = Copy-DenebSpec $Spec
        if ($PSBoundParameters.ContainsKey('Width')) { $copy['width'] = $Width }
        if ($PSBoundParameters.ContainsKey('Height')) { $copy['height'] = $Height }
        $copy
    }
}

function Show-DenebSpec {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Spec,

        [Parameter(Mandatory)]
        [ValidateSet('vega', 'vega-lite')]
        [string]$Mode,

        [ValidateSet('svg', 'canvas')]
        [string]$Renderer = 'svg'
    )

    $specJson = $Spec | ConvertTo-Json -Depth 100 -Compress
    $specBase64 = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($specJson)
    )

    $document = @"
<!doctype html>
<html>
<head>
    <meta charset="utf-8">
    <script src="https://cdn.jsdelivr.net/npm/vega@6.3.1"></script>
    <script src="https://cdn.jsdelivr.net/npm/vega-lite@6.4.3"></script>
    <script src="https://cdn.jsdelivr.net/npm/vega-embed@7.1.0"></script>
    <style>
        html, body {
            margin: 0;
            padding: 0;
            background: var(--verso-cell-output-background, white);
            color: var(--verso-cell-output-foreground, #1e1e1e);
            font-family: var(--verso-ui-font-family, sans-serif);
        }
        #vis { width: 100%; overflow: auto; }
        .error { padding: 12px; color: #c62828; white-space: pre-wrap; }
    </style>
</head>
<body>
    <div id="vis"></div>
    <script>
        const binary = atob("$specBase64");
        const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
        const spec = JSON.parse(new TextDecoder().decode(bytes));

        vegaEmbed("#vis", spec, {
            mode: "$Mode",
            renderer: "$Renderer",
            actions: true
        }).catch(error => {
            document.body.innerHTML = '<div class="error"></div>';
            document.querySelector(".error").textContent = error.stack || error.message;
        });
    </script>
</body>
</html>
"@

    $document | Display -MimeType 'text/x-verso-widget'
}

function Show-Vega {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        $Spec,

        [ValidateSet('svg', 'canvas')]
        [string]$Renderer = 'svg'
    )

    process {
        $copy = Copy-DenebSpec $Spec
        if ((Get-DenebSpecMode $copy) -ne 'vega') {
            throw 'Show-Vega received a Vega-Lite specification.'
        }
        Show-DenebSpec -Spec $copy -Mode vega -Renderer $Renderer
    }
}

function Show-VegaLite {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        $Spec,

        [ValidateSet('svg', 'canvas')]
        [string]$Renderer = 'svg'
    )

    process {
        $copy = Copy-DenebSpec $Spec
        if ((Get-DenebSpecMode $copy) -ne 'vega-lite') {
            throw 'Show-VegaLite received a Vega specification.'
        }
        Show-DenebSpec -Spec $copy -Mode vega-lite -Renderer $Renderer
    }
}

function Show-DenebShowcaseExample {
    [CmdletBinding(DefaultParameterSetName = 'Original')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Path,

        [string]$Ref = $script:DenebShowcaseRef,

        [Parameter(Mandatory, ParameterSetName = 'SingleSource')]
        [AllowEmptyCollection()]
        [object[]]$Data,

        [Parameter(ParameterSetName = 'SingleSource')]
        [string]$DataName,

        [Parameter(Mandatory, ParameterSetName = 'NamedSources')]
        [System.Collections.IDictionary]$DataByName,

        [ValidateSet('svg', 'canvas')]
        [string]$Renderer = 'svg'
    )

    $spec = Get-DenebShowcaseSpec -Path $Path -Ref $Ref

    if ($PSCmdlet.ParameterSetName -eq 'SingleSource') {
        $parameters = @{ Spec = $spec; Data = $Data }
        if ($PSBoundParameters.ContainsKey('DataName')) {
            $parameters.DataName = $DataName
        }
        $spec = Set-DenebSpecData @parameters
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'NamedSources') {
        $spec = Set-DenebSpecData -Spec $spec -DataByName $DataByName
    }

    $mode = Get-DenebSpecMode $spec
    Show-DenebSpec -Spec $spec -Mode $mode -Renderer $Renderer
}
