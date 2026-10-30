# install-sops-age.ps1 - put the pinned sops + age binaries on a CI runner's PATH
# License: Apache-2.0
#
# Why this exists: SopsContractTests is the only test that runs the REAL sops binary
# against SopsProvider, and both CI lanes used to skip it. verify (windows) had no sops
# on the host and the in-test download is gated behind the container opt-in; verify-l1
# (ubuntu) reached the download, but the age archives unpack into an age/ subfolder the
# provisioner did not look in, so no key file was generated and the test skipped. The
# skip text never surfaced in the summary, so "Passed!" hid it.
#
# Installing here instead of inside the test also matters for the provider itself:
# SopsProvider resolves its binary once, in a static initializer, from SOPS_PATH / PATH.
# Other tests touch SopsProvider long before SopsContractTests sets SOPS_PATH, so only a
# binary that is already on PATH when the test process starts is guaranteed to be the
# one the provider runs.
#
# Versions match L1ToolProvisioner (SopsVersion / AgeVersion). Every download is checked
# against a pinned SHA-256 before it is made executable: a mismatch fails the step, and
# a failed step fails the job.
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path ([IO.Path]::GetTempPath()) 'env-manager-ci-sops-age')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SopsVersion = '3.13.3'
$AgeVersion = '1.3.2'

# sops: from the release's own sops-v<ver>.checksums.txt.
# age: the release publishes .proof files, not checksums; these are the SHA-256 of the
# release archives as downloaded from the v1.3.2 tag.
$assets = if ($IsWindows) {
    @{
        SopsUrl  = "https://github.com/getsops/sops/releases/download/v$SopsVersion/sops-v$SopsVersion.amd64.exe"
        SopsSha  = 'a4a9a398858fe8b2ef72d9686d930bf7c5cece9be74ad83ac3b53cfdd70e6b1c'
        SopsName = 'sops.exe'
        AgeUrl   = "https://github.com/FiloSottile/age/releases/download/v$AgeVersion/age-v$AgeVersion-windows-amd64.zip"
        AgeSha   = 'f48d8f8f9ebe903ab5027ed067652f2cc1db94bc206976430133b905dcd8e8c7'
        AgeFile  = 'age.zip'
    }
} elseif ($IsLinux) {
    @{
        SopsUrl  = "https://github.com/getsops/sops/releases/download/v$SopsVersion/sops-v$SopsVersion.linux.amd64"
        SopsSha  = 'e5bec3346a873ae91d871550f3e698c1aad962aff462a080e40f25fde17fef6b'
        SopsName = 'sops'
        AgeUrl   = "https://github.com/FiloSottile/age/releases/download/v$AgeVersion/age-v$AgeVersion-linux-amd64.tar.gz"
        AgeSha   = 'cbe24006683f8eb669266162894b9a522a1af52f2665fbc63a4bb032ed26ac10'
        AgeFile  = 'age.tar.gz'
    }
} else {
    throw "install-sops-age: unsupported runner OS (windows and linux amd64 only)"
}

function Save-Verified([string]$Url, [string]$Target, [string]$Sha256) {
    Invoke-WebRequest -Uri $Url -OutFile $Target -UseBasicParsing
    $actual = (Get-FileHash -Path $Target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) {
        Remove-Item -Force $Target
        throw "install-sops-age: SHA-256 mismatch for $Url (expected $Sha256, got $actual)"
    }
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$sopsPath = Join-Path $InstallDir $assets.SopsName
Save-Verified $assets.SopsUrl $sopsPath $assets.SopsSha

$archive = Join-Path $InstallDir $assets.AgeFile
Save-Verified $assets.AgeUrl $archive $assets.AgeSha
if ($IsWindows) {
    Expand-Archive -Path $archive -DestinationPath $InstallDir -Force
} else {
    & tar -xzf $archive -C $InstallDir
    if ($LASTEXITCODE -ne 0) { throw "install-sops-age: tar exited $LASTEXITCODE" }
}
Remove-Item -Force $archive

# Both archives unpack into an age/ subfolder; that folder is what goes on PATH.
$ageDir = Join-Path $InstallDir 'age'
if (-not $IsWindows) {
    & chmod +x $sopsPath (Join-Path $ageDir 'age') (Join-Path $ageDir 'age-keygen')
    if ($LASTEXITCODE -ne 0) { throw "install-sops-age: chmod exited $LASTEXITCODE" }
}

$exe = if ($IsWindows) { '.exe' } else { '' }
foreach ($required in @($sopsPath, (Join-Path $ageDir "age$exe"), (Join-Path $ageDir "age-keygen$exe"))) {
    if (-not (Test-Path $required)) { throw "install-sops-age: expected binary missing after install: $required" }
}

& $sopsPath --version
if ($LASTEXITCODE -ne 0) { throw "install-sops-age: sops --version exited $LASTEXITCODE" }

if ($env:GITHUB_PATH) {
    Add-Content -Path $env:GITHUB_PATH -Value $InstallDir
    Add-Content -Path $env:GITHUB_PATH -Value $ageDir
}
Write-Host "install-sops-age: sops $SopsVersion + age $AgeVersion ready in $InstallDir (age in $ageDir)"
