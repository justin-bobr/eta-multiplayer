using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Quake/CS-Style Dev-Konsole. Per Hotkey (Default ^) auf-/zugeklappt; Bottom-Panel mit Log + LineEdit.
/// Command-Routing:
///   - "sv_*" → Server-ConVar / -Command (via ConVarSync-Packet an Server geschickt; Server validiert,
///     applied, broadcastet zurück an alle Clients).
///   - "cl_*" → Client-ConVar via <see cref="ConVars.TrySet"/>
///   - "echo", "help", "clear", "quit", "history" → Built-In
///   - Default → ConVars.TrySet versuchen, sonst "Unknown command".
///
/// Input-Verlauf via ↑/↓-Pfeiltasten (wenn Typeahead zu), max 64 Einträge.
/// Typeahead: zeigt während des Tippens die Top-10 matchenden ConVars (sv_*+cl_*) — sortiert nach
/// Prefix-Match zuerst, dann Contains. Tab autocompletet zum ersten Match. ↑/↓ navigiert die Liste.
/// Enter wählt das Highlight aus (oder submitted die Zeile wenn nichts highlighted).
/// </summary>
public partial class ConsoleHud : CanvasLayer
{
	/// <summary>True wenn die Console geöffnet ist — InputGate konsultiert das und blockiert
	/// Movement/Fire/Mouse-Look während Tipp-Mode.</summary>
	public static bool IsAnyOpen { get; private set; }

	/// <summary>Most recently created ConsoleHud. Used by <see cref="NetClient.HandleServerLog"/> to
	/// echo server-broadcast diagnostic messages into the in-game console panel without needing the
	/// user to alt-tab to a stdout window. Cleared in <see cref="_ExitTree"/>.</summary>
	public static ConsoleHud Instance { get; private set; }

	[Export] public int LayerOrder = 200;

	private Control _root;
	private RichTextLabel _log;
	private LineEdit _input;
	private ItemList _suggestions;
	private bool _isOpen;
	private readonly List<string> _history = new();
	private int _historyIdx = -1;
	private const int MaxHistory = 64;
	private const int MaxLogLines = 200;
	private const int MaxSuggestions = 10;
	private const int _suggestionRowHeight = 22;
	private readonly List<string> _currentSuggestions = new();

	public override void _Ready()
	{
		Layer = LayerOrder;
		Instance = this;
		BuildUi();
		SetOpen(false);
		PrintLine("[color=#888888]ETA Console — type 'help' for commands.[/color]");
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	private void BuildUi()
	{
		_root = new Control
		{
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0.4f,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		AddChild(_root);

		var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(bg);

		var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		// Pure schwarz, kein Rand. Transparenz ~0.65 damit man noch sehen kann was im Spiel passiert.
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.65f),
		};
		style.ContentMarginLeft = 12f;
		style.ContentMarginRight = 12f;
		style.ContentMarginTop = 8f;
		style.ContentMarginBottom = 8f;
		panel.AddThemeStyleboxOverride("panel", style);
		_root.AddChild(panel);

		var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		vbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(vbox);

		_log = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			SelectionEnabled = true,
			MouseFilter = Control.MouseFilterEnum.Pass,
		};
		_log.AddThemeFontSizeOverride("normal_font_size", 13);
		_log.AddThemeColorOverride("default_color", new Color(0.88f, 0.95f, 0.88f));
		vbox.AddChild(_log);

		// Typeahead-Liste ÜBER dem Input. Default hidden — wird erst sichtbar wenn _input non-empty
		// + matching ConVars vorhanden. CustomMinimumSize wird in RefreshSuggestions explizit
		// gesetzt (item_count × _suggestionRowHeight) — AutoHeight allokiert in einer VBox nicht
		// zuverlässig Platz vor dem ersten Layout-Pass.
		_suggestions = new ItemList
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			SameColumnWidth = true,
			MaxColumns = 1,
			SelectMode = ItemList.SelectModeEnum.Single,
			AllowReselect = true,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_suggestions.AddThemeFontSizeOverride("font_size", 13);
		// StyleBox so dass das Typeahead-Panel sich vom Console-BG abhebt aber gleicher Look.
		var sugBg = new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.05f, 0.92f) };
		sugBg.ContentMarginLeft = 6f; sugBg.ContentMarginRight = 6f;
		sugBg.ContentMarginTop = 4f; sugBg.ContentMarginBottom = 4f;
		_suggestions.AddThemeStyleboxOverride("panel", sugBg);
		_suggestions.ItemActivated += OnSuggestionActivated;
		vbox.AddChild(_suggestions);

		_input = new LineEdit
		{
			PlaceholderText = "command…",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CaretBlink = true,
		};
		_input.AddThemeFontSizeOverride("font_size", 14);
		_input.TextSubmitted += OnInputSubmitted;
		_input.TextChanged += OnInputTextChanged;
		_input.GuiInput += OnInputGuiEvent;
		vbox.AddChild(_input);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Toggle via "console" InputMap-Action (definiert in project.godot). Default-Bind ist Tilde/^.
		// User kann das im Project Settings → Input Map ändern ohne Code-Edit.
		if (@event.IsActionPressed(InputActions.Console))
		{
			SetOpen(!_isOpen);
			GetViewport().SetInputAsHandled();
		}
	}

	private void SetOpen(bool open)
	{
		_isOpen = open;
		IsAnyOpen = open;
		_root.Visible = open;
		if (open)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_input.GrabFocus();
		}
		else
		{
			// Nur zurück auf Captured wenn KEIN anderes Menü auf ist (Scoreboard, Settings).
			if (!SettingsMenu.IsAnyOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
			_input.ReleaseFocus();
			HideSuggestions();
		}
	}

	private void OnInputSubmitted(string text)
	{
		string trimmed = text?.Trim() ?? "";
		_input.Text = "";
		_historyIdx = -1;
		HideSuggestions();
		if (string.IsNullOrEmpty(trimmed)) return;

		// History: nur eindeutige + nicht-Duplikate.
		if (_history.Count == 0 || _history[_history.Count - 1] != trimmed)
		{
			_history.Add(trimmed);
			if (_history.Count > MaxHistory) _history.RemoveAt(0);
		}

		PrintLine($"[color=#7ec8e3]> {trimmed}[/color]");
		Execute(trimmed);
	}

	/// <summary>Pfeil-↑/↓ blättert im History-Stack ODER navigiert die Typeahead-Liste wenn sichtbar.
	/// Tab autocompletet zum ersten Suggestion. Enter wird via TextSubmitted gehandhabt, aber wenn ein
	/// Suggestion selected ist wollen wir das DIESES gewählt wird statt submit.</summary>
	private void OnInputGuiEvent(InputEvent @event)
	{
		if (@event is not InputEventKey k || !k.Pressed) return;

		// Tab: autocomplete zum ersten / aktuell selektierten Suggestion.
		if (k.Keycode == Key.Tab)
		{
			if (_suggestions.Visible && _currentSuggestions.Count > 0)
			{
				int idx = _suggestions.GetSelectedItems().Length > 0 ? _suggestions.GetSelectedItems()[0] : 0;
				ApplySuggestion(_currentSuggestions[idx]);
			}
			GetViewport().SetInputAsHandled();
			return;
		}

		// ↑/↓: wenn Suggestions sichtbar → Liste navigieren, sonst History.
		if (_suggestions.Visible && _currentSuggestions.Count > 0)
		{
			if (k.Keycode == Key.Up)
			{
				int cur = _suggestions.GetSelectedItems().Length > 0 ? _suggestions.GetSelectedItems()[0] : 0;
				int next = Mathf.Max(0, cur - 1);
				_suggestions.Select(next);
				_suggestions.EnsureCurrentIsVisible();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (k.Keycode == Key.Down)
			{
				int cur = _suggestions.GetSelectedItems().Length > 0 ? _suggestions.GetSelectedItems()[0] : -1;
				int next = Mathf.Min(_currentSuggestions.Count - 1, cur + 1);
				_suggestions.Select(next);
				_suggestions.EnsureCurrentIsVisible();
				GetViewport().SetInputAsHandled();
				return;
			}
			// Escape: close suggestions, keep input open.
			if (k.Keycode == Key.Escape)
			{
				HideSuggestions();
				GetViewport().SetInputAsHandled();
				return;
			}
		}
		else
		{
			if (_history.Count == 0) return;
			if (k.Keycode == Key.Up)
			{
				if (_historyIdx == -1) _historyIdx = _history.Count - 1;
				else if (_historyIdx > 0) _historyIdx--;
				_input.Text = _history[_historyIdx];
				_input.CaretColumn = _input.Text.Length;
				GetViewport().SetInputAsHandled();
			}
			else if (k.Keycode == Key.Down)
			{
				if (_historyIdx == -1) return;
				_historyIdx++;
				if (_historyIdx >= _history.Count) { _historyIdx = -1; _input.Text = ""; }
				else { _input.Text = _history[_historyIdx]; _input.CaretColumn = _input.Text.Length; }
				GetViewport().SetInputAsHandled();
			}
		}
	}

	/// <summary>LineEdit.TextChanged → Typeahead refreshen. Nur den COMMAND-Token vor dem ersten Space
	/// anschauen — Argumente nach dem Space ignorieren.</summary>
	private void OnInputTextChanged(string newText)
	{
		string token = (newText ?? "").TrimStart();
		int sp = token.IndexOf(' ');
		if (sp >= 0) token = token[..sp];
		if (string.IsNullOrEmpty(token)) { HideSuggestions(); return; }

		_currentSuggestions.Clear();
		string lo = token.ToLowerInvariant();
		var prefixMatches = new List<string>();
		var containsMatches = new List<string>();
		foreach (var name in ConVars.List())
		{
			if (name.StartsWith(lo)) prefixMatches.Add(name);
			else if (name.Contains(lo)) containsMatches.Add(name);
		}
		prefixMatches.Sort(StringComparer.Ordinal);
		containsMatches.Sort(StringComparer.Ordinal);
		foreach (var n in prefixMatches)
		{
			if (_currentSuggestions.Count >= MaxSuggestions) break;
			_currentSuggestions.Add(n);
		}
		foreach (var n in containsMatches)
		{
			if (_currentSuggestions.Count >= MaxSuggestions) break;
			_currentSuggestions.Add(n);
		}

		if (_currentSuggestions.Count == 0) { HideSuggestions(); return; }

		_suggestions.Clear();
		foreach (var name in _currentSuggestions)
		{
			string typ = ConVars.TypeFriendlyName(ConVars.GetFieldType(name) ?? typeof(string));
			string cur = ConVars.Get(name) ?? "?";
			_suggestions.AddItem($"{name}   [{typ}]   = {cur}");
		}
		_suggestions.Select(0);
		// Explizite Höhe — count × row-height + ein bisschen Padding. So muss die VBox kein
		// AutoHeight-Recompute machen und es ist sofort beim ersten Frame sichtbar.
		_suggestions.CustomMinimumSize = new Vector2(0, _currentSuggestions.Count * _suggestionRowHeight + 10);
		_suggestions.Visible = true;
	}

	private void OnSuggestionActivated(long idx)
	{
		if (idx < 0 || idx >= _currentSuggestions.Count) return;
		ApplySuggestion(_currentSuggestions[(int)idx]);
	}

	/// <summary>Schreibt den vollen ConVar-Namen ins LineEdit + Space, ready für den Value. Caret ans
	/// Ende. Schließt die Suggestion-Liste — der User tippt jetzt das Argument, nicht den Command.</summary>
	private void ApplySuggestion(string name)
	{
		_input.Text = name + " ";
		_input.CaretColumn = _input.Text.Length;
		_input.GrabFocus();
		HideSuggestions();
	}

	private void HideSuggestions()
	{
		_currentSuggestions.Clear();
		if (_suggestions != null) { _suggestions.Clear(); _suggestions.Visible = false; }
	}

	private void Execute(string line)
	{
		var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		string cmd = parts[0].ToLowerInvariant();
		string arg = parts.Length > 1 ? parts[1] : "";

		switch (cmd)
		{
			case "help":
				PrintLine("Available:");
				PrintLine("  [color=#aaa]help[/color]              — this list");
				PrintLine("  [color=#aaa]clear[/color]             — clear log");
				PrintLine("  [color=#aaa]echo <text>[/color]       — print text");
				PrintLine("  [color=#aaa]history[/color]           — show input history");
				PrintLine("  [color=#aaa]quit[/color] / [color=#aaa]exit[/color]      — quit game");
				PrintLine("  [color=#aaa]sv_<var> <value>[/color]  — set server ConVar (sent to server)");
				PrintLine("  [color=#aaa]cl_<var> <value>[/color]  — set client ConVar (local)");
				return;
			case "clear":
				_log.Clear();
				return;
			case "echo":
				PrintLine(arg);
				return;
			case "history":
				for (int i = 0; i < _history.Count; i++) PrintLine($"  {i + 1}: {_history[i]}");
				return;
			case "quit":
			case "exit":
				GetTree().Quit();
				return;
		}

		// sv_* → Server-Command via ConVarSyncRequest. Vor dem Senden validieren — sonst landet jede
		// Falscheingabe als verlorenes Roundtrip beim Server. ValidateValue prüft ob arg zum Field-Type
		// passt (z.B. "true" für bool, "1.5" für float).
		if (cmd.StartsWith("sv_"))
		{
			var (ok, typ) = ConVars.ValidateValue(cmd, arg);
			if (typ == "unknown") { PrintLine($"[color=#dd6666]Unknown ConVar: {cmd}[/color]"); return; }
			if (!ok) { PrintLine($"[color=#dd6666]Bad value '{arg}' for {cmd} — expected {typ}.[/color]"); return; }

			var client = NetMain.Instance?.Client;
			if (client == null)
			{
				PrintLine($"[color=#dd6666]sv_*-Command nur im Client-Mode verfügbar (kein NetClient gefunden).[/color]");
				return;
			}
			client.SendConVarSyncRequest(cmd, arg);
			PrintLine($"[color=#aaaa55]→ sent sv-request: {cmd} {arg}[/color]");
			return;
		}

		// cl_* + alles andere: lokale ConVars.TrySet versuchen. Vorher validieren für friendly Errors.
		if (cmd.StartsWith("cl_"))
		{
			var (ok, typ) = ConVars.ValidateValue(cmd, arg);
			if (typ == "unknown") { PrintLine($"[color=#dd6666]Unknown ConVar: {cmd}[/color]"); return; }
			if (!ok) { PrintLine($"[color=#dd6666]Bad value '{arg}' for {cmd} — expected {typ}.[/color]"); return; }
		}

		if (ConVars.TrySet(cmd, arg))
		{
			PrintLine($"[color=#7ace7a]{cmd} = {arg}[/color]");
			return;
		}

		PrintLine($"[color=#dd6666]Unknown command: {cmd}[/color]");
	}

	/// <summary>Hängt eine Zeile ans Log + trimmt auf <see cref="MaxLogLines"/>.</summary>
	public void PrintLine(string bbcode)
	{
		_log.AppendText(bbcode + "\n");
		// Trim: RichTextLabel hat keinen direkten Line-Count getter; via paragraphs.
		while (_log.GetParagraphCount() > MaxLogLines)
			_log.RemoveParagraph(0);
	}
}
