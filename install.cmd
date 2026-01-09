@echo off
REM USL installer for Windows

REM Change this path if your Python project is somewhere else
set USL_DIR=%~dp0

REM Where to put the executable (default: in PATH)
set TARGET=%USERPROFILE%\AppData\Local\Microsoft\WindowsApps

if not exist "%TARGET%" (
    mkdir "%TARGET%"
)

REM Create a batch wrapper called usl.cmd
(
echo @echo off
echo python "%USL_DIR%run.py" %%*
) > "%TARGET%\usl.cmd"

echo USL installed!
echo You can now run `usl` from any command prompt.
pause
