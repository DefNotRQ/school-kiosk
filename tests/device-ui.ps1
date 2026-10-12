# Interactive QA helper. Dumps stay in ignored artifacts; never publish device data.
param([Parameter(Mandatory=$true)][string]$Adb)
$script:KioskAdb = $Adb
$script:KioskUiFile = Join-Path $PSScriptRoot '../artifacts/current-ui.xml'
function Get-KioskUi {
    & $script:KioskAdb shell uiautomator dump /sdcard/kiosk-qa.xml | Out-Null
    & $script:KioskAdb pull /sdcard/kiosk-qa.xml $script:KioskUiFile | Out-Null
    [xml](Get-Content -LiteralPath $script:KioskUiFile)
}
function Tap-KioskText([string]$Text) {
    $ui = Get-KioskUi
    $node = $ui.SelectNodes('//node') | Where-Object { $_.text -eq $Text -or $_.'content-desc' -eq $Text } | Select-Object -First 1
    if (-not $node) { throw "UI control not found: $Text" }
    $c = [regex]::Matches($node.bounds, '\d+')
    $x = [int](([int]$c[0].Value + [int]$c[2].Value) / 2)
    $y = [int](([int]$c[1].Value + [int]$c[3].Value) / 2)
    if ($x -le 0 -or $y -le 0) { throw "UI control not visible: $Text" }
    & $script:KioskAdb shell input tap $x $y
    Start-Sleep -Milliseconds 500
}
