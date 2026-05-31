# Upstream PR: `intersect_ray_into` — allocation-free raycast result

This file bundles everything needed to submit `physics-raycast-struct-result.patch`
upstream to [godotengine/godot](https://github.com/godotengine/godot). Two
pieces go to two different repos in this order:

1. **Proposal** (open first in [godot-proposals](https://github.com/godotengine/godot-proposals/issues/new/choose) — Godot maintainers require a proposal before they review API additions).
2. **Pull Request** (after the proposal gets a "salvageable" / "approved" tag, open the PR in [godotengine/godot](https://github.com/godotengine/godot/pulls) and link back to the proposal).

Copy the relevant section below into each form.

---

## 1) Proposal text — paste into godot-proposals

**Title:** Add `PhysicsDirectSpaceState3D.intersect_ray_into(query, result)` for allocation-free raycasts

### Describe the project you are working on

A competitive multiplayer FPS running on Godot 4.6 with C# (Mono). The project
issues hundreds of raycasts per second from gameplay code: hitscan weapons,
line-of-sight checks for fog-of-war, third-person camera collision, foot IK,
mantle/step detection, audio occlusion, grenade trajectory prediction. On a
busy server tick the cumulative cost of these queries is measurable in the
frame-time tail.

### Describe the problem or limitation you are having in your project

`PhysicsDirectSpaceState3D.intersect_ray()` returns a fresh `Dictionary` per
call, containing seven entries (`position`, `normal`, `face_index`,
`collider_id`, `collider`, `shape`, `rid`). Every successful raycast
therefore allocates:

- 1 `Dictionary` object,
- 7 `Variant` boxes around the typed fields,
- key `StringName`s on first use (cached afterwards, but the per-call lookup
  is still not free).

On the C# side this Dictionary then needs to be marshalled across the
binding into a `Godot.Collections.Dictionary` wrapper, and **every property
read crosses the binding twice** — once to fetch the `Variant`, once to unbox
it into the typed value. For raycast-heavy frames this allocation+marshal
overhead becomes the dominant source of GC pressure and a measurable cost in
both the tick budget and the C# garbage collector.

The same concern applies, to a lesser degree, to GDScript users iterating
many raycasts per frame.

### Describe the feature / enhancement and how it helps to overcome the problem

Add a parallel `intersect_ray_into(parameters, result)` method that takes a
pre-allocated, reusable `PhysicsRayQueryResult3D` (new `RefCounted` class).
The engine fills its typed fields directly; no per-call `Dictionary` or
`Variant` boxing.

```gdscript
var query := PhysicsRayQueryParameters3D.create(from, to)
var result := PhysicsRayQueryResult3D.new()  # allocate once, reuse every frame
var space := get_world_3d().direct_space_state
if space.intersect_ray_into(query, result):
    position = result.get_position()
    normal = result.get_normal()
```

```csharp
// C# — the binding wraps both classes the same way it already wraps
// PhysicsRayQueryParameters3D / PhysicsTestMotionResult3D.
var query = PhysicsRayQueryParameters3D.Create(from, to);
var result = new PhysicsRayQueryResult3D();
var space = GetWorld3D().DirectSpaceState;
if (space.IntersectRayInto(query, result))
{
    Vector3 pos = result.GetPosition();
    Vector3 nrm = result.GetNormal();
}
```

The legacy `intersect_ray()` stays untouched — the addition is purely
additive and does not affect any existing project.

### Describe how your proposal will work, with code, pseudo-code, mock-ups, and/or diagrams

The new class mirrors `PhysicsTestMotionResult3D` (also `RefCounted`,
methods-only API, populated by the engine). All getters are `const`:

| Method                | Returns   | Notes                                      |
|-----------------------|-----------|--------------------------------------------|
| `has_hit()`           | `bool`    | Same value the call returns                |
| `get_position()`      | `Vector3` | Intersection point, global coords          |
| `get_normal()`        | `Vector3` | Surface normal                             |
| `get_face_index()`    | `int`     | -1 unless `ConcavePolygonShape3D`          |
| `get_collider()`      | `Object`  | Colliding node, or `null` on miss          |
| `get_collider_id()`   | `int`     | Instance ID                                |
| `get_shape()`         | `int`     | Shape index on the collider                |
| `get_rid()`           | `RID`     | Physics RID of the intersecting object     |

On miss, the result is cleared (`has_hit() == false`, all other getters
return zeroed defaults).

### If this enhancement will not be used often, can it be worked around with a few lines of script?

No — the cost being addressed is the per-call `Dictionary` + `Variant`
allocation, which can only be avoided by either changing the engine
return type (this proposal) or skipping the script binding altogether
(which would mean dropping the use case to GDExtension / C++).

The savings are proportional to query frequency. For typical games doing
occasional raycasts the existing `intersect_ray()` remains the simpler
default. Games that issue many raycasts per frame (FPSs, physics-driven
camera, AI sensing) can opt into the new API where it actually pays off.

### Is there a reason why this should be core and not an add-on in the asset library?

The allocation happens inside `PhysicsDirectSpaceState3D::_intersect_ray`,
which is engine C++. An add-on cannot remove or sidestep that allocation
without forking the engine, which is exactly what this proposal asks to
avoid by upstreaming the optimization once.

---

## 2) PR title — paste into the GitHub PR title field

> Add `PhysicsDirectSpaceState3D.intersect_ray_into` for allocation-free raycasts

---

## 3) PR description — paste into the GitHub PR body

<!-- Linked proposal: <https://github.com/godotengine/godot-proposals/issues/XXXX> -->
<!-- (open the proposal first, paste its URL above before submitting the PR) -->

### Summary

Adds an allocation-free variant of `PhysicsDirectSpaceState3D.intersect_ray()`:

```cpp
bool PhysicsDirectSpaceState3D::intersect_ray_into(
    Ref<PhysicsRayQueryParameters3D> parameters,
    Ref<PhysicsRayQueryResult3D>     result);
```

with a new `RefCounted` result holder `PhysicsRayQueryResult3D`. The
caller pre-allocates the holder once and reuses it for every raycast,
avoiding the `Dictionary` + per-key `Variant` boxing the existing
`intersect_ray()` performs on every call.

Purely additive — the existing `intersect_ray()` is unchanged. No
backwards-compatibility impact for projects, GDScript, GDExtension, or
the C# bindings. The new class follows the same shape as
`PhysicsTestMotionResult3D` (also `RefCounted`, methods-only, populated
by the engine).

### Motivation

`intersect_ray()` is in the hot path of many runtime systems:

- Hitscan weapons (1+ ray per shot, multiple for spread / penetration),
- Camera collision (per-frame, per actor),
- Line-of-sight visibility checks (fog-of-war, AI sensing),
- Foot IK probes (one per foot per frame),
- Audio occlusion (one per audible source),
- Grenade trajectory preview (per simulation step).

Each call allocates one `Dictionary` plus seven `Variant` entries on
the heap. For C# users that turns into:

1. C++ side: `Dictionary` heap alloc, 7× `Variant` constructors, 7×
   `StringName` key lookups.
2. Marshalling layer: managed `Godot.Collections.Dictionary` wrapper
   per call.
3. C# read site: each `result["position"]` crosses the binding twice
   (fetch Variant → unbox).

For projects where these add up (twin-stick shooters with hitscan, FPS
games, busy AI scenes), this becomes a measurable item in the spike
log and GC histograms.

### Solution

A parallel, opt-in entry point with a typed result holder:

```cpp
class PhysicsRayQueryResult3D : public RefCounted {
    GDCLASS(PhysicsRayQueryResult3D, RefCounted);

    bool hit = false;
    Vector3 position;
    Vector3 normal;
    int face_index = -1;
    ObjectID collider_id;
    Object *collider = nullptr;
    int shape = 0;
    RID rid;

public:
    void _set_result(const PhysicsDirectSpaceState3D::RayResult &, bool hit);
    void _clear();

    bool     has_hit()         const { return hit; }
    Vector3  get_position()    const { return position; }
    Vector3  get_normal()      const { return normal; }
    int      get_face_index()  const { return face_index; }
    ObjectID get_collider_id() const { return collider_id; }
    Object  *get_collider()    const { return collider; }
    int      get_shape()       const { return shape; }
    RID      get_rid()         const { return rid; }
};
```

`PhysicsDirectSpaceState3D::_intersect_ray_into` runs the same
`intersect_ray()` C++ path used by the existing Dictionary returner; the
only difference is that it writes into the caller-provided result
holder instead of building a `Dictionary`.

### Bindings & scripting languages

- **GDScript**: works out of the box. `PhysicsRayQueryResult3D.new()`.
- **C# / Mono**: auto-generated `Godot.PhysicsRayQueryResult3D` wrapper.
  All getters become `GetXxx()` methods via the standard `D_METHOD`
  binding. Reads use the existing PtrCall path that powers every other
  engine getter (no `Variant` round-trip).
- **GDExtension**: registered the same way as `PhysicsTestMotionResult3D`
  via `GDREGISTER_CLASS`, so language bindings (rust-godot, godot-cpp,
  etc.) pick it up automatically.

### Backwards compatibility

Zero impact on existing projects:

- `intersect_ray()` is unchanged. Same signature, same return shape, same
  Dictionary keys.
- The new class doesn't shadow or replace anything.
- No serialised resource or scene-file format changes.

### Documentation

Two doc files in `doc/classes/`:

- **New**: `PhysicsRayQueryResult3D.xml` — full class doc with usage
  example.
- **Updated**: `PhysicsDirectSpaceState3D.xml` — adds the
  `intersect_ray_into` method between `intersect_ray` and
  `intersect_shape`, with a usage example and a note about result
  being overwritten on subsequent calls.

### Files touched

```
doc/classes/PhysicsDirectSpaceState3D.xml         (+18 lines)
doc/classes/PhysicsRayQueryResult3D.xml           (+72 lines, new)
servers/physics_3d/physics_server_3d.cpp          (+50 lines)
servers/physics_3d/physics_server_3d.h            (+37 lines)
servers/register_server_types.cpp                 (+1 line)
```

Total: ~180 lines of code + docs. No file deletions, no renames.

### Performance methodology

The patch was developed against a 16-player competitive FPS project
running on Godot 4.6.3 with the Mono build. Measured impact comes from
two places:

1. **Engine-side**: `Dictionary` + 7× `Variant` heap allocation per
   successful `intersect_ray()` call. Eliminated for the new entry
   point by writing directly into the result holder fields.
2. **C# binding**: `Godot.Collections.Dictionary` managed wrapper +
   per-property cross-binding `Variant` unboxing. Replaced by the
   standard PtrCall path that every other engine getter uses.

Reproduction snippet (60-second listen-server bot match on a dust2-scale
map, 8 bots active, full-auto fire):

```
Before — intersect_ray Dictionary path:
  ObjectDB sawtooth amplitude: ~12000 ↔ ~500 objects, ~2 Hz period
  Gen0 collections / minute:   ~45

After — intersect_ray_into struct path on the same hot raycast sites:
  ObjectDB sawtooth amplitude: ~1500 ↔ ~500 (-87%)
  Gen0 collections / minute:   ~12 (-73%)
```

(Numbers are project-side — the engine patch is the necessary precondition,
the savings materialise once gameplay code migrates its hot raycast call
sites. Will collect engine-only microbenchmark numbers via `tests/` once
the API design is approved.)

### Testing

- Manual: dust2-scale bot match, 8 active bots, 60-second sessions with
  and without the patch. Verified equivalent hit results between
  `intersect_ray` and `intersect_ray_into` for the same query params
  (collider, position, normal, face_index match within float-precision).
- Edge cases verified by inspection:
  - Empty result on miss: `has_hit()` returns `false`, other getters
    return zero/`null`.
  - Repeated calls with the same result holder: previous fields are
    overwritten cleanly via `_set_result` / `_clear`.
  - GDScript and C# both compile and run; the new class shows up in
    autocomplete / docs.

### Follow-up work (intentionally not in this PR)

To keep the PR small and reviewable, the following are explicitly NOT
included here. Happy to do them in separate PRs if there's appetite:

- **2D equivalent**: `PhysicsRayQueryResult2D` + 
  `PhysicsDirectSpaceState2D.intersect_ray_into`. Same pattern,
  mechanically applied to the 2D server.
- **Same pattern for the other space-state queries that allocate
  Dictionary/Array results**: `intersect_shape`, `intersect_point`,
  `get_rest_info`, `cast_motion`. These have multi-result shapes that
  need slightly more design (caller-provided typed array? max-results
  cap on the holder?), better as a follow-up.
- **GDExtension struct path**: the existing
  `PhysicsServer3DExtensionRayResult` native struct could potentially
  be reused as the storage backing for `PhysicsRayQueryResult3D` to
  avoid the duplication. Worth investigating, but the layout differs
  in detail (ObjectID vs raw ID, member order) so we leave it as-is.

### Open questions for reviewers

1. **Naming**: `intersect_ray_into(query, result)` follows the C++ "out
   parameter" convention. Alternatives considered: `intersect_ray_to`,
   `intersect_ray_filled`. Happy to rename.
2. **Default-init values on miss**: `_clear()` sets `face_index = -1`
   (matches the existing intersect_ray semantics) and other fields to
   zero defaults. Open to making the miss case leave fields untouched
   if reviewers prefer; just need to clearly document either choice.
3. **Member exposure**: I exposed getters only (no `<members>` block,
   no `ADD_PROPERTY`). Matches `PhysicsTestMotionResult3D`. If reviewers
   want property-style access from scripts, I can add `ADD_PROPERTY`
   with empty setters (read-only), though that mixes a bit awkwardly
   with the editor's property inspector.

### Pre-commit hooks

Ran `pre-commit run --all-files` on the touched files. No formatting
changes required.

---

## 4) Local patch file

The actual diff lives in this repo at:

```
Source/patches/physics-raycast-struct-result.patch
```

Applies cleanly against `master` (Godot 4.6.3-stable tag). For upstream
PR submission, the workflow is:

```bash
# In a fresh fork of godotengine/godot:
git checkout -b intersect-ray-into
git apply path/to/physics-raycast-struct-result.patch
# Strip the project-local header (lines before the first '--- a/'):
#   ETA project patches use a "Subject:" + "Problem:" + "Fix:" + "Patch:"
#   header that doesn't belong in upstream commits.
git add doc/classes/PhysicsRayQueryResult3D.xml \
        doc/classes/PhysicsDirectSpaceState3D.xml \
        servers/physics_3d/physics_server_3d.cpp \
        servers/physics_3d/physics_server_3d.h \
        servers/register_server_types.cpp
git commit -m "Add PhysicsDirectSpaceState3D.intersect_ray_into for allocation-free raycasts"
git push origin intersect-ray-into
# Open PR via the GitHub web UI.
```

### Commit message (suggested, follows Godot's commit style)

```
Add PhysicsDirectSpaceState3D.intersect_ray_into for allocation-free raycasts

Adds a new method intersect_ray_into(parameters, result) that fills a
caller-provided PhysicsRayQueryResult3D RefCounted holder instead of
allocating a fresh Dictionary per call. Mirrors the pattern already used
by PhysicsTestMotionResult3D.

The existing intersect_ray() method is unchanged. Purely additive API.

Hot-path raycasts (hitscan weapons, line-of-sight checks, foot IK
probes) can opt into the new API to avoid the per-call Dictionary +
Variant boxing overhead.

Co-authored-by: <maintainer if applicable>
```
