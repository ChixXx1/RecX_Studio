# download-ffmpeg.ps1
param(
    [string]$TargetPath = "RecX_Studio/bin/Debug/net9.0-windows"
)

Write-Host "Downloading FFmpeg..." -ForegroundColor Green

# URL для скачивания FFmpeg (более легкая версия)
$ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"

$tempDir = "temp-ffmpeg"
$zipPath = "ffmpeg-temp.zip"

# Создать целевую папку
New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null

try {
    # Скачать FFmpeg
    Write-Host "Downloading from $ffmpegUrl ..."
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath

    # Распаковать
    Write-Host "Extracting..."
    Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force

    # Найти файлы
    $ffmpegExe = Get-ChildItem -Path $tempDir -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    $ffprobeExe = Get-ChildItem -Path $tempDir -Recurse -Filter "ffprobe.exe" | Select-Object -First 1

    if ($ffmpegExe -and $ffprobeExe) {
        # Копировать файлы
        Copy-Item $ffmpegExe.FullName -Destination "$TargetPath/ffmpeg.exe" -Force
        Copy-Item $ffprobeExe.FullName -Destination "$TargetPath/ffprobe.exe" -Force
        
        Write-Host "FFmpeg successfully installed to $TargetPath" -ForegroundColor Green
        Write-Host "Files: ffmpeg.exe ($([math]::Round((Get-Item "$TargetPath/ffmpeg.exe").Length/1MB, 2)) MB)" -ForegroundColor Yellow
        Write-Host "       ffprobe.exe ($([math]::Round((Get-Item "$TargetPath/ffprobe.exe").Length/1MB, 2)) MB)" -ForegroundColor Yellow
    } else {
        Write-Host "Error: FFmpeg files not found in archive" -ForegroundColor Red
    }
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    # Очистка
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
}