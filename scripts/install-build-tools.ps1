param([string]$ToolsDirectory = (Join-Path $PSScriptRoot '..\artifacts\tools'))
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $ToolsDirectory -Force)
$ToolsDirectory = (Resolve-Path -LiteralPath $ToolsDirectory).Path
$download = Join-Path $ToolsDirectory 'innosetup-6.7.3.exe'
$expectedHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
if(-not (Test-Path -LiteralPath $download)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
}
if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno Setup download hash mismatch' }
if((Get-AuthenticodeSignature -LiteralPath $download).Status -ne 'Valid') { throw 'Inno Setup publisher signature could not be verified' }
$destination = Join-Path $ToolsDirectory 'InnoSetup'
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="'+$destination+'"'
$process = Start-Process -FilePath $download -ArgumentList $arguments -PassThru -WindowStyle Hidden
if(-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Build tool installation timed out' }
if($process.ExitCode -ne 0) { throw 'Build tool installation failed' }
$compiler = Join-Path $destination 'ISCC.exe'
if(-not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup compiler missing' }
Write-Output $compiler
