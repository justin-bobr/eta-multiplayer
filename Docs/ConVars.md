# ConVars

Global ConVar container. Instances are passed around (constructor/property) rather than accessed statically so code stays testable with mock instances.

## Methods

| Name | Summary |
|------|---------|
| `Get(string)` | Gets a ConVar value as string, or null if not found. AOT-safe via the same dispatch as TrySet. |
| `GetFieldType(string)` | Returns a ConVar's .NET type (float/int/bool/string), or null if unknown. AOT-safe. |
| `GetFieldTypeOn``1(string)` | Type-explicit field-type lookup. |
| `GetOn``1(``0, string)` | Type-explicit get helper. |
| `List(string)` | Enumerates all ConVar names in snake_case (e.g. "sv_debug_hitboxes"), optionally filtered by prefix. |
| `ParseValue(string, Type)` | Parses a string into the requested primitive type (float/int/bool/string). |
| `ToSnakeCase(string)` | Converts "DebugHitboxes" → "debug_hitboxes" for console display and matching. |
| `TrySet(string, string)` | Sets a ConVar by name (sv_*/cl_*); returns true on success. AOT-safe via type-explicit prefix dispatch. |
| `TrySetOn``1(``0, string, string)` | Type-explicit set helper; the DynamicallyAccessedMembers attribute keeps field metadata under AOT. |
| `TypeFriendlyName(Type)` | Friendly name for UI/errors: float/int/bool/string instead of Single/Int32/Boolean/String. |
| `ValidateValue(string, string)` | Checks whether a value string is compatible with the field type without setting it. Returns (ok, friendlyTypeName) for error messages. |
