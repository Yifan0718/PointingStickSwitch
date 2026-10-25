param([string]$ResultsDirectory = (Join-Path $PSScriptRoot '..\artifacts\tests'))
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $repoRoot 'src\build.ps1')
& (Join-Path $repoRoot 'tests\run-tests.ps1') -ResultsDirectory $ResultsDirectory
$ResultsDirectory = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$layoutDirectory = Join-Path $ResultsDirectory 'layout'
$appPath = Join-Path $repoRoot 'artifacts\bin\TrackPointSwitch.exe'
# Wait only for this process, not for a process tree that can include a user agent.
$layoutError = Join-Path $ResultsDirectory 'layout-error.txt'
$layout = Start-Process -FilePath $appPath -ArgumentList ('--layout-test "'+$layoutDirectory+'" --quiet') -PassThru -WindowStyle Hidden -RedirectStandardError $layoutError
if(-not $layout.WaitForExit(120000)) { $layout.Kill(); throw 'Layout test timed out' }
if($layout.ExitCode -ne 0) { throw ('Layout test failed: '+(Get-Content -LiteralPath $layoutError -Raw)) }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object {$_.FullName})
$fontTest = Join-Path $ResultsDirectory 'LayoutStress.exe'
& $compilerPath /nologo /target:exe /platform:anycpu /codepage:65001 /warnaserror+ /main:LayoutStress /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll ('/out:'+$fontTest) @sources (Join-Path $repoRoot 'tests\LayoutStress.cs')
if($LASTEXITCODE -ne 0) { throw 'Font test build failed' }
& $fontTest (Join-Path $ResultsDirectory 'layout-font-only.txt')
if($LASTEXITCODE -ne 0) { throw 'Font test failed' }
Write-Output 'PASS: core x64/x86 and 24 layout scenarios. No physical device state was changed.'
