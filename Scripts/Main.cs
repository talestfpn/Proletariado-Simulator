using Godot;
using Idle.Core;
using Idle.Persistence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Idle.Game;

public partial class Main : Control
{
	private static readonly Color Background = new("#10151d");
	private static readonly Color Surface = new("#18212b");
	private static readonly Color Elevated = new("#202c39");
	private static readonly Color Ink = new("#f1f4f6");
	private static readonly Color Muted = new("#90a0ae");
	private static readonly Color Lime = new("#b7ef83");
	private static readonly Color Orange = new("#ffb77a");
	private GameEngine _engine = null!;
	private SaveStore _saves = null!;
	private PresentationDefinition _presentation = new();
	private string _worldId = "world-1";
	private string _selectedProducer = "producer-1";
	private int _buyAmount = 1;
	private double _refreshTime;
	private double _saveTime;
	private double _smokeTime;
	private bool _smoke;
	private bool _qaSession;
	private bool _uiFlowTest;
	private bool _ready;
	private Color _accent = Lime;
	private Label _balance = null!;
	private Label _rate = null!;
	private Label _bonus = null!;
	private Label _currency = null!;
	private Label _worldTitle = null!;
	private Label _worldDescription = null!;
	private Label _feedback = null!;
	private Label _saveStatus = null!;
	private Label _prestigePoints = null!;
	private Label _prestigePending = null!;
	private Label _prestigeBonus = null!;
	private Label _upgradeTitle = null!;
	private Label _upgradeDescription = null!;
	private Label _milestoneInfo = null!;
	private Button _prestigeButton = null!;
	private VBoxContainer _producerList = null!;
	private VBoxContainer _upgrades = null!;
	private ConfirmationDialog _prestigeDialog = null!;
	private AcceptDialog _noticeDialog = null!;
	private readonly Dictionary<string, Button> _worldButtons = new();
	private readonly Dictionary<int, Button> _quantityButtons = new();
	private readonly List<ProducerRow> _rows = new();
	private readonly List<(string Id, Button Button)> _upgradeButtons = new();

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
			var saveDirectory = ProjectSettings.GlobalizePath(_qaSession
				? "user://qa/" + Guid.NewGuid().ToString("N")
				: _smoke ? "user://smoke/" + Guid.NewGuid().ToString("N") : "user://");
			_saves = new SaveStore(saveDirectory, definition);
			if (_qaSession) GD.Print("UI_QA_SAVE_DIR=" + saveDirectory);
			var loaded = _saves.Load(DateTimeOffset.UtcNow);
			_engine = loaded.Engine;
			_worldId = _engine.State.SelectedWorldId;
			GetTree().AutoAcceptQuit = false;
			if (_uiFlowTest) GetViewport().GuiEmbedSubwindows = true;
			BuildInterface();
			if (_qaSession && (arguments.Contains("--qa-fixture") || _uiFlowTest)) PrepareQaFixture();
			RebuildWorld();
			_ready = true;
			Refresh();
			if (loaded.OfflineEarnings.Any(x => x.Value > BigValue.FromDouble(0)))
			{
				var lines = loaded.OfflineEarnings.Where(x => x.Value > BigValue.FromDouble(0))
					.Select(x => $"{WorldTheme(x.Key).Name}: +{x.Value.Format()} {WorldTheme(x.Key).CurrencyName}");
				ShowNotice("Seus sistemas continuaram trabalhando", "Produção automática durante sua ausência (até 8 horas):\n\n" + string.Join("\n", lines) + "\n\nOs ganhos já foram adicionados.");
			}
			if (!string.IsNullOrWhiteSpace(loaded.Notice))
				Feedback(loaded.Notice, Orange);
			if (_smoke)
				GD.Print("UI_READY worlds=3 producers=8");
			if (_qaSession)
				Feedback("SESSÃO QA ISOLADA  ·  Save de teste em " + saveDirectory, Orange);
			if (_uiFlowTest) Callable.From(RunUiFlowTest).CallDeferred();
		}
		catch (Exception ex)
		{
			GD.PushError(ex.ToString());
			BuildFatalError(ex.Message);
		}
	}

	private static string ReadData(string name)
	{
		var path = "res://Data/" + name;
		using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (file is null) throw new InvalidOperationException($"Não foi possível ler {path}.");
		return file.GetAsText();
	}

	public override void _Process(double delta)
	{
		if (!_ready) return;
		if (!_uiFlowTest) _engine.Advance(delta);
		_refreshTime += delta;
		_saveTime += delta;
		foreach (var row in _rows)
		{
			var state = _engine.State.Worlds[_worldId].Producers[row.Id];
			row.Progress.Value = state.CycleProgress * 100;
		}
		if (_refreshTime >= .1) { _refreshTime = 0; Refresh(); }
		if (_saveTime >= 30) Save();
		if (_smoke && !_qaSession && (_smokeTime += delta) >= 3)
		{
			GD.Print("UI_SMOKE_OK");
			GetTree().Quit();
		}
	}

	public override void _Notification(int what)
	{
		if (what != NotificationWMCloseRequest) return;
		if (!_ready || Save()) GetTree().Quit();
		else ShowNotice("Não foi possível salvar antes de sair", "O jogo continua aberto para preservar o progresso desta sessão.\n\nTente salvar novamente usando ‘Salvar agora’. Depois, feche a janela para sair.\n\nO detalhe da falha aparece no rodapé e no console do Godot.");
	}

	private WorldPresentation WorldTheme(string id) => _presentation.Worlds.FirstOrDefault(x => x.Id == id)
		?? new WorldPresentation { Id = id, Name = "Setor " + id.Split('-').Last() };

	private ProducerPresentation ProducerTheme(string id) => WorldTheme(_worldId).Producers.FirstOrDefault(x => x.Id == id)
		?? new ProducerPresentation { Id = id, Name = "Módulo " + id.Split('-').Last(), Glyph = id.Split('-').Last() };

	private void BuildInterface()
	{
		var background = new ColorRect { Color = Background, MouseFilter = MouseFilterEnum.Ignore };
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(background);
		Theme = CreateTheme();
		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (var edge in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 24);
		AddChild(margin);
		var page = Column(18);
		margin.AddChild(page);

		var header = Row(18);
		page.AddChild(header);
		var brand = Column(3);
		brand.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(brand);
		var titleRow = Row(10);
		brand.AddChild(titleRow);
		titleRow.AddChild(Text(_presentation.GameTitle, 28, Ink));
		titleRow.AddChild(Tag("PROTÓTIPO  /  01", Lime));
		brand.AddChild(Text(_presentation.Tagline, 13, Muted));
		var saveBox = Column(4);
		header.AddChild(saveBox);
		_saveStatus = Text("●  Progresso local", 12, Muted);
		_saveStatus.HorizontalAlignment = HorizontalAlignment.Right;
		saveBox.AddChild(_saveStatus);
		var saveButton = Button("Salvar agora", () => Save());
		saveButton.CustomMinimumSize = new Vector2(120, 32);
		saveBox.AddChild(saveButton);

		var stats = Row(14);
		page.AddChild(stats);
		_balance = Stat(stats, "RECURSOS DISPONÍVEIS", out _currency);
		_rate = Stat(stats, "PRODUÇÃO AUTOMATIZADA", out _);
		_bonus = Stat(stats, "MULTIPLICADOR PERMANENTE", out _);

		var body = Row(16);
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		page.AddChild(body);
		BuildNavigation(body);
		var middle = Column(12);
		middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		middle.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddChild(middle);
		var sectionHeader = Row(12);
		middle.AddChild(sectionHeader);
		var sectionTitles = Column(3);
		sectionTitles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sectionHeader.AddChild(sectionTitles);
		_worldTitle = Text("", 23, Ink);
		sectionTitles.AddChild(_worldTitle);
		_worldDescription = Text("Inicie um ciclo, reinvista e automatize.", 12, Muted);
		sectionTitles.AddChild(_worldDescription);
		var selector = Row(4);
		selector.Alignment = BoxContainer.AlignmentMode.End;
		middle.AddChild(selector);
		selector.AddChild(Text("COMPRAR", 10, Muted));
		foreach (var value in new[] { 1, 10, 100, -1 })
		{
			var captured = value;
			var button = Button(value < 0 ? "Máximo" : "×" + value, () => { _buyAmount = captured; Refresh(); });
			button.CustomMinimumSize = new Vector2(value < 0 ? 70 : 48, 32);
			_quantityButtons[value] = button;
			selector.AddChild(button);
		}
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		middle.AddChild(scroll);
		_producerList = Column(10);
		_producerList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(_producerList);
		BuildDetails(body);
		var footer = Row(10);
		page.AddChild(footer);
		_feedback = Text("Tudo começa com um ciclo. Clique em Iniciar para produzir.", 12, Muted);
		_feedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_feedback.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		footer.AddChild(_feedback);
		footer.AddChild(Text("AUTO SAVE 30s  /  OFFLINE 8h", 10, Muted));

		_prestigeDialog = new ConfirmationDialog { Title = "Começar uma nova jornada", MinSize = new Vector2I(460, 270) };
		_prestigeDialog.GetOkButton().Text = "Reiniciar e receber bônus";
		_prestigeDialog.GetCancelButton().Text = "Continuar nesta run";
		_prestigeDialog.Confirmed += () =>
		{
			if (!_engine.Prestige(_worldId)) return;
			_selectedProducer = "producer-1";
			Save();
			RebuildWorld();
			Feedback("Novo começo! Seu bônus permanente já está ativo.", Lime);
		};
		AddChild(_prestigeDialog);
		_noticeDialog = new AcceptDialog { Title = "Bem-vindo de volta", MinSize = new Vector2I(480, 260) };
		_noticeDialog.GetOkButton().Text = "Continuar";
		AddChild(_noticeDialog);
	}

	private void BuildNavigation(HBoxContainer body)
	{
		var sidebar = Column(10);
		sidebar.CustomMinimumSize = new Vector2(150, 0);
		body.AddChild(sidebar);
		sidebar.AddChild(Text("SEUS MUNDOS", 10, Muted));
		foreach (var world in _engine.Definition.Worlds)
		{
			var id = world.Id;
			var button = Button("", () =>
			{
				if (!_engine.SelectWorld(id)) return;
				_worldId = id;
				_selectedProducer = "producer-1";
				RebuildWorld();
				Save();
			});
			button.Alignment = HorizontalAlignment.Left;
			button.Icon = LoadPresentationIcon(WorldTheme(id).IconPath);
			button.ExpandIcon = true;
			button.AddThemeConstantOverride("icon_max_width", 22);
			button.CustomMinimumSize = new Vector2(150, 64);
			_worldButtons[id] = button;
			sidebar.AddChild(button);
		}
		var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
		sidebar.AddChild(spacer);
		var guide = Column(9);
		sidebar.AddChild(guide);
		guide.AddChild(Text("O RITMO DO JOGO", 10, Lime));
		foreach (var line in new[] { "01  Inicie a produção", "02  Reinvista recursos", "03  Automatize ciclos", "04  Reinicie com bônus" })
			guide.AddChild(Text(line, 12, Muted));
		var hint = Text("Mundos liberados produzem em paralelo. Automação funciona também enquanto você está fora.", 12, Muted);
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.CustomMinimumSize = new Vector2(150, 0);
		sidebar.AddChild(hint);
	}

	private void BuildDetails(HBoxContainer body)
	{
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(246, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		body.AddChild(scroll);
		var details = Column(14);
		details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(details);
		var upgradePanel = Panel(Surface, 16);
		details.AddChild(upgradePanel);
		var content = Column(9);
		upgradePanel.AddChild(content);
		content.AddChild(Text("APERFEIÇOAMENTOS", 10, Lime));
		_upgradeTitle = Text("", 18, Ink);
		_upgradeTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_upgradeTitle);
		_upgradeDescription = Text("", 12, Muted);
		_upgradeDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_upgradeDescription);
		_milestoneInfo = Text("", 12, Orange);
		_milestoneInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_milestoneInfo);
		_upgrades = Column(8);
		content.AddChild(_upgrades);
		var selectionHint = Text("Selecione o nome de um produtor para ver suas melhorias.", 11, Muted);
		selectionHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(selectionHint);

		var prestigePanel = Panel(new Color("#203128"), 16);
		prestigePanel.AddThemeStyleboxOverride("panel", Box(new Color("#203128"), new Color("#3c5541"), 14, 16));
		details.AddChild(prestigePanel);
		var prestige = Column(8);
		prestigePanel.AddChild(prestige);
		prestige.AddChild(Text("PRESTÍGIO  /  PRÓXIMA RUN", 10, Lime));
		_prestigePoints = Text("", 13, Ink);
		prestige.AddChild(_prestigePoints);
		_prestigePending = Text("", 26, Lime);
		prestige.AddChild(_prestigePending);
		_prestigeBonus = Text("", 12, new Color("#b3c6b7"));
		_prestigeBonus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		prestige.AddChild(_prestigeBonus);
		_prestigeButton = Button("Reiniciar com bônus", PreviewPrestige, true);
		_prestigeButton.CustomMinimumSize = new Vector2(0, 42);
		_prestigeButton.TooltipText = "Reinicie este mundo com um bônus permanente de produção. O primeiro reinício libera o próximo mundo. Você confirmará as perdas antes de reiniciar.";
		prestige.AddChild(_prestigeButton);
	}

	private void RebuildWorld()
	{
		_accent = Color.FromString(WorldTheme(_worldId).AccentColor, Lime);
		foreach (var child in _producerList.GetChildren()) { _producerList.RemoveChild(child); child.QueueFree(); }
		_rows.Clear();
		var world = _engine.Definition.Worlds.First(x => x.Id == _worldId);
		foreach (var producer in world.Producers)
		{
			var id = producer.Id;
			var presentation = ProducerTheme(id);
			var card = Panel(Surface, 12);
			_producerList.AddChild(card);
			var main = Column(8);
			card.AddChild(main);
			var top = Row(10);
			main.AddChild(top);
			var icon = new PlaceholderGlyph { Glyph = presentation.Glyph, Icon = LoadPresentationIcon(presentation.IconPath), Accent = _accent, CustomMinimumSize = new Vector2(40, 40) };
			top.AddChild(icon);
			var identity = Column(0);
			identity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			top.AddChild(identity);
			var name = Button(presentation.Name, () => { _selectedProducer = id; RebuildUpgrades(); Refresh(); });
			name.Alignment = HorizontalAlignment.Left;
			name.AddThemeStyleboxOverride("normal", Box(Colors.Transparent, Colors.Transparent, 0, 0));
			name.AddThemeStyleboxOverride("hover", Box(Elevated, Colors.Transparent, 4, 0));
			name.AddThemeFontSizeOverride("font_size", 16);
			name.CustomMinimumSize = new Vector2(0, 24);
			identity.AddChild(name);
			var detail = Text("", 11, Muted);
			identity.AddChild(detail);
			var quantity = Text("", 21, Ink);
			top.AddChild(quantity);
			var cycleRow = Row(9);
			main.AddChild(cycleRow);
			var progress = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0, 10), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
			progress.AddThemeStyleboxOverride("background", Box(new Color("#0d141c"), Colors.Transparent, 5, 0));
			progress.AddThemeStyleboxOverride("fill", Box(_accent, Colors.Transparent, 5, 0));
			cycleRow.AddChild(progress);
			var cycle = Text("", 11, Muted);
			cycle.CustomMinimumSize = new Vector2(62, 0);
			cycle.HorizontalAlignment = HorizontalAlignment.Right;
			cycleRow.AddChild(cycle);
			var start = Button("Iniciar", () => Act(() => _engine.StartCycle(_worldId, id), "Ciclo iniciado."));
			start.CustomMinimumSize = new Vector2(82, 30);
			cycleRow.AddChild(start);
			var actions = Row(8);
			main.AddChild(actions);
			var payout = Text("", 12, _accent);
			payout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			actions.AddChild(payout);
			var buy = Button("", () =>
			{
				var count = _buyAmount < 0 ? _engine.GetMaxAffordable(_worldId, id) : _buyAmount;
				Act(() => _engine.Buy(_worldId, id, count), "Produção ampliada.");
			}, true);
			buy.CustomMinimumSize = new Vector2(130, 32);
			buy.ClipText = true;
			actions.AddChild(buy);
			var automate = Button("", () => Act(() => _engine.BuyAutomation(_worldId, id), "Automação ativada. Este produtor agora trabalha sozinho."));
			automate.CustomMinimumSize = new Vector2(138, 32);
			automate.ClipText = true;
			actions.AddChild(automate);
			_rows.Add(new ProducerRow(id, card, name, detail, quantity, progress, cycle, start, payout, buy, automate));
		}
		_worldTitle.Text = WorldTheme(_worldId).Name;
		RebuildUpgrades();
		Refresh();
	}

	private void RebuildUpgrades()
	{
		foreach (var child in _upgrades.GetChildren()) { _upgrades.RemoveChild(child); child.QueueFree(); }
		_upgradeButtons.Clear();
		var producer = _engine.Definition.Worlds.First(x => x.Id == _worldId).Producers.First(x => x.Id == _selectedProducer);
		_upgradeTitle.Text = ProducerTheme(_selectedProducer).Name;
		_upgradeDescription.Text = ProducerTheme(_selectedProducer).Description;
		foreach (var upgrade in producer.Upgrades)
		{
			var id = upgrade.Id;
			var effects = new List<string>();
			if (upgrade.ProductionMultiplier != 1) effects.Add("Rendimento ×" + Number(upgrade.ProductionMultiplier));
			if (upgrade.SpeedMultiplier != 1) effects.Add("Ciclo ÷" + Number(upgrade.SpeedMultiplier));
			var label = string.Join("  /  ", effects);
			var button = Button(label + "\n" + upgrade.Cost.Format(), () => Act(() => _engine.BuyUpgrade(_worldId, _selectedProducer, id), "Melhoria aplicada."));
			button.CustomMinimumSize = new Vector2(210, 52);
			button.TooltipText = label;
			button.SetMeta("effect", label);
			_upgrades.AddChild(button);
			_upgradeButtons.Add((id, button));
		}
	}

	private void Refresh()
	{
		var world = _engine.State.Worlds[_worldId];
		var definition = _engine.Definition.Worlds.First(x => x.Id == _worldId);
		var presentation = WorldTheme(_worldId);
		_balance.Text = world.Balance.Format();
		_currency.Text = presentation.CurrencyName;
		_rate.Text = _engine.GetProductionPerSecond(_worldId).Format() + " /s";
		_bonus.Text = "×" + _engine.GetPrestigeMultiplier(_worldId).Format();
		_worldDescription.Text = "8 produtores  ·  " + presentation.CurrencyName + "  ·  Todos os mundos ativos em paralelo";
		foreach (var (id, button) in _worldButtons)
		{
			var state = _engine.State.Worlds[id];
			var theme = WorldTheme(id);
			button.Disabled = !state.Unlocked;
			var glyph = button.Icon is not null ? "" : (string.IsNullOrWhiteSpace(theme.Glyph) ? id.Split('-').Last().PadLeft(2, '0') : theme.Glyph) + "  ";
			button.Text = glyph + theme.Name + "\n      " + (state.Unlocked ? id == _worldId ? "EM EXPLORAÇÃO" : "DISPONÍVEL" : "PRIMEIRO REINÍCIO");
			button.AddThemeStyleboxOverride("normal", Box(id == _worldId ? new Color("#263a30") : Surface, id == _worldId ? _accent.Darkened(.45f) : new Color("#283442"), 10, 12));
			button.TooltipText = state.Unlocked ? theme.Name : "Reinicie o mundo anterior uma vez para liberar.";
		}
		foreach (var (value, button) in _quantityButtons)
			button.AddThemeStyleboxOverride("normal", Box(value == _buyAmount ? new Color("#354b35") : Elevated, value == _buyAmount ? _accent.Darkened(.3f) : new Color("#2b3b49"), 6, 6));
		foreach (var row in _rows)
		{
			var state = world.Producers[row.Id];
			var producer = definition.Producers.First(x => x.Id == row.Id);
			row.Quantity.Text = "×" + state.Quantity.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
			var next = producer.Milestones.FirstOrDefault(x => x.Quantity > state.Quantity);
			row.Detail.Text = state.Automated ? "AUTOMATIZADO" : "ACIONAMENTO MANUAL";
			row.Detail.Text += next is null ? "  ·  Todos os marcos atingidos" : "  ·  Próximo marco: " + next.Quantity;
			row.Detail.Modulate = state.Automated ? _accent : Colors.White;
			row.Cycle.Text = Seconds(_engine.GetCycleSeconds(_worldId, row.Id));
			row.Start.Disabled = state.Quantity == 0 || state.Automated || state.Running;
			row.Start.Text = state.Automated ? "Auto" : state.Running ? "Em ciclo" : "Iniciar";
			row.Payout.Text = "+" + _engine.GetCyclePayout(_worldId, row.Id).Format() + "/ciclo";
			int count = _buyAmount < 0 ? _engine.GetMaxAffordable(_worldId, row.Id) : _buyAmount;
			var hasCapacity = count > 0 && count <= int.MaxValue - state.Quantity;
			row.Buy.Text = hasCapacity ? $"+{count}  ·  {_engine.GetCost(_worldId, row.Id, count).Format()}" : count <= 0 ? "Máximo  ·  0" : "Limite de unidades";
			row.Buy.Disabled = !hasCapacity || world.Balance < _engine.GetCost(_worldId, row.Id, count);
			row.Buy.TooltipText = "Comprar " + Math.Max(0, count) + " unidade(s).";
			row.Automate.Text = state.Automated ? "✓ Automatizado" : "Auto  ·  " + producer.AutomationCost.Format();
			row.Automate.Disabled = state.Automated || state.Quantity == 0 || world.Balance < producer.AutomationCost;
			row.Card.AddThemeStyleboxOverride("panel", Box(Surface, _selectedProducer == row.Id ? _accent.Darkened(.55f) : new Color("#283442"), 12, 12));
		}
		var selected = definition.Producers.First(x => x.Id == _selectedProducer);
		var selectedState = world.Producers[_selectedProducer];
		var milestone = selected.Milestones.FirstOrDefault(x => x.Quantity > selectedState.Quantity);
		_milestoneInfo.Text = milestone is null ? "Todos os marcos de quantidade concluídos." : $"MARCO {milestone.Quantity}  ·  Rendimento ×{Number(milestone.ProductionMultiplier)}  /  Ciclo ÷{Number(milestone.SpeedMultiplier)}";
		foreach (var (id, button) in _upgradeButtons)
		{
			var upgrade = selected.Upgrades.First(x => x.Id == id);
			var purchased = selectedState.PurchasedUpgrades.Contains(id);
			button.Disabled = purchased || selectedState.Quantity == 0 || world.Balance < upgrade.Cost;
			button.Text = button.GetMeta("effect").AsString() + "\n" + (purchased ? "✓ Adquirido" : upgrade.Cost.Format() + " " + presentation.CurrencyName);
		}
		var pending = _engine.PreviewPrestige(_worldId);
		_prestigePoints.Text = world.PrestigePoints.Format() + " " + presentation.PrestigeName + " ativos";
		_prestigePending.Text = "+" + pending.Format();
		_prestigeButton.Disabled = pending <= BigValue.FromDouble(0);
		var nextMultiplier = BigValue.One + (world.PrestigePoints + pending) * _engine.Definition.PrestigeBonusPerPoint;
		var nextPoint = world.PrestigePoints + BigValue.One;
		_prestigeBonus.Text = pending > BigValue.FromDouble(0)
			? "Produção após reinício: ×" + nextMultiplier.Format() + "."
			: "Próximo: " + world.LifetimeEarnings.Format() + " / " + (nextPoint * nextPoint * definition.PrestigeScale).Format() + " ganhos totais.";
		_prestigeBonus.TooltipText = "O prestígio considera todos os recursos produzidos, inclusive os já gastos. Cada ponto aumenta permanentemente a produção deste mundo.";
	}

	private void PreviewPrestige()
	{
		var reward = _engine.PreviewPrestige(_worldId);
		if (reward <= BigValue.FromDouble(0)) return;
		var presentation = WorldTheme(_worldId);
		_prestigeDialog.DialogText = $"Receber +{reward.Format()} {presentation.PrestigeName} em {presentation.Name}?\n\nEste mundo perderá o saldo, produtores, melhorias, automações\ne ciclos atuais. Os bônus permanentes e mundos liberados ficam.\nOs outros mundos continuam com seu progresso.\n\nO bônus de produção aumenta com cada ponto recebido.";
		_prestigeDialog.PopupCentered();
	}

	private void Act(Func<bool> action, string feedback)
	{
		try
		{
			if (!action()) { Feedback("Recursos insuficientes ou ação indisponível.", Orange); return; }
			Save();
			Feedback(feedback, _accent);
			Refresh();
		}
		catch (Exception ex) { GD.PushError(ex.ToString()); Feedback("Não foi possível concluir a ação: " + ex.Message, Orange); }
	}

	private bool Save()
	{
		try
		{
			_saves.Save(_engine, DateTimeOffset.UtcNow);
			_saveTime = 0;
			_saveStatus.Text = "●  Salvo às " + DateTime.Now.ToString("HH:mm:ss");
			_saveStatus.Modulate = Lime;
			return true;
		}
		catch (Exception ex)
		{
			GD.PushError(ex.ToString());
			_saveStatus.Text = "!  Falha ao salvar";
			_saveStatus.Modulate = Orange;
			Feedback("Seu progresso continua na sessão, mas o save falhou: " + ex.Message, Orange);
			return false;
		}
	}

	private void Feedback(string message, Color color) { _feedback.Text = message; _feedback.Modulate = color; _feedback.TooltipText = message; }
	private void ShowNotice(string title, string message) { _noticeDialog.Title = title; _noticeDialog.DialogText = message; _noticeDialog.PopupCentered(); }

	private void BuildFatalError(string message)
	{
		_ready = false;
		var panel = new ColorRect { Color = Background };
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(panel);
		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		var text = Text("Não foi possível iniciar o jogo.\n\n" + message + "\n\nConsulte o console do Godot para mais detalhes.", 18, Ink);
		text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		text.CustomMinimumSize = new Vector2(620, 0);
		center.AddChild(text);
		if (_smoke) GetTree().Quit(1);
	}

	private static Texture2D? LoadPresentationIcon(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal) || !ResourceLoader.Exists(path, "Texture2D")) return null;
		try { return GD.Load<Texture2D>(path); }
		catch (Exception error) { GD.PushWarning("Ícone inválido; usando placeholder: " + path + " (" + error.Message + ")"); return null; }
	}

	private static string Number(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
	private static string Seconds(double value) => value >= 60 ? Number(value / 60) + " min" : Number(value) + " s";
	private static VBoxContainer Column(int separation) { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", separation); return box; }
	private static HBoxContainer Row(int separation) { var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", separation); return box; }
	private static Label Text(string text, int size, Color color)
	{
		var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}
	private static Label Tag(string text, Color color)
	{
		var label = Text(text, 9, color);
		label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		return label;
	}
	private static Label Stat(HBoxContainer parent, string heading, out Label sublabel)
	{
		var panel = Panel(Surface, 14);
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(panel);
		var column = Column(3);
		panel.AddChild(column);
		column.AddChild(Text(heading, 10, Muted));
		var row = Row(9);
		column.AddChild(row);
		var value = Text("0", 26, Ink);
		value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(value);
		sublabel = Text("", 12, Muted);
		sublabel.SizeFlagsVertical = SizeFlags.ShrinkEnd;
		row.AddChild(sublabel);
		return value;
	}
	private static PanelContainer Panel(Color color, int padding)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", Box(color, new Color("#283442"), 12, padding));
		return panel;
	}
	private static StyleBoxFlat Box(Color color, Color border, int radius, int padding)
	{
		return new StyleBoxFlat
		{
			BgColor = color, BorderColor = border,
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
			ContentMarginLeft = padding, ContentMarginTop = padding, ContentMarginRight = padding, ContentMarginBottom = padding
		};
	}
	private static Button Button(string text, Action action, bool primary = false)
	{
		var button = new Button { Text = text, MouseDefaultCursorShape = CursorShape.PointingHand, FocusMode = FocusModeEnum.All };
		button.Pressed += action;
		if (primary)
		{
			button.AddThemeStyleboxOverride("normal", Box(new Color("#b7ef83"), new Color("#b7ef83"), 7, 8));
			button.AddThemeStyleboxOverride("hover", Box(new Color("#ceffa4"), new Color("#ceffa4"), 7, 8));
			button.AddThemeStyleboxOverride("pressed", Box(new Color("#8fcb5c"), new Color("#8fcb5c"), 7, 8));
			button.AddThemeColorOverride("font_color", new Color("#1b2a16"));
			button.AddThemeColorOverride("font_hover_color", new Color("#1b2a16"));
			button.AddThemeColorOverride("font_pressed_color", new Color("#1b2a16"));
		}
		return button;
	}
	private static Theme CreateTheme()
	{
		var theme = new Theme { DefaultFontSize = 13 };
		theme.SetColor("font_color", "Label", Ink);
		theme.SetColor("font_color", "Button", Ink);
		theme.SetColor("font_hover_color", "Button", Colors.White);
		theme.SetColor("font_disabled_color", "Button", new Color("#637485"));
		theme.SetStylebox("normal", "Button", Box(Elevated, new Color("#304252"), 7, 8));
		theme.SetStylebox("hover", "Button", Box(new Color("#304051"), new Color("#698675"), 7, 8));
		theme.SetStylebox("pressed", "Button", Box(new Color("#344b3c"), Lime, 7, 8));
		theme.SetStylebox("disabled", "Button", Box(new Color("#17212b"), new Color("#263442"), 7, 8));
		var focus = Box(Colors.Transparent, Lime, 7, 0);
		focus.BorderWidthLeft = focus.BorderWidthTop = focus.BorderWidthRight = focus.BorderWidthBottom = 2;
		theme.SetStylebox("focus", "Button", focus);
		theme.SetStylebox("panel", "AcceptDialog", Box(Surface, new Color("#435748"), 12, 18));
		return theme;
	}

	private sealed record ProducerRow(string Id, PanelContainer Card, Button Name, Label Detail, Label Quantity, ProgressBar Progress, Label Cycle, Button Start, Label Payout, Button Buy, Button Automate);
}

internal partial class PlaceholderGlyph : Control
{
	public string Glyph { get; init; } = "";
	public Texture2D? Icon { get; init; }
	public Color Accent { get; init; } = new("#b7ef83");
	public override void _Draw()
	{
		if (Icon is not null)
		{
			var dimensions = Icon.GetSize();
			var scaled = dimensions * Mathf.Min(Size.X / dimensions.X, Size.Y / dimensions.Y);
			DrawTextureRect(Icon, new Rect2((Size - scaled) / 2, scaled), false);
			return;
		}
		var center = Size / 2;
		DrawCircle(center, 19, new Color(Accent, .09f));
		DrawArc(center, 17, 0, Mathf.Tau, 24, new Color(Accent, .5f), 1, true);
		var points = new[] { center + new Vector2(0, -9), center + new Vector2(9, 0), center + new Vector2(0, 9), center + new Vector2(-9, 0) };
		DrawColoredPolygon(points, new Color(Accent, .25f));
		if (!string.IsNullOrWhiteSpace(Glyph))
		{
			var font = ThemeDB.FallbackFont;
			var symbol = Glyph.Length > 3 ? Glyph[..3] : Glyph;
			var width = font.GetStringSize(symbol, HorizontalAlignment.Left, -1, 11).X;
			DrawString(font, center + new Vector2(-width / 2, 4), symbol, HorizontalAlignment.Left, -1, 11, Accent);
		}
	}
}
