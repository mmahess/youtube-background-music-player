@echo off
title YT Launcher Compiler
echo Compiling YTLauncher.cs...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:YTLauncher.exe YTLauncher.cs
if %errorlevel% equ 0 (
    echo Compilation successful! Generated YTLauncher.exe
    echo You can now run YTLauncher.exe or use the tray shortcut.
) else (
    echo Compilation failed!
    pause
)
