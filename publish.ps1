# 鼠标计数器发布脚本
# 生成精简版本的发布包

param(
    [string]$OutputPath = ".\publish",
    [string]$Configuration = "Release"
)

Write-Host "开始发布 MouseClickRecorder..." -ForegroundColor Green

# 清理旧的构建文件
Write-Host "清理旧的构建文件..." -ForegroundColor Yellow
Remove-Item -Recurse -Force bin, obj -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $OutputPath -ErrorAction SilentlyContinue

# 发布项目
Write-Host "发布项目..." -ForegroundColor Yellow
dotnet publish -c $Configuration -r win-x64 --self-contained false -o $OutputPath -f net6.0-windows

if ($LASTEXITCODE -ne 0) {
    Write-Host "发布失败！" -ForegroundColor Red
    exit 1
}

# 复制ico目录
Write-Host "复制ico目录..." -ForegroundColor Yellow
if (Test-Path ".\ico") {
    New-Item -ItemType Directory -Path "$OutputPath\ico" -Force | Out-Null
    Copy-Item -Path ".\ico\*" -Destination "$OutputPath\ico" -Force
}

# 删除不必要的文件
Write-Host "清理不必要的文件..." -ForegroundColor Yellow
$filesToRemove = @(
    "$OutputPath\*.pdb",
    "$OutputPath\log.txt"
)

foreach ($pattern in $filesToRemove) {
    Remove-Item -Force $pattern -ErrorAction SilentlyContinue
}

# 显示发布结果
Write-Host "`n发布完成！" -ForegroundColor Green
Write-Host "输出目录: $OutputPath" -ForegroundColor Cyan
Write-Host "`n文件列表:" -ForegroundColor Cyan

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

Write-Host "`n总大小: $totalSizeStr" -ForegroundColor Green

# 创建压缩包
$zipPath = ".\MouseClickRecorder-v1.0.zip"
Write-Host "`n创建压缩包: $zipPath" -ForegroundColor Yellow

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

Write-Host "压缩包大小: $zipSizeStr" -ForegroundColor Green
Write-Host "`n发布成功！" -ForegroundColor Green
