using Godot;
using Idle.Core;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Idle.Game;

public partial class Main
{
    private void PrepareQaFixture()
    {
        // Debug-only fixtures use a brand-new directory, never the player's save.
        foreach (var id in new[] { "world-1", "world-2" })
        {
            var definition = _engine.Definition.Worlds.First(x => x.Id == id); var state = _engine.State.Worlds[id];
            state.LifetimeEarnings = definition.PrestigeScale * 4; state.Balance = state.LifetimeEarnings;
            UiAssert(_engine.Prestige(id), "fixture unlock " + id);
        }
        foreach (var definition in _engine.Definition.Worlds)
        {
            var state = _engine.State.Worlds[definition.Id]; state.LifetimeEarnings = BigValue.FromDouble(1e30); state.Balance = state.LifetimeEarnings;
            var quantities = new[] { 105, 67, 40, 26, 14, 8, 3, 1 };
            for (var i = 0; i < definition.Producers.Count; i++)
            {
                var producer = definition.Producers[i];
                _engine.Buy(definition.Id, producer.Id, quantities[i] - state.Producers[producer.Id].Quantity);
                if (i < 4) _engine.BuyAutomation(definition.Id, producer.Id);
            }
        }
        _engine.SynchronizePoliticalSupport(); _engine.SelectWorld("world-1"); _worldId = "world-1";
        Save(); GD.Print("UI_QA_FIXTURE_READY worlds=3 politics=enabled");
    }

    // Signals dispatch the same production callbacks as input. Time is explicit and deterministic.
    private async void RunUiFlowTest()
    {
        try
        {
            UiAssert(_uiFlowTest && _qaSession && OS.IsDebugBuild(), "isolated debug session");
            GetWindow().Size = new Vector2I(1280, 800);
            var fixture = _engine;
            _engine = new GameEngine(fixture.Definition); _worldId = "world-1"; RebuildWorld();
            await WaitForUiLayout(); CheckBoardBounds();
            UiAssert(_engine.State.Worlds[_worldId].Balance.IsZero && _worldButtons["world-2"].Disabled && !Producer("producer-1").Start.Disabled, "new game locks and first manual cycle");
            Click(Producer("producer-1").Start); _engine.Advance(_engine.GetCycleSeconds(_worldId, "producer-1")); Refresh();
            UiAssert(_engine.State.Worlds[_worldId].Balance > BigValue.Zero && !_engine.State.Worlds[_worldId].Producers["producer-1"].Running, "clickable production bar pays once");
            SaveUiScreenshot("ui-new-game.png");

            // Selective theme changes must not mutate the economy or invalidate the save.
            var presentation = WorldTheme("world-1"); var activity = presentation.Producers.First(x => x.Id == "producer-1");
            var original = (presentation.Name, presentation.AccentColor, activity.Name, activity.IconPath);
            var before = JsonSerializer.Serialize(_engine.State);
            presentation.Name = "Tema alternativo"; presentation.AccentColor = "#3567AB"; activity.Name = "Outro corre"; activity.IconPath = "res://Assets/Brand/icon.svg";
            RebuildWorld(); await WaitForUiLayout();
            UiAssert(_worldTitle.Text == "TEMA ALTERNATIVO" && _worldTitle.GetThemeColor("font_color") == new Color("#3567AB")
                && Producer("producer-1").Name.Text == "Outro corre" && LoadPresentationIcon(activity.IconPath) is not null, "theme names colors icons are data driven");
            UiAssert(before == JsonSerializer.Serialize(_engine.State), "theme switch preserves same state");
            (presentation.Name, presentation.AccentColor, activity.Name, activity.IconPath) = original;

            _engine = fixture; _worldId = "world-1"; RebuildWorld();
            await WaitForUiLayout(); CheckBoardBounds(); SaveUiScreenshot("ui-corre.png");
            GetWindow().Size = new Vector2I(1100, 700); await WaitForUiLayout(); CheckBoardBounds(); SaveUiScreenshot("ui-minimum.png");
            GetWindow().Size = new Vector2I(1280, 800); await WaitForUiLayout();
            Click(_worldButtons["world-2"]); await WaitForUiLayout(); CheckBoardBounds(); SaveUiScreenshot("ui-sindicato.png");
            Click(_worldButtons["world-3"]); await WaitForUiLayout(); CheckBoardBounds(); SaveUiScreenshot("ui-brasilia.png");
            Click(_worldButtons["world-1"]);

            var world = _engine.State.Worlds["world-1"]; var oldQuantity = world.Producers["producer-1"].Quantity;
            Click(_quantityButtons[10]); Click(Producer("producer-1").Buy);
            UiAssert(world.Producers["producer-1"].Quantity == oldQuantity + 10 && _balance.Text == world.Balance.Format(), "buy ten and refresh balance");
            Click(_quantityButtons[100]); Click(Producer("producer-2").Buy);
            UiAssert(world.Producers["producer-2"].Quantity == 167, "buy hundred");
            Click(_quantityButtons[-1]); var max = _engine.GetMaxAffordable("world-1", "producer-8"); var old = world.Producers["producer-8"].Quantity;
            Click(Producer("producer-8").Buy); UiAssert(world.Producers["producer-8"].Quantity == old + max, "buy max through button");
            // Refill only this isolated fixture for subsequent callback checks.
            world.Balance = world.LifetimeEarnings; Refresh(); Click(_quantityButtons[1]);
            Click(Producer("producer-5").Automate); UiAssert(world.Producers["producer-5"].Automated, "automation through card");
            Click(_navButtons["parceiros"]); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-partners.png");
            Click(_partnerButtons["producer-6"]); UiAssert(world.Producers["producer-6"].Automated, "automation through partners menu"); CloseOverlay();

            Click(Producer("producer-3").Name); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-upgrades.png");
            _engine.Advance(_engine.GetCycleSeconds("world-1", "producer-3") * .35); var fraction = world.Producers["producer-3"].CycleProgress;
            var duration = _engine.GetCycleSeconds("world-1", "producer-3");
            Click(_upgradeButtons.First(x => x.Producer == "producer-3" && x.Id == "speed-1").Button);
            UiAssert(world.Producers["producer-3"].PurchasedUpgrades.Contains("speed-1") && _engine.GetCycleSeconds("world-1", "producer-3") < duration
                && Math.Abs(fraction - world.Producers["producer-3"].CycleProgress) < 1e-10, "speed upgrade preserves cycle fraction");
            CloseOverlay(); Click(_navButtons["marcos"]); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-milestones.png"); CloseOverlay();

            Click(_navButtons["prestigio"]); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-prestige.png");
            var points = world.PrestigePoints; var lifetime = world.LifetimeEarnings; var reward = _engine.PreviewPrestige("world-1");
            Click(_prestigeButton); UiAssert(_prestigeDialog.Visible, "prestige confirmation");
            _prestigeDialog.EmitSignal(AcceptDialog.SignalName.Canceled); _prestigeDialog.Hide(); UiAssert(points == world.PrestigePoints, "prestige cancellation preserves progress");
            Click(_prestigeButton); var others = JsonSerializer.Serialize(_engine.State.Worlds["world-2"]);
            _prestigeDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _prestigeDialog.Hide();
            UiAssert(world.PrestigePoints == points + reward && world.LifetimeEarnings == lifetime && world.Balance.IsZero, "prestige grants and resets once");
            UiAssert(world.Producers["producer-1"].Quantity == 1 && world.Producers.Values.All(x => !x.Automated && !x.Running && x.PurchasedUpgrades.Count == 0), "prestige clears run");
            UiAssert(JsonSerializer.Serialize(_engine.State.Worlds["world-2"]) == others && _engine.State.Worlds.Values.All(x => x.Unlocked), "prestige isolation and permanent unlocks");
            world.Balance = BigValue.FromDouble(1000); Refresh(); Click(Producer("producer-1").Automate);
            Click(_worldButtons["world-2"]); var background = world.Balance; _engine.Advance(3); Refresh();
            UiAssert(world.Balance > background && _worldId == "world-2", "unselected world keeps producing");

            await TestPoliticalUi();
            UiAssert(Save(), "save final isolated fixture");
            GD.Print("UI_FLOW_PASS=all_economy_politics_theme_layout_1280x800_1100x700"); GetTree().Quit(0);
        }
        catch (Exception error) { GD.PushError("UI_FLOW_FAIL=" + error); GetTree().Quit(1); }
    }

    private async Task TestPoliticalUi()
    {
        Click(_worldButtons["world-3"]); Click(_navButtons["campanha"]); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-campaign-offer.png");
        Click(_investmentButtons[CampaignInvestment.AllIn]); UiAssert(_engine.PreviewCampaign(_campaignInvestment).WinChance == .75, "all in preview");
        Click(_investmentButtons[CampaignInvestment.Normal]); Click(_startCampaignButton!);
        UiAssert(_campaignConfirm.Visible, "campaign confirms investment"); _campaignConfirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _campaignConfirm.Hide();
        UiAssert(_engine.State.Politics.ActiveCampaign is not null, "campaign launch callback");
        _engine.State.Politics.ActiveCampaign!.RandomSample = .99; // isolated deterministic defeat
        OpenMenu("prestigio"); UiAssert(_prestigeButton.Disabled, "Brasilia prestige blocked during campaign"); CloseOverlay();
        OpenMenu("campanha"); _engine.Advance(10000); Refresh();
        UiAssert(_engine.State.Politics.ActiveCampaign!.PendingDecision && _engine.State.Politics.ActiveCampaign.Choices.Count == 0, "campaign pauses at first choice");
        await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-campaign-decision.png");
        Click(FindButton(_overlayContent, "+10 pontos percentuais")); UiAssert(_engine.State.Politics.ActiveCampaign!.Choices.Count == 1, "base decision callback");
        _engine.Advance(10000); Refresh(); Click(FindButton(_overlayContent, "+5% de produção"));
        UiAssert(_engine.State.Politics.ActiveCampaign!.Choices.Count == 2, "personal decision callback");
        _engine.Advance(10000); Refresh(); UiAssert(_engine.State.Politics.LastResult is { Won: false, Cancelled: false }, "defeat keeps career");
        await WaitForUiLayout(); SaveUiScreenshot("ui-campaign-defeat.png"); Click(FindButton(_overlayContent, "SEGUIR COM A BASE"));

        Click(_startCampaignButton!); _campaignConfirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _campaignConfirm.Hide();
        Click(FindButton(_overlayContent, "CANCELAR CAMPANHA")); UiAssert(_cancelConfirm.Visible, "cancel campaign confirmation");
        _cancelConfirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _cancelConfirm.Hide();
        UiAssert(_engine.State.Politics.LastResult is { Cancelled: true }, "cancel campaign callback");
        Click(FindButton(_overlayContent, "SEGUIR COM A BASE"));

        for (var office = 1; office <= 4; office++)
        {
            Click(_startCampaignButton!); _campaignConfirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _campaignConfirm.Hide();
            _engine.State.Politics.ActiveCampaign!.RandomSample = .01; // isolated deterministic victory
            _engine.Advance(10000); Refresh(); Click(FindButton(_overlayContent, "+10 pontos percentuais"));
            _engine.Advance(10000); Refresh(); Click(FindButton(_overlayContent, "+10 pontos percentuais"));
            _engine.Advance(10000); Refresh(); UiAssert(_engine.State.Politics.CurrentOffice == office && _engine.State.Politics.LastResult is { Won: true }, "elect office " + office);
            await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot(office == 4 ? "ui-presidency.png" : "ui-campaign-victory-" + office + ".png");
            var multiplier = _engine.GetGlobalProductionMultiplier(); _engine.Advance(0); UiAssert(multiplier == _engine.GetGlobalProductionMultiplier(), "political victory applies once");
            Click(FindButton(_overlayContent, office == 4 ? "O CORRE CONTINUA!" : "SEGUIR COM A BASE"));
        }
        UiAssert(_engine.State.Politics.CurrentOffice == 4 && _engine.GetGlobalProductionMultiplier() == BigValue.FromDouble(1.75), "presidency completion and global bonus");
        CloseOverlay(); await WaitForUiLayout(); SaveUiScreenshot("ui-president-board.png");
        GetWindow().Size = new Vector2I(1100, 700); await WaitForUiLayout(); CheckBoardBounds();
        OpenMenu("campanha"); await WaitForUiLayout(); CheckOverlayBounds(); SaveUiScreenshot("ui-presidency-minimum.png");
    }

    private ProducerRow Producer(string id) => _rows.First(x => x.Id == id);
    private static Button FindButton(Node node, string text)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button button && button.Text.Contains(text, StringComparison.Ordinal)) return button;
            var result = FindButtonOrNull(child, text); if (result is not null) return result;
        }
        throw new InvalidOperationException("UI button missing: " + text);
    }
    private static Button? FindButtonOrNull(Node node, string text)
    {
        foreach (var child in node.GetChildren())
        { if (child is Button button && button.Text.Contains(text, StringComparison.Ordinal)) return button; var result = FindButtonOrNull(child, text); if (result is not null) return result; }
        return null;
    }
    private static void UiAssert(bool condition, string scenario)
    { if (!condition) throw new InvalidOperationException("UI flow assertion failed: " + scenario); GD.Print("UI_FLOW_PASS=" + scenario); }
    private static void Click(Button button)
    { if (button.Disabled) throw new InvalidOperationException("UI flow attempted disabled button: " + button.Text); button.EmitSignal(Godot.Button.SignalName.Pressed); }
    private async Task WaitForUiLayout()
    { for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
    private void CheckBoardBounds()
    {
        var visible = GetViewport().GetVisibleRect(); UiAssert(_rows.Count == 8, "eight activities visible");
        foreach (var control in _rows.Select(x => (Control)x.Card).Concat(_worldButtons.Values).Concat(new Control[] { _balance, _rate, _bonus, _portrait }))
        { var rect = control.GetGlobalRect(); UiAssert(rect.Position.X >= -1 && rect.End.X <= visible.Size.X + 1 && rect.Position.Y >= -1 && rect.End.Y <= visible.Size.Y + 1, "board bounds " + control.GetType().Name + " " + GetWindow().Size); }
        foreach (var row in _rows) UiAssert(row.Card.GetGlobalRect().Size.Y >= 110 && row.Buy.GetGlobalRect().Size.X > 90, "usable activity card " + row.Id);
    }
    private void CheckOverlayBounds()
    {
        var visible = GetViewport().GetVisibleRect(); var rect = _overlayContent.GetGlobalRect();
        UiAssert(rect.Position.X >= 0 && rect.End.X <= visible.Size.X + 1 && rect.Position.Y >= 0 && rect.End.Y < visible.Size.Y, "overlay fits " + _menu);
    }
    private void SaveUiScreenshot(string name)
    {
        var path = ProjectSettings.GlobalizePath("res://artifacts/" + name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage(); var result = image.SavePng(path);
        if (result != Error.Ok) throw new IOException("Screenshot failed: " + result); GD.Print("UI_SCREENSHOT=" + path);
    }
}
