@echo off
setlocal
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL exit /b 1
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
pushd "%~dp0..\src\native"
cl /nologo /std:c++17 /O2 /MT /EHsc sc62015.cpp machine.cpp ce140f.cpp ..\..\tests\test_core.cpp /Fe:..\..\tests\test-core.exe /link /INCREMENTAL:NO
if errorlevel 1 (popd & exit /b 1)
..\..\tests\test-core.exe
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
