#!/usr/bin/env bash
# =============================================================================
#  ETA Linux - Release Build.
#  Single binary used as both client and server:
#    Export/Linux/eta.x86_64                              -> client
#    Export/Linux/eta.x86_64 --headless -- --server       -> dedicated server
#
#  Run on a Linux host (or copy this repo + Game folder + Engine folder there).
#  Assumes:
#    - dotnet SDK 8.0+ available  (apt install dotnet-sdk-8.0 or via MS repo)
#    - Godot editor binary at ./Engine/Editor.x86_64 (Linux Mono build)
#    - Custom or stock Linux export template at
#      ~/.local/share/godot/export_templates/<version>.stable.mono/linux_release.x86_64
#
#  Output: Export/Linux/eta.x86_64
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Allow env override; otherwise try the Linux editor binary next to Engine/.
if [ -z "${GODOT:-}" ] || [ ! -x "$GODOT" ]; then
    if [ -x "$SCRIPT_DIR/Engine/Editor.x86_64" ]; then
        GODOT="$SCRIPT_DIR/Engine/Editor.x86_64"
    elif command -v godot >/dev/null 2>&1; then
        GODOT="$(command -v godot)"
    else
        echo "[ERR] No Godot binary found. Set GODOT env var or place Engine/Editor.x86_64"
        exit 1
    fi
fi

TEMPLATE="$SCRIPT_DIR/Engine/Templates/linux_release.x86_64"
if [ ! -f "$TEMPLATE" ]; then
    echo
    echo "[ERR] Custom template not found at $TEMPLATE"
    echo "Run build-engine.sh first to build it,"
    echo "or edit Game/export_presets.cfg to clear custom_template/release"
    echo "and Godot will use the stock template from setup-editor.cmd."
    exit 1
fi

echo
echo "=== ETA LINUX BUILD ==="
echo "Godot:    $GODOT"
echo "Template: $TEMPLATE"
echo "Project:  $SCRIPT_DIR/Game"
echo "Output:   $SCRIPT_DIR/Export/Linux/eta.x86_64"
echo

echo "[1/2] dotnet build, ExportRelease..."
dotnet build "Game/keta.csproj" -c ExportRelease -nologo

echo
echo "[2/2] Godot export..."
mkdir -p Export/Linux
"$GODOT" --headless --path "Game" --export-release "Linux" "$SCRIPT_DIR/Export/Linux/eta.x86_64"
chmod +x "$SCRIPT_DIR/Export/Linux/eta.x86_64"

# -----------------------------------------------------------------------------
# Convenience launchers - operator can run client.sh / server.sh instead of
# memorising CLI args. eta.x86_64 stays the actual binary.
# -----------------------------------------------------------------------------
cat > "$SCRIPT_DIR/Export/Linux/client.sh" <<'EOF'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/eta.x86_64"
EOF
cat > "$SCRIPT_DIR/Export/Linux/server.sh" <<'EOF'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/eta.x86_64" --headless -- --server --max-players 16
EOF
chmod +x "$SCRIPT_DIR/Export/Linux/client.sh" "$SCRIPT_DIR/Export/Linux/server.sh"

echo
echo "=== BUILD OK ==="
echo "Binary:   $SCRIPT_DIR/Export/Linux/eta.x86_64"
echo "Client:   $SCRIPT_DIR/Export/Linux/client.sh"
echo "Server:   $SCRIPT_DIR/Export/Linux/server.sh"
ls -la "$SCRIPT_DIR/Export/Linux/eta.x86_64" | awk '{print "Size:     " $5 " bytes"}'
