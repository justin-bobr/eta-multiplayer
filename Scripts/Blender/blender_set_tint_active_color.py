# ============================================================
#  Blender: "TINT"-Color-Attribute als aktiv/render setzen
# ============================================================
#  Setzt auf JEDEM Mesh die Color-Attribute namens "TINT" als
#  aktive UND aktive Render-Color-Attribute.
#
#  Warum: Der glTF/GLB-Exporter schreibt nur die aktive RENDER-
#  Color-Attribute nach COLOR_0. Godot liest COLOR_0 → Shader-
#  `COLOR` (use_vertex_color_tint). Ist TINT nicht render-aktiv,
#  fehlt der Tint im Export → Mesh rendert ungetintet (weiss).
#
#  Anwendung:
#    1. Scripting-Tab → Text → Open → diese Datei
#    2. (optional) TINT_NAME unten anpassen
#    3. "Run Script" (Play-Icon) oder Alt+P
#    4. Console öffnen (Window → Toggle System Console) für Report
# ============================================================
import bpy

# --- Konfiguration ---
# Name der Color-Attribute die als Render-Color gesetzt werden soll.
TINT_NAME = "TINT"


def set_tint_active_color():
    changed = 0
    already = 0
    missing = 0

    for mesh in bpy.data.meshes:
        color_attrs = mesh.color_attributes

        # Index der TINT-Attribute in der Color-Attributes-Liste suchen
        idx = -1
        for i, attr in enumerate(color_attrs):
            if attr.name == TINT_NAME:
                idx = i
                break

        if idx == -1:
            missing += 1
            continue

        # Schon korrekt gesetzt? (aktiv + render zeigen beide auf TINT)
        if color_attrs.active_color_index == idx and color_attrs.render_color_index == idx:
            already += 1
            continue

        # Als aktive (Editor) UND aktive Render-Color-Attribute setzen.
        # Nur render_color_index landet im glTF-Export — active_color_index
        # wird mitgesetzt damit Viewport/Edit konsistent ist.
        color_attrs.active_color_index = idx
        color_attrs.render_color_index = idx
        print(f"[tint] '{mesh.name}': '{TINT_NAME}' als Render-Color gesetzt (Index {idx})")
        changed += 1

    # Report
    print(f"[tint] FERTIG: {changed} Meshes gesetzt, {already} bereits korrekt, "
          f"{missing} ohne '{TINT_NAME}'-Attribut")
    if missing > 0:
        print(f"[tint] Hinweis: {missing} Meshes haben keine '{TINT_NAME}'-Color-Attribute — "
              f"die exportieren keinen Vertex-Tint.")


set_tint_active_color()
