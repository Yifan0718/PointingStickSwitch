param([Parameter(Mandatory=$true)][string]$IsccPath)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$versionSource=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Version.cs') -Raw
$version=[regex]::Match($versionSource,'Value = "([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
if(-not $version){throw 'Missing application version'}
& $IsccPath ('/DAppVersion='+$version) (Join-Path $PSScriptRoot 'setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
