# Сборка FuelLog: тесты, публикация в .\publish, переносной архив и (если установлен Inno Setup 6) установщик в .\dist.
# Версия берётся из Directory.Build.props.
#   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = (Select-String -Path Directory.Build.props -Pattern '<Version>(.+)</Version>').Matches[0].Groups[1].Value

dotnet test FuelLog.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Тесты не прошли — сборка остановлена.' }

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src/FuelLog.App -c Release -o publish
if ($LASTEXITCODE -ne 0) { throw 'Ошибка публикации.' }

# Переносная версия: архив с папкой FuelLog внутри.
New-Item -ItemType Directory -Force dist | Out-Null
$stage = Join-Path ([IO.Path]::GetTempPath()) "FuelLog_portable_$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item publish (Join-Path $stage 'FuelLog') -Recurse
$zip = Join-Path $PSScriptRoot "dist\FuelLog_${version}_portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Системный tar (Windows 10+) пишет стандартный zip с прямыми слешами в путях.
& "$env:SystemRoot\System32\tar.exe" -a -c -f $zip -C $stage FuelLog
if ($LASTEXITCODE -ne 0) { throw 'Ошибка упаковки переносной версии.' }
Remove-Item $stage -Recurse -Force

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$version" installer\FuelLog.iss
    if ($LASTEXITCODE -ne 0) { throw 'Ошибка сборки установщика.' }
    Write-Host "Готово ($version): установщик и переносной архив в папке dist."
} else {
    Write-Host "Готово ($version): переносной архив в папке dist (Inno Setup 6 не найден — установщик не собран)."
}
