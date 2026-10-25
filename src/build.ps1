$ErrorActionPreference='Stop'
$productDir=Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\bin'
[void](New-Item -ItemType Directory -Path $productDir -Force)
$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compilerPath)){$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object {$_.FullName})
$references=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Core.dll','/r:System.ServiceProcess.dll','/r:System.Web.Extensions.dll','/r:Microsoft.CSharp.dll')
foreach($build in @(@('TrackPointSwitch.exe','anycpu','winexe'),@('SynapticsHost32.exe','x86','exe'))){
  if($build[1] -eq 'x86'){$compilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
  & $compilerPath /nologo ('/target:'+$build[2]) ('/platform:'+$build[1]) /optimize+ /codepage:65001 /warnaserror+ @references ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) ('/out:'+(Join-Path $productDir $build[0])) @sources
  if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $productDir ($build[0]+'.config')) -Force
}
Write-Output 'Built AnyCPU main application and x86 vendor COM helper.'
