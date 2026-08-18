# HotspotTray 설치 / 업데이트 스크립트
#
#   irm https://raw.githubusercontent.com/13wing-boop/hotspot-tray/main/install.ps1 | iex
#
# 옵션을 주려면 파일로 받아서 실행:
#   .\install.ps1 -NoAutoRun -NoStart

[CmdletBinding()]
param(
    [string]$Dir = "$env:LOCALAPPDATA\Programs\HotspotTray",
    [switch]$NoAutoRun,   # 로그온 시 자동 실행을 등록하지 않음
    [switch]$NoStart      # 설치 후 실행하지 않음
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = '13wing-boop/hotspot-tray'
$ua   = @{ 'User-Agent' = 'HotspotTray-installer' }

Write-Host "HotspotTray 설치" -ForegroundColor Cyan

# 실행 중이면 종료 (파일 잠금 해제)
$running = Get-Process HotspotTray -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "  실행 중인 인스턴스 종료..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $ua
$tag = $rel.tag_name
$url = "https://github.com/$repo/releases/download/$tag/HotspotTray.exe"
Write-Host "  최신 버전: $tag"

New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$exe = Join-Path $Dir 'HotspotTray.exe'

Write-Host "  다운로드: $url"
Invoke-WebRequest $url -OutFile $exe -UseBasicParsing -Headers $ua
Unblock-File $exe   # SmartScreen/MOTW 표시 제거

$info = Get-Item $exe
Write-Host ("  설치 완료: {0} ({1} KB, v{2})" -f $exe, [math]::Round($info.Length/1KB,1), $info.VersionInfo.FileVersion)

if (-not $NoAutoRun) {
    New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'HotspotTray' -Value "`"$exe`"" -PropertyType String -Force | Out-Null
    Write-Host "  자동 실행 등록: HKCU\...\CurrentVersion\Run\HotspotTray"
} else {
    Write-Host "  자동 실행 등록 건너뜀 (-NoAutoRun)"
}

if (-not $NoStart) {
    Start-Process $exe
    Write-Host "  실행했습니다. 작업표시줄 트레이 아이콘을 확인하세요." -ForegroundColor Green
} else {
    Write-Host "  실행 건너뜀 (-NoStart)"
}

Write-Host ""
Write-Host "설정 → 네트워크 및 인터넷 → 모바일 핫스팟 에서" -ForegroundColor Yellow
Write-Host "'전원 절약' 토글을 꺼두면 기기가 없어도 핫스팟이 유지됩니다." -ForegroundColor Yellow
