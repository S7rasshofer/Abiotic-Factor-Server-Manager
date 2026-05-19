# Keeps publish/FacilityOverseer.exe in sync with the source tree.
#
# Invoked by the project Stop hook (.claude/settings.json) after every turn.
# Cheap no-op unless something under src/ is newer than the published exe.
#
# IMPORTANT: publish/ may live inside OneDrive. The single-file bundler does a
# delete-then-write on the exe, which OneDrive/AV intermittently locks
# ("Access to the path ... is denied"). So we publish to a NON-synced staging
# dir under %LOCALAPPDATA% and then drop just the self-contained exe into
# publish/ with a short retry loop. Best-effort: never throws, always exits 0.

$ErrorActionPreference = 'Stop'

function Write-Log([string] $msg) {
    try {
        $logPath = Join-Path $PSScriptRoot '..\publish\publish-hook.log'
        New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
        ("[{0}] {1}" -f (Get-Date -Format o), $msg) |
            Out-File -FilePath $logPath -Append -Encoding utf8
    }
    catch { }
}

try {
    $repo = Split-Path -Parent $PSScriptRoot          # tools\ -> repo root
    $src  = Join-Path $repo 'src'
    $exe  = Join-Path $repo 'publish\FacilityOverseer.exe'
    $proj = Join-Path $repo 'src\AbioticServerManager.App\AbioticServerManager.App.csproj'

    if (-not (Test-Path $src)) { exit 0 }

    $newest = Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $newest) { exit 0 }

    $exeItem = Get-Item $exe -ErrorAction SilentlyContinue
    if ($null -ne $exeItem -and
        $exeItem.LastWriteTimeUtc -ge $newest.LastWriteTimeUtc) {
        exit 0   # already current - silent no-op
    }

    Write-Log ("republish triggered by {0}" -f $newest.Name)

    $stage = Join-Path $env:LOCALAPPDATA 'FacilityOverseer\publish-stage'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    $out = & dotnet publish $proj -c Release -o $stage --nologo 2>&1 | Out-String
    Write-Log $out

    $stagedExe = Join-Path $stage 'FacilityOverseer.exe'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $stagedExe)) {
        '{"systemMessage":"FacilityOverseer.exe republish FAILED (build) - see publish/publish-hook.log"}'
        exit 0
    }

    # Drop the single self-contained exe into publish/, retrying past the
    # transient OneDrive/AV lock that breaks an in-place single-file publish.
    New-Item -ItemType Directory -Force -Path (Split-Path $exe) | Out-Null
    $copied = $false
    for ($i = 1; $i -le 6; $i++) {
        try {
            Copy-Item -LiteralPath $stagedExe -Destination $exe -Force
            $copied = $true
            break
        }
        catch {
            Write-Log ("copy attempt {0} failed: {1}" -f $i, $_.Exception.Message)
            Start-Sleep -Seconds 3
        }
    }

    if ($copied) {
        '{"systemMessage":"FacilityOverseer.exe republished (source changed)."}'
    }
    else {
        '{"systemMessage":"FacilityOverseer.exe rebuilt but could not be copied into publish/ (OneDrive lock). See publish/publish-hook.log"}'
    }

    exit 0
}
catch {
    Write-Log ("hook error: {0}" -f $_.Exception.Message)
    exit 0
}
