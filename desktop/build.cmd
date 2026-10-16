@echo off
rem Builds XiaoKe.exe from XiaoKe.cs with the C# compiler that ships with .NET Framework 4.x (no SDK needed).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
cd /d "%~dp0"
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:XiaoKe.exe /win32icon:assets\xiaoke.ico ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Drawing.dll" ^
  XiaoKe.cs
