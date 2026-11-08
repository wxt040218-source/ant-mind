$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repoRoot '.tools\dotnet'
$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'

if (Test-Path -LiteralPath $dotnetExe) {
    $env:DOTNET_ROOT = $dotnetRoot
} else {
    $systemDotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if ($null -eq $systemDotnet) {
        throw 'The .NET 10 SDK is not available. Follow docs/DEVELOPMENT_SETUP.md to install it.'
    }
    $dotnetExe = $systemDotnet.Source
}

$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.tools\nuget'

New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES -Force | Out-Null
& $dotnetExe @args
exit $LASTEXITCODE
