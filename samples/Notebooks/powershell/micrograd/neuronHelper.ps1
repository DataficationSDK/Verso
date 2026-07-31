class Neuron {
    [Value[]]$w
    [Value]$b
    [bool]$nonlin

    Neuron([int]$nin) {
        $this.Init($nin, $true)
    }

    Neuron([int]$nin, [bool]$nonlin) {
        $this.Init($nin, $nonlin)
    }

    hidden [void] Init([int]$nin, [bool]$nonlin) {
        $this.w = for ($i = 0; $i -lt $nin; $i++) {
            [Value]::new(([Random]::Shared.NextDouble() * 2 - 1), "w$i")
        }

        $this.b = [Value]::new(([Random]::Shared.NextDouble() * 2 - 1), "b")
        $this.nonlin = $nonlin
    }

    [Value] Invoke([Value[]]$x) {
        if ($x.Count -ne $this.w.Count) {
            throw "Expected $($this.w.Count) inputs, got $($x.Count)."
        }

        $sum = $this.b
        for ($i = 0; $i -lt $this.w.Count; $i++) {
            $sum = $sum + ($this.w[$i] * $x[$i])
        }

        if ($this.nonlin) {
            return $sum.Tanh()
        }

        return $sum
    }

    [Value[]] parameters(){
        return @($this.w) + @($this.b)
    }
}

class Layer {
    [Neuron[]]$neurons = @()

    Layer([int]$nin, [int]$nout) {
        $this.Init($nin, $nout, $true)
    }

    Layer([int]$nin, [int]$nout, [bool]$nonlin) {
        $this.Init($nin, $nout, $nonlin)
    }

    hidden [void] Init([int]$nin, [int]$nout, [bool]$nonlin) {
        $this.neurons = for ($i = 0; $i -lt $nout; $i++) {
            [Neuron]::new($nin, $nonlin)
        }
    }

    [Value[]]Invoke([Value[]]$x) {
        $out = @()
        $out = foreach ($neuron in $this.neurons) {
            $neuron.Invoke($x)
        }

        return $out
    }

    [Value[]] parameters(){
        [Value[]]$params = @()
        foreach ($n in $this.neurons){
            $params += $n.parameters()
        }

        return $params
    }
}

class MLP {
    [Layer[]]$layers = @()

    MLP([int]$nin, [int[]]$nouts) {
        $this.Init($nin, $nouts, $true)
    }

    MLP([int]$nin, [int[]]$nouts, [bool]$outputNonlinear) {
        $this.Init($nin, $nouts, $outputNonlinear)
    }

    hidden [void] Init([int]$nin, [int[]]$nouts, [bool]$outputNonlinear) {
        $sizes = @($nin) + $nouts

        $this.layers = for ($i = 0; $i -lt $nouts.Count; $i++) {
            $nonlin = $outputNonlinear -or $i -ne ($nouts.Count - 1)
            [Layer]::new($sizes[$i], $sizes[$i + 1], $nonlin)
        }
    }

    [Value[]]Invoke([Value[]]$x) {
        $out = $x

        foreach ($layer in $this.layers) {
            $out = $layer.Invoke($out)
        }

        return $out;
    }
    [Value[]] parameters(){
        [Value[]]$params = @()
        foreach ($l in $this.layers){
            $params += $l.parameters()
        }

        return $params
    }
}
