# Сборка пакета релиза Veylix для GitHub Releases.
# Запуск: release.bat  (или powershell -ExecutionPolicy Bypass -File tools\make-release.ps1)
# Результат — папка release\ с двумя файлами для загрузки в релиз:
#   Veylix-X.Y.Z-win-x64.zip         готовое приложение (Veylix.exe + README.txt)
#   Veylix-X.Y.Z-win-x64.zip.sha256  контрольная сумма SHA-256
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1. Версия — единственный источник: Program.Version в src\Program.cs
$src = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'src\Program.cs')
$m = [regex]::Match($src, 'public const string Version = "(\d+\.\d+\.\d+)";')
if (-not $m.Success) { throw 'Не найдена строка public const string Version = "X.Y.Z"; в src\Program.cs' }
$ver = $m.Groups[1].Value
Write-Host "Версия: $ver (тег релиза: v$ver)"

# 2. Сборка и тесты
& cmd /c "`"$root\build.bat`" test"
if ($LASTEXITCODE -ne 0) { throw 'Сборка или тесты не прошли — релиз не создан' }
$exe = Join-Path $root 'dist\Veylix.exe'
$an = [Reflection.AssemblyName]::GetAssemblyName($exe)
if ($an.Name -ne 'Veylix' -or ("{0}.{1}.{2}" -f $an.Version.Major, $an.Version.Minor, $an.Version.Build) -ne $ver) {
    throw "Версия собранного Veylix.exe ($($an.Version)) не совпадает с $ver"
}

# 3. Пакет: Veylix.exe в корне архива
$out = Join-Path $root 'release'
New-Item -ItemType Directory -Force $out | Out-Null
$name = "Veylix-$ver-win-x64.zip"
$zip = Join-Path $out $name
if (Test-Path $zip) { Remove-Item $zip }
$tmp = Join-Path $out "pkg-$ver"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory $tmp | Out-Null
Copy-Item $exe (Join-Path $tmp 'Veylix.exe')
Copy-Item (Join-Path $root 'README.txt') (Join-Path $tmp 'README.txt')
Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $zip
Remove-Item -Recurse -Force $tmp

# 4. Контрольная сумма (формат sha256sum: "<хеш> *<имя файла>")
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash *$name`n", (New-Object Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Готово: $zip"
Write-Host "SHA-256: $hash"
Write-Host ""
Write-Host "Публикация (подробно — RELEASING.txt):"
Write-Host "  1. GitHub → FreeSize83/Veylix → Releases → Draft a new release"
Write-Host "  2. Тег: v$ver   Заголовок: Veylix $ver   (тег создать на текущем коммите)"
Write-Host "  3. Описание: список изменений"
Write-Host "  4. Прикрепить: $name и $name.sha256"
Write-Host "  5. НЕ отмечать «Set as a pre-release». Нажать «Publish release»."
