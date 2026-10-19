@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem ใช้ตัวคอมไพล์ C# ที่มากับ Windows (.NET Framework 4.x) ไม่ต้องติดตั้งโปรแกรมเพิ่ม
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ผิดพลาด] ไม่พบ csc.exe ของ .NET Framework 4
  pause
  exit /b 1
)

if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /win32icon:app.ico /win32manifest:app.manifest /resource:app.ico,QuickCopy.app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Runtime.Serialization.dll /r:System.Xml.dll ^
  /out:dist\QuickCopyPanel.exe src\*.cs

if errorlevel 1 (
  echo.
  echo [ผิดพลาด] คอมไพล์ไม่สำเร็จ
  if "%CI%"=="" pause
  exit /b 1
)
echo.
echo สำเร็จ! ไฟล์โปรแกรมอยู่ที่ dist\QuickCopyPanel.exe
if "%CI%"=="" pause
