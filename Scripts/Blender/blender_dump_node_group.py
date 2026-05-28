# ============================================================
#  Blender: Node-Group-Verdrahtung als Text ausgeben
# ============================================================
#  Dumpt für jede angegebene Node-Group ALLE Nodes mit Typ,
#  Blend-Mode/Operation und JEDER Eingangs-Verbindung bzw.
#  Default-Wert. Damit lässt sich der Graph 1:1 in einen
#  Shader übersetzen — ohne Screenshot-Raterei.
#
#  Anwendung:
#    1. Scripting-Tab → Text → Open → diese Datei
#    2. (optional) GROUPS unten anpassen ([] = alle Groups)
#    3. "Run Script" (Play-Icon) oder Alt+P
#    4. Console öffnen (Window → Toggle System Console) → Output
#       kopieren und an Claude geben.
# ============================================================
import bpy

# --- Konfiguration ---
# Liste der Node-Group-Namen die gedumpt werden sollen.
# Leer lassen ([]) → ALLE Node-Groups dumpen.
GROUPS = ["csgo_complex.vfx"]

# Node-Properties die (falls vorhanden) mit ausgegeben werden.
_PROP_KEYS = ["blend_type", "operation", "data_type", "use_clamp",
              "clamp_factor", "clamp_result", "mode"]


def _fmt(val):
    if hasattr(val, "__len__") and not isinstance(val, str):
        return "(" + ", ".join(f"{float(v):.4f}" for v in val) + ")"
    if isinstance(val, float):
        return f"{val:.4f}"
    return str(val)


def dump_group(ng):
    print("\n" + "=" * 60)
    print(f"NODE GROUP: {ng.name}")
    print("=" * 60)

    # Group-Input/Output-Schnittstelle
    for node in ng.nodes:
        if node.type == "GROUP_INPUT":
            names = [s.name for s in node.outputs if s.name]
            print(f"  GROUP INPUTS : {names}")
        elif node.type == "GROUP_OUTPUT":
            names = [s.name for s in node.inputs if s.name]
            print(f"  GROUP OUTPUTS: {names}")

    for node in ng.nodes:
        if node.type in ("GROUP_INPUT", "GROUP_OUTPUT"):
            continue
        line = f"\n[{node.name}]  type={node.type}"
        for key in _PROP_KEYS:
            if hasattr(node, key):
                line += f"  {key}={getattr(node, key)}"
        print(line)

        for inp in node.inputs:
            label = inp.name if inp.name else "(unnamed)"
            if inp.is_linked:
                for link in inp.links:
                    out_label = link.from_socket.name or "(out)"
                    print(f"    IN  {label:<14} <- [{link.from_node.name}].{out_label}")
            else:
                try:
                    print(f"    IN  {label:<14} = {_fmt(inp.default_value)}")
                except Exception:
                    print(f"    IN  {label:<14} = <kein default>")

    # Was speist den Group-Output?
    for node in ng.nodes:
        if node.type == "GROUP_OUTPUT":
            for inp in node.inputs:
                if inp.is_linked:
                    for link in inp.links:
                        out_label = link.from_socket.name or "(out)"
                        print(f"\n  → OUTPUT '{inp.name}' <- [{link.from_node.name}].{out_label}")


def main():
    all_groups = list(bpy.data.node_groups)
    print(f"[dump] {len(all_groups)} Node-Groups im File: {[g.name for g in all_groups]}")

    targets = all_groups if not GROUPS else [
        g for g in all_groups if g.name in GROUPS
    ]
    if not targets:
        print(f"[dump] KEINE der gesuchten Groups gefunden: {GROUPS}")
        print("[dump] → GROUPS oben mit echten Namen aus der Liste füllen.")
        return

    for ng in targets:
        dump_group(ng)
    print("\n[dump] FERTIG")


main()
