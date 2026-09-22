@echo off
rem Build the Pocket Rogues cheats plugin (BepInEx 5) and copy it into the loader's plugins folder.
rem Compiler: csc.exe of .NET Framework 4 (ships with Windows, C# 5). References: the game's own
rem Managed DLLs and BepInEx core. Nothing is downloaded.
rem
rem Where the game is: the default is the usual Steam folder. If yours differs, create
rem build.local.cmd next to this file with lines like:
rem   set "GAME=D:\Games\Steam\steamapps\common\Pocket Rogues"
rem   set "LOADER=D:\Games\Steam\steamapps\common\Pocket Rogues\BepInEx"
rem GAME is the folder with "Pocket Rogues.exe"; LOADER is the BepInEx folder (with core and
rem plugins inside), by default GAME\BepInEx.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "GAME=%ProgramFiles(x86)%\Steam\steamapps\common\Pocket Rogues"
set "LOADER="
if exist "%~dp0build.local.cmd" call "%~dp0build.local.cmd"
if "%LOADER%"=="" set "LOADER=%GAME%\BepInEx"
set "M=%GAME%\Pocket Rogues_Data\Managed"
set "OUT=%~dp0bin\PocketRoguesCheats.dll"

if not exist "%CSC%" goto nocsc
if not exist "%M%\Assembly-CSharp.dll" goto nogame
if not exist "%LOADER%\core\BepInEx.dll" goto noloader

if not exist "%~dp0bin" mkdir "%~dp0bin"
echo Building with %CSC%
"%CSC%" /nologo /target:library /optimize+ /codepage:65001 /nostdlib+ /noconfig /out:"%OUT%" ^
  /r:"%M%\mscorlib.dll" /r:"%M%\netstandard.dll" /r:"%M%\System.dll" /r:"%M%\System.Core.dll" ^
  /r:"%M%\UnityEngine.dll" /r:"%M%\UnityEngine.CoreModule.dll" /r:"%M%\UnityEngine.IMGUIModule.dll" ^
  /r:"%M%\UnityEngine.InputLegacyModule.dll" /r:"%M%\UnityEngine.AudioModule.dll" /r:"%M%\UnityEngine.TextRenderingModule.dll" /r:"%M%\UnityEngine.Physics2DModule.dll" ^
  /r:"%M%\Assembly-CSharp.dll" /r:"%M%\Assembly-CSharp-firstpass.dll" /r:"%LOADER%\core\BepInEx.dll" /r:"%LOADER%\core\0Harmony.dll" ^
  "%~dp0src\*.cs"
if errorlevel 1 (
  echo [FAIL] build
  exit /b 1
)
echo [OK] bin\PocketRoguesCheats.dll

tasklist /fi "imagename eq Pocket Rogues.exe" | "%SystemRoot%\System32\find.exe" /i "Pocket Rogues.exe" >nul
if not errorlevel 1 (
  echo [SKIP] the game is running - plugin not copied, close the game and build again
  exit /b 2
)
if not exist "%LOADER%\plugins" mkdir "%LOADER%\plugins"
copy /y "%OUT%" "%LOADER%\plugins\PocketRoguesCheats.dll" >nul
if errorlevel 1 (
  echo [FAIL] copy to %LOADER%\plugins
  exit /b 1
)
echo [OK] copied to %LOADER%\plugins
exit /b 0

:nocsc
echo [FAIL] C# compiler of .NET Framework 4 not found: %CSC%
exit /b 3
:nogame
echo [FAIL] game not found: %M%\Assembly-CSharp.dll
echo        set GAME in build.local.cmd (see the top of this file)
exit /b 3
:noloader
echo [FAIL] BepInEx not found: %LOADER%\core\BepInEx.dll
echo        install BepInEx 5 into the game folder or set LOADER in build.local.cmd
exit /b 3
