param(
    [string]$GodotPath,
    [switch]$SmokeTest,
    [switch]$UiFlowTest
)
$ErrorActionPreference = 'Stop'
if ($SmokeTest -and $UiFlowTest) { throw 'Escolha SmokeTest ou UiFlowTest.' }
if (-not $GodotPath -and $env:GODOT4) { $GodotPath = $env:GODOT4 }
if (-not $GodotPath) {
    $godotCommand = Get-Command godot,godot4 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($godotCommand) { $GodotPath = $godotCommand.Source }
}
if (-not $GodotPath) {
    $downloadRoot = Join-Path $env:USERPROFILE 'Downloads'
    $godotDirectories = Get-ChildItem -LiteralPath $downloadRoot -Directory -Filter 'Godot*mono*' -ErrorAction SilentlyContinue
    $godotCandidates = foreach ($godotDirectory in $godotDirectories) {
        Get-ChildItem -LiteralPath $godotDirectory.FullName -File -Filter 'Godot*_mono_win64.exe'
        foreach ($nestedDirectory in (Get-ChildItem -LiteralPath $godotDirectory.FullName -Directory)) {
            Get-ChildItem -LiteralPath $nestedDirectory.FullName -File -Filter 'Godot*_mono_win64.exe'
        }
    }
    $GodotPath = ($godotCandidates | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
if (-not $GodotPath -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Informe o executável Godot .NET: .\run.ps1 -GodotPath "C:\caminho\Godot_mono.exe"'
}
Push-Location $PSScriptRoot
$previousCompatLayer = $env:__COMPAT_LAYER
try {
    & dotnet build 'IdleLab.csproj' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
    # Apply only to this child process; no Windows compatibility settings are changed.
    $env:__COMPAT_LAYER = 'RunAsInvoker'
    $gameArguments = @('--path', ('"' + $PSScriptRoot + '"'))
    $windowStyle = 'Normal'
    if ($SmokeTest) {
        $gameArguments += @('--headless', '--', '--smoke-test')
        $windowStyle = 'Hidden'
    } elseif ($UiFlowTest) {
        $gameArguments += @('--', '--ui-flow-test')
    }
    $gameProcess = Start-Process -FilePath $GodotPath -ArgumentList $gameArguments -WorkingDirectory $PSScriptRoot -WindowStyle $windowStyle -PassThru -Wait
    if ($gameProcess.ExitCode -ne 0) { throw "Godot encerrou com código $($gameProcess.ExitCode)." }
} finally {
    $env:__COMPAT_LAYER = $previousCompatLayer
    Pop-Location
}
