param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "BrandTheBuilding.csproj"

dotnet build $project -c $Configuration

if ($LASTEXITCODE -ne 0) {
    throw "Brand the Building build failed with exit code $LASTEXITCODE."
}
