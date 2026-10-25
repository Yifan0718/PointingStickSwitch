param([string]$GitRef = 'HEAD')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $git = (Get-Command git -ErrorAction Stop).Source
    if(& $git status --porcelain) { throw 'Commit the source and evidence before packaging' }
    $commit = & $git rev-parse ($GitRef+'^{commit}')
    if($LASTEXITCODE -ne 0) { throw 'Cannot resolve release commit' }
    $head = & $git rev-parse HEAD
    if($head -ne $commit) { throw 'Check out the release commit before packaging' }
    $version = [regex]::Match((Get-Content src\Version.cs -Raw),'Value = "([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
    $release = Join-Path $repoRoot 'artifacts\release'
    $setup = Join-Path $release ('PointingStickSwitch-Setup-'+$version+'.exe')
    if(-not (Test-Path -LiteralPath $setup)) { throw 'Build or download the verified installer first' }
    if((Get-Item -LiteralPath $setup).VersionInfo.FileVersion -ne ($version+'.0')) { throw 'Installer version mismatch' }
    $source = Join-Path $release ('PointingStickSwitch-'+$version+'-source.zip')
    & $git archive --format=zip ('--prefix=PointingStickSwitch-'+$version+'/') ('--output='+$source) $commit
    if($LASTEXITCODE -ne 0) { throw 'Source archive failed' }
    $info = [ordered]@{ product='PointingStickSwitch'; version=$version; commit=$commit; compiler='Inno Setup 6.7.3'; framework='.NET Framework 4.8'; authenticodeSigned=$false; packagedUtc=[DateTime]::UtcNow.ToString('o'); installerSha256=(Get-FileHash -LiteralPath $setup).Hash; sourceSha256=(Get-FileHash -LiteralPath $source).Hash }
    $metadata = Join-Path $release 'build-info.json'
    [IO.File]::WriteAllText($metadata,($info | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $lines = @($setup,$source,$metadata) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) }
    [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
    $lines | Write-Output
} finally { Pop-Location }
