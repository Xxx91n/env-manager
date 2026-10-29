# residue-probe.ps1 - PROTOTYPE helper: list what a helper left behind under a temp root,
# and whether a plaintext marker string is actually in those bytes. Runnable as
# LocalSystem (for C:\Windows\Temp) or as the interactive user (for their own %TEMP%).

param(
    [Parameter(Mandatory)][string]$Root,
    [string]$Pattern = 'env-manager-sops-*',
    [string]$Marker = '',
    [switch]$Delete
)

$ErrorActionPreference = 'Continue'
$identity = try { [System.Security.Principal.WindowsIdentity]::GetCurrent().Name } catch { 'unknown' }
Write-Output "RESIDUE identity=$identity root=$Root pattern=$Pattern"

if (-not (Test-Path $Root)) {
    Write-Output 'RESIDUE root_missing=True'
    exit 0
}

$dirs = @(Get-ChildItem -Path $Root -Filter $Pattern -Directory -ErrorAction SilentlyContinue)
Write-Output "RESIDUE dirs=$($dirs.Count)"
foreach ($d in $dirs) {
    $files = @(Get-ChildItem -Path $d.FullName -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($f in $files) {
        $head = ''
        $hasMarker = $false
        try {
            $txt = [System.IO.File]::ReadAllText($f.FullName)
            $head = ($txt -replace '\s+', ' ')
            if ($head.Length -gt 220) { $head = $head.Substring(0, 220) + '...' }
            if ($Marker) { $hasMarker = $txt.Contains($Marker) }
        } catch { $head = '(unreadable: ' + $_.Exception.GetType().Name + ')' }
        Write-Output "RESIDUE FILE path=$($f.FullName) bytes=$($f.Length) contains_marker=$hasMarker head=$head"
    }
    Write-Output "RESIDUE DIR path=$($d.FullName) files=$($files.Count) created=$($d.CreationTime.ToString('o'))"
}

if ($Delete) {
    foreach ($d in $dirs) {
        try { Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction Stop; Write-Output "RESIDUE DELETED $($d.FullName)" }
        catch { Write-Output "RESIDUE DELETE-FAILED $($d.FullName) $($_.Exception.Message)" }
    }
}
Write-Output 'RESIDUE done'
