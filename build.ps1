$ErrorActionPreference = "Stop"

$root = Get-Location
$dist = Join-Path $root "dist"
$projDir = Join-Path $root "Ollangc"

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $dist | Out-Null

$vcvars = & {
    $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
    if (-not $vsPath) { $vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community" }
    Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
}

if (Test-Path $vcvars) {
    cmd /c "call `"$vcvars`" >nul 2>&1 && cl /LD /O2 /EHsc OllangNativeDLL.cpp /Fe:`"dist\OllangNativeDLL.dll`" /link /DLL"
} else {
    Write-Warning "build tools not found. https://aka.ms/vs/stable/vs_BuildTools.exe"
    Start-Process "https://www.example.com"
}

Set-Location $projDir
dotnet publish -c Release -r win-x64 --self-contained --nologo -o publish_tmp

Set-Location $root
Copy-Item "$projDir\publish_tmp\ollang.exe" "$dist\" -Force
if (Test-Path "$projDir\stdlib") {
    Copy-Item "$projDir\stdlib" "$dist\" -Recurse -Force
}

Remove-Item -Recurse -Force "$projDir\publish_tmp" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$projDir\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$projDir\obj" -ErrorAction SilentlyContinue
Remove-Item "$dist\OllangNativeDLL.lib" -ErrorAction SilentlyContinue
Remove-Item "$dist\OllangNativeDLL.exp" -ErrorAction SilentlyContinue
Remove-Item "$root\OllangNativeDLL.obj" -ErrorAction SilentlyContinue
Remove-Item "$root\OllangNativeDLL.lib" -ErrorAction SilentlyContinue
Remove-Item "$root\OllangNativeDLL.exp" -ErrorAction SilentlyContinue
Remove-Item "$root\OllangNativeDLL.dll" -ErrorAction SilentlyContinue
Remove-Item "$root\ollang.exe" -ErrorAction SilentlyContinue
Remove-Item "$root\build_dist.ps1" -ErrorAction SilentlyContinue
Remove-Item "$root\*.asm" -ErrorAction SilentlyContinue
Remove-Item "$root\*.bin" -ErrorAction SilentlyContinue
Remove-Item "$root\*.log" -ErrorAction SilentlyContinue