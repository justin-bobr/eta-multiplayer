#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GODOT_SRC="$SCRIPT_DIR/Source/godot"
TEMPLATES_DIR="$SCRIPT_DIR/Engine/Templates"

echo
echo "=== GODOT LINUX TEMPLATE BUILD (custom, stripped) ==="
echo "Script:    $SCRIPT_DIR"
echo "Source:    $GODOT_SRC"
echo "Output:    $TEMPLATES_DIR/linux_release.x86_64"
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

if [ ! -f "$GODOT_SRC/SConstruct" ]; then
    echo "[ERR] Godot source missing at $GODOT_SRC"
    echo "Run build-engine.cmd on Windows first to clone the source tree."
    exit 1
fi

mkdir -p "$TEMPLATES_DIR"

EDITOR_BIN="$SCRIPT_DIR/Engine/Editor.x86_64"
if [ ! -x "$EDITOR_BIN" ]; then
    EDITOR_BIN="$(command -v godot 2>/dev/null || true)"
fi
if [ -z "$EDITOR_BIN" ] || [ ! -x "$EDITOR_BIN" ]; then
    echo "[ERR] Mono-enabled Godot editor binary not found."
    echo "Place a Linux Godot Mono editor at \$SCRIPT_DIR/Engine/Editor.x86_64"
    echo "or have 'godot' in PATH. Needed to generate mono glue."
    exit 1
fi

if [ ! -d "$GODOT_SRC/modules/mono/glue/GodotSharp/GodotSharp/Generated" ]; then
    echo "[mono] generating mono glue via $EDITOR_BIN..."
    "$EDITOR_BIN" --headless --generate-mono-glue "$GODOT_SRC/modules/mono/glue"
fi

if [ ! -d "$GODOT_SRC/bin/GodotSharp" ]; then
    echo "[mono] building managed assemblies..."
    (cd "$GODOT_SRC" && python3 modules/mono/build_scripts/build_assemblies.py --godot-output-dir=bin)
fi

cd "$GODOT_SRC"
echo
echo "[scons] starting linuxbsd template_release build (30-60 min initial)..."
scons platform=linuxbsd target=template_release production=yes \
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
    module_godot_physics_3d_enabled=no

SRC_OUT="$GODOT_SRC/bin/godot.linuxbsd.template_release.x86_64.mono"
if [ ! -f "$SRC_OUT" ]; then
    SRC_OUT="$GODOT_SRC/bin/godot.linuxbsd.template_release.x86_64"
fi
if [ ! -f "$SRC_OUT" ]; then
    echo "[ERR] scons did not produce an expected output binary"
    ls "$GODOT_SRC/bin/" || true
    exit 1
fi

cp -f "$SRC_OUT" "$TEMPLATES_DIR/linux_release.x86_64"
chmod +x "$TEMPLATES_DIR/linux_release.x86_64"
if [ -d "$GODOT_SRC/bin/GodotSharp" ]; then
    rm -rf "$TEMPLATES_DIR/GodotSharp"
    cp -rf "$GODOT_SRC/bin/GodotSharp" "$TEMPLATES_DIR/GodotSharp"
fi

echo
echo "=== LINUX BUILD OK ==="
ls -la "$TEMPLATES_DIR/linux_release.x86_64"
echo
echo "Next: in Godot Editor, point Linux Client/Server preset's"
echo "      Custom Template Release at:"
echo "      $TEMPLATES_DIR/linux_release.x86_64"
