@echo off
chcp 65001 >nul
setlocal
echo ========================================================
echo   摸鱼台 (MoyuDesk) 固定目录安装 / 平滑升级工具
echo   安装位置: %%LOCALAPPDATA%%\MoyuDesk\app
echo   您的正式任务与配置数据 (InstantTaskboard) 不会被修改
echo ========================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"
if errorlevel 1 (
    echo.
    echo [错误] 安装或升级未完成，请查看上方提示信息。
    pause
    exit /b 1
)
echo.
echo [完成] 安装或升级已完成！
timeout /t 3 >nul
endlocal
