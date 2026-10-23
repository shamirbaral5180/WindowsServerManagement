param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts/publish/win-x64"
)

$ErrorActionPreference = "Stop"

$solutionRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $solutionRoot "src/WindowsServerManagement.App/WindowsServerManagement.App.csproj"
$outputPath = Join-Path $solutionRoot $Output

Write-Host "Publishing Server Manager..." -ForegroundColor Cyan
Write-Host "Project: $project"
Write-Host "Output:  $outputPath"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "Publishing failed with exit code $LASTEXITCODE."
}

Write-Host "Published successfully: $outputPath" -ForegroundColor Green
