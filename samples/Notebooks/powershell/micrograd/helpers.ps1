function Zip {
    param(
        [Parameter(Mandatory)]
        [object[]]$Left,

        [Parameter(Mandatory)]
        [object[]]$Right
    )

    $count = [Math]::Min($Left.Count, $Right.Count)

    for ($i = 0; $i -lt $count; $i++) {
        [pscustomobject]@{
            Left  = $Left[$i]
            Right = $Right[$i]
        }
    }
}

function Sum-Value {
    param([scriptblock]$Selector)

    begin { $sum = [Value]::new(0.0, 'loss') }
    process { $sum = $sum + (& $Selector $_) }
    end { $sum }
}

function Invoke-ValueBackward {
    param(
        [Parameter(Mandatory)]
        [Value]$Value
    )

    $topologicalOrder = [System.Collections.Generic.List[Value]]::new()
    $visited = [System.Collections.Generic.HashSet[Value]]::new()

    function Visit-Value([Value]$Current) {
        if (-not $visited.Add($Current)) {
            return
        }

        foreach ($child in $Current.children) {
            Visit-Value $child
        }

        $topologicalOrder.Add($Current)
    }

    Visit-Value $Value
    $Value.grad = 1.0

    for ($i = $topologicalOrder.Count - 1; $i -ge 0; $i--) {
        & $topologicalOrder[$i].backward
    }
}
