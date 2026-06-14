#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

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
echo "=== VANTIX LINUX BUILD ==="
echo "Godot:    $GODOT"
echo "Template: $TEMPLATE"
echo "Project:  $SCRIPT_DIR/Game"
echo "Output:   $SCRIPT_DIR/Export/Linux/vantix.x86_64"
echo

echo "[1/2] dotnet build, ExportRelease..."
dotnet build "Game/vantix.csproj" -c ExportRelease -nologo

echo
echo "[2/2] Godot export..."
mkdir -p Export/Linux
"$GODOT" --headless --path "Game" --export-release "Linux" "$SCRIPT_DIR/Export/Linux/vantix.x86_64"
chmod +x "$SCRIPT_DIR/Export/Linux/vantix.x86_64"

cat > "$SCRIPT_DIR/Export/Linux/client.sh" <<'EOF'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/vantix.x86_64"
EOF
cat > "$SCRIPT_DIR/Export/Linux/server.sh" <<'EOF'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/vantix.x86_64" --headless -- --server --max-players 16
EOF
chmod +x "$SCRIPT_DIR/Export/Linux/client.sh" "$SCRIPT_DIR/Export/Linux/server.sh"

echo
echo "=== BUILD OK ==="
echo "Binary:   $SCRIPT_DIR/Export/Linux/vantix.x86_64"
echo "Client:   $SCRIPT_DIR/Export/Linux/client.sh"
echo "Server:   $SCRIPT_DIR/Export/Linux/server.sh"
ls -la "$SCRIPT_DIR/Export/Linux/vantix.x86_64" | awk '{print "Size:     " $5 " bytes"}'
