# Installs Mono for Windows x64 for windows-host.yml: 6.12.0.206, the last Windows release
# (Mono 6.14 from WineHQ has no Windows installer). The MSI comes from download.mono-project.com
# and its SHA-256 is checked on every use (the same value as Chocolatey's mono package,
# 2026-10-28). It is kept in $env:RUNNER_TEMP\mono-msi, which actions/cache restores and saves
# (.github/CI.md, "Caches"). Mono goes to C:\Program Files\Mono; nothing is added to PATH.
$ErrorActionPreference = 'Stop'
$Version = '6.12.0.206'
$Sha256 = '4125f57d97cfa88257915edc969e913de198cd8e22396a29849037479a0ac368'
$Dir = Join-Path $env:RUNNER_TEMP 'mono-msi'
$Msi = Join-Path $Dir "mono-$Version-x64-0.msi"
New-Item -ItemType Directory -Force -Path $Dir | Out-Null
if (-not (Test-Path $Msi)) {
    & curl.exe -sSfL --retry 3 -o $Msi "https://download.mono-project.com/archive/6.12.0/windows-installer/mono-$Version-x64-0.msi"
    if ($LASTEXITCODE -ne 0) { throw "download failed: curl exit $LASTEXITCODE" }
}
$Actual = (Get-FileHash -Algorithm SHA256 -Path $Msi).Hash.ToLowerInvariant()
if ($Actual -ne $Sha256) {
    Remove-Item $Msi
    throw "mono-$Version-x64-0.msi: SHA-256 $Actual, expected $Sha256"
}
$Log = Join-Path $env:RUNNER_TEMP 'mono-install.log'
$Install = Start-Process -FilePath msiexec.exe -ArgumentList @('/i', "`"$Msi`"", '/qn', '/norestart', '/l*v', "`"$Log`"") -Wait -PassThru
if ($Install.ExitCode -ne 0) {
    Get-Content $Log -Tail 40
    throw "msiexec exit $($Install.ExitCode)"
}
& 'C:\Program Files\Mono\bin\mono.exe' --version | Select-Object -First 1
