using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Vantix;

/// <summary>Global ConVar container. Instances are passed around (constructor/property) rather than
/// accessed statically so code stays testable with mock instances.</summary>
public static class ConVars
{
	public static readonly SvConVars Sv = new();
	public static readonly ClConVars Cl = new();

	/// <summary>Weapon registry. One immutable definition per weapon. Add new weapons here.</summary>
	public static class Weapons
	{
		public static readonly WeaponStats AR15 = new()
		{
			Name = "AR15",
			FireRate = 8.0f,
			FireMode = 0,
			MoveSpeedMul = 1.0f,
			SprintSpeedMul = 1.0f,

			ReloadTime = 2.6f,
			MagazineSize = 30,
			MaxReserveAmmo = 90,
			PatternScale = 1.0f,
			PatternResetDelay = 0.35f,
			AimPunchMaxClimb = 4.5f,
			AimPunchRecoveryFiring = 3.0f,
			AimPunchRecoveryReleased = 18.0f,
			HipfireBaseSpread = 2.5f,
			MovementSpread = 1.4f,
			MovementSpreadShift = 0.15f,
			MovementSpreadWalk = 0.55f,
			CameraAimPunchMul = 0.50f,
			WeaponKickPitch = 0.3f,
			WeaponKickYaw = 0.10f,
			WeaponKickRoll = 0.0f,
			WeaponKickBack = 0.015f,
			WeaponKickUp = 0.020f,
			WeaponKickStiffness = 200f,
			WeaponKickDamping = 28f,
			WeaponRandomness = 0.2f,
			SpreadWeaponMul = 0.5f,
			AimPunchSmoothing = 18.0f,
			FingerKickZ = -4.0f,
			FingerKickRecovery = 12.0f,
			AdsFov = 60f,
			AdsPosOffset = new Godot.Vector3(-0.084f, 0.01f, 0.076f),
			AdsRotOffset = new Godot.Vector3(3.28f, 5.285f, -1.23f),
			AdsKickMul = 0.08f,
			AdsKickPosMul = 0.18f,
			AdsAmbientMul = 0.3f,
			RecoilPattern = new Godot.Vector2[]
			{
				new(0.00f, 0.40f),
				new(0.05f, 0.95f),
				new(0.10f, 1.25f),
				new(0.05f, 1.30f),
				new(-0.05f, 1.15f),
				new(-0.18f, 0.95f),
				new(-0.10f, 0.75f),
				new(0.15f, 0.55f),
				new(0.35f, 0.45f),
				new(0.50f, 0.40f),
				new(0.45f, 0.35f),
				new(0.25f, 0.30f),
				new(-0.05f, 0.30f),
				new(-0.35f, 0.25f),
				new(-0.60f, 0.25f),
				new(-0.70f, 0.20f),
				new(-0.65f, 0.20f),
				new(-0.45f, 0.20f),
				new(-0.20f, 0.15f),
				new(0.05f, 0.15f),
				new(0.30f, 0.15f),
				new(0.45f, 0.10f),
				new(0.50f, 0.10f),
				new(0.40f, 0.10f),
				new(0.20f, 0.10f),
				new(-0.05f, 0.10f),
				new(-0.25f, 0.05f),
				new(-0.30f, 0.05f),
				new(-0.25f, 0.05f),
				new(-0.15f, 0.05f),
			},
			ShootBodyClips = System.Array.Empty<string>(),
			ShootMechClips = System.Array.Empty<string>(),
			ShootTailClips = System.Array.Empty<string>(),
			ShootDistantClips = System.Array.Empty<string>(),
			ReloadClips = System.Array.Empty<string>(),
			DryFireClips = System.Array.Empty<string>(),
			ShootVolumeDb = 0f,
			DistantCrossoverM = 28f,
		};
	}

	/// <summary>Sets a ConVar by name (sv_*/cl_*); returns true on success. AOT-safe via type-explicit prefix dispatch.</summary>
	public static bool TrySet(string name, string value)
	{
		if (string.IsNullOrEmpty(name))
			return false;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return TrySetOn(Sv, lower[3..].Replace("_", ""), value);
		if (lower.StartsWith("cl_"))
			return TrySetOn(Cl, lower[3..].Replace("_", ""), value);
		return false;
	}

	/// <summary>Type-explicit set helper; the DynamicallyAccessedMembers attribute keeps field metadata under AOT.</summary>
	private static bool TrySetOn<
		[DynamicallyAccessedMembers(
			DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
		)]
			T
	>(T instance, string fieldName, string value)
	{
		if (instance == null)
			return false;
		var field = typeof(T).GetField(
			fieldName,
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);
		if (field == null)
			return false;
		try
		{
			object parsed = ParseValue(value, field.FieldType);
			if (parsed == null)
				return false;
			field.SetValue(instance, parsed);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Gets a ConVar value as string, or null if not found. AOT-safe via the same dispatch as TrySet.</summary>
	public static string Get(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return GetOn(Sv, lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_"))
			return GetOn(Cl, lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit get helper.</summary>
	private static string GetOn<
		[DynamicallyAccessedMembers(
			DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
		)]
			T
	>(T instance, string fieldName)
	{
		if (instance == null)
			return null;
		var field = typeof(T).GetField(
			fieldName,
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);
		return field?.GetValue(instance)?.ToString();
	}

	/// <summary>Enumerates all ConVar names in snake_case (e.g. "sv_debug_hitboxes"), optionally filtered by prefix.</summary>
	public static IEnumerable<string> List(string prefix = null)
	{
		foreach (var f in typeof(SvConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("sv_", StringComparison.OrdinalIgnoreCase))
				yield return "sv_" + ToSnakeCase(f.Name);
		foreach (var f in typeof(ClConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("cl_", StringComparison.OrdinalIgnoreCase))
				yield return "cl_" + ToSnakeCase(f.Name);
	}

	/// <summary>Converts "DebugHitboxes" → "debug_hitboxes" for console display and matching.</summary>
	private static string ToSnakeCase(string camelCase)
	{
		if (string.IsNullOrEmpty(camelCase))
			return camelCase;
		var sb = new System.Text.StringBuilder(camelCase.Length + 4);
		for (int i = 0; i < camelCase.Length; i++)
		{
			char c = camelCase[i];
			if (char.IsUpper(c) && i > 0)
				sb.Append('_');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}

	/// <summary>Returns a ConVar's .NET type (float/int/bool/string), or null if unknown. AOT-safe.</summary>
	public static Type GetFieldType(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return GetFieldTypeOn<SvConVars>(lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_"))
			return GetFieldTypeOn<ClConVars>(lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit field-type lookup.</summary>
	private static Type GetFieldTypeOn<
		[DynamicallyAccessedMembers(
			DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
		)]
			T
	>(string fieldName)
	{
		var field = typeof(T).GetField(
			fieldName,
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);
		return field?.FieldType;
	}

	/// <summary>Checks whether a value string is compatible with the field type without setting it.
	/// Returns (ok, friendlyTypeName) for error messages.</summary>
	public static (bool ok, string typeName) ValidateValue(string name, string value)
	{
		var type = GetFieldType(name);
		if (type == null)
			return (false, "unknown");
		string typeName = TypeFriendlyName(type);
		try
		{
			object parsed = ParseValue(value, type);
			return (parsed != null, typeName);
		}
		catch
		{
			return (false, typeName);
		}
	}

	/// <summary>Friendly name for UI/errors: float/int/bool/string instead of Single/Int32/Boolean/String.</summary>
	public static string TypeFriendlyName(Type t)
	{
		if (t == typeof(float))
			return "float";
		if (t == typeof(int))
			return "int";
		if (t == typeof(bool))
			return "bool";
		if (t == typeof(string))
			return "string";
		return t.Name.ToLowerInvariant();
	}

	/// <summary>Parses a string into the requested primitive type (float/int/bool/string).</summary>
	private static object ParseValue(string value, Type targetType)
	{
		var culture = CultureInfo.InvariantCulture;
		if (targetType == typeof(float))
			return float.Parse(value, culture);
		if (targetType == typeof(int))
			return int.Parse(value, culture);
		if (targetType == typeof(bool))
			return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
		if (targetType == typeof(string))
			return value;
		return null;
	}
}
