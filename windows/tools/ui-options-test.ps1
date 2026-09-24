# Drives the installed control panel with real mouse input and checks that
# every option of every module reaches settings.json. Run from windows/.
param([string]$Exe = "$env:LOCALAPPDATA\Programs\AfterDork\AfterDork.exe")

$ui = (Resolve-Path "tools\AfterDork.UiDriver\bin\Release\net8.0-windows\uidriver.exe").Path
$settingsPath = "$env:APPDATA\AfterDork\settings.json"
$catalog = @(
    @{ id = 'FlyingFlasks';    opts = @(@{k='flock';kind='int';min=4;max=40}, @{k='drips';kind='check'}) },
    @{ id = 'GlasswarePipes';  opts = @(@{k='speed';kind='float';min=0.3;max=3}, @{k='alembics';kind='check'}) },
    @{ id = 'LatticeMaze';     opts = @(@{k='speed';kind='float';min=0.3;max=3}, @{k='gas';kind='int';min=0;max=60}) },
    @{ id = 'MystifyPolymers'; opts = @(@{k='speed';kind='float';min=0.3;max=3}, @{k='echo';kind='int';min=4;max=30}) },
    @{ id = 'StoddartReef';    opts = @(@{k='population';kind='int';min=3;max=20}, @{k='bubbles';kind='check'}) },
    @{ id = 'OrbitalBox';      opts = @(@{k='spin';kind='float';min=0.2;max=3}, @{k='morph';kind='float';min=0.3;max=3}) },
    @{ id = 'SmilesRain';      opts = @(@{k='speed';kind='float';min=0.3;max=3}, @{k='reveals';kind='check'}) },
    @{ id = 'CastawayChemist'; opts = @(@{k='chaos';kind='float';min=0.3;max=3}, @{k='quench';kind='check'}) }
)

function Get-Opt($m, $k) {
    try { $j = Get-Content $settingsPath -Raw | ConvertFrom-Json; return $j.$m.$k } catch { return $null }
}

Set-Content $settingsPath '{}'  # start from defaults (every check on)
$p = Start-Process $Exe -PassThru
Start-Sleep 5
$w = & $ui window "After Dork 1.2"
if ($w -notmatch 'client=\((-?\d+),(-?\d+),') { throw "control panel window not found" }
$ox = [int]$matches[1]; $oy = [int]$matches[2]
$fail = 0
for ($i = 0; $i -lt $catalog.Count; $i++) {
    $mod = $catalog[$i]
    & $ui click ($ox + 60) ($oy + 36 + 2 + $i * 20 + 10); Start-Sleep 2
    for ($r = 0; $r -lt 2; $r++) {
        $o = $mod.opts[$r]; $cy = $oy + 352 + $r * 34 + 9
        if ($o.kind -eq 'check') {
            & $ui click ($ox + 387) $cy; Start-Sleep 1; $v1 = Get-Opt $mod.id $o.k
            & $ui click ($ox + 387) $cy; Start-Sleep 1; $v2 = Get-Opt $mod.id $o.k
            $ok = ($v1 -eq $false) -and ($v2 -eq $true)
            "{0} {1,-16} {2,-10} off->{3} on->{4}" -f ($(if ($ok) {'PASS'} else {'FAIL'})), $mod.id, $o.k, $v1, $v2
        } else {
            & $ui click ($ox + 380 + 2) $cy; Start-Sleep 1; $vmin = Get-Opt $mod.id $o.k
            & $ui drag ($ox + 380 + 10) $cy ($ox + 380 + 224) $cy 10; Start-Sleep 1; $vmax = Get-Opt $mod.id $o.k
            $ok = ([math]::Abs([double]$vmin - $o.min) -lt 1e-9) -and ([math]::Abs([double]$vmax - $o.max) -lt 1e-9)
            "{0} {1,-16} {2,-10} min->{3} max->{4}" -f ($(if ($ok) {'PASS'} else {'FAIL'})), $mod.id, $o.k, $vmin, $vmax
        }
        if (-not $ok) { $fail++ }
    }
    & $ui capture "out\opts-$($mod.id).png" $ox $oy 720 540 | Out-Null
}
& $ui close "After Dork 1.2" | Out-Null
"failures: $fail"
