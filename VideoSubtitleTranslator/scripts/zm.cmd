@echo off
rem Windows 启动器：与 zimu.exe 行为完全一致，供习惯短名的用户使用。
rem 优先使用同目录下的 zimu.exe（构建/发布产物），否则回退到 PATH 中的 zimu。
setlocal
if exist "%~dp0zimu.exe" (
  "%~dp0zimu.exe" %*
  exit /b %ERRORLEVEL%
)
zimu %*
exit /b %ERRORLEVEL%
