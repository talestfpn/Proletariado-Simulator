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
        // Debug-only entrypoint, with a brand-new save directory for every session.
        foreach (var id in new[] { "world-1", "world-2" })
        {
            var definition = _engine.Definition.Worlds.First(x => x.Id == id);
            var state = _engine.State.Worlds[id];
            state.LifetimeEarnings = definition.PrestigeScale * 4;
            state.Balance = state.LifetimeEarnings;
            if (!_engine.Prestige(id)) throw new InvalidOperationException("Falha ao preparar o prestígio da sessão QA.");
        }
        var first = _engine.Definition.Worlds.First(x => x.Id == "world-1");
        var firstState = _engine.State.Worlds["world-1"];
        firstState.LifetimeEarnings = first.PrestigeScale * 100;
        firstState.Balance = firstState.LifetimeEarnings;
        foreach (var id in new[] { "producer-1", "producer-2" })
        {
            _engine.Buy("world-1", id, 25 - firstState.Producers[id].Quantity);
            _engine.BuyAutomation("world-1", id);
        }
        _engine.SelectWorld("world-1");
        _worldId = "world-1";
        Save();
        GD.Print("UI_QA_FIXTURE_READY worlds=3 prestige=2 pending=" + _engine.PreviewPrestige(_worldId).Format());
    }

    // All UI actions go through production callbacks. Explicit time advancement
    // makes assertions deterministic without sending input to the user's desktop.
    private async void RunUiFlowTest()
    {
        try
        {
            UiAssert(_uiFlowTest && _qaSession && OS.IsDebugBuild(), "isolated debug session");
            GetWindow().Size = new Vector2I(1280, 800);
            var fixtureEngine = _engine;
            _engine = new GameEngine(fixtureEngine.Definition);
            _worldId = _engine.State.SelectedWorldId;
            RebuildWorld();
            Feedback("Tudo começa com um ciclo. Clique em Iniciar para produzir.", Muted);
            await WaitForUiLayout();
            UiAssert(_engine.State.Worlds[_worldId].Balance.IsZero && _worldButtons["world-2"].Disabled && !_rows[0].Start.Disabled, "fresh game balance locks and first cycle");
            CheckHorizontalBounds();
            SaveUiScreenshot("ui-new-game.png");
            _engine = fixtureEngine;
            _worldId = _engine.State.SelectedWorldId;
            RebuildWorld();
            Feedback("Sessão de validação isolada: três mundos disponíveis.", Orange);
            await WaitForUiLayout();
            CheckHorizontalBounds();
            UiAssert(_prestigeButton.GetGlobalRect().End.Y < GetViewport().GetVisibleRect().Size.Y - 16, "prestige action visible without scrolling at 1280x800");
            SaveUiScreenshot("ui-initial.png");
            GetWindow().Size = new Vector2I(1100, 700);
            await WaitForUiLayout();
            CheckHorizontalBounds();
            SaveUiScreenshot("ui-minimum.png");
            GetWindow().Size = new Vector2I(1280, 800);
            await WaitForUiLayout();

            // Change actual presentation objects, then rebuild against the same economy/save.
            var theme = WorldTheme("world-1");
            var producerTheme = theme.Producers.First(x => x.Id == "producer-1");
            var originalTheme = (theme.Name, theme.AccentColor, producerTheme.Name, producerTheme.Glyph, producerTheme.IconPath);
            var stateBeforeTheme = JsonSerializer.Serialize(_engine.State);
            var payoutBeforeTheme = _engine.GetCyclePayout("world-1", "producer-1");
            theme.Name = "Tema de teste";
            theme.AccentColor = "#80bfff";
            producerTheme.Name = "Produtor alternativo";
            producerTheme.Glyph = "[]";
            producerTheme.IconPath = "res://icon.svg";
            RebuildWorld();
            await WaitForUiLayout();
            UiAssert(_worldTitle.Text == theme.Name && _rows[0].Name.Text == producerTheme.Name && LoadPresentationIcon(producerTheme.IconPath) != null,
                "theme names colors and texture load through presentation data");
            UiAssert(JsonSerializer.Serialize(_engine.State) == stateBeforeTheme && _engine.GetCyclePayout("world-1", "producer-1") == payoutBeforeTheme,
                "theme rebuild preserves save and economy");
            (theme.Name, theme.AccentColor, producerTheme.Name, producerTheme.Glyph, producerTheme.IconPath) = originalTheme;
            RebuildWorld();
            await WaitForUiLayout();

            Click(_worldButtons["world-2"]);
            UiAssert(_worldId == "world-2" && _engine.State.SelectedWorldId == "world-2", "select second world");
            UiAssert(_worldTitle.Text == WorldTheme("world-2").Name, "selected world label");
            Click(_worldButtons["world-1"]);
            UiAssert(_worldId == "world-1", "return to first world");

            Click(_quantityButtons[10]);
            var world = _engine.State.Worlds["world-1"];
            var oldQuantity = world.Producers["producer-1"].Quantity;
            var oldBalance = world.Balance;
            Click(_rows.First(x => x.Id == "producer-1").Buy);
            UiAssert(world.Producers["producer-1"].Quantity == oldQuantity + 10 && world.Balance < oldBalance, "buy ten through button");
            UiAssert(_balance.Text == world.Balance.Format(), "balance label refresh");

            // Verify fixed-lot capacity without saving the temporary boundary state.
            var first = world.Producers["producer-1"];
            var quantityBeforeBoundary = first.Quantity;
            first.Quantity = int.MaxValue;
            Refresh();
            UiAssert(_rows.First(x => x.Id == "producer-1").Buy.Disabled, "fixed lot at integer capacity");
            first.Quantity = quantityBeforeBoundary;
            Refresh();

            var thirdRow = _rows.First(x => x.Id == "producer-3");
            Click(thirdRow.Buy);
            UiAssert(world.Producers["producer-3"].Quantity == 10, "buy new producer");
            Click(thirdRow.Automate);
            UiAssert(world.Producers["producer-3"].Automated && world.Producers["producer-3"].Running, "automate through button");
            UiAssert(thirdRow.Automate.Disabled && thirdRow.Automate.Text.Contains("Automatizado"), "automation label and disabled state");

            Click(thirdRow.Name);
            UiAssert(_selectedProducer == "producer-3" && _upgradeTitle.Text == ProducerTheme("producer-3").Name, "select producer improvements");
            var durationBefore = _engine.GetCycleSeconds("world-1", "producer-3");
            _engine.Advance(durationBefore * .35);
            Refresh();
            var progressBefore = world.Producers["producer-3"].CycleProgress;
            var speedId = _engine.Definition.Worlds.First(x => x.Id == "world-1").Producers.First(x => x.Id == "producer-3").Upgrades.First(x => x.SpeedMultiplier > 1).Id;
            Click(_upgradeButtons.First(x => x.Id == speedId).Button);
            UiAssert(world.Producers["producer-3"].PurchasedUpgrades.Contains(speedId), "buy speed improvement through button");
            UiAssert(Math.Abs(progressBefore - world.Producers["producer-3"].CycleProgress) < 1e-10 && _engine.GetCycleSeconds("world-1", "producer-3") < durationBefore, "speed keeps cycle fraction");

            var pointsBefore = world.PrestigePoints;
            var pending = _engine.PreviewPrestige("world-1");
            Click(_prestigeButton);
            UiAssert(_prestigeDialog.Visible, "prestige preview opens confirmation");
            // Dialog cancellation also closes its native Window. Emitting a Button
            // signal alone bypasses that native action, so reproduce both parts of
            // the dialog contract instead of changing production cancellation.
            _prestigeDialog.EmitSignal(AcceptDialog.SignalName.Canceled);
            UiAssert(world.PrestigePoints == pointsBefore, "cancel prestige keeps points");
            _prestigeDialog.Hide();
            await WaitForUiLayout();
            UiAssert(!_prestigeDialog.Visible, "canceled prestige dialog closes");
            UiAssert(world.PrestigePoints == pointsBefore, "cancel prestige keeps progress");
            Click(_prestigeButton);
            await WaitForUiLayout();
            SaveUiScreenshot("ui-prestige.png");
            var secondBefore = JsonSerializer.Serialize(_engine.State.Worlds["world-2"]);
            var thirdBefore = JsonSerializer.Serialize(_engine.State.Worlds["world-3"]);
            var lifetimeBefore = world.LifetimeEarnings;
            _prestigeDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            _prestigeDialog.Hide();
            UiAssert(world.PrestigePoints == pointsBefore + pending && world.LifetimeEarnings == lifetimeBefore && world.Balance.IsZero, "confirm prestige resets run and grants points");
            UiAssert(world.Producers["producer-1"].Quantity == 1 && world.Producers.Values.All(x => !x.Automated && !x.Running && x.PurchasedUpgrades.Count == 0), "prestige resets producers and automation");
            UiAssert(JsonSerializer.Serialize(_engine.State.Worlds["world-2"]) == secondBefore && JsonSerializer.Serialize(_engine.State.Worlds["world-3"]) == thirdBefore, "prestige preserves other worlds");
            UiAssert(_engine.State.Worlds.Values.All(x => x.Unlocked) && _prestigeButton.Disabled, "unlocks persist and repeated prestige disabled");

            world.Balance = BigValue.FromDouble(1000);
            Refresh();
            Click(_quantityButtons[1]);
            Click(_rows.First(x => x.Id == "producer-1").Buy);
            Click(_rows.First(x => x.Id == "producer-1").Automate);
            Click(_worldButtons["world-2"]);
            var backgroundBefore = world.Balance;
            var second = _engine.State.Worlds["world-2"];
            var secondBalanceBefore = second.Balance;
            Click(_rows.First(x => x.Id == "producer-1").Start);
            UiAssert(second.Producers["producer-1"].Running, "start manual cycle through button");
            _engine.Advance(Math.Max(_engine.GetCycleSeconds("world-1", "producer-1"), _engine.GetCycleSeconds("world-2", "producer-1")) + .1);
            Refresh();
            UiAssert(world.Balance > backgroundBefore && _worldId == "world-2", "unselected world produces in background");
            UiAssert(second.Balance > secondBalanceBefore && !second.Producers["producer-1"].Running, "manual cycle completes and stops");
            UiAssert(_balance.Text == second.Balance.Format(), "visible balance follows selected world");
            UiAssert(Save(), "save final isolated UI fixture");
            GD.Print("UI_FLOW_PASS=all_callbacks_layout_1280x800_1100x700_prestige_background_speed");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("UI_FLOW_FAIL=" + error);
            GetTree().Quit(1);
        }
    }

    private static void UiAssert(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException("UI flow assertion failed: " + scenario);
        GD.Print("UI_FLOW_PASS=" + scenario);
    }

    private static void Click(Button button)
    {
        if (button.Disabled) throw new InvalidOperationException("UI flow attempted a disabled button: " + button.Text);
        button.EmitSignal(Godot.Button.SignalName.Pressed);
    }

    private async Task WaitForUiLayout()
    {
        for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private void CheckHorizontalBounds()
    {
        var width = GetViewport().GetVisibleRect().Size.X;
        foreach (var value in new[] { _balance, _rate, _bonus })
            UiAssert(!string.IsNullOrWhiteSpace(value.Text) && value.GetGlobalRect().Size.X > 30, "stat value has visible width and text");
        var controls = _worldButtons.Values.Cast<Control>().Concat(new Control[] { _balance, _bonus, _prestigeButton, _rows[0].Buy, _rows[0].Automate });
        foreach (var control in controls)
        {
            var rect = control.GetGlobalRect();
            UiAssert(rect.Position.X >= -1 && rect.End.X <= width + 1, "horizontal bounds " + control.GetType().Name + " at " + GetWindow().Size);
        }
    }

    private void SaveUiScreenshot(string fileName)
    {
        var path = ProjectSettings.GlobalizePath("res://artifacts/" + fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        var result = image.SavePng(path);
        if (result != Error.Ok) throw new IOException("Screenshot failed: " + result);
        GD.Print("UI_SCREENSHOT=" + path);
    }
}
