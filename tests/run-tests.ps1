param([string]$ResultsDirectory=(Join-Path $PSScriptRoot '..\artifacts\tests'))
$ErrorActionPreference='Stop'
[void](New-Item -ItemType Directory -Path $ResultsDirectory -Force)
$ResultsDirectory=(Resolve-Path -LiteralPath $ResultsDirectory).Path
$sourceDir=Join-Path $PSScriptRoot '..\src'
$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compilerPath)){$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$sources=@(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | ForEach-Object {$_.FullName})
foreach($architecture in @('anycpu','x86')){
 if($architecture -eq 'x86'){$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
 $testExe=Join-Path $ResultsDirectory ('Tests-'+$architecture+'.exe')
 & $compilerPath /nologo /target:exe ('/platform:'+$architecture) /optimize+ /codepage:65001 /warnaserror+ /main:Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll ('/out:'+$testExe) @sources (Join-Path $PSScriptRoot 'Tests.cs') (Join-Path $PSScriptRoot 'RegressionTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Test build failed'}
 & $testExe $PSScriptRoot (Join-Path $ResultsDirectory ('core-results-'+$architecture+'.txt'))
 if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
}
