param(
    [string]$OutputPath = ".\publish",
    [string]$Configuration = "Release"
)

Write-Host "Publishing MouseClickRecorder..." -ForegroundColor Green

Write-Host "Cleaning previous outputs..." -ForegroundColor Yellow
Remove-Item -Recurse -Force bin, obj -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $OutputPath -ErrorAction SilentlyContinue

Write-Host "Running dotnet publish..." -ForegroundColor Yellow
dotnet publish -c $Configuration -r win-x64 --self-contained false -o $OutputPath -f net8.0-windows

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed." -ForegroundColor Red
    exit 1
}

Write-Host "Copying icon assets..." -ForegroundColor Yellow
if (Test-Path ".\ico") {
    New-Item -ItemType Directory -Path "$OutputPath\ico" -Force | Out-Null
    Copy-Item -Path ".\ico\*" -Destination "$OutputPath\ico" -Force
}

Write-Host "Removing unnecessary files..." -ForegroundColor Yellow
$filesToRemove = @(
    "$OutputPath\*.pdb",
    "$OutputPath\log.txt"
)

foreach ($pattern in $filesToRemove) {
    Remove-Item -Force $pattern -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Publish completed." -ForegroundColor Green
Write-Host "Output directory: $OutputPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files:" -ForegroundColor Cyan

$files = Get-ChildItem -Path $OutputPath -Recurse | Where-Object { -not $_.PSIsContainer }
$totalSize = 0

foreach ($file in $files) {
    $size = $file.Length
    $totalSize += $size
    $sizeStr = if ($size -gt 1MB) {
        "{0:N2} MB" -f ($size / 1MB)
    } elseif ($size -gt 1KB) {
        "{0:N2} KB" -f ($size / 1KB)
    } else {
        "$size B"
    }

    Write-Host ("  {0,-40} {1,10}" -f $file.Name, $sizeStr)
}

$totalSizeStr = if ($totalSize -gt 1MB) {
    "{0:N2} MB" -f ($totalSize / 1MB)
} elseif ($totalSize -gt 1KB) {
    "{0:N2} KB" -f ($totalSize / 1KB)
} else {
    "$totalSize B"
}

Write-Host ""
Write-Host "Total size: $totalSizeStr" -ForegroundColor Green

$zipPath = ".\MouseClickRecorder-v1.0.zip"
Write-Host ""
Write-Host "Creating archive: $zipPath" -ForegroundColor Yellow

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path "$OutputPath\*" -DestinationPath $zipPath -Force

$zipSize = (Get-Item $zipPath).Length
$zipSizeStr = if ($zipSize -gt 1MB) {
    "{0:N2} MB" -f ($zipSize / 1MB)
} elseif ($zipSize -gt 1KB) {
    "{0:N2} KB" -f ($zipSize / 1KB)
} else {
    "$zipSize B"
}

Write-Host "Archive size: $zipSizeStr" -ForegroundColor Green
Write-Host ""
Write-Host "Publish succeeded." -ForegroundColor Green
