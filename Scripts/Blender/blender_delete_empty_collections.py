# ============================================================
#  Blender: leere Collections löschen
# ============================================================
#  Entfernt alle Collections die KEINE Objekte und KEINE
#  Child-Collections (mehr) enthalten.
#
#  Iterativ → behandelt auch verschachtelte Fälle:
#  eine Collection die nur leere Sub-Collections hat wird
#  in der nächsten Runde auch leer → und gelöscht.
#
#  Die Master "Scene Collection" wird NIE angefasst
#  (die ist nicht in bpy.data.collections).
#
#  Anwendung:
#    1. Scripting-Tab → Text → Open → diese Datei
#    2. (optional) PROTECT unten befüllen
#    3. "Run Script" (Play-Icon) oder Alt+P
#    4. Console öffnen (Window → Toggle System Console) für Report
# ============================================================
import bpy

# --- Konfiguration ---
# Collection-Namen die NICHT gelöscht werden sollen, auch wenn leer.
PROTECT = set()   # z.B. {"Spawns", "Lights"}


def is_empty(coll):
    """True wenn Collection weder direkte Objekte noch Child-Collections hat."""
    return len(coll.objects) == 0 and len(coll.children) == 0


def delete_empty_collections():
    removed_total = 0
    round_num = 0

    while True:
        round_num += 1
        empties = [
            c for c in bpy.data.collections
            if is_empty(c) and c.name not in PROTECT
        ]
        if not empties:
            break

        for c in empties:
            print(f"[cleanup] Runde {round_num}: lösche leere Collection '{c.name}'")
            bpy.data.collections.remove(c)
            removed_total += 1

    if removed_total == 0:
        print("[cleanup] Keine leeren Collections gefunden — nichts zu tun")
    else:
        print(f"[cleanup] FERTIG: {removed_total} leere Collections gelöscht "
              f"(in {round_num - 1} Runden)")

    remaining = len(bpy.data.collections)
    print(f"[cleanup] Übrig: {remaining} Collections")


delete_empty_collections()
