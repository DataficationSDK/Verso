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
    $scheduled = [System.Collections.Generic.HashSet[Value]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    $pending.Push([pscustomobject]@{ Value = $Value; Expanded = $false })

    while ($pending.Count -gt 0) {
        $frame = $pending.Pop()
        $current = [Value]$frame.Value

        if ($frame.Expanded) {
            $topologicalOrder.Add($current)
            continue
        }

        if (-not $scheduled.Add($current)) {
            continue
        }

        $pending.Push([pscustomobject]@{ Value = $current; Expanded = $true })
        foreach ($child in $current.children) {
            if (-not $scheduled.Contains($child)) {
                $pending.Push([pscustomobject]@{ Value = $child; Expanded = $false })
            }
        }
    }

    $Value.grad = 1.0

    for ($i = $topologicalOrder.Count - 1; $i -ge 0; $i--) {
        & $topologicalOrder[$i].backward
    }
}
