# Полная проверка автообновления на локальном тестовом пакете (без GitHub).
# Собирает «установленную» версию X.Y.Z и пакет X.Y.(Z+1), затем прогоняет сценарии:
# нет обновления, повреждённый и недокачанный пакет, чужая версия в пакете, Veylix не закрылся,
# установка не удалась (файл занят), новая версия не стартовала (откат), успешное обновление.
# Сборки используют tests\tests.manifest (без запроса прав администратора).
# Запуск: powershell -ExecutionPolicy Bypass -File tests\update-e2e.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP 'veylix-e2e'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$script:pass = 0; $script:fail = 0

function Check($cond, $name) {
    if ($cond) { $script:pass++; Write-Host "  OK   $name" }
    else { $script:fail++; Write-Host "  FAIL $name" -ForegroundColor Red }
}
function VersionOf($exe) { $n = [Reflection.AssemblyName]::GetAssemblyName($exe); "{0}.{1}.{2}" -f $n.Version.Major, $n.Version.Minor, $n.Version.Build }
function KillApps() {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($work, 'OrdinalIgnoreCase') } | ForEach-Object { try { $_.Kill(); $_.WaitForExit(5000) } catch {} }
}
function Build($srcDir, $out) {
    $refs = @("/lib:$fw\WPF", '/r:PresentationCore.dll', '/r:PresentationFramework.dll', '/r:WindowsBase.dll', '/r:System.Xaml.dll', '/r:System.Core.dll',
              '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
              '/r:System.Web.Extensions.dll', '/r:System.ServiceProcess.dll')
    & "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ "/out:$out" "/win32icon:$root\Veylix.ico" "/win32manifest:$root\tests\tests.manifest" "/resource:$root\zapret.zip,zapret.zip" @refs "$srcDir\*.cs" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "сборка $out не удалась" }
}
# Ждём только сам процесс: Start-Process -Wait в Windows PowerShell ждёт и все дочерние,
# а помощник запускает Veylix, который работает до закрытия окна.
function Run($exe, $argList) {
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $psi.Arguments = ($argList | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $p.WaitForExit()
    return $p.ExitCode
}
function WaitHelper($seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    Start-Sleep -Milliseconds 800
    while ((Get-Date) -lt $until) {
        $h = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like '*\updates\helper\Veylix-updater.exe' -and $_.Path.StartsWith($work, 'OrdinalIgnoreCase') }
        if (-not $h) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}
function Hash($f) { (Get-FileHash -Algorithm SHA256 $f).Hash.ToLowerInvariant() }

KillApps
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory $work | Out-Null
$inst = Join-Path $work 'install'; New-Item -ItemType Directory $inst | Out-Null
$data = Join-Path $work 'data'; New-Item -ItemType Directory $data | Out-Null

# ---- версии ----
$src = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'src\Program.cs')
$old = [regex]::Match($src, 'public const string Version = "(\d+\.\d+\.\d+)";').Groups[1].Value
$p3 = $old.Split('.'); $new = "{0}.{1}.{2}" -f $p3[0], $p3[1], ([int]$p3[2] + 1)
Write-Host "Установленная версия: $old, пакет: $new"

Build (Join-Path $root 'src') (Join-Path $inst 'Veylix.exe')
$srcNew = Join-Path $work 'src-new'; Copy-Item -Recurse (Join-Path $root 'src') $srcNew
$pf = Join-Path $srcNew 'Program.cs'
[IO.File]::WriteAllText($pf, ([IO.File]::ReadAllText($pf).Replace("Version = `"$old`"", "Version = `"$new`"")), (New-Object Text.UTF8Encoding($false)))
$pkgDir = Join-Path $work 'pkg'; New-Item -ItemType Directory $pkgDir | Out-Null
Build $srcNew (Join-Path $pkgDir 'Veylix.exe')
$pkg = Join-Path $work "Veylix-$new-win-x64.zip"
Compress-Archive -Path (Join-Path $pkgDir '*') -DestinationPath $pkg
$sha = Hash $pkg
$installExe = Join-Path $inst 'Veylix.exe'
$oldHash = Hash $installExe

# ---- пользовательские данные, которые обязаны сохраниться ----
$settingsText = "LastStrategy=ALT3`r`nTheme=night`r`nThemeMode=manual`r`nStudioAccent=#D9573C`r`nAppUpdateCheck=0`r`nCustomFutureKey=keep-me`r`n"
[IO.File]::WriteAllText((Join-Path $data 'settings.ini'), $settingsText)
[IO.File]::WriteAllText((Join-Path $data 'veylix.log'), "2026-10-15 10:00:00  ИНФО     старая запись журнала`r`n")
New-Item -ItemType Directory (Join-Path $data 'themes') | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'themes\my.veylix-theme.json'), '{"format":"veylix-theme","version":1,"accent":"#C2185B"}')
$settingsHash = Hash (Join-Path $data 'settings.ini'); $themeHash = Hash (Join-Path $data 'themes\my.veylix-theme.json')

Write-Host "`n1. Нет обновления (пакет той же версии)"
$same = Join-Path $work "Veylix-$old-win-x64.zip"; Copy-Item $pkg $same
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $same, (Hash $same))
Check ($code -eq 11) "та же версия не устанавливается (код $code)"
Check ((Hash $installExe) -eq $oldHash) "Veylix.exe не изменён"

Write-Host "`n2. Повреждённый пакет"
$bad = Join-Path $work 'bad'; New-Item -ItemType Directory $bad | Out-Null
$badPkg = Join-Path $bad "Veylix-$new-win-x64.zip"; Copy-Item $pkg $badPkg
$b = [IO.File]::ReadAllBytes($badPkg); $b[[int]($b.Length / 2)] = $b[[int]($b.Length / 2)] -bxor 0xFF; [IO.File]::WriteAllBytes($badPkg, $b)
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $badPkg, $sha)
Check ($code -eq 12) "повреждённый пакет отклонён (код $code)"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\stage-error.txt')) -match 'контрольная сумма') "причина: контрольная сумма"
Check ((Hash $installExe) -eq $oldHash) "Veylix.exe не изменён"

Write-Host "`n3. Прерванная загрузка (недокачанный пакет)"
$cut = Join-Path $work 'cut'; New-Item -ItemType Directory $cut | Out-Null
$cutPkg = Join-Path $cut "Veylix-$new-win-x64.zip"
$all = [IO.File]::ReadAllBytes($pkg); [IO.File]::WriteAllBytes($cutPkg, $all[0..([int]($all.Length / 2))])
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $cutPkg, $sha)
Check ($code -eq 12) "недокачанный пакет отклонён (код $code)"
Check ((Hash $installExe) -eq $oldHash) "Veylix.exe не изменён"

Write-Host "`n4. Версия в пакете не совпадает с версией релиза"
$wrongVer = "{0}.{1}.{2}" -f $p3[0], $p3[1], ([int]$p3[2] + 2)
$wr = Join-Path $work 'wrong'; New-Item -ItemType Directory $wr | Out-Null
$wrPkg = Join-Path $wr "Veylix-$wrongVer-win-x64.zip"; Copy-Item $pkg $wrPkg
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $wrPkg, (Hash $wrPkg))
Check ($code -eq 12) "чужая версия отклонена (код $code)"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\stage-error.txt')) -match 'не совпадает с версией релиза') "причина: версия не совпадает"

# подготовленная новая версия для сценариев с помощником
$staged = Join-Path $data "updates\app-$new"
New-Item -ItemType Directory -Force $staged | Out-Null
Copy-Item (Join-Path $pkgDir 'Veylix.exe') (Join-Path $staged 'Veylix.exe') -Force
$stagedSha = Hash (Join-Path $staged 'Veylix.exe')
$helper = Join-Path $work 'helper.exe'; Copy-Item $installExe $helper
function Plan($pid_, $closeSec) {
    $plan = Join-Path $data 'updates\test-plan.ini'
    $t = "TargetExe=$installExe`r`nNewExe=$staged\Veylix.exe`r`nBackupExe=$installExe.backup-$old`r`nOldVersion=$old`r`nNewVersion=$new`r`nNewSha256=$stagedSha`r`nDataDir=$data`r`nPid=$pid_`r`nCloseTimeoutSec=$closeSec`r`nReadyTimeoutSec=30`r`n"
    [IO.File]::WriteAllText($plan, $t); return $plan
}

Write-Host "`n5. Veylix не закрывается"
$busy = Start-Process -FilePath "$env:WINDIR\System32\PING.EXE" -ArgumentList @('-n', '60', '127.0.0.1') -WindowStyle Hidden -PassThru
$code = Run $helper @('--apply-update', (Plan $busy.Id 3), '--data-dir', $data)
try { $busy.Kill() } catch {}
Check ($code -eq 2) "помощник ждал и отменил обновление (код $code)"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\update-result.ini')) -match 'не закрылся') "причина записана: не закрылся"
Check ((Hash $installExe) -eq $oldHash) "Veylix.exe не изменён"
Remove-Item (Join-Path $data 'updates\update-result.ini')

Write-Host "`n6. Установка не удалась (Veylix.exe занят)"
# эксклюзивная блокировка (как будто файл держит антивирус); сам файл может быть ненадолго занят сканированием
$lock = $null
for ($i = 0; $i -lt 40 -and -not $lock; $i++) { try { $lock = [IO.File]::Open($installExe, 'Open', 'Read', 'None') } catch { Start-Sleep -Milliseconds 500 } }
if (-not $lock) { throw 'не удалось заблокировать Veylix.exe для сценария 6' }
$code = Run $helper @('--apply-update', (Plan 0 5), '--data-dir', $data)
$lock.Close()
Check ($code -eq 3) "замена не удалась, помощник остановился (код $code)"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\update-result.ini')) -match 'не тронута|Не удалось') "понятная причина в результате"
Check ((Hash $installExe) -eq $oldHash) "прежняя версия на месте"
Check (-not (Test-Path "$installExe.new")) "временный файл убран"
KillApps
Remove-Item (Join-Path $data 'updates\update-result.ini') -ErrorAction SilentlyContinue

Write-Host "`n7. Новая версия не запустилась — откат"
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $pkg, $sha, '--test-fail-start')
Check ($code -eq 0) "пакет проверен, помощник запущен (код $code)"
Check (WaitHelper 90) "помощник завершил работу"
Start-Sleep -Seconds 2
Check ((VersionOf $installExe) -eq $old) "после отката снова версия $old"
Check ((Hash $installExe) -eq $oldHash) "файл побайтно совпадает с прежним"
$res = Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\update-result.ini') -ErrorAction SilentlyContinue
# результат показывается один раз: его забирает перезапущенная прежняя версия и пишет в журнал
$until = (Get-Date).AddSeconds(20); $seen = ($res -match 'Status=rolledback')
while ((Get-Date) -lt $until -and -not $seen) { $seen = (Get-Content -Raw -Encoding UTF8 (Join-Path $data 'veylix.log')) -match 'не запустилась, поэтому восстановлена прежняя версия'; Start-Sleep -Milliseconds 500 }
Check $seen "прежняя версия перезапущена и сообщила об откате"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\update.log')) -match 'откат') "откат записан в update.log"
KillApps
Remove-Item (Join-Path $data 'updates\update-result.ini') -ErrorAction SilentlyContinue

Write-Host "`n8. Успешное обновление"
$code = Run $installExe @('--data-dir', $data, '--update-from-file', $pkg, $sha)
Check ($code -eq 0) "пакет проверен, помощник запущен (код $code)"
Check (WaitHelper 90) "помощник завершил работу"
Check ((VersionOf $installExe) -eq $new) "установлена версия $new"
Check ((Test-Path "$installExe.backup-$old") -and ((VersionOf "$installExe.backup-$old") -eq $old)) "прежняя версия сохранена как Veylix.exe.backup-$old"
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installExe }
Check ($running -ne $null) "обновлённый Veylix запущен"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'updates\update.log')) -match 'подтвердила готовность') "новая версия подтвердила запуск"
Check (-not (Test-Path (Join-Path $data 'updates\update-result.ini'))) "сообщения об ошибке нет"
Check ((Hash (Join-Path $data 'settings.ini')) -eq $settingsHash) "settings.ini не изменён (в т.ч. неизвестные ключи)"
Check ((Hash (Join-Path $data 'themes\my.veylix-theme.json')) -eq $themeHash) "пользовательская тема на месте"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'veylix.log')) -match 'старая запись журнала') "старые записи журнала сохранены"
Check ((Get-Content -Raw -Encoding UTF8 (Join-Path $data 'veylix.log')) -match "обновлён: $old") "новая версия записала в журнал об обновлении"
Check (-not (Test-Path $staged)) "временная папка обновления удалена"
KillApps

Write-Host "`nИтого: пройдено $script:pass, провалено $script:fail"
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
