# Builds a release version of InstaPrompt into publish\InstaPrompt (framework-dependent) or publish\InstaPrompt-selfcontained.
#   powershell -File tools\publish.ps1                 # needs the .NET 10 Desktop Runtime on the target PC (small)
#   powershell -File tools\publish.ps1 -SelfContained  # runs anywhere, ~150 MB
# ReadyToRun pre-compiles the code, which is what makes the first hotkey fast after a cold start.
param([switch]$SelfContained, [switch]$NoReadyToRun)

$root = Split-Path -Parent $PSScriptRoot
$name = if ($SelfContained) { 'InstaPrompt-selfcontained' } else { 'InstaPrompt' }
$out = Join-Path $root "publish\$name"

dotnet publish (Join-Path $root 'src\InstaPrompt.App\InstaPrompt.App.csproj') `
    -c Release -r win-x64 `
    --self-contained:$SelfContained `
    -p:PublishReadyToRun=$(-not $NoReadyToRun) `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Published to $out ({0:F1} MB)" -f $size
