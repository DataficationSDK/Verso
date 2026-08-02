#Requires -Modules @{ ModuleName = 'PSQuickGraph'; ModuleVersion = '2.6.0' }
#Requires -Modules @{ ModuleName = 'PSGraphView'; ModuleVersion = '0.2.0' }

function New-ValueNode([Value]$value) {
    [pscustomobject]@{
        kind  = 'value'
        value = $value
        label = $value.label
        data  = $value.data
        grad  = $value.grad
    }
}

function New-OperationNode([Value]$value) {
    [pscustomobject]@{
        kind      = 'op'
        value     = $value
        label     = $value.operation
        operation = $value.operation
        data      = $value.data
    }
}


# visualisation
function New-ExpressionGraph {
    param(
        [Value]$val,
        $graph = $null,
        [hashtable]$valueVertices = $null,
        [hashtable]$operationVertices = $null
    )

    if ($null -eq $graph) {
        $graph = New-Graph -UseNonUniqueLabels
    }

    if ($null -eq $valueVertices) {
        $valueVertices = @{}
    }

    if ($null -eq $operationVertices) {
        $operationVertices = @{}
    }

    # A Value is reachable from every parent that consumed it, so this is a DAG rather than
    # a tree. Without this guard a shared node picks up one duplicate edge per parent and
    # has its whole subtree walked again each time it is reached.
    if ($valueVertices.ContainsKey($val)) {
        return $graph
    }

    $valueVertex = Add-Vertex -Graph $graph -Vertex (New-ValueNode $val) -PassThru
    $valueVertices[$val] = $valueVertex

    if ($val.operation) {
        $operationVertex = Add-Vertex -Graph $graph -Vertex (New-OperationNode $val) -PassThru
        $operationVertices[$val] = $operationVertex

        Add-Edge -From $operationVertex -To $valueVertex -Graph $graph | Out-Null
    }

    foreach ($child in $val.children) {
        New-ExpressionGraph `
            -val $child `
            -graph $graph `
            -valueVertices $valueVertices `
            -operationVertices $operationVertices | Out-Null

        if ($val.operation) {
            Add-Edge -From $valueVertices[$child] -To $operationVertex -Graph $graph | Out-Null
        }
    }

    return $graph
}



# backprop
function New-BackpropagationGraph {
    param(
        [Value]$val,
        $graph = $null,
        [hashtable]$valueVertices = $null
    )

    if ($null -eq $graph) {
        $graph = New-Graph -UseNonUniqueLabels
    }

    if ($null -eq $valueVertices) {
        $valueVertices = @{}
    }

    if (-not $valueVertices.ContainsKey($val)) {
        $valueVertices[$val] = Add-Vertex -Graph $graph -Vertex $val -PassThru
    }

    $currentVertex = $valueVertices[$val]

    foreach ($child in $val.children) {
        if (-not $valueVertices.ContainsKey($child)) {
            New-BackpropagationGraph `
                -Graph $graph `
                -val $child `
                -valueVertices $valueVertices | Out-Null
        }

        $childVertex = $valueVertices[$child]
        Add-Edge -Graph $graph -From $childVertex -To $currentVertex | Out-Null
    }

    return $graph
}


function Show-ExpressionGraph {
    param (
        $graph,
        $rankdir = "LR"
    )

    # node formatting lambda
    $vs = {
        $node = $_

        if ($node.kind -eq 'op') {
            @{
                shape = 'Ellipse'
                label = $node.label
            }
        }
        else {
            $name = if ($node.label) { $node.label } else { '' }

            @{
                shape = 'Record'
                label = "{ $name | data $($node.data) | grad $($node.grad) }"
            }
        }
    }

    $dot = Export-Graph -Graph $graph -Format Graphviz -GraphScript { @{ rankdir = $rankdir; label = 'Micrograd' } } -VertexScript $vs

    $svg = Export-GraphvizView -InputObject $dot -Renderer Dot -As Svg
    Display $svg 'image/svg+xml'
}
