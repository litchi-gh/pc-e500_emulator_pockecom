@echo off
setlocal
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL exit /b 1
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
set "CASSETTE_TEST_DIR=%TEMP%\e500-cassette-%RANDOM%-%RANDOM%"
mkdir "%CASSETTE_TEST_DIR%"
if errorlevel 1 exit /b 1
cl /nologo /utf-8 /std:c++17 /O2 /MT /EHsc "%~dp0..\tests\test_cassette.cpp" /Fo:"%CASSETTE_TEST_DIR%\\" /Fe:"%CASSETTE_TEST_DIR%\test-cassette.exe" /link /INCREMENTAL:NO
if errorlevel 1 exit /b 1
"%CASSETTE_TEST_DIR%\test-cassette.exe"
exit /b %errorlevel%
