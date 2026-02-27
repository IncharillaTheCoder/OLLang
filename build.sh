#!/bin/bash

set -e
ROOT=$(pwd)
DIST="$ROOT/dist"
PROJ_DIR="$ROOT/Ollangc"
rm -rf "$DIST"
mkdir -p "$DIST"

cd "$PROJ_DIR"
dotnet publish -c Release -nologo -o publish_tmp


cd "$ROOT"
cp "$PROJ_DIR/publish_tmp/ollang" "$DIST/" 2>/dev/null || cp "$PROJ_DIR/publish_tmp/ollang.exe" "$DIST/"
if [ -d "$PROJ_DIR/stdlib" ]; then
    cp -r "$PROJ_DIR/stdlib" "$DIST/"
fi

if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" ]]; then
    if command -v g++ &> /dev/null; then
        g++ -shared -O2 OllangNativeDLL.cpp -o "$DIST/OllangNativeDLL.dll" -lurlmon
    else
        echo "no c++ compiler found"
    fi
else
    if command -v x86_64-w64-mingw32-g++ &> /dev/null; then
        x86_64-w64-mingw32-g++ -shared -O2 OllangNativeDLL.cpp -o "$DIST/OllangNativeDLL.dll" -lurlmon
    else
        echo "skipping windows dll mingw wasnt found"
    fi
fi

rm -rf "$PROJ_DIR/publish_tmp"
rm -rf "$PROJ_DIR/bin"
rm -rf "$PROJ_DIR/obj"
rm -f "$DIST/OllangNativeDLL.lib"
rm -f "$DIST/OllangNativeDLL.exp"
rm -f "$ROOT/OllangNativeDLL.obj"
rm -f "$ROOT/OllangNativeDLL.o"
rm -f "$ROOT/OllangNativeDLL.lib"
rm -f "$ROOT/OllangNativeDLL.exp"
rm -f "$ROOT/OllangNativeDLL.dll"
rm -f "$ROOT/ollang"
rm -f "$ROOT/ollang.exe"
rm -f "$ROOT/build_dist.ps1"
rm -f "$ROOT"/*.asm
rm -f "$ROOT"/*.bin
rm -f "$ROOT"/*.log
