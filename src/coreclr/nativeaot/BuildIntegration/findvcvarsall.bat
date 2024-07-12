@ECHO OFF
SETLOCAL

IF "%~1"=="" (
    ECHO Usage: %~nx0 ^<arch^>
    GOTO :ERROR
)

SET vswherePath=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
IF NOT EXIST "%vswherePath%" GOTO :ERROR

SET toolsSuffix=x86.x64
IF /I "%PROCESSOR_ARCHITECTURE%" == "arm64" (
    SET vcEnvironment=arm64
    IF /I "%~1" == "x64" ( SET vcEnvironment=arm64_amd64 )
    IF /I "%~1" == "x86" ( SET vcEnvironment=arm64_x86 )
    IF /I "%~1" == "arm64" ( SET toolsSuffix=arm64 )
) ELSE (
    SET vcEnvironment=amd64
    IF /I "%~1" == "x86" ( SET vcEnvironment=amd64_x86 )
    IF /I "%~1" == "arm64" ( SET vcEnvironment=amd64_arm64 & toolsSuffix=arm64 )
)

FOR /F "tokens=*" %%i IN (
    '"%vswherePath%" -latest -prerelease -products * ^
    -requires Microsoft.VisualStudio.Component.VC.Tools.%toolsSuffix% ^
    -version [16^,18^) ^
    -property installationPath'
) DO SET vsBase=%%i

IF "%vsBase%"=="" GOTO :ERROR

CALL "%vsBase%\vc\Auxiliary\Build\vcvarsall.bat" %vcEnvironment% > NUL

FOR /F "delims=" %%W IN ('where link') DO (
    FOR %%A IN ("%%W") DO ECHO %%~dpA#
    GOTO :CAPTURE_LIB_PATHS
)

GOTO :ERROR

:CAPTURE_LIB_PATHS
IF "%LIB%"=="" GOTO :ERROR
ECHO %LIB%

ENDLOCAL

EXIT /B 0

:ERROR
EXIT /B 1
