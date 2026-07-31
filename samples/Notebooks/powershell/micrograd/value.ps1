class Value {
    hidden [double]$data
    hidden [string]$label=""
    hidden [array]$children = @()
    hidden [string]$operation = ""
    hidden [double]$grad = 0.0
    hidden $backward = {}

    Value([double] $data){
        $this.data = $data
    }

    Value([double] $data, [array] $children){
        $this.data = $data
        $this.children = $children
    }

    Value([double] $data, [string] $label){
        $this.data = $data
        $this.label = $label
    }

    Value([double] $data, $children, $operation){
        $this.data = $data
        $this.children = $children
        $this.operation = $operation
    }

    Value([double] $data, [array] $children, [string] $operation, [string] $label){
        $this.data = $data
        $this.label = $label
        $this.children = $children
        $this.operation = $operation
    }

    static [Value] op_Addition([Value]$left, [Value]$right) {
        $out = [Value]::new($left.data + $right.data, @($left, $right), "+", "+_res")

        $out.backward = {
            $left.grad += 1 * $out.grad
            $right.grad += 1 * $out.grad
        }.GetNewClosure()

        return $out
    }

    static [Value] op_Subtraction([Value]$left, [Value]$right) {
        $out = [Value]::new($left.data - $right.data, @($left, $right), "-", "-_res")

        $out.backward = {
            $left.grad += 1 * $out.grad
            $right.grad += -1 * $out.grad
        }.GetNewClosure()

        return $out
    }

    static [Value] op_Multiply([Value]$left, [Value]$right) {
        $out = [Value]::new($left.data * $right.data, @($left, $right), "*", "*_res")

        $out.backward = {
            $left.grad += $right.data * $out.grad
            $right.grad += $left.data * $out.grad
        }.GetNewClosure()

        return $out
    }

    [Value] Tanh(){
        $v = $this
        $t = [Math]::Tanh($this.data)
        $out = [Value]::new($t, @($this), "tanh")

        $out.backward = {
            $v.grad += (1 - [Math]::Pow($t, 2)) * $out.grad
        }.GetNewClosure()

        return $out

    }
}

Update-FormatData -path (Join-Path $PSScriptRoot 'Value.format.ps1xml')
