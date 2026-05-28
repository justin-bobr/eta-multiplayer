# ============================================================
#  Blender: unbenutzte Materials löschen
# ============================================================
#  Entfernt alle Materials die KEINEM Mesh zugewiesen sind —
#  weder in Mesh-Daten noch in Object-Material-Slots.
#
#  Typisch nach dem Merge-Script: Materials von gelöschten
#  Einzelmeshes bleiben als "Leichen" im .blend.
#
#  Anwendung:
#    1. Scripting-Tab → Text → Open → diese Datei
#    2. (optional) KEEP_FAKE_USER unten anpassen
#    3. "Run Script" (Play-Icon) oder Alt+P
#    4. Console öffnen (Window → Toggle System Console) für Report
# ============================================================
import bpy

# --- Konfiguration ---
# True  = Materials mit "Fake User" (Schild-Icon) behalten, auch wenn unbenutzt.
#         Sinnvoll wenn du Materials bewusst zum Wiederverwenden markiert hast.
# False = auch Fake-User-Materials löschen wenn nirgends zugewiesen.
KEEP_FAKE_USER = True


def delete_unused_materials():
    # Schritt 1: alle tatsächlich zugewiesenen Materials sammeln
    used = set()

    # aus Mesh-Daten (mesh.materials)
    for mesh in bpy.data.meshes:
        for mat in mesh.materials:
            if mat is not None:
                used.add(mat)

    # aus Object-Material-Slots (Objekte können Slots überschreiben)
    for obj in bpy.data.objects:
        for slot in obj.material_slots:
            if slot.material is not None:
                used.add(slot.material)

    # Schritt 2: alles was nicht in 'used' ist → löschen
    removed = 0
    kept_fake = 0
    for mat in list(bpy.data.materials):
        if mat in used:
            continue
        if KEEP_FAKE_USER and mat.use_fake_user:
            kept_fake += 1
            continue
        print(f"[mat-cleanup] Lösche unbenutztes Material: '{mat.name}'")
        bpy.data.materials.remove(mat)
        removed += 1

    # Report
    if removed == 0:
        print("[mat-cleanup] Keine unbenutzten Materials gefunden — nichts zu tun")
    else:
        print(f"[mat-cleanup] FERTIG: {removed} unbenutzte Materials gelöscht")
    if kept_fake > 0:
        print(f"[mat-cleanup] {kept_fake} unbenutzte Materials behalten (Fake User)")
    print(f"[mat-cleanup] Übrig: {len(bpy.data.materials)} Materials")


delete_unused_materials()
