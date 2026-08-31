# Small, read-only Wikidata and MusicBrainz adapters for the original Deneb graph.
# API documentation: https://www.wikidata.org/wiki/Wikidata:Data_access
# https://musicbrainz.org/doc/MusicBrainz_API

function Invoke-KnowledgeGraphRequest {
    param([Parameter(Mandatory)][string]$Uri)
    $headers = @{ 'User-Agent' = 'VersoDenebShowcase/0.1 (https://github.com/DataficationSDK/Verso)'; Accept = 'application/json' }
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        # MusicBrainz requires at most one call per second, including retries.
        Start-Sleep -Milliseconds 1100
        try {
            $response = Invoke-RestMethod -Uri $Uri -Headers $headers -TimeoutSec 45 -ErrorAction Stop
            if ($response.error) { throw "Knowledge graph API error: $($response.error | ConvertTo-Json -Compress -Depth 10)" }
            return $response
        }
        catch {
            $status = [int]$_.Exception.Response.StatusCode
            if ($status -notin @(429, 500, 502, 503, 504) -or $attempt -eq 3) { throw }
            Start-Sleep -Seconds ([math]::Pow(2, $attempt + 1))
        }
    }
}

function Get-WikidataQualifierText {
    param($Claim, [string]$Property)
    $values = foreach ($snak in @($Claim.qualifiers.$Property)) {
        if ($null -eq $snak) { continue }
        if ($snak.snaktype -ne 'value') { $snak.snaktype; continue }
        $value = $snak.datavalue.value
        if ($value.time) {
            # Preserve source precision: a year is not January 1, and unknown is not now.
            $time = [string]$value.time
            if ($time -match '^\+(\d{4})-(\d{2})-(\d{2})T') {
                switch ([int]$value.precision) {
                    9 { $Matches[1] }
                    10 { "$($Matches[1])-$($Matches[2])" }
                    11 { "$($Matches[1])-$($Matches[2])-$($Matches[3])" }
                    default { "$time (precision $($value.precision))" }
                }
            } else { $time }
        }
        elseif ($null -ne $value.amount) { "$($value.amount) [unit: $($value.unit)]" }
        elseif ($value.id) { $value.id }
        else { $value | ConvertTo-Json -Depth 10 -Compress }
    }
    $values -join '; '
}

function Select-WikidataOwnershipClaims {
    param([Parameter(Mandatory)]$Entity, [bool]$IncludeHistorical = $false)
    $eligible = @($Entity.claims.P127 | Where-Object {
        $_.rank -in @('normal', 'preferred') -and $_.mainsnak.snaktype -eq 'value' -and
        $_.mainsnak.datavalue.value.id -cmatch '^Q[1-9]\d*$'
    })
    if (-not $IncludeHistorical) {
        # Conservative open-ended view, NOT a claim that ownership is current.
        # Point-in-time statements are historical snapshots even without an end date.
        $eligible = @($eligible | Where-Object { -not $_.qualifiers.P582 -and -not $_.qualifiers.P585 })
        if (@($eligible | Where-Object rank -eq 'preferred').Count -gt 0) {
            $eligible = @($eligible | Where-Object rank -eq 'preferred')
        }
    }
    $eligible
}

function Get-WikidataOwnershipGraph {
    <#
    .SYNOPSIS
    Follows P127 (owned by) outward from explicit Wikidata item IDs.
    .DESCRIPTION
    The default seeds are selected automotive brands/companies, not an exhaustive
    industry list. Default view excludes end-dated and point-in-time statements,
    prefers preferred rank among eligible claims, and never uses deprecated rank.
    Open-ended does NOT mean current/verified: missing dates, stale shares, incomplete
    records and future starts remain possible. Qualifiers/references/revisions are
    retained; no inference from P749 parent organization or transitive ownership.
    -IncludeHistorical includes normal/preferred historical statements with dates.
    Depth is an explicit expansion boundary. MaxNodes aborts instead of truncating.
    #>
    [CmdletBinding()]
    param(
        [ValidateNotNullOrEmpty()][ValidatePattern('^Q[1-9]\d*$')][string[]]$EntityId = @('Q23317', 'Q40993', 'Q27224', 'Q35886', 'Q29637', 'Q116232', 'Q234803', 'Q271812'),
        [ValidateRange(1, 3)][int]$Depth = 2,
        [ValidateRange(2, 80)][int]$MaxNodes = 40,
        [bool]$IncludeHistorical = $false
    )
    $seeds = @($EntityId | Select-Object -Unique)
    if ($seeds.Count -ne $EntityId.Count) { throw 'Duplicate Wikidata seed ID.' }
    if ($seeds.Count -gt $MaxNodes) { throw 'Wikidata seeds exceed MaxNodes.' }
    $entities = @{}; $discovered = @{}; $edges = @{}
    $queries = [Collections.Generic.List[string]]::new()
    $coverage = [Collections.Generic.List[object]]::new()
    foreach ($id in $seeds) { $discovered[$id] = 0 }
    for ($level = 0; $level -le $Depth; $level++) {
        $frontier = @($discovered.Keys | Where-Object { $discovered[$_] -eq $level -and -not $entities.ContainsKey($_) } | Sort-Object)
        for ($offset = 0; $offset -lt $frontier.Count; $offset += 50) {
            $batch = @($frontier | Select-Object -Skip $offset -First 50)
            $ids = [uri]::EscapeDataString($batch -join '|')
            $uri = "https://www.wikidata.org/w/api.php?action=wbgetentities&ids=$ids&props=labels%7Cdescriptions%7Cclaims%7Cinfo&languages=en&format=json"
            $page = Invoke-KnowledgeGraphRequest $uri
            $queries.Add($uri)
            foreach ($id in $batch) {
                $entity = $page.entities.$id
                if ($null -eq $entity -or $null -ne $entity.missing -or $entity.id -cne $id -or $entity.type -ne 'item') {
                    throw "Wikidata item $id is missing, redirected or invalid. Select its canonical Q ID."
                }
                $entities[$id] = $entity
            }
        }
        foreach ($id in $frontier) {
            $entity = $entities[$id]
            $claims = @(Select-WikidataOwnershipClaims $entity -IncludeHistorical $IncludeHistorical)
            $coverage.Add([ordered]@{ Id = $id; Label = $entity.labels.en.value; Level = $level
                Expanded = ($level -lt $Depth); AllP127 = @($entity.claims.P127 | Where-Object { $null -ne $_ }).Count; EligibleP127 = $claims.Count
                Revision = $entity.lastrevid; Modified = $entity.modified })
            if ($level -eq $Depth) { continue }
            foreach ($claim in $claims) {
                $owner = [string]$claim.mainsnak.datavalue.value.id
                if (-not $discovered.ContainsKey($owner)) {
                    if ($discovered.Count -ge $MaxNodes) { throw 'Wikidata graph exceeds MaxNodes. Reduce seeds/Depth or explicitly raise the limit.' }
                    $discovered[$owner] = $level + 1
                }
                $key = "$id/$owner"
                if (-not $edges.ContainsKey($key)) {
                    $edges[$key] = [ordered]@{ source = $id; target = $owner; value = 'owned by (P127)'
                        Statements = [Collections.Generic.List[object]]::new(); Historical = $true }
                }
                $edges[$key].Statements.Add($claim)
                if (-not $claim.qualifiers.P582 -and -not $claim.qualifiers.P585) { $edges[$key].Historical = $false }
            }
        }
    }
    $nodes = foreach ($id in $discovered.Keys | Sort-Object) {
        $entity = $entities[$id]
        $label = if ($entity.labels.en.value) { $entity.labels.en.value } else { $id }
        [ordered]@{ name = $id; Label = $label; group = $(if ($id -in $seeds) { 'Selected brand/company' } else { 'Listed owner' })
            Url = "https://www.wikidata.org/wiki/$id"; Description = [string]$entity.descriptions.en.value
            Level = $discovered[$id]; Revision = $entity.lastrevid }
    }
    $nodeMap = @{}; foreach ($node in $nodes) { $nodeMap[$node.name] = $node }
    $links = foreach ($key in $edges.Keys | Sort-Object) {
        $edge = $edges[$key]
        $details = foreach ($claim in $edge.Statements) {
            $parts = @("Rank: $($claim.rank)")
            foreach ($property in @('P580', 'P582', 'P585', 'P1107', 'P518')) {
                $text = Get-WikidataQualifierText $claim $property
                if ($text) { $parts += "$property`: $text" }
            }
            $parts += "References: $(@($claim.references | Where-Object { $null -ne $_ }).Count)"
            $parts -join ' | '
        }
        [ordered]@{ source = $edge.source; target = $edge.target; value = $edge.value; Historical = $edge.Historical
            SourceLabel = $nodeMap[$edge.source].Label; TargetLabel = $nodeMap[$edge.target].Label
            Details = ($details -join "`n"); Statements = $edge.Statements.ToArray() }
    }
    if (@($links).Count -eq 0) { throw 'No eligible P127 ownership statements. Missing statements do not imply independent ownership.' }
    $mode = if ($IncludeHistorical) { 'Includes historical statements; dashed links have only dated statements.' } else { 'Open-ended P127 statements only; NOT verified current ownership.' }
    [ordered]@{ Nodes = @($nodes); Links = @($links); Source = 'Wikidata'; Title = 'Who owns the brands? | Wikidata'
        Subtitle = @($mode, "Arrows: brand/company to listed owner. Depth $Depth; isolated seeds have no eligible P127.")
        Queries = $queries.ToArray(); Coverage = $coverage.ToArray(); DownloadedAt = [datetime]::UtcNow.ToString('o')
        SeedIds = $seeds; Depth = $Depth; IncludeHistorical = $IncludeHistorical; MaxNodes = $MaxNodes
        SourcePage = 'https://www.wikidata.org/wiki/Property:P127'; License = 'Wikidata structured data: CC0'
        Note = 'Partial community-maintained statements, not a legal ownership registry. Shares may be partial or stale; no ownership percentages are inferred.' }
}

function ConvertFrom-MusicBrainzMembership {
    param([Parameter(Mandatory)]$Artist, [bool]$IncludeFormerMembers = $true)
    if (-not $Artist.id -or -not $Artist.name -or $Artist.type -ne 'Group' -or $null -eq $Artist.relations) {
        throw 'Expected a MusicBrainz Group with artist relations.'
    }
    foreach ($relation in $Artist.relations) {
        if ($relation.'type-id' -ne '5be4c609-9afa-4ea0-910b-12ffb71e3821') { continue }
        if ($relation.direction -ne 'backward') { throw 'Unexpected member-of-band direction for a group.' }
        $member = $relation.artist
        if ($member.id -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$' -or -not $member.name) { throw 'MusicBrainz membership lacks an artist ID/name.' }
        $ended = $relation.ended -eq $true -or -not [string]::IsNullOrEmpty($relation.end)
        if (-not $IncludeFormerMembers -and $ended) { continue }
        # Retain all periods/roles, including rejoining and concurrent instruments.
        [ordered]@{ Member = $member; Band = $Artist; Begin = $relation.begin; End = $relation.end
            Ended = $ended; Attributes = @($relation.attributes); TypeId = $relation.'type-id' }
    }
}

function Get-MusicBrainzBandGraph {
    <#
    .SYNOPSIS
    Shows the members shared by explicitly selected MusicBrainz groups.
    .DESCRIPTION
    Each MBID is looked up once with artist-rels, not release credits or inferred
    collaborations. Member -> band direction is normalized from backward relations.
    Multiple periods/instruments are retained under one edge per member/band pair.
    Dates and ended flags are source data; no end recorded is not proof of membership
    today. No API key, user account, personal listening history or audio is used.
    SharedMembersOnly keeps people with edges to >=2 selected bands, counting bands
    rather than periods/instruments. MaxNodes bounds downloaded nodes before filtering
    and fails visibly instead of discarding members. Public API: noncommercial
    demo use, descriptive User-Agent, <=1 request/second. Data may be incomplete.
    #>
    [CmdletBinding()]
    param(
        [ValidateNotNullOrEmpty()][ValidatePattern('^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$')][string[]]$BandId = @(
            '5b11f4ce-a62d-471e-81fc-a69a8278c7da', # Nirvana
            '67f66c07-6e61-4026-ade5-7e782fad3a5d', # Foo Fighters
            '7dc8f5bd-9d0b-4087-9f73-dc164950bbd8', # Queens of the Stone Age
            '4bc09c51-5d42-4c93-9ba6-8cc21a0edb8d', # Them Crooked Vultures
            '1f36a3a2-9687-4819-ac55-54d7ff0b8b88', # Ария
            '76f95aaa-9be1-47a1-8db8-731cb77cf938'   # Кипелов (group, not Валерий Кипелов)
        ),
        [bool]$IncludeFormerMembers = $true,
        [bool]$SharedMembersOnly = $true,
        [ValidateRange(2, 80)][int]$MaxNodes = 65
    )
    if (@($BandId | Select-Object -Unique).Count -ne $BandId.Count) { throw 'Duplicate MusicBrainz band ID.' }
    if ($BandId.Count -gt $MaxNodes) { throw 'MusicBrainz bands exceed MaxNodes.' }
    $nodes = @{}; $edges = @{}
    $queries = [Collections.Generic.List[string]]::new()
    $coverage = [Collections.Generic.List[object]]::new()
    foreach ($id in $BandId) {
        $uri = "https://musicbrainz.org/ws/2/artist/$($id.ToLowerInvariant())?inc=artist-rels&fmt=json"
        $artist = Invoke-KnowledgeGraphRequest $uri
        $queries.Add($uri)
        if ($artist.id -ne $id) { throw 'MusicBrainz returned a different artist ID (possibly merged); use the canonical MBID.' }
        $memberships = @(ConvertFrom-MusicBrainzMembership $artist -IncludeFormerMembers $IncludeFormerMembers)
        $nodes[$id] = [ordered]@{ name = $id; Label = $artist.name; group = 'Band'; Url = "https://musicbrainz.org/artist/$id"
            Description = [string]$artist.disambiguation }
        $coverage.Add([ordered]@{ Id = $id; Name = $artist.name; MembershipRecords = $memberships.Count
            NoEndRecorded = @($memberships | Where-Object { -not $_.Ended }).Count })
        foreach ($membership in $memberships) {
            $member = $membership.Member
            if (-not $nodes.ContainsKey($member.id)) {
                $nodes[$member.id] = [ordered]@{ name = $member.id; Label = $member.name; group = 'Musician'
                    Url = "https://musicbrainz.org/artist/$($member.id)"; Description = [string]$member.disambiguation }
            }
            $key = "$($member.id)/$id"
            if (-not $edges.ContainsKey($key)) {
                $edges[$key] = [ordered]@{ source = $member.id; target = $id; value = 'member of band'
                    SourceLabel = $member.name; TargetLabel = $artist.name; Historical = $true
                    Periods = [Collections.Generic.List[object]]::new() }
            }
            $edges[$key].Periods.Add([ordered]@{ Begin = $membership.Begin; End = $membership.End; Ended = $membership.Ended; Attributes = $membership.Attributes })
            if (-not $membership.Ended) { $edges[$key].Historical = $false }
        }
        if ($nodes.Count -gt $MaxNodes) { throw 'MusicBrainz graph exceeds MaxNodes. Select fewer bands or explicitly raise the limit.' }
    }
    $downloadedNodes = $nodes.Count
    $membershipCount = @{}
    foreach ($edge in $edges.Values) { $membershipCount[$edge.source] = 1 + [int]$membershipCount[$edge.source] }
    $links = foreach ($key in $edges.Keys | Sort-Object) {
        $edge = $edges[$key]
        if ($SharedMembersOnly -and $membershipCount[$edge.source] -lt 2) { continue }
        $details = foreach ($period in $edge.Periods) {
            $begin = if ($period.Begin) { $period.Begin } else { 'start unknown' }
            $end = if ($period.End) { $period.End } elseif ($period.Ended) { 'ended, date unknown' } else { 'no end recorded' }
            "$begin -> $end | $($period.Attributes -join ', ')"
        }
        [ordered]@{ source = $edge.source; target = $edge.target; value = $edge.value; Historical = $edge.Historical
            SourceLabel = $edge.SourceLabel; TargetLabel = $edge.TargetLabel; Details = ($details -join "`n"); Periods = $edge.Periods.ToArray() }
    }
    if (@($links).Count -eq 0) { throw 'No MusicBrainz membership relations for this selection. Try SharedMembersOnly = $false or include former members.' }
    $visible = @{}; foreach ($id in $BandId) { $visible[$id] = $true }
    foreach ($link in $links) { $visible[$link.source] = $true; $visible[$link.target] = $true }
    foreach ($id in @($nodes.Keys)) { if (-not $visible.ContainsKey($id)) { $nodes.Remove($id) } }
    $scope = if ($SharedMembersOnly) { 'Shared members of selected bands' } else { 'All returned members of selected bands' }
    [ordered]@{ Nodes = @($nodes.Values | Sort-Object Label); Links = @($links); Source = 'MusicBrainz'; Title = 'Musical connections | MusicBrainz'
        Subtitle = @("$scope. Arrows: musician to band.",
            'Dashed: all returned periods ended. Dates/roles in tooltips; NOT a verified current lineup.')
        Queries = $queries.ToArray(); Coverage = $coverage.ToArray(); DownloadedAt = [datetime]::UtcNow.ToString('o')
        BandIds = @($BandId); IncludeFormerMembers = $IncludeFormerMembers; SharedMembersOnly = $SharedMembersOnly
        DownloadedNodes = $downloadedNodes; FilteredNodes = $downloadedNodes - $nodes.Count; MaxNodes = $MaxNodes
        SourcePage = 'https://musicbrainz.org/relationship/5be4c609-9afa-4ea0-910b-12ffb71e3821'
        Note = 'Community-maintained membership relationships, including additional members; not a complete collaboration graph or current lineup.' }
}

function Set-DenebRelationshipGraph {
    <# Binds either adapter to the pinned Force Directed Graph. No changes to helper.ps1. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][Collections.IDictionary]$Spec,
        [Parameter(Mandatory)][Collections.IDictionary]$Graph
    )
    process {
        $ids = [Collections.Generic.HashSet[string]]::new()
        foreach ($node in $Graph.Nodes) {
            if (-not $node.name -or $node.name.Contains(',') -or -not $node.Label -or -not $ids.Add($node.name)) { throw 'Invalid/duplicate relationship graph node.' }
        }
        if (@($Graph.Links).Count -eq 0) { throw 'Relationship graph has no links.' }
        foreach ($link in $Graph.Links) {
            if (-not $ids.Contains($link.source) -or -not $ids.Contains($link.target)) { throw 'Relationship graph link references a missing node.' }
        }
        $copy = $Spec | Set-DenebSpecData -DataByName @{ 'node-data' = @($Graph.Nodes); 'link-data-raw' = @($Graph.Links) } |
            Set-DenebSpecSize -Width 820 -Height 500
        # Short multiline display labels; full source names stay unchanged in tooltips.
        foreach ($node in @($copy.data | Where-Object name -eq 'node-data')[0].values) {
            $lines = [Collections.Generic.List[string]]::new(); $line = ''
            foreach ($word in ($node.Label -split '\s+')) {
                if ($line -and ($line.Length + $word.Length + 1) -gt 18) { $lines.Add($line); $line = '' }
                $line = if ($line) { "$line $word" } else { $word }
            }
            if ($line) { $lines.Add($line) }
            $node['DisplayLabel'] = $lines.ToArray()
        }
        $copy.title = @{ text = $Graph.Title; anchor = 'start'; color = '#333333'; subtitleColor = '#595959'
            subtitleFontSize = 11; subtitle = @($Graph.Subtitle) }
        $copy.legends = @(@{ fill = 'color'; orient = 'bottom'; offset = 44; direction = 'horizontal'; title = 'Node type' })
        $linkMark = @($copy.marks | Where-Object name -eq 'links')[0]
        $linkMark.interactive = $true
        $linkMark.encode.update.strokeWidth = @{ signal = "datum.source.index===nodeHover.id || datum.target.index===nodeHover.id ? 2.5 : 1.1" }
        $linkMark.encode.update.strokeDash = @{ signal = 'datum.Historical ? [5,3] : [1,0]' }
        $linkMark.encode.update.tooltip = @{ signal = "{'From':datum.SourceLabel,'To':datum.TargetLabel,'Relationship':datum.value,'Source details':datum.Details}" }
        @($copy.marks | Where-Object name -eq 'arrows')[0].interactive = $false
        $nodeMark = @($copy.marks | Where-Object name -eq 'nodes')[0]
        $nodeMark.encode.hover.tooltip = @{ signal = "{'Name':datum.Label,'Type':datum.group,'Description':datum.Description,'Source':datum.Url}" }
        $labels = @($copy.marks | Where-Object name -eq 'labels')[0]
        $labels.encode.update.text = @{ field = 'datum.DisplayLabel' }
        $labels.encode.update.fill = @{ value = '#333333' }
        $labels.encode.update.x = @{ field = 'x' }
        $labels.encode.update.y = @{ field = 'y' }
        $labels.encode.update.align = @{ value = 'center' }
        $labels.encode.update.dy = @{ signal = 'datum.datum.index % 2 ? -18 : 18' }
        $labels.encode.update.baseline = @{ signal = "datum.datum.index % 2 ? 'bottom' : 'top'" }
        $labels.encode.update.lineHeight = @{ value = 12 }
        $labels.encode.update.limit = @{ value = 135 }
        $labels.encode.update.ellipsis = @{ value = '…' }
        $labels.encode.update.fontSize = @{ value = 11 }
        # Keep labels bound to node scene coordinates during force ticks, dragging and zoom.
        foreach ($signal in $copy.signals) {
            if ($signal.name -in @('nodeRadius', 'nodeRadiusKey')) { $signal.value = 10 }
            if ($signal.name -eq 'linkDistance') { $signal.value = 150 }
            if ($signal.name -eq 'nodeCharge') { $signal.value = -90 }
        }
        $credit = @($copy.marks | Where-Object { ($_.encode.update.text.value -join ' ') -match 'Dataviz: David Bacci' })
        if ($credit.Count -ne 1) { throw 'Expected original force-graph author credit.' }
        $credit[0].encode.update.x = @{ value = 4 }
        # Reserve a small footer instead of allowing moving nodes to cover attribution.
        $credit[0].encode.update.y = @{ signal = 'height + 20' }
        $credit[0].encode.update.text.value = @("Source: $($Graph.Source) | $($Graph.Nodes.Count) nodes, $($Graph.Links.Count) links | Dataviz: David Bacci")
        $copy.usermeta = Copy-DenebSpec $Graph
        $copy.usermeta.Remove('Nodes'); $copy.usermeta.Remove('Links')
        $copy
    }
}
