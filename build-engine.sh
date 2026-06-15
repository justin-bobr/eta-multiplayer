#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GODOT_SRC="$SCRIPT_DIR/Source/godot"
ENGINE_DIR="$SCRIPT_DIR/Engine"
TEMPLATES_DIR="$ENGINE_DIR/Templates"
PATCH_DIR="$SCRIPT_DIR/Source/patches"

echo
echo "=== GODOT LINUX ENGINE BUILD (custom, patched, stripped) ==="
echo "Script:    $SCRIPT_DIR"
echo "Source:    $GODOT_SRC"
echo "Output:    $ENGINE_DIR"
echo

echo "[deps] checking apt packages..."
MISSING=()
for pkg in build-essential scons pkg-config libx11-dev libxcursor-dev \
           libxinerama-dev libgl1-mesa-dev libglu1-mesa-dev libasound2-dev \
           libpulse-dev libudev-dev libxi-dev libxrandr-dev libwayland-dev \
           python3 python3-pip; do
    if ! dpkg -s "$pkg" >/dev/null 2>&1; then
        MISSING+=("$pkg")
    fi
done
if [ ${#MISSING[@]} -gt 0 ]; then
    echo "[deps] installing: ${MISSING[*]}"
    sudo apt update
    sudo apt install -y "${MISSING[@]}"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[deps] installing dotnet 8 SDK..."
    if ! sudo apt install -y dotnet-sdk-8.0 2>/dev/null; then
        echo "[deps] adding Microsoft repo for dotnet..."
        . /etc/os-release
        wget -q "https://packages.microsoft.com/config/$ID/$VERSION_ID/packages-microsoft-prod.deb" -O /tmp/ms.deb
        sudo dpkg -i /tmp/ms.deb
        rm /tmp/ms.deb
        sudo apt update
        sudo apt install -y dotnet-sdk-8.0
    fi
fi

if command -v scons >/dev/null 2>&1; then
    SCONS=(scons)
else
    SCONS=(python3 -m SCons)
fi

if [ ! -f "$GODOT_SRC/SConstruct" ]; then
    echo "[ERR] Godot source missing at $GODOT_SRC"
    echo "Clone godotengine/godot there first (CI does this at the resolved tag)."
    exit 1
fi

mkdir -p "$TEMPLATES_DIR"

echo "[patch] hard-resetting Source/godot and applying patches..."
git -C "$GODOT_SRC" reset --hard HEAD
git -C "$GODOT_SRC" clean -fd
shopt -s nullglob
PATCHES=("$PATCH_DIR"/*.patch)
shopt -u nullglob
if [ ${#PATCHES[@]} -gt 0 ]; then
    for p in "${PATCHES[@]}"; do
        git -C "$GODOT_SRC" apply --whitespace=nowarn "$p"
        echo "[patch] applied $(basename "$p")"
    done
else
    echo "[patch] no patches present - tree is vanilla"
fi

EDITOR_BIN="$ENGINE_DIR/Editor.x86_64"
if [ ! -x "$EDITOR_BIN" ]; then
    EDITOR_BIN="$(command -v godot 2>/dev/null || true)"
fi
if [ -z "$EDITOR_BIN" ] || [ ! -x "$EDITOR_BIN" ]; then
    echo "[ERR] Mono-enabled Godot editor binary not found."
    echo "Place a stock Linux Godot Mono editor at $ENGINE_DIR/Editor.x86_64"
    echo "or have 'godot' on PATH. Needed to generate mono glue."
    exit 1
fi

if [ ! -d "$GODOT_SRC/modules/mono/glue/GodotSharp/GodotSharp/Generated" ]; then
    echo "[mono] generating mono glue via $EDITOR_BIN..."
    "$EDITOR_BIN" --headless --generate-mono-glue "$GODOT_SRC/modules/mono/glue"
fi

if [ ! -d "$GODOT_SRC/bin/GodotSharp/Api" ]; then
    echo "[mono] building managed assemblies..."
    rm -rf "$GODOT_SRC/bin/GodotSharp"
    mkdir -p "$ENGINE_DIR/GodotSharp/Tools/nupkgs"
    ( cd "$GODOT_SRC" && python3 modules/mono/build_scripts/build_assemblies.py \
        --godot-output-dir=bin \
        --push-nupkgs-local "$ENGINE_DIR/GodotSharp/Tools/nupkgs" )
fi
if [ ! -d "$GODOT_SRC/bin/GodotSharp/Api" ]; then
    echo "[ERR] build_assemblies.py did not produce bin/GodotSharp/Api"
    exit 1
fi

echo
echo "=== GODOT EDITOR BUILD - full, mono, patched ==="
( cd "$GODOT_SRC" && "${SCONS[@]}" platform=linuxbsd target=editor production=yes \
    module_mono_enabled=yes )

EDITOR_OUT="$GODOT_SRC/bin/godot.linuxbsd.editor.x86_64.mono"
if [ ! -f "$EDITOR_OUT" ]; then
    echo "[ERR] editor build produced no binary at $EDITOR_OUT"
    ls "$GODOT_SRC/bin/" || true
    exit 1
fi
echo "[Copy] editor -> Engine/Editor.x86_64"
cp -f "$EDITOR_OUT" "$ENGINE_DIR/Editor.x86_64"
chmod +x "$ENGINE_DIR/Editor.x86_64"
rm -rf "$ENGINE_DIR/GodotSharp"
cp -rf "$GODOT_SRC/bin/GodotSharp" "$ENGINE_DIR/GodotSharp"

echo
echo "=== GODOT TEMPLATE BUILD - custom, stripped, mono ==="
( cd "$GODOT_SRC" && "${SCONS[@]}" platform=linuxbsd target=template_release production=yes \
    use_static_cpp=yes \
    module_mono_enabled=yes \
    module_bullet_enabled=no \
    module_navigation_2d_enabled=no \
    module_navigation_3d_enabled=no \
    module_webxr_enabled=no \
    module_openxr_enabled=no \
    module_mobile_vr_enabled=no \
    module_csg_enabled=no \
    module_gridmap_enabled=no \
    module_camera_enabled=no \
    module_websocket_enabled=no \
    module_webrtc_enabled=no \
    module_enet_enabled=no \
    module_multiplayer_enabled=no \
    module_mbedtls_enabled=no \
    module_upnp_enabled=no \
    module_basis_universal_enabled=no \
    module_squish_enabled=no \
    module_gltf_enabled=no \
    module_astcenc_enabled=no \
    module_etcpak_enabled=no \
    module_ktx_enabled=no \
    module_godot_physics_2d_enabled=no \
    module_godot_physics_3d_enabled=no )

TPL_OUT="$GODOT_SRC/bin/godot.linuxbsd.template_release.x86_64.mono"
if [ ! -f "$TPL_OUT" ]; then
    TPL_OUT="$GODOT_SRC/bin/godot.linuxbsd.template_release.x86_64"
fi
if [ ! -f "$TPL_OUT" ]; then
    echo "[ERR] scons did not produce an expected template binary"
    ls "$GODOT_SRC/bin/" || true
    exit 1
fi

echo "[Copy] template -> Engine/Templates/linux_release.x86_64"
cp -f "$TPL_OUT" "$TEMPLATES_DIR/linux_release.x86_64"
chmod +x "$TEMPLATES_DIR/linux_release.x86_64"
rm -rf "$TEMPLATES_DIR/GodotSharp"
cp -rf "$GODOT_SRC/bin/GodotSharp" "$TEMPLATES_DIR/GodotSharp"

echo
echo "=== LINUX ENGINE BUILD OK ==="
ls -la "$ENGINE_DIR/Editor.x86_64" "$TEMPLATES_DIR/linux_release.x86_64"
