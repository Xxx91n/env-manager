# Env Manager .NET Runtime Check
# Called by the portable/CLI-only ZIP to verify .NET 10 Desktop Runtime is installed.
# If missing, shows a friendly message with the official download link instead of
# the raw "You must install .NET to run this application" apphost error.

[CmdletBinding()]
param()

$requiredMajor = 10
$downloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0"

function Test-DotnetRuntime {
    try {
        $runtimes = dotnet --list-runtimes 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $runtimes) {
            return $false
        }
        return ($runtimes | Where-Object { $_ -match "Microsoft\.NETCore\.App\s+$requiredMajor\." }).Count -gt 0
    } catch {
        return $false
    }
}

if (-not (Test-DotnetRuntime)) {
    Write-Host ""
    Write-Host "================ Env Manager ================" -ForegroundColor Yellow
    Write-Host " .NET $requiredMajor Desktop Runtime not found." -ForegroundColor Yellow
    Write-Host ""
    Write-Host " Env Manager CLI requires .NET $requiredMajor Desktop Runtime." -ForegroundColor White
    Write-Host " Download for your architecture (x64 / x86 / ARM64):"
    Write-Host "   $downloadUrl"
    Write-Host ""
    Write-Host " After installing, re-run this script or env-manager-cli.exe."
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "[OK] .NET $requiredMajor runtime detected." -ForegroundColor Green
exit 0
