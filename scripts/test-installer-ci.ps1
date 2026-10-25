# Destructive install/uninstall tests run only on a disposable GitHub-hosted VM.
$ErrorActionPreference = 'Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This script is restricted to disposable GitHub-hosted runners.'
}
if(Get-Service -Name 'AccuPointSwitch' -ErrorAction SilentlyContinue) { throw 'Refusing to replace an existing service' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$setup = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts\release') -Filter '*Setup*.exe')
if($setup.Count -ne 1) { throw 'Expected exactly one installer' }
$version = [regex]::Match((Get-Content -LiteralPath (Join-Path $repoRoot 'src\Version.cs') -Raw),'Value = "([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
$installDir = Join-Path $env:ProgramFiles 'AccuPointSwitch'
$report = [Collections.Generic.List[string]]::new()
$report.Add('SOURCE_COMMIT='+$env:GITHUB_SHA)
$report.Add('INSTALLER_SHA256='+(Get-FileHash -LiteralPath $setup[0].FullName -Algorithm SHA256).Hash)
$report.Add('OS='+[Environment]::OSVersion.VersionString)
function Run-Installer([string]$File, [string]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if(-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Installer test timeout' }
    if($process.ExitCode -ne 0) { throw "Installer returned $($process.ExitCode)" }
}
try {
    foreach($phase in @('fresh','upgrade')) {
        Run-Installer $setup[0].FullName '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
        $service = Get-CimInstance Win32_Service -Filter "Name='AccuPointSwitch'"
        if($service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or $service.StartName -ne 'LocalSystem') { throw 'Service configuration mismatch' }
        if($service.PathName -ne ('"'+(Join-Path $installDir 'TrackPointSwitch.exe')+'" --service')) { throw 'Service image path mismatch' }
        foreach($file in @('TrackPointSwitch.exe','TrackPointSwitch.exe.config','SynapticsHost32.exe','SynapticsHost32.exe.config')) {
            $source = Join-Path $repoRoot ('artifacts\bin\'+$file)
            if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installDir $file)).Hash) { throw "Payload mismatch: $file" }
        }
        if((Get-Item -LiteralPath (Join-Path $installDir 'TrackPointSwitch.exe')).VersionInfo.FileVersion -ne ($version+'.0')) { throw 'Application version mismatch' }
        $shell = New-Object -ComObject WScript.Shell
        $shortcutIndex = 0
        $allLinks = @(Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('CommonDesktopDirectory')) -Filter '*.lnk' | ForEach-Object {
            # WSH may fail to load a Unicode shortcut filename on a non-Chinese
            # system locale. Inspect an identical copy with an ASCII filename.
            $readPath = Join-Path $repoRoot ('artifacts\tests\shortcut-check-'+($shortcutIndex++)+'.lnk')
            Copy-Item -LiteralPath $_.FullName -Destination $readPath -Force
            $shortcut = $shell.CreateShortcut($readPath)
            [pscustomobject]@{Name=$_.Name; TargetPath=$shortcut.TargetPath; Arguments=$shortcut.Arguments}
        })
        $links = @($allLinks | Where-Object {$_.TargetPath -eq (Join-Path $installDir 'TrackPointSwitch.exe')})
        if($links.Count -ne 2 -or '--toggle' -notin $links.Arguments -or '--ui' -notin $links.Arguments) { throw ('Desktop shortcuts mismatch: '+($allLinks | ConvertTo-Json -Compress)) }
        $report.Add("PASS $phase install: service, payload hashes, version and desktop shortcuts")
    }
    Run-Installer (Join-Path $installDir 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    if(Get-Service -Name 'AccuPointSwitch' -ErrorAction SilentlyContinue) { throw 'Service remains after uninstall' }
    if(Test-Path -LiteralPath (Join-Path $installDir 'TrackPointSwitch.exe')) { throw 'Application remains after uninstall' }
    $report.Add('PASS uninstall: service and application removed')
    $report.Add('SCOPE disposable Windows VM; no physical pointing-stick actuation tested')
    $report.Add('RESULT=PASS')
} catch { $report.Add('FAIL '+$_.Exception.Message); throw }
finally {
    $report | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts\tests\installer-ci.txt') -Encoding UTF8
    $report | Write-Output
}
