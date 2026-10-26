#!/usr/bin/env bash
# Builds one of Capsule's native packages' contents for one runtime identifier, on that identifier's own
# operating system and architecture, into artifacts/natives/<package>/ laid out as the package holds it.
# natives.yml runs it once per package and identifier. A workstation runs it the same way for its own.
#
#   bash natives/build.sh sdl3|shadertools win-x64|win-arm64|linux-x64|linux-arm64|osx
#
# Linux needs the SDL build dependencies natives.yml installs. macOS builds one universal binary.
# Not -u: macOS's bash 3.2 calls an empty array unbound.
set -eo pipefail

# The upstream releases the packages carry. A bump edits the ref here and the package's Version.
SDL3_REF=release-3.4.18
DXC_REF=v1.9.2607
SPIRV_CROSS_REF=vulkan-sdk-1.4.350.0

package=$1
rid=$2

root=$(cd "$(dirname "$0")/.." && pwd)
work=$root/artifacts/natives/obj/$rid
out=$root/artifacts/natives/$package

# The SDL libraries carry no C runtime on Windows. The tools link it statically, so a workstation
# installs no redistributable.
configure=(-DCMAKE_BUILD_TYPE=Release)
case $rid in
    win-x64) configure+=(-A x64) ;;
    win-arm64) configure+=(-A ARM64) ;;
    linux-x64 | linux-arm64) ;;
    osx) configure+=("-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64") ;;
    *)
        echo "unknown runtime identifier: $rid" >&2
        exit 1
        ;;
esac

# Every commit and no tree but the release's. DXC stamps its version with the history's commit count,
# which a shallow clone reports as 1.
fetch() {
    if [ ! -d "$work/src/$1" ]; then
        git -c advice.detachedHead=false clone --quiet --filter=tree:0 --branch "$2" --recurse-submodules --shallow-submodules "$3" "$work/src/$1"
    fi
}

# One job per processor. A bare --parallel is an unbounded make -j, and DXC's build runs out of memory under it.
jobs=$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo "${NUMBER_OF_PROCESSORS:-4}")

build() {
    cmake --build "$work/build/$1" --config Release --parallel "$jobs" "${@:2}"
}

# Mach-O files are signed ad hoc, which arm64 requires. A game's release signs its bundle again.
sign() {
    if [ "$rid" = osx ]; then
        codesign --force --sign - "$@"
    fi
}

sdl3() {
    fetch SDL "$SDL3_REF" https://github.com/libsdl-org/SDL.git

    # Capsule's fixes SDL has not released. A checkout a previous run patched is left as it is.
    local patch
    for patch in "$root"/natives/patches/SDL3-*.patch; do
        git -C "$work/src/SDL" apply --reverse --check "$patch" 2>/dev/null || git -C "$work/src/SDL" apply "$patch"
    done

    # The host's macOS floor.
    local stage=$work/stage deployment=()
    if [ "$rid" = osx ]; then
        deployment=(-DCMAKE_OSX_DEPLOYMENT_TARGET=10.14)
    fi

    cmake -S "$work/src/SDL" -B "$work/build/SDL" "${configure[@]}" "${deployment[@]}" \
        -DCMAKE_INSTALL_LIBDIR=lib -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF
    build SDL
    cmake --install "$work/build/SDL" --config Release --prefix "$stage"

    # The file names the host loads by.
    local native=$out/runtimes/$rid/native
    rm -rf "$native"
    mkdir -p "$native" "$out/licenses"
    case $rid in
        win-*)
            cp "$stage/bin/SDL3.dll" "$native/"
            ;;
        linux-*)
            cp -L "$stage/lib/libSDL3.so.0" "$native/libSDL3.so.0"
            strip --strip-unneeded "$native"/*
            ;;
        osx)
            cp -L "$stage/lib/libSDL3.dylib" "$native/libSDL3.dylib"
            install_name_tool -id @rpath/libSDL3.dylib "$native/libSDL3.dylib"
            sign "$native"/*
            ;;
    esac
    cp "$work/src/SDL/LICENSE.txt" "$out/licenses/SDL3.txt"
}

shadertools() {
    fetch dxc "$DXC_REF" https://github.com/microsoft/DirectXShaderCompiler.git
    fetch SPIRV-Cross "$SPIRV_CROSS_REF" https://github.com/KhronosGroup/SPIRV-Cross.git

    local static=() deployment=()
    case $rid in
        win-*) static=(-DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DLLVM_USE_CRT_RELEASE=MT) ;;
        # DXC's std::filesystem needs 10.15.
        osx) deployment=(-DCMAKE_OSX_DEPLOYMENT_TARGET=10.15) ;;
    esac

    cmake -S "$work/src/dxc" -B "$work/build/dxc" -C "$work/src/dxc/cmake/caches/PredefinedParams.cmake" \
        "${configure[@]}" "${static[@]}" "${deployment[@]}" \
        -DHLSL_INCLUDE_TESTS=OFF -DSPIRV_BUILD_TESTS=OFF -DLLVM_INCLUDE_TESTS=OFF -DCLANG_INCLUDE_TESTS=OFF
    build dxc --target dxc dxcompiler

    cmake -S "$work/src/SPIRV-Cross" -B "$work/build/SPIRV-Cross" "${configure[@]}" "${static[@]}" "${deployment[@]}" \
        -DSPIRV_CROSS_SHARED=OFF -DSPIRV_CROSS_ENABLE_TESTS=OFF
    build SPIRV-Cross --target spirv-cross

    # The upstream build trees' layout, where each dxc finds its library.
    local tools=$out/tools/$rid
    rm -rf "$tools"
    mkdir -p "$tools/bin" "$out/licenses"
    case $rid in
        win-*)
            cp "$work/build/dxc/Release/bin/dxc.exe" "$work/build/dxc/Release/bin/dxcompiler.dll" "$tools/bin/"
            cp "$work/build/SPIRV-Cross/Release/spirv-cross.exe" "$tools/bin/"
            ;;
        linux-*)
            mkdir -p "$tools/lib"
            cp -L "$work/build/dxc/bin/dxc" "$tools/bin/dxc"
            cp -L "$work/build/dxc/lib/libdxcompiler.so" "$tools/lib/libdxcompiler.so"
            cp "$work/build/SPIRV-Cross/spirv-cross" "$tools/bin/"
            strip --strip-unneeded "$tools/bin/dxc" "$tools/lib/libdxcompiler.so" "$tools/bin/spirv-cross"
            ;;
        osx)
            mkdir -p "$tools/lib"
            cp -L "$work/build/dxc/bin/dxc" "$tools/bin/dxc"
            cp -L "$work/build/dxc/lib/libdxcompiler.dylib" "$tools/lib/libdxcompiler.dylib"
            cp "$work/build/SPIRV-Cross/spirv-cross" "$tools/bin/"
            sign "$tools/bin/dxc" "$tools/lib/libdxcompiler.dylib" "$tools/bin/spirv-cross"
            ;;
    esac
    cp "$work/src/dxc/LICENSE.TXT" "$out/licenses/DirectXShaderCompiler.txt"
    cp "$work/src/dxc/ThirdPartyNotices.txt" "$out/licenses/DirectXShaderCompiler-ThirdPartyNotices.txt"
    cp "$work/src/SPIRV-Cross/LICENSE" "$out/licenses/SPIRV-Cross.txt"
}

case $package in
    sdl3) sdl3 ;;
    shadertools) shadertools ;;
    *)
        echo "unknown package: $package" >&2
        exit 1
        ;;
esac
