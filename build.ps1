# 编译脚本

# 设置构建配置
$configuration = "Release"
$outputDir = ".\bin\$configuration"
$zipFile = ".\release\MouseClickRecorder.zip"

# 创建输出目录（如果不存在）
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir
}

# 创建release目录（如果不存在）
if (-not (Test-Path ".\release")) {
    New-Item -ItemType Directory -Force -Path ".\release"
}

# 恢复依赖项
Write-Host "恢复依赖项..."
dotnet restore

# 编译项目
Write-Host "编译项目..."
dotnet publish MouseClickRecorder.csproj --configuration $configuration --output $outputDir

# 检查编译输出
Write-Host "编译输出内容:"
Get-ChildItem -Path $outputDir | ForEach-Object { Write-Host "  $($_.Name)" }

# 复制必要的文件
$icoDir = ".\ico"
$imgDir = ".\img"

if (Test-Path $icoDir) {
    New-Item -ItemType Directory -Force -Path "$outputDir\ico"
    Copy-Item "$icoDir\*" "$outputDir\ico" -Recurse -Force
}

if (Test-Path $imgDir) {
    New-Item -ItemType Directory -Force -Path "$outputDir\img"
    Copy-Item "$imgDir\*" "$outputDir\img" -Recurse -Force
}

# 创建压缩包（保留编译文件）
Write-Host "创建压缩包..."
Compress-Archive -Path "$outputDir\*" -DestinationPath $zipFile -Force

# 输出构建结果
Write-Host ""
Write-Host "========================================"
Write-Host "构建完成！"
Write-Host "========================================"
Write-Host "编译输出目录: $outputDir"
Write-Host "可执行文件: $outputDir\MouseClickRecorder.exe"
Write-Host "压缩包: $zipFile"
Write-Host "========================================"
Write-Host "注意：编译出来的文件保留在 $outputDir 目录中"
Write-Host "========================================"
