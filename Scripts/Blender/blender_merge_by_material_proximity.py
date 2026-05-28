# ============================================================
#  Blender Mesh-Merge: gleiches Material + räumlich nah
# ============================================================
#  Joint Mesh-Objekte NUR wenn beide Bedingungen erfüllt sind:
#    1. Sie nutzen dasselbe Material (gleiche Material-Slots)
#    2. Ihre Bounding-Box-Zentren sind näher als MERGE_DISTANCE
#
#  Proximity via Connected-Components (Union-Find): A nah B, B nah C
#  → A,B,C landen im selben Cluster. Spatial-Hash macht's O(n), kein O(n²).
#
#  Ziel: dust2 hat ~3578 Einzelmeshes → ~7000 Draw-Calls/Frame.
#  Nach dem Merge: ~30-100 Meshes → spielbare FPS.
#
#  Anwendung:
#    1. dust2 .blend in Blender öffnen
#    2. Scripting-Tab → Text → Open → diese Datei
#    3. MERGE_DISTANCE unten einstellen
#    4. "Run Script" (Play-Icon) oder Alt+P
#    5. Console öffnen (Window → Toggle System Console) für den Report
# ============================================================
import bpy
import mathutils
from collections import defaultdict

# --- Konfiguration ---
# Meshes deren Zentren näher als dieser Wert (Meter) sind + gleiches Material → merge.
# Klein (5-10)  = wenig Chaining, mehr Meshes, feineres Occlusion-Culling
# Groß  (20-50) = mehr Chaining, weniger Meshes, weniger Draw-Calls
# Für dust2 Start-Empfehlung: 15.0
MERGE_DISTANCE = 15.0

ONLY_SELECTED = False    # True = nur selektierte Objekte, False = ganze Szene
RENAME_MERGED = True     # gemergte Objekte umbenennen zu "merged_<material>"


def material_signature(obj):
    """Hashbare Signatur aller Material-Slots — Objekte müssen identische Slots haben."""
    if not obj.material_slots:
        return ("__NO_MAT__",)
    return tuple(sorted(
        s.material.name if s.material else "__EMPTY__"
        for s in obj.material_slots
    ))


def world_center(obj):
    """Welt-Space Zentrum der Objekt-Bounding-Box."""
    local = sum((mathutils.Vector(c) for c in obj.bound_box), mathutils.Vector()) / 8.0
    return obj.matrix_world @ local


class UnionFind:
    """Union-Find mit Path-Compression — für Connected-Components-Clustering."""
    def __init__(self, n):
        self.parent = list(range(n))

    def find(self, x):
        while self.parent[x] != x:
            self.parent[x] = self.parent[self.parent[x]]
            x = self.parent[x]
        return x

    def union(self, a, b):
        ra, rb = self.find(a), self.find(b)
        if ra != rb:
            self.parent[ra] = rb


def cluster_by_proximity(objs, distance):
    """Gruppiert objs in Cluster wo jedes Objekt < distance zu mind. einem anderen ist.
    Spatial-Hash beschleunigt: nur Nachbar-Zellen werden geprüft → ~O(n)."""
    n = len(objs)
    centers = [world_center(o) for o in objs]
    uf = UnionFind(n)
    dist_sq = distance * distance
    cell = distance  # Zellgröße = Merge-Distanz → Nachbarn liegen in ±1 Zelle

    # Spatial-Hash aufbauen: Zell-Koord → Liste von Objekt-Indizes
    grid = defaultdict(list)
    coords = []
    for i, c in enumerate(centers):
        key = (int(c.x // cell), int(c.y // cell), int(c.z // cell))
        coords.append(key)
        grid[key].append(i)

    # Für jedes Objekt nur 27 Nachbar-Zellen prüfen statt aller n Objekte
    for i in range(n):
        cx, cy, cz = coords[i]
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for j in grid.get((cx + dx, cy + dy, cz + dz), ()):
                        if j > i and (centers[i] - centers[j]).length_squared <= dist_sq:
                            uf.union(i, j)

    # Cluster einsammeln
    clusters = defaultdict(list)
    for i in range(n):
        clusters[uf.find(i)].append(objs[i])
    return list(clusters.values())


def merge_meshes():
    # Object-Mode erzwingen — join() funktioniert nur dort
    if bpy.context.view_layer.objects.active and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    source = bpy.context.selected_objects if ONLY_SELECTED \
        else list(bpy.context.view_layer.objects)
    meshes = [o for o in source if o.type == 'MESH' and o.library is None]

    before = len(meshes)
    if before == 0:
        print("[merge] Keine Mesh-Objekte gefunden — abbruch")
        return
    print(f"[merge] Start: {before} Mesh-Objekte, MERGE_DISTANCE={MERGE_DISTANCE}m")

    # Schritt 1: nach Material gruppieren
    by_material = defaultdict(list)
    for obj in meshes:
        by_material[material_signature(obj)].append(obj)
    print(f"[merge] {len(by_material)} unterschiedliche Material-Sets")

    # Schritt 2: pro Material-Gruppe nach Proximity clustern
    join_groups = []
    for sig, objs in by_material.items():
        if len(objs) < 2:
            continue
        for cluster in cluster_by_proximity(objs, MERGE_DISTANCE):
            if len(cluster) > 1:
                join_groups.append((sig, cluster))

    # Schritt 3: joinen
    merged_objects = 0
    for sig, cluster in join_groups:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in cluster:
            obj.hide_set(False)          # versteckte sichtbar machen, sonst kein select
            obj.select_set(True)
        target = cluster[0]
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.join()
        if RENAME_MERGED:
            target.name = "merged_" + sig[0]
        merged_objects += len(cluster)

    after = len([o for o in bpy.context.view_layer.objects if o.type == 'MESH'])
    print(f"[merge] FERTIG: {before} → {after} Mesh-Objekte")
    print(f"[merge] {len(join_groups)} Cluster gejoint ({merged_objects} Objekte zusammengefasst)")
    print(f"[merge] Draw-Call-Reduktion: ~{before} → ~{after}")


merge_meshes()
