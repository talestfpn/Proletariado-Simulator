using Godot;
using Idle.Core;
using Idle.Persistence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Idle.Game;

// The scene presents state and dispatches commands. Economy and politics live in Core.
public partial class Main : Control
{
	private static readonly Color Paper = new("#F4E7C6"), Cream = new("#FFF5D9"), Ink = new("#252523"),
		Brick = new("#BD3B32"), Petrol = new("#245A57"), Mustard = new("#E8AF37"), Muted = new("#746E5B");
	private GameEngine _engine = null!;
	private SaveStore _saves = null!;
	private PresentationDefinition _presentation = new();
	private string _worldId = "world-1", _selectedProducer = "producer-1", _menu = "", _portraitId = "";
	private int _buyAmount = 1;
	private double _refreshTime, _saveTime, _smokeTime;
	private bool _ready, _smoke, _qaSession, _uiFlowTest;
	private Color _accent = Brick;
	private Label _balance = null!, _rate = null!, _bonus = null!, _currency = null!, _worldTitle = null!,
		_worldDescription = null!, _feedback = null!, _saveStatus = null!, _support = null!, _rank = null!;
	private TextureRect _background = null!, _portrait = null!;
	private GridContainer _producerGrid = null!;
	private Control _overlay = null!;
	private VBoxContainer _overlayContent = null!;
	private Label _overlayTitle = null!;
	private Action? _refreshOverlay;
	private ConfirmationDialog _prestigeDialog = null!, _campaignConfirm = null!, _cancelConfirm = null!;
	private AcceptDialog _noticeDialog = null!;
	private Button _prestigeButton = null!;
	private CampaignInvestment _campaignInvestment = CampaignInvestment.Normal;
	private string _campaignSignature = "", _shownResult = "", _lastDecision = "";
	private readonly Dictionary<string, Button> _worldButtons = new(), _navButtons = new();
	private readonly Dictionary<int, Button> _quantityButtons = new();
	private readonly List<ProducerRow> _rows = new();
	private readonly List<(string Producer, string Id, Button Button)> _upgradeButtons = new();
	private readonly Dictionary<string, Button> _partnerButtons = new();

	public override void _Ready()
	{
		try
		{
			var arguments = OS.GetCmdlineUserArgs();
			_smoke = arguments.Contains("--smoke-test");
			_uiFlowTest = OS.IsDebugBuild() && arguments.Contains("--ui-flow-test");
			_qaSession = OS.IsDebugBuild() && (arguments.Contains("--qa-session") || _uiFlowTest);
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var definition = JsonSerializer.Deserialize<GameDefinition>(ReadData("balance.json"), options)
				?? throw new InvalidOperationException("A definição de economia está vazia.");
			_presentation = JsonSerializer.Deserialize<PresentationDefinition>(ReadData("theme.json"), options)
				?? throw new InvalidOperationException("A definição de apresentação está vazia.");
			var directory = ProjectSettings.GlobalizePath(_qaSession ? "user://qa/" + Guid.NewGuid().ToString("N")
				: _smoke ? "user://smoke/" + Guid.NewGuid().ToString("N") : "user://");
			_saves = new SaveStore(directory, definition);
			var loaded = _saves.Load(DateTimeOffset.UtcNow);
			_engine = loaded.Engine;
			_worldId = _engine.State.SelectedWorldId;
			GetTree().AutoAcceptQuit = false;
			GetViewport().GuiEmbedSubwindows = true;
			BuildInterface();
			if (_qaSession && (arguments.Contains("--qa-fixture") || _uiFlowTest)) PrepareQaFixture();
			RebuildWorld();
			_ready = true;
			Refresh();
			if (loaded.OfflineEarnings.Any(x => x.Value > BigValue.Zero))
			{
				var lines = loaded.OfflineEarnings.Where(x => x.Value > BigValue.Zero).Select(x => $"{WorldTheme(x.Key).Name}: +R$ {x.Value.Format()}");
				ShowNotice("O corre não para", "Seus parceiros produziram enquanto você estava fora (até 8 horas):\n\n"
					+ string.Join("\n", lines) + "\n\nA grana já entrou no caixa. Decisões de campanha esperam por você.");
			}
			if (!string.IsNullOrWhiteSpace(loaded.Notice)) Feedback(loaded.Notice, Brick);
			if (_smoke) GD.Print("UI_READY worlds=3 producers=8 retro_politics=ready");
			if (_qaSession) GD.Print("UI_QA_SAVE_DIR=" + directory);
			if (_uiFlowTest) Callable.From(RunUiFlowTest).CallDeferred();
		}
		catch (Exception error) { GD.PushError(error.ToString()); BuildFatalError(error.Message); }
	}

	private static string ReadData(string name)
	{
		using var file = Godot.FileAccess.Open("res://Data/" + name, Godot.FileAccess.ModeFlags.Read);
		return file?.GetAsText() ?? throw new InvalidOperationException("Não foi possível ler Data/" + name);
	}

	public override void _Process(double delta)
	{
		if (!_ready) return;
		if (!_uiFlowTest) _engine.Advance(delta);
		_refreshTime += delta; _saveTime += delta;
		foreach (var row in _rows) row.Progress.Value = _engine.State.Worlds[_worldId].Producers[row.Id].CycleProgress * 100;
		if (_refreshTime >= .1) { _refreshTime = 0; Refresh(); }
		if (_saveTime >= 30) Save();
		if (_smoke && !_qaSession && (_smokeTime += delta) >= 3) { GD.Print("UI_SMOKE_OK"); GetTree().Quit(); }
	}

	public override void _Notification(int what)
	{
		if (what != NotificationWMCloseRequest) return;
		if (!_ready || Save()) GetTree().Quit();
		else ShowNotice("O save deu ruim", "O jogo continua aberto para preservar esta sessão. Tente ‘Salvar agora’ antes de sair. O erro aparece no rodapé.");
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _overlay.Visible)
		{ CloseOverlay(); GetViewport().SetInputAsHandled(); }
	}

	private WorldPresentation WorldTheme(string id) => _presentation.Worlds.FirstOrDefault(x => x.Id == id) ?? new WorldPresentation { Id = id, Name = id };
	private ProducerPresentation ProducerTheme(string id) => WorldTheme(_worldId).Producers.FirstOrDefault(x => x.Id == id)
		?? new ProducerPresentation { Id = id, Name = id, Glyph = id.Split('-').Last() };
	private WorldDefinition WorldDefinition => _engine.Definition.Worlds.First(x => x.Id == _worldId);

	private void BuildInterface()
	{
		Theme = CreateTheme();
		var paper = new ColorRect { Color = Paper, MouseFilter = MouseFilterEnum.Ignore };
		paper.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(paper);
		_background = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Modulate = new Color(1, 1, 1, .17f), MouseFilter = MouseFilterEnum.Ignore };
		_background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_background);
		var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (var edge in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 14);
		AddChild(margin);
		var body = Row(14); margin.AddChild(body); BuildSidebar(body);
		var main = Column(9); main.SizeFlagsHorizontal = main.SizeFlagsVertical = SizeFlags.ExpandFill; body.AddChild(main);
		BuildHeader(main);
		var toolbar = Row(12); main.AddChild(toolbar);
		var titles = Column(0); titles.SizeFlagsHorizontal = SizeFlags.ExpandFill; toolbar.AddChild(titles);
		_worldTitle = Heading("", 27, Ink); titles.AddChild(_worldTitle);
		_worldDescription = Text("", 11, Muted); _worldDescription.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; titles.AddChild(_worldDescription);
		var quantities = Row(3); quantities.SizeFlagsVertical = SizeFlags.ShrinkCenter; toolbar.AddChild(quantities); quantities.AddChild(Text("COMPRAR", 10, Muted));
		foreach (var count in new[] { 1, 10, 100, -1 })
		{
			var amount = count; var button = MakeButton(count < 0 ? "MÁX." : "×" + count, () => { _buyAmount = amount; Refresh(); });
			button.CustomMinimumSize = new Vector2(count < 0 ? 57 : 44, 32); _quantityButtons[count] = button; quantities.AddChild(button);
		}
		_producerGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		_producerGrid.AddThemeConstantOverride("h_separation", 10); _producerGrid.AddThemeConstantOverride("v_separation", 8); main.AddChild(_producerGrid);
		var worlds = Row(8); main.AddChild(worlds);
		foreach (var definition in _engine.Definition.Worlds)
		{
			var id = definition.Id; var button = MakeButton("", () => SelectWorld(id));
			button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new Vector2(0, 46); _worldButtons[id] = button; worlds.AddChild(button);
		}
		_feedback = Text("Começa no café. Termina no palanque. Clique na barra para trabalhar.", 11, Petrol);
		_feedback.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; main.AddChild(_feedback);
		BuildOverlay(); BuildDialogs();
	}

	private void BuildSidebar(HBoxContainer body)
	{
		var sidebar = Panel(Petrol, 11, Ink, 2); sidebar.CustomMinimumSize = new Vector2(192, 0); body.AddChild(sidebar);
		var column = Column(7); sidebar.AddChild(column);
		var titleWords = _presentation.GameTitle.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		column.AddChild(Heading(titleWords.Length > 1 ? string.Join(" ", titleWords[..^1]).ToUpperInvariant() : _presentation.GameTitle.ToUpperInvariant(), 27, Mustard));
		var subtitle = Heading(titleWords.Length > 1 ? titleWords[^1].ToUpperInvariant() : "", 24, Cream); subtitle.HorizontalAlignment = HorizontalAlignment.Right; column.AddChild(subtitle);
		var portraitFrame = Panel(Mustard, 3, Ink, 2); column.AddChild(portraitFrame);
		_portrait = new TextureRect { CustomMinimumSize = new Vector2(0, 174), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore }; portraitFrame.AddChild(_portrait);
		var name = Heading(_presentation.ProtagonistName.ToUpperInvariant(), 22, Cream); name.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(name);
		_rank = Text("TRABALHADOR DA FIRMA", 10, Mustard); _rank.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(_rank);
		var separator = new HSeparator(); separator.AddThemeStyleboxOverride("separator", Line(new Color(1, 1, 1, .2f))); column.AddChild(separator);
		foreach (var (id, title) in new[] { ("atividades", "01   ATIVIDADES"), ("melhorias", "02   MELHORIAS"), ("parceiros", "03   PARCEIROS"),
			("marcos", "04   MARCOS"), ("prestigio", "05   PRESTÍGIO"), ("campanha", "06   CAMPANHA") })
		{
			var key = id; var button = MakeButton(title, () => OpenMenu(key)); button.Alignment = HorizontalAlignment.Left;
			button.CustomMinimumSize = new Vector2(0, 39); button.AddThemeFontSizeOverride("font_size", 12); _navButtons[id] = button; column.AddChild(button);
		}
		column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		var tagline = Wrap(_presentation.Tagline, 11, Cream, 165); tagline.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(tagline);
		_saveStatus = Text("● PROGRESSO LOCAL", 9, Mustard); _saveStatus.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(_saveStatus);
		var save = MakeButton("SALVAR AGORA", () => Save()); save.AddThemeFontSizeOverride("font_size", 10); save.CustomMinimumSize = new Vector2(0, 27); column.AddChild(save);
	}

	private void BuildHeader(VBoxContainer main)
	{
		var panel = Panel(new Color(Cream, .94f), 11, Ink, 2); main.AddChild(panel); var row = Row(18); panel.AddChild(row);
		var money = Column(0); money.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(money);
		_currency = Text("GRANA DO CORRE", 11, Brick); money.AddChild(_currency);
		var moneyRow = Row(8); money.AddChild(moneyRow); var symbol = Heading("R$", 29, Brick); symbol.SizeFlagsVertical = SizeFlags.ShrinkCenter; moneyRow.AddChild(symbol);
		_balance = Heading("0", 49, Ink); _balance.SizeFlagsHorizontal = SizeFlags.ExpandFill; _balance.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; moneyRow.AddChild(_balance);
		var stats = Column(3); stats.CustomMinimumSize = new Vector2(245, 0); stats.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(stats);
		_rate = Text("", 15, Petrol); stats.AddChild(_rate); _bonus = Text("", 12, Muted); stats.AddChild(_bonus); _support = Text("", 12, Brick); stats.AddChild(_support);
	}

	private void RebuildWorld()
	{
		_accent = Color.FromString(WorldTheme(_worldId).AccentColor, Brick); Clear(_producerGrid); _rows.Clear();
		var definitions = WorldDefinition.Producers;
		// Read down each column, while retaining the existing stable producer IDs.
		foreach (var producer in Enumerable.Range(0, definitions.Count).Select(i => definitions[(i % 2) * 4 + i / 2])) BuildProducerCard(producer);
		_background.Texture = LoadPresentationIcon(WorldTheme(_worldId).BackgroundPath);
		_worldTitle.Text = WorldTheme(_worldId).Name.ToUpperInvariant(); _worldTitle.AddThemeColorOverride("font_color", _accent);
		_worldDescription.Text = WorldTheme(_worldId).Description; _portraitId = ""; Refresh();
	}

	private void BuildProducerCard(ProducerDefinition definition)
	{
		var id = definition.Id; var theme = ProducerTheme(id); var panel = Panel(new Color(Cream, .97f), 8, Ink, 2);
		panel.SizeFlagsHorizontal = panel.SizeFlagsVertical = SizeFlags.ExpandFill; panel.CustomMinimumSize = new Vector2(0, 115); _producerGrid.AddChild(panel);
		var content = Column(3); panel.AddChild(content); var top = Row(8); content.AddChild(top);
		var iconFrame = Panel(Paper, 1, Ink, 1); iconFrame.CustomMinimumSize = new Vector2(43, 42); top.AddChild(iconFrame);
		iconFrame.AddChild(new PlaceholderGlyph { Icon = LoadPresentationIcon(theme.IconPath), Glyph = theme.Glyph, Accent = _accent });
		var title = Column(1); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.AddChild(title);
		var name = MakeButton(theme.Name, () => { _selectedProducer = id; OpenMenu("melhorias"); });
		name.Alignment = HorizontalAlignment.Left; name.ClipText = true; name.AddThemeFontSizeOverride("font_size", 14);
		name.AddThemeStyleboxOverride("normal", Box(Colors.Transparent, Colors.Transparent, 0, 0)); name.AddThemeStyleboxOverride("hover", Box(Paper, Colors.Transparent, 0, 0)); title.AddChild(name);
		var payout = Text("", 12, Petrol); title.AddChild(payout);
		var quantity = Heading("", 27, Ink); quantity.HorizontalAlignment = HorizontalAlignment.Right; quantity.CustomMinimumSize = new Vector2(45, 0); top.AddChild(quantity);
		var cycleButton = MakeButton("", () => { if (_engine.StartCycle(_worldId, id)) { Pulse(panel); Refresh(); } });
		cycleButton.CustomMinimumSize = new Vector2(0, 24); cycleButton.ClipContents = true; content.AddChild(cycleButton);
		var progress = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 100, MouseFilter = MouseFilterEnum.Ignore };
		progress.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); progress.AddThemeStyleboxOverride("background", Box(Paper, Colors.Transparent, 0, 0));
		progress.AddThemeStyleboxOverride("fill", Box(Mustard, Colors.Transparent, 0, 0)); cycleButton.AddChild(progress);
		var cycle = Text("", 10, Ink); cycle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); cycle.HorizontalAlignment = HorizontalAlignment.Center;
		cycle.VerticalAlignment = VerticalAlignment.Center; cycleButton.AddChild(cycle);
		var actions = Row(6); content.AddChild(actions);
		var automate = MakeButton("", () => Act(() => _engine.BuyAutomation(_worldId, id), "Parceiro contratado. Agora esse corre anda sozinho.", panel));
		automate.AddThemeFontSizeOverride("font_size", 10); automate.CustomMinimumSize = new Vector2(105, 27); automate.ClipText = true; actions.AddChild(automate);
		var buy = MakeButton("", () => { var count = _buyAmount < 0 ? _engine.GetMaxAffordable(_worldId, id) : _buyAmount;
			Act(() => _engine.Buy(_worldId, id, count), "Mais gente no corre. Agora vai, porra!", panel); }, true);
		buy.AddThemeFontSizeOverride("font_size", 11); buy.SizeFlagsHorizontal = SizeFlags.ExpandFill; buy.CustomMinimumSize = new Vector2(0, 27); buy.ClipText = true; actions.AddChild(buy);
		_rows.Add(new ProducerRow(id, panel, name, quantity, payout, cycleButton, progress, cycle, buy, automate));
	}

	private void SelectWorld(string id)
	{
		if (!_engine.SelectWorld(id)) return;
		_worldId = id; _selectedProducer = "producer-1"; CloseOverlay(); RebuildWorld(); Save(); Feedback(WorldTheme(id).Description, Petrol);
	}

	private void Refresh()
	{
		if (_balance is null || _engine is null) return;
		var state = _engine.State.Worlds[_worldId]; var definition = WorldDefinition;
		_balance.Text = state.Balance.Format(); _currency.Text = WorldTheme(_worldId).CurrencyName.ToUpperInvariant();
		_rate.Text = "+R$ " + _engine.GetProductionPerSecond(_worldId).Format() + " / segundo";
		_bonus.Text = WorldTheme(_worldId).PrestigeName + " ×" + _engine.GetPrestigeMultiplier(_worldId).Format() + "   •   Política ×" + _engine.GetGlobalProductionMultiplier().Format();
		_support.Text = _engine.State.Worlds["world-3"].Unlocked ? "APOIO POLÍTICO  " + _engine.State.Politics.Support.Format() : "DO CORRE AO PODER. UM CICLO POR VEZ.";
		foreach (var (id, button) in _worldButtons)
		{
			var unlocked = _engine.State.Worlds[id].Unlocked; button.Disabled = !unlocked;
			button.Text = (unlocked ? "" : "○  ") + WorldTheme(id).Name.ToUpperInvariant() + "   /   "
				+ (id == _worldId ? "VOCÊ ESTÁ AQUI" : unlocked ? "IR PARA O MUNDO" : "PRESTIGIE O ANTERIOR");
			button.AddThemeFontSizeOverride("font_size", 12); button.AddThemeStyleboxOverride("normal", Box(id == _worldId ? Petrol : Paper, Ink, 2, 7));
			button.AddThemeColorOverride("font_color", id == _worldId ? Cream : Ink);
		}
		foreach (var (count, button) in _quantityButtons) button.AddThemeStyleboxOverride("normal", Box(count == _buyAmount ? Mustard : Cream, Ink, 2, 5));
		foreach (var row in _rows)
		{
			var producer = state.Producers[row.Id]; var rules = definition.Producers.First(x => x.Id == row.Id);
			row.Quantity.Text = "×" + producer.Quantity.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
			row.Payout.Text = "+R$ " + _engine.GetCyclePayout(_worldId, row.Id).Format() + " / ciclo";
			var duration = _engine.GetCycleSeconds(_worldId, row.Id);
			row.Cycle.Text = producer.Quantity == 0 ? "COMPRE PARA COMEÇAR" : producer.Automated ? "NO AUTOMÁTICO  •  " + Seconds(duration * (1 - producer.CycleProgress))
				: producer.Running ? "NO CORRE  •  " + Seconds(duration * (1 - producer.CycleProgress)) : "CLIQUE PARA TRABALHAR  •  " + Seconds(duration);
			row.Start.Disabled = producer.Quantity == 0 || producer.Running; row.Progress.Value = producer.CycleProgress * 100;
			var count = _buyAmount < 0 ? _engine.GetMaxAffordable(_worldId, row.Id) : _buyAmount; var capacity = count > 0 && count <= int.MaxValue - producer.Quantity;
			var cost = capacity ? _engine.GetCost(_worldId, row.Id, count) : BigValue.Zero;
			row.Buy.Text = capacity ? "COMPRAR +" + count + "   R$ " + cost.Format() : count == 0 ? "MÁXIMO: SEM GRANA" : "LIMITE ATINGIDO"; row.Buy.Disabled = !capacity || state.Balance < cost;
			row.Automate.Text = producer.Automated ? "✓ PARCEIRO" : "PARCEIRO  " + rules.AutomationCost.Format();
			row.Automate.Disabled = producer.Automated || producer.Quantity == 0 || state.Balance < rules.AutomationCost;
			row.Automate.TooltipText = ProducerTheme(row.Id).PartnerName + ": " + ProducerTheme(row.Id).PartnerDescription;
			var next = rules.Milestones.FirstOrDefault(x => x.Quantity > producer.Quantity); row.Quantity.TooltipText = next is null ? "Todos os marcos atingidos!" : "Próximo marco: " + next.Quantity + " unidades";
			if (row.LastQuantity >= 0 && producer.Quantity > row.LastQuantity && rules.Milestones.Any(x => x.Quantity > row.LastQuantity && x.Quantity <= producer.Quantity))
			{ Pulse(row.Card); Feedback("MARCO ATINGIDO! " + ProducerTheme(row.Id).Name + " passou de nível.", Brick); }
			if (row.LastRunning && producer.CycleProgress < row.LastProgress && producer.Quantity > 0) Pulse(row.Payout);
			row.LastQuantity = producer.Quantity; row.LastProgress = producer.CycleProgress; row.LastRunning = producer.Running;
		}
		UpdatePortrait();
		foreach (var (id, button) in _navButtons)
		{
			var active = _menu == id || id == "atividades" && !_overlay.Visible;
			button.AddThemeStyleboxOverride("normal", Box(active ? Mustard : new Color("#1C4C49"), active ? Ink : new Color("#49736D"), 2, 7));
			button.AddThemeColorOverride("font_color", active ? Ink : Cream);
		}
		var campaign = _engine.State.Politics.ActiveCampaign;
		_navButtons["campanha"].Text = campaign?.PendingDecision == true ? "06   DECISÃO PENDENTE!" : "06   CAMPANHA";
		if (campaign?.PendingDecision == true)
		{
			var key = campaign.OfficeIndex + ":" + campaign.Choices.Count;
			if (_lastDecision != key) { _lastDecision = key; Save(); Feedback("A base está esperando sua decisão. Abra Campanha.", Brick); }
		}
		else _lastDecision = "";
		_refreshOverlay?.Invoke(); var result = _engine.State.Politics.LastResult;
		if (!_uiFlowTest && result is { Acknowledged: false } && result.Id != _shownResult)
		{ _shownResult = result.Id; Save(); OpenMenu("campanha"); }
	}

	private void UpdatePortrait()
	{
		var politics = _engine.State.Politics; var portrait = politics.CurrentOffice >= 4 ? "president" : _engine.State.Worlds["world-3"].Unlocked ? "candidate"
			: _engine.State.Worlds["world-2"].Unlocked ? "union" : "worker";
		_rank.Text = politics.CurrentOffice > 0 ? OfficeName(politics.CurrentOffice).ToUpperInvariant() : portrait == "candidate" ? "A BASE AGORA TEM CANDIDATO"
			: portrait == "union" ? "VOZ DA CLASSE TRABALHADORA" : "TRABALHADOR DA FIRMA";
		if (_portraitId == portrait) return; _portraitId = portrait;
		var path = _presentation.Portraits.FirstOrDefault(x => x.Id == portrait)?.Path ?? "res://Assets/Art/portrait-" + portrait + ".png"; _portrait.Texture = LoadPresentationIcon(path);
	}

	private void BuildOverlay()
	{
		_overlay = new Control { Visible = false }; _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_overlay);
		var shade = new ColorRect { Color = new Color("#172C29C8"), MouseFilter = MouseFilterEnum.Stop }; shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _overlay.AddChild(shade);
		var center = new CenterContainer(); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _overlay.AddChild(center);
		var panel = Panel(Cream, 18, Ink, 3); panel.CustomMinimumSize = new Vector2(900, 615); center.AddChild(panel);
		var column = Column(12); panel.AddChild(column); var header = Row(12); column.AddChild(header);
		_overlayTitle = Heading("", 32, Brick); _overlayTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(_overlayTitle);
		var close = MakeButton("FECHAR  ×", CloseOverlay); close.CustomMinimumSize = new Vector2(96, 34); header.AddChild(close); column.AddChild(new HSeparator());
		_overlayContent = Column(10); _overlayContent.SizeFlagsVertical = SizeFlags.ExpandFill; column.AddChild(_overlayContent);
		column.AddChild(Text("A produção continua enquanto você faz seus planos.  •  ESC para voltar", 10, Muted));
	}

	private void OpenMenu(string menu)
	{
		if (menu == "atividades") { CloseOverlay(); return; }
		_menu = menu; _overlay.Visible = true; _refreshOverlay = null; Clear(_overlayContent); _upgradeButtons.Clear(); _partnerButtons.Clear();
		_overlayTitle.Text = menu switch { "melhorias" => "MELHORIAS DO CORRE", "parceiros" => "NINGUÉM VENCE SOZINHO", "marcos" => "CADA CONQUISTA CONTA",
			"prestigio" => "A EXPERIÊNCIA FICA", "campanha" => "A BASE CHEGOU AO PODER", _ => menu.ToUpperInvariant() };
		switch (menu)
		{
			case "melhorias": BuildUpgradeMenu(); break; case "parceiros": BuildPartnerMenu(); break; case "marcos": BuildMilestoneMenu(); break;
			case "prestigio": BuildPrestigeMenu(); break; case "campanha": BuildCampaignMenu(); break;
		}
		Refresh();
	}
	private void CloseOverlay() { _overlay.Visible = false; _menu = ""; _refreshOverlay = null; }
	private VBoxContainer OverlayScroll()
	{
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 455), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_overlayContent.AddChild(scroll); var list = Column(8); list.SizeFlagsHorizontal = SizeFlags.ExpandFill; scroll.AddChild(list); return list;
	}

	private void BuildUpgradeMenu()
	{
		_overlayContent.AddChild(Text("Uma boa ideia, uma gambiarra e um orçamento duvidoso.", 13, Muted)); var list = OverlayScroll();
		foreach (var producer in WorldDefinition.Producers.OrderBy(x => x.Id == _selectedProducer ? 0 : 1))
		{
			var presentation = ProducerTheme(producer.Id); var card = Panel(Paper, 10, Ink, 1); list.AddChild(card);
			var column = Column(6); card.AddChild(column); column.AddChild(Heading(presentation.Name.ToUpperInvariant(), 20, Petrol)); var buttons = Row(7); column.AddChild(buttons);
			foreach (var upgrade in producer.Upgrades)
			{
				var theme = presentation.Upgrades.FirstOrDefault(x => x.Id == upgrade.Id); var id = upgrade.Id; var producerId = producer.Id;
				var button = MakeButton("", () => Act(() => _engine.BuyUpgrade(_worldId, producerId, id), "Melhoria aprovada sem licitação emocional."));
				button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new Vector2(0, 65); button.AddThemeFontSizeOverride("font_size", 11); button.ClipText = true;
				button.TooltipText = (theme?.Description ?? "") + "\n" + UpgradeEffect(upgrade); button.SetMeta("label", (theme?.Name ?? "Jeitinho homologado") + "\n" + UpgradeEffect(upgrade));
				buttons.AddChild(button); _upgradeButtons.Add((producerId, id, button));
			}
		}
		_refreshOverlay = () =>
		{
			var world = _engine.State.Worlds[_worldId];
			foreach (var item in _upgradeButtons)
			{
				var state = world.Producers[item.Producer]; var upgrade = WorldDefinition.Producers.First(x => x.Id == item.Producer).Upgrades.First(x => x.Id == item.Id);
				var purchased = state.PurchasedUpgrades.Contains(item.Id);
				item.Button.Text = item.Button.GetMeta("label").AsString() + "\n" + (purchased ? "✓ ADQUIRIDA" : "R$ " + upgrade.Cost.Format());
				item.Button.Disabled = purchased || state.Quantity == 0 || world.Balance < upgrade.Cost;
			}
		};
	}

	private void BuildPartnerMenu()
	{
		_overlayContent.AddChild(Text("Contrate uma vez. Eles repetem os ciclos, inclusive até 8 horas offline.", 13, Muted)); var list = OverlayScroll();
		foreach (var producer in WorldDefinition.Producers)
		{
			var theme = ProducerTheme(producer.Id); var card = Panel(Paper, 10, Ink, 1); list.AddChild(card); var row = Row(12); card.AddChild(row);
			row.AddChild(new PlaceholderGlyph { Icon = LoadPresentationIcon(theme.IconPath), Glyph = theme.Glyph, Accent = Petrol, CustomMinimumSize = new Vector2(52, 52) });
			var info = Column(2); info.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(info); info.AddChild(Heading(theme.PartnerName, 21, Petrol));
			info.AddChild(Text(theme.Name, 11, Brick)); info.AddChild(Wrap(theme.PartnerDescription, 11, Muted, 0));
			var id = producer.Id; var button = MakeButton("", () => Act(() => _engine.BuyAutomation(_worldId, id), "Parceiro na área! O trabalho agora se organiza sozinho."), true);
			button.CustomMinimumSize = new Vector2(155, 48); row.AddChild(button); _partnerButtons[id] = button;
		}
		_refreshOverlay = () =>
		{
			var world = _engine.State.Worlds[_worldId];
			foreach (var (id, button) in _partnerButtons)
			{
				var producer = world.Producers[id]; var cost = WorldDefinition.Producers.First(x => x.Id == id).AutomationCost;
				button.Text = producer.Automated ? "✓ CONTRATADO" : "CONTRATAR\nR$ " + cost.Format(); button.Disabled = producer.Automated || producer.Quantity == 0 || world.Balance < cost;
			}
		};
	}

	private void BuildMilestoneMenu()
	{
		_overlayContent.AddChild(Text("Quantidade também é qualidade. Ao menos nesta planilha.", 13, Muted)); var list = OverlayScroll();
		foreach (var producer in WorldDefinition.Producers)
		{
			var quantity = _engine.State.Worlds[_worldId].Producers[producer.Id].Quantity; var panel = Panel(Paper, 10, Ink, 1); list.AddChild(panel);
			var column = Column(6); panel.AddChild(column); column.AddChild(Heading(ProducerTheme(producer.Id).Name + "   ×" + quantity, 20, Petrol));
			var row = new GridContainer { Columns = 3 }; row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 5); column.AddChild(row);
			foreach (var milestone in producer.Milestones)
			{
				var achieved = quantity >= milestone.Quantity; var tag = Panel(achieved ? Mustard : Cream, 7, Ink, 1); tag.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(tag);
				tag.AddChild(Text((achieved ? "✓ " : "○ ") + milestone.Quantity + " unidades\nRendimento ×" + Number(milestone.ProductionMultiplier) + "  •  Tempo ÷" + Number(milestone.SpeedMultiplier), 11, achieved ? Ink : Muted));
			}
		}
	}

	private void BuildPrestigeMenu()
	{
		var state = _engine.State.Worlds[_worldId]; var theme = WorldTheme(_worldId); var reward = _engine.PreviewPrestige(_worldId);
		_overlayContent.AddChild(Heading(theme.PrestigeName.ToUpperInvariant(), 22, Petrol));
		_overlayContent.AddChild(Wrap("O caixa zera. O aprendizado, não. Recomece este mundo com produção maior e transforme a experiência em vantagem.", 16, Ink, 0));
		var stats = Row(12); _overlayContent.AddChild(stats); var pending = ValuePanel(stats, "VOCÊ LEVA AGORA", "+" + reward.Format(), Brick);
		var total = ValuePanel(stats, "EXPERIÊNCIA ACUMULADA", state.PrestigePoints.Format(), Petrol);
		var next = ValuePanel(stats, "PRODUÇÃO NA PRÓXIMA RUN", "×" + (BigValue.One + (state.PrestigePoints + reward) * _engine.Definition.PrestigeBonusPerPoint).Format(), Petrol);
		var progress = Wrap("", 14, Muted, 0); _overlayContent.AddChild(progress);
		_overlayContent.AddChild(Wrap("Zera: grana deste mundo, atividades, parceiros, melhorias e ciclos.\nFica: experiência, ganhos históricos, cargos, apoio político e mundos liberados.\nOs outros mundos continuam exatamente como estão.", 14, Ink, 0));
		_overlayContent.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		_prestigeButton = MakeButton("RECOMEÇAR COM EXPERIÊNCIA", PreviewPrestige, true); _prestigeButton.CustomMinimumSize = new Vector2(0, 54); _overlayContent.AddChild(_prestigeButton);
		_refreshOverlay = () =>
		{
			var points = _engine.PreviewPrestige(_worldId); pending.Text = "+" + points.Format(); total.Text = state.PrestigePoints.Format();
			next.Text = "×" + (BigValue.One + (state.PrestigePoints + points) * _engine.Definition.PrestigeBonusPerPoint).Format();
			var blocked = _worldId == "world-3" && _engine.State.Politics.ActiveCampaign is not null; _prestigeButton.Disabled = points <= BigValue.Zero || blocked;
			var target = (state.PrestigePoints + BigValue.One) * (state.PrestigePoints + BigValue.One) * WorldDefinition.PrestigeScale;
			progress.Text = blocked ? "Uma campanha está ativa. Conclua ou cancele antes de prestigiar Brasília." : points > BigValue.Zero
				? "Cada ponto acrescenta " + Number(_engine.Definition.PrestigeBonusPerPoint * 100) + "% à produção. O primeiro prestígio libera o próximo mundo."
				: "Próximo ponto: R$ " + state.LifetimeEarnings.Format() + " / R$ " + target.Format() + " em ganhos históricos.";
		};
	}

	private void PreviewPrestige()
	{
		var reward = _engine.PreviewPrestige(_worldId); if (reward <= BigValue.Zero) return;
		_prestigeDialog.DialogText = "Receber +" + reward.Format() + " " + WorldTheme(_worldId).PrestigeName + "?\n\nO saldo, atividades, parceiros, melhorias e ciclos deste mundo serão zerados.\nExperiência, cargos, apoio e mundos liberados permanecem.\nOs outros mundos não mudam.";
		_prestigeDialog.PopupCentered();
	}

	private void BuildCampaignMenu()
	{
		var politics = _engine.State.Politics; _campaignSignature = CampaignSignature(); ResetCampaignControls();
		if (!_engine.State.Worlds["world-3"].Unlocked)
		{
			_overlayContent.AddChild(Heading("PRIMEIRO, CONQUISTE A BASE.", 32, Petrol));
			_overlayContent.AddChild(Wrap("De baixo para cima: faça o primeiro prestígio no Corre para liberar o Sindicato. Depois, prestigie o Sindicato para chegar a Brasília e lançar sua carreira política.", 18, Ink, 0));
			_overlayContent.AddChild(Wrap("Quatro cargos, decisões difíceis e muito santinho no chão. Da vereança à Presidência, sem perder o senso de humor.", 15, Muted, 0));
		}
		else if (politics.LastResult is { Acknowledged: false } result) BuildCampaignResult(result);
		else if (politics.ActiveCampaign is not null) BuildActiveCampaign();
		else if (politics.CurrentOffice >= 4)
		{
			_overlayContent.AddChild(Heading("A FAIXA É SUA. O BOLETO TAMBÉM.", 33, Brick));
			_overlayContent.AddChild(Wrap(_presentation.ProtagonistName + " chegou à Presidência. A base vibra, o gabinete faz reunião e a impressora já está sem toner. Você concluiu o arco político!", 21, Ink, 0));
			_overlayContent.AddChild(Wrap("O país não fecha para balanço: continue produzindo, contratando e prestigiando. Toda sua influência e os bônus conquistados continuam valendo.", 16, Muted, 0));
			_overlayContent.AddChild(Heading("BÔNUS POLÍTICO GLOBAL: ×" + _engine.GetGlobalProductionMultiplier().Format(), 28, Petrol));
		}
		else BuildCampaignOffer();
		_refreshOverlay = () =>
		{
			if (_menu == "campanha" && CampaignSignature() != _campaignSignature) { OpenMenu("campanha"); return; }
			RefreshCampaignValues();
		};
	}
	private Label? _campaignTime, _campaignChance, _campaignSupport, _campaignMoney, _campaignReason;
	private ProgressBar? _campaignProgress;
	private Button? _startCampaignButton;
	private readonly Dictionary<CampaignInvestment, Button> _investmentButtons = new();
	private void ResetCampaignControls() { _campaignTime = _campaignChance = _campaignSupport = _campaignMoney = _campaignReason = null; _campaignProgress = null; _startCampaignButton = null; _investmentButtons.Clear(); }
	private string CampaignSignature()
	{
		var state = _engine.State.Politics; var active = state.ActiveCampaign;
		return state.CurrentOffice + ":" + active?.OfficeIndex + ":" + active?.Choices.Count + ":" + active?.PendingDecision
			+ ":" + state.LastResult?.Id + ":" + state.LastResult?.Acknowledged + ":" + _engine.State.Worlds["world-3"].Unlocked;
	}

	private void BuildCampaignOffer()
	{
		var preview = _engine.PreviewCampaign(_campaignInvestment);
		_overlayContent.AddChild(Heading("PRÓXIMO CARGO: " + preview.Office!.Name.ToUpperInvariant(), 28, Petrol));
		_overlayContent.AddChild(Wrap("A base já sabe trabalhar. Agora precisa aprender a contar voto. Escolha o tamanho da campanha — o caixa e o apoio são cobrados na largada.", 14, Muted, 0));
		var choices = Row(9); _overlayContent.AddChild(choices);
		foreach (var (investment, label, detail) in new[] { (CampaignInvestment.Normal, "NORMAL", "1× de investimento • 55% inicial"),
			(CampaignInvestment.Reinforced, "REFORÇADO", "2× de investimento • 65% inicial"), (CampaignInvestment.AllIn, "TUDO NO PALANQUE", "4× de investimento • 75% inicial") })
		{
			var mode = investment; var button = MakeButton(label + "\n" + detail, () => { _campaignInvestment = mode; OpenMenu("campanha"); });
			button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new Vector2(0, 65); button.AddThemeFontSizeOverride("font_size", 11);
			button.AddThemeStyleboxOverride("normal", Box(investment == _campaignInvestment ? Mustard : Paper, Ink, 2, 7)); choices.AddChild(button); _investmentButtons[investment] = button;
		}
		var cost = Row(12); _overlayContent.AddChild(cost); _campaignMoney = ValuePanel(cost, "VERBA DE BRASÍLIA", "", Brick);
		_campaignSupport = ValuePanel(cost, "APOIO POLÍTICO", "", Petrol); _campaignChance = ValuePanel(cost, "CHANCE INICIAL", "", Petrol);
		_campaignTime = Text("", 13, Ink); _overlayContent.AddChild(_campaignTime);
		_overlayContent.AddChild(Wrap("Duas decisões durante a campanha: +10 pontos de chance OU +5% de produção permanente se vencer. A chance final para em 90%. Derrotas custam o investimento; sua carreira continua.", 13, Muted, 0));
		_campaignReason = Text("", 12, Brick); _overlayContent.AddChild(_campaignReason); _overlayContent.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		_startCampaignButton = MakeButton("LANÇAR CAMPANHA", () =>
		{
			var current = _engine.PreviewCampaign(_campaignInvestment);
			_campaignConfirm.DialogText = "Lançar candidatura a " + current.Office!.Name + "?\n\nR$ " + current.MoneyCost.Format() + " de Brasília\n"
				+ current.SupportCost.Format() + " de Apoio Político\n" + Seconds(current.DurationSeconds) + " de campanha\nChance inicial: " + Number(current.WinChance * 100)
				+ "%\n\nO investimento é pago agora e não é devolvido na derrota ou cancelamento.";
			_campaignConfirm.PopupCentered();
		}, true);
		_startCampaignButton.CustomMinimumSize = new Vector2(0, 49); _overlayContent.AddChild(_startCampaignButton); RefreshCampaignValues();
	}

	private void BuildActiveCampaign()
	{
		var active = _engine.State.Politics.ActiveCampaign!;
		_overlayContent.AddChild(Heading("CAMPANHA PARA " + OfficeName(active.OfficeIndex).ToUpperInvariant(), 28, Petrol));
		_campaignProgress = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0, 20) };
		_campaignProgress.AddThemeStyleboxOverride("background", Box(Paper, Ink, 1, 0)); _campaignProgress.AddThemeStyleboxOverride("fill", Box(Mustard, Colors.Transparent, 1, 0)); _overlayContent.AddChild(_campaignProgress);
		var details = Row(15); _overlayContent.AddChild(details); _campaignTime = Text("", 14, Ink); details.AddChild(_campaignTime); _campaignChance = Text("", 14, Petrol); details.AddChild(_campaignChance);
		if (active.PendingDecision)
		{
			var index = active.Choices.Count + 1; var officeIds = new[] { "", "vereador", "deputado", "governador", "presidente" };
			var eventTheme = _presentation.CampaignEvents.FirstOrDefault(x => x.OfficeId == officeIds[active.OfficeIndex] && x.DecisionIndex == index);
			_overlayContent.AddChild(Heading("DECISÃO " + index + "/2  •  " + (eventTheme?.Title ?? "A BASE QUER SABER"), 25, Brick));
			_overlayContent.AddChild(Wrap(eventTheme?.Description ?? "A assessoria pede um vídeo seu. A base pede presença. O dia tem 24 horas e a reunião já comeu oito. Qual é a prioridade?", 16, Ink, 0));
			var options = Row(12); _overlayContent.AddChild(options);
			var baseChoice = MakeButton((eventTheme?.BaseChoice ?? "FORTALECER A BASE") + "\n\n+10 pontos percentuais de chance", () => ChooseCampaign(CampaignChoice.StrengthenBase), true);
			var imageChoice = MakeButton((eventTheme?.PersonalChoice ?? "PROJETAR A PRÓPRIA IMAGEM") + "\n\n+5% de produção se vencer", () => ChooseCampaign(CampaignChoice.PersonalProjection));
			foreach (var button in new[] { baseChoice, imageChoice }) { button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new Vector2(0, 110); button.AddThemeFontSizeOverride("font_size", 12); options.AddChild(button); }
			_overlayContent.AddChild(Text("A campanha espera pela sua escolha. A economia continua produzindo.", 12, Muted));
		}
		else
		{
			_overlayContent.AddChild(Heading("O PALANQUE ESTÁ ARMADO.", 29, Brick));
			_overlayContent.AddChild(Wrap("O carro de som roda, o café esfria e a equipe garante que ‘essa eleição tá na mão’. Você pode continuar tocando o corre. Avisaremos quando houver uma decisão.", 17, Ink, 0));
			_overlayContent.AddChild(Wrap("Offline, a campanha avança até a próxima decisão pendente. Depois da segunda escolha, a votação pode acontecer durante sua ausência.", 13, Muted, 0));
		}
		_overlayContent.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		var cancel = MakeButton("CANCELAR CAMPANHA", () => _cancelConfirm.PopupCentered()); cancel.CustomMinimumSize = new Vector2(0, 40); _overlayContent.AddChild(cancel); RefreshCampaignValues();
	}

	private void BuildCampaignResult(CampaignResult result)
	{
		var presidential = result.Won && result.OfficeIndex == 4;
		_overlayTitle.Text = presidential ? "DA MARMITA À FAIXA PRESIDENCIAL" : result.Won ? "DEU BASE. DEU VOTO. DEU VITÓRIA!" : result.Cancelled ? "O PALANQUE FOI DESMONTADO" : "NÃO FOI DESTA VEZ, COMPANHEIRO";
		_overlayContent.AddChild(Heading(result.Won ? "ELEITO: " + OfficeName(result.OfficeIndex).ToUpperInvariant() : result.Cancelled ? "CAMPANHA CANCELADA" : "A URNA NÃO PERDOOU", 33, result.Won ? Petrol : Brick));
		var description = presidential ? "O homem saiu do café no sinal e chegou ao Planalto. " + _presentation.ProtagonistName + " veste a faixa, promete ouvir a base e pergunta onde fica o vale-refeição presidencial. Parabéns: você concluiu o arco político!"
			: result.Won ? "O povo escolheu, a base comemorou e a assessoria já abriu um grupo novo. Mais poder para transformar o corre — e mais reunião para ocupar a agenda."
			: result.Cancelled ? "O investimento ficou pelo caminho. Seu cargo, atividades e conquistas continuam com você. Respire e reorganize a próxima tentativa."
			: "A pesquisa dizia uma coisa. A urna mandou outra. O investimento foi gasto, mas seu cargo anterior, seu corre e os bônus conquistados seguem intactos. A próxima campanha já pode começar.";
		if (result.Won)
		{
			var celebration = Row(18); _overlayContent.AddChild(celebration);
			var portraitFrame = Panel(Mustard, 3, Ink, 2); celebration.AddChild(portraitFrame);
			var portraitId = presidential ? "president" : "candidate";
			var image = new TextureRect { CustomMinimumSize = new Vector2(185, 220), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Texture = LoadPresentationIcon(_presentation.Portraits.FirstOrDefault(x => x.Id == portraitId)?.Path ?? ""), MouseFilter = MouseFilterEnum.Ignore };
			portraitFrame.AddChild(image);
			var words = Wrap(description, 20, Ink, 0); words.SizeFlagsHorizontal = SizeFlags.ExpandFill; words.SizeFlagsVertical = SizeFlags.ShrinkCenter; celebration.AddChild(words);
		}
		else _overlayContent.AddChild(Wrap(description, 20, Ink, 0));
		if (result.Won) _overlayContent.AddChild(Heading("+" + Number(result.ProductionBonusGranted * 100) + "% DE PRODUÇÃO EM TODOS OS MUNDOS", 27, Petrol));
		_overlayContent.AddChild(Wrap("Bônus político total: ×" + _engine.GetGlobalProductionMultiplier().Format() + ". O apoio e a carreira permanecem após prestígios.", 14, Muted, 0));
		_overlayContent.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		var button = MakeButton(presidential ? "O CORRE CONTINUA!" : "SEGUIR COM A BASE", () => { _engine.AcknowledgeCampaignResult(); Save(); OpenMenu("campanha"); }, true);
		button.CustomMinimumSize = new Vector2(0, 55); _overlayContent.AddChild(button);
	}

	private void RefreshCampaignValues()
	{
		if (_menu != "campanha") return;
		var active = _engine.State.Politics.ActiveCampaign;
		if (active is not null)
		{
			if (_campaignTime is not null) _campaignTime.Text = active.PendingDecision ? "PAUSADA PARA DECISÃO" : "Votação em " + Seconds(Math.Max(0, active.DurationSeconds - active.ElapsedSeconds));
			if (_campaignChance is not null) _campaignChance.Text = "Chance atual: " + Number(active.WinChance * 100) + "%   •   Bônus na vitória: +" + Number(active.VictoryProductionBonus * 100) + "%";
			if (_campaignProgress is not null) _campaignProgress.Value = active.ElapsedSeconds / active.DurationSeconds * 100;
		}
		else if (_startCampaignButton is not null && GodotObject.IsInstanceValid(_startCampaignButton))
		{
			var preview = _engine.PreviewCampaign(_campaignInvestment); _campaignMoney!.Text = "R$ " + preview.MoneyCost.Format();
			_campaignSupport!.Text = preview.SupportCost.Format(); _campaignChance!.Text = Number(preview.WinChance * 100) + "%";
			_campaignTime!.Text = "Duração: " + Seconds(preview.DurationSeconds) + "   •   Caixa: R$ " + _engine.State.Worlds["world-3"].Balance.Format() + "   •   Seu apoio: " + _engine.State.Politics.Support.Format();
			_startCampaignButton.Disabled = !preview.CanStart; _campaignReason!.Text = preview.CanStart ? "Tudo pronto. A base espera a largada." : preview.BlockReason;
		}
	}
	private void ChooseCampaign(CampaignChoice choice) { if (_engine.ChooseCampaignOption(choice)) { Save(); OpenMenu("campanha"); } }

	private void BuildDialogs()
	{
		_prestigeDialog = new ConfirmationDialog { Title = "Bora começar de novo?", MinSize = new Vector2I(570, 250) };
		_prestigeDialog.GetOkButton().Text = "Recomeçar com bônus"; _prestigeDialog.GetCancelButton().Text = "Ficar nesta run";
		_prestigeDialog.Confirmed += () => { if (_engine.Prestige(_worldId)) { Save(); CloseOverlay(); RebuildWorld(); Feedback("O caixa zerou. A experiência ficou. Bora de novo!", Petrol); } }; AddChild(_prestigeDialog);
		_campaignConfirm = new ConfirmationDialog { Title = "Confirmar lançamento da campanha", MinSize = new Vector2I(560, 300) };
		_campaignConfirm.GetOkButton().Text = "Bota o bloco na rua"; _campaignConfirm.GetCancelButton().Text = "Ainda não";
		_campaignConfirm.Confirmed += () => { if (_engine.StartCampaign(_campaignInvestment)) { Save(); OpenMenu("campanha"); Feedback("A campanha começou. O corre segue produzindo.", Petrol); } }; AddChild(_campaignConfirm);
		_cancelConfirm = new ConfirmationDialog { Title = "Desmontar o palanque?", MinSize = new Vector2I(510, 210), DialogText = "Cancelar encerra esta campanha e perde todo o investimento pago.\nSeu cargo anterior e seus bônus permanecem.\n\nDeseja cancelar?" };
		_cancelConfirm.GetOkButton().Text = "Cancelar e perder investimento"; _cancelConfirm.GetCancelButton().Text = "Continuar campanha";
		_cancelConfirm.Confirmed += () => { if (_engine.CancelCampaign()) { Save(); OpenMenu("campanha"); } }; AddChild(_cancelConfirm);
		_noticeDialog = new AcceptDialog { Title = "O corre não para", MinSize = new Vector2I(550, 280) }; _noticeDialog.GetOkButton().Text = "Bora pro corre"; AddChild(_noticeDialog);
	}

	private void Act(Func<bool> action, string message, Control? animated = null)
	{
		try { if (!action()) { Feedback("A vontade é grande, mas o caixa não acompanhou.", Brick); return; } Save(); Feedback(message, Petrol); if (animated is not null) Pulse(animated); Refresh(); }
		catch (Exception error) { GD.PushError(error.ToString()); Feedback("Não foi possível concluir: " + error.Message, Brick); }
	}
	private bool Save()
	{
		try { _saves.Save(_engine, DateTimeOffset.UtcNow); _saveTime = 0; _saveStatus.Text = "● SALVO ÀS " + DateTime.Now.ToString("HH:mm:ss"); _saveStatus.Modulate = Colors.White; return true; }
		catch (Exception error) { GD.PushError(error.ToString()); _saveStatus.Text = "! FALHA AO SALVAR"; Feedback("O save falhou: " + error.Message, Brick); return false; }
	}
	private void Feedback(string text, Color color) { _feedback.Text = text; _feedback.AddThemeColorOverride("font_color", color); _feedback.TooltipText = text; }
	private void ShowNotice(string title, string text) { _noticeDialog.Title = title; _noticeDialog.DialogText = text; _noticeDialog.PopupCentered(); }
	private void Pulse(Control control) { if (_uiFlowTest) return; control.Modulate = new Color(1.12f, 1.06f, .78f); CreateTween().TweenProperty(control, "modulate", Colors.White, .24); }
	private void BuildFatalError(string message)
	{
		_ready = false; var paper = new ColorRect { Color = Paper }; paper.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(paper);
		var center = new CenterContainer(); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center); center.AddChild(Wrap("O corre não conseguiu começar.\n\n" + message + "\n\nConsulte o console do Godot.", 20, Ink, 650)); if (_smoke) GetTree().Quit(1);
	}

	private static string OfficeName(int office) => office switch { 1 => "Vereador", 2 => "Deputado", 3 => "Governador", 4 => "Presidente", _ => "Trabalhador" };
	private static string UpgradeEffect(UpgradeDefinition definition) => string.Join(" • ", new[] { definition.ProductionMultiplier != 1 ? "Rendimento ×" + Number(definition.ProductionMultiplier) : "", definition.SpeedMultiplier != 1 ? "Tempo ÷" + Number(definition.SpeedMultiplier) : "" }.Where(x => x.Length > 0));
	private static string Number(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
	private static string Seconds(double seconds) => seconds >= 60 ? Math.Ceiling(seconds / 60).ToString("N0") + " min" : Math.Max(0, seconds).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " s";
	private static void Clear(Node parent) { foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
	private static Texture2D? LoadPresentationIcon(string path) { if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal) || !ResourceLoader.Exists(path, "Texture2D")) return null; return GD.Load<Texture2D>(path); }
	private static VBoxContainer Column(int separation) { var node = new VBoxContainer(); node.AddThemeConstantOverride("separation", separation); return node; }
	private static HBoxContainer Row(int separation) { var node = new HBoxContainer(); node.AddThemeConstantOverride("separation", separation); return node; }
	private static Label Text(string value, int size, Color color) { var label = new Label { Text = value, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color); return label; }
	private static Label Heading(string value, int size, Color color) { var label = Text(value, size, color); var font = LoadFont("res://Assets/Fonts/BarlowCondensed.ttf"); if (font is not null) label.AddThemeFontOverride("font", font); return label; }
	private static Label Wrap(string value, int size, Color color, float width) { var label = Text(value, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.CustomMinimumSize = new Vector2(width, 0); return label; }
	private static Font? LoadFont(string path) => ResourceLoader.Exists(path) ? GD.Load<Font>(path) : null;
	private static Label ValuePanel(HBoxContainer row, string caption, string value, Color color)
	{
		var panel = Panel(Paper, 12, Ink, 1); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(panel); var column = Column(5); panel.AddChild(column);
		column.AddChild(Text(caption, 10, Muted)); var label = Heading(value, 31, color); column.AddChild(label); return label;
	}
	private static PanelContainer Panel(Color color, int padding, Color border, int width) { var panel = new PanelContainer(); var style = Box(color, border, 3, padding); style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = width; panel.AddThemeStyleboxOverride("panel", style); return panel; }
	private static StyleBoxLine Line(Color color) => new() { Color = color, Thickness = 1 };
	private static StyleBoxFlat Box(Color color, Color border, int radius, int padding) => new() { BgColor = color, BorderColor = border, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 2, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = padding, ContentMarginTop = padding, ContentMarginRight = padding, ContentMarginBottom = padding };
	private static Button MakeButton(string value, Action action, bool primary = false)
	{
		var button = new Button { Text = value, MouseDefaultCursorShape = CursorShape.PointingHand, FocusMode = FocusModeEnum.All }; button.Pressed += action;
		if (primary) { button.AddThemeStyleboxOverride("normal", Box(Mustard, Ink, 2, 6)); button.AddThemeStyleboxOverride("hover", Box(new Color("#F3C65C"), Ink, 2, 6)); button.AddThemeStyleboxOverride("pressed", Box(new Color("#D39B28"), Ink, 2, 6)); } return button;
	}
	private static Theme CreateTheme()
	{
		var theme = new Theme { DefaultFontSize = 13 }; var font = LoadFont("res://Assets/Fonts/NunitoSans.ttf"); if (font is not null) theme.DefaultFont = font;
		theme.SetColor("font_color", "Label", Ink); theme.SetColor("font_color", "Button", Ink); theme.SetColor("font_hover_color", "Button", Ink); theme.SetColor("font_pressed_color", "Button", Ink);
		theme.SetColor("font_disabled_color", "Button", new Color("#857F6D")); theme.SetStylebox("normal", "Button", Box(Paper, Ink, 2, 6)); theme.SetStylebox("hover", "Button", Box(new Color("#FFF6DB"), Petrol, 2, 6));
		theme.SetStylebox("pressed", "Button", Box(Mustard, Ink, 2, 6)); theme.SetStylebox("disabled", "Button", Box(new Color("#E4D9BD"), new Color("#AEA58F"), 2, 6));
		var focus = Box(Colors.Transparent, Brick, 2, 0); focus.BorderWidthLeft = focus.BorderWidthTop = focus.BorderWidthRight = focus.BorderWidthBottom = 2; theme.SetStylebox("focus", "Button", focus); theme.SetStylebox("panel", "AcceptDialog", Box(Cream, Ink, 2, 18)); return theme;
	}
	private sealed class ProducerRow(string id, PanelContainer card, Button name, Label quantity, Label payout, Button start, ProgressBar progress, Label cycle, Button buy, Button automate)
	{
		public string Id = id; public PanelContainer Card = card; public Button Name = name; public Label Quantity = quantity, Payout = payout, Cycle = cycle;
		public Button Start = start, Buy = buy, Automate = automate; public ProgressBar Progress = progress; public int LastQuantity = -1; public double LastProgress; public bool LastRunning;
	}
}

internal partial class PlaceholderGlyph : Control
{
	public string Glyph { get; init; } = "";
	public Texture2D? Icon { get; init; }
	public Color Accent { get; init; } = new("#245A57");
	public override void _Draw()
	{
		if (Icon is not null) { var dimensions = Icon.GetSize(); var scaled = dimensions * Mathf.Min(Size.X / dimensions.X, Size.Y / dimensions.Y); DrawTextureRect(Icon, new Rect2((Size - scaled) / 2, scaled), false); return; }
		var center = Size / 2; DrawCircle(center, Math.Min(Size.X, Size.Y) * .4f, Accent); var font = ThemeDB.FallbackFont; var text = Glyph.Length > 3 ? Glyph[..3] : Glyph;
		var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 14).X; DrawString(font, center + new Vector2(-width / 2, 5), text, HorizontalAlignment.Left, -1, 14, new Color("#F4E7C6"));
	}
}
