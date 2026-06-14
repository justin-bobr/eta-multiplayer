using Godot;
using System;
using System.Collections.Generic;

namespace Vantix.UI;

/// <summary>
/// Quake-style dev console (toggle hotkey default ^). Routes "sv_*" to server ConVars (via ConVarSync
/// packet), "cl_*" to client ConVars, plus built-ins (echo/help/clear/quit/history); else ConVars.TrySet.
/// ↑/↓ history (max 64); typeahead lists top-10 matching ConVars, Tab completes, Enter selects.
/// </summary>
public partial class ConsoleHud : CanvasLayer
{
	/// <summary>True while the console is open; InputGate reads this to block movement/fire/look during typing.</summary>
	public static bool IsAnyOpen { get; private set; }

	/// <summary>Most recently created ConsoleHud; used by <see cref="NetClient.HandleServerLog"/> to echo
	/// server-broadcast messages into the panel. Cleared in <see cref="_ExitTree"/>.</summary>
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
		PrintLine("[color=#888888]VANTIX Console — type 'help' for commands.[/color]");
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

		if (_history.Count == 0 || _history[_history.Count - 1] != trimmed)
		{
			_history.Add(trimmed);
			if (_history.Count > MaxHistory) _history.RemoveAt(0);
		}

		PrintLine($"[color=#7ec8e3]> {trimmed}[/color]");
		Execute(trimmed);
	}

	/// <summary>↑/↓ scrolls history or navigates the typeahead list when visible; Tab autocompletes; Esc closes it.</summary>
	private void OnInputGuiEvent(InputEvent @event)
	{
		if (@event is not InputEventKey k || !k.Pressed) return;

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

	/// <summary>Refreshes the typeahead from the command token before the first space; ignores arguments.</summary>
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
		_suggestions.CustomMinimumSize = new Vector2(0, _currentSuggestions.Count * _suggestionRowHeight + 10);
		_suggestions.Visible = true;
	}

	private void OnSuggestionActivated(long idx)
	{
		if (idx < 0 || idx >= _currentSuggestions.Count) return;
		ApplySuggestion(_currentSuggestions[(int)idx]);
	}

	/// <summary>Writes the ConVar name plus a trailing space into the LineEdit and closes the suggestion list.</summary>
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

		if (cmd.StartsWith("sv_"))
		{
			var (ok, typ) = ConVars.ValidateValue(cmd, arg);
			if (typ == "unknown") { PrintLine($"[color=#dd6666]Unknown ConVar: {cmd}[/color]"); return; }
			if (!ok) { PrintLine($"[color=#dd6666]Bad value '{arg}' for {cmd} — expected {typ}.[/color]"); return; }

			var client = NetMain.Instance?.Client;
			if (client == null)
			{
				PrintLine($"[color=#dd6666]sv_* commands are only available in client mode (no NetClient found).[/color]");
				return;
			}
			client.SendConVarSyncRequest(cmd, arg);
			PrintLine($"[color=#aaaa55]→ sent sv-request: {cmd} {arg}[/color]");
			return;
		}

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

	/// <summary>Appends a line to the log and trims it to <see cref="MaxLogLines"/>.</summary>
	public void PrintLine(string bbcode)
	{
		_log.AppendText(bbcode + "\n");
		while (_log.GetParagraphCount() > MaxLogLines)
			_log.RemoveParagraph(0);
	}
}
