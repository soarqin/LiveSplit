# Verifies that the built DLL is a valid LiveSplit component plugin.
# Exit 0 = pass; non-zero = fail.
# NOTE: components/Directory.Build.props sets <OutputPath>$(BuildPath)\Components</OutputPath>
#       and props/LiveSplit.Paths.props sets $(BuildPath)=$(RootPath)\bin\$(Configuration.ToLowerInvariant()).
#       So the DLL ends up at <repo-root>/bin/<config-lower>/Components/LiveSplit.PinnedSplits.dll.
param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$configLower = $Configuration.ToLowerInvariant()
$dll = Join-Path $repoRoot "bin\$configLower\Components\LiveSplit.PinnedSplits.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    Write-Error "DLL not found at $dll"
    exit 1
}
Write-Host "DLL found: $dll"

# Pre-load core DLL (dependency) so reflection can resolve types
$coreDll = Join-Path $repoRoot "bin\$configLower\LiveSplit.Core.dll"
if (Test-Path -LiteralPath $coreDll) {
    [Reflection.Assembly]::LoadFrom((Resolve-Path $coreDll)) | Out-Null
}

$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll))
Write-Host "Assembly loaded: $($asm.FullName)"

# Find ComponentFactoryAttribute (case-insensitive class-name match for robustness)
$attrs = $asm.GetCustomAttributes($false) | Where-Object {
    $_.GetType().Name -eq 'ComponentFactoryAttribute'
}
if ($attrs.Count -ne 1) {
    Write-Error "Expected exactly 1 ComponentFactoryAttribute, found $($attrs.Count)"
    exit 2
}
Write-Host "ComponentFactoryAttribute count: 1 OK"

# The attribute exposes ComponentFactoryClassType (not Type) per
# src/LiveSplit.Core/UI/Components/ComponentFactoryAttribute.cs.
$factoryType = $attrs[0].ComponentFactoryClassType
if ($null -eq $factoryType) {
    Write-Error "ComponentFactoryAttribute.ComponentFactoryClassType is null"
    exit 3
}
if ($factoryType.Name -ne 'PinnedSplitsComponentFactory') {
    Write-Error "Expected PinnedSplitsComponentFactory, got $($factoryType.Name)"
    exit 4
}
Write-Host "Factory type: $($factoryType.FullName) OK"

Write-Host "All checks passed."
exit 0
