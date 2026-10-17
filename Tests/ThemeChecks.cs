using System.Text.Json.Nodes;
using System.Xml.Linq;
using Idle.Core;

public static class ThemeChecks
{
    public static void Run(GameDefinition definition)
    {
        var theme = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "theme.json")))!;
        var worlds = theme["worlds"]!.AsArray();
        TestHarness.Check(theme["protagonistName"]!.GetValue<string>() == "Zé do Corre" &&
            worlds.Select(world => world!["name"]!.GetValue<string>()).SequenceEqual(new[] { "Corre", "Sindicato", "Brasília" }),
            "Tema: protagonista e jornada autorais, com três mundos nomeados");
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var valid = worlds.Count == definition.Worlds.Count;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var uniqueArt = new HashSet<string>(StringComparer.Ordinal);
        var upgradeCount = 0;
        foreach (var world in definition.Worlds)
        {
            var presentation = worlds.Single(item => item!["id"]!.GetValue<string>() == world.Id)!;
            foreach (var producer in world.Producers)
            {
                var item = presentation["producers"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == producer.Id)!;
                valid &= !string.IsNullOrWhiteSpace(item["partnerName"]?.GetValue<string>()) &&
                         !string.IsNullOrWhiteSpace(item["partnerDescription"]?.GetValue<string>());
                var icon = item["iconPath"]!.GetValue<string>();
                paths.Add(icon);
                var svg = File.ReadAllText(Path.Combine(repoRoot, icon.Replace("res://", "")));
                var document = XDocument.Parse(svg);
                valid &= document.Root?.Name.LocalName == "svg" && document.Root.Attribute("viewBox")?.Value == "0 0 96 96";
                uniqueArt.Add(string.Concat(document.Root!.Elements().Where(element => element.Name.LocalName != "title")));
                var upgrades = item["upgrades"]!.AsArray();
                valid &= upgrades.Count == producer.Upgrades.Count;
                foreach (var upgrade in producer.Upgrades)
                {
                    var copy = upgrades.Single(item => item!["id"]!.GetValue<string>() == upgrade.Id)!;
                    valid &= new[] { "name", "description", "effect" }.All(key => !string.IsNullOrWhiteSpace(copy[key]?.GetValue<string>()));
                    upgradeCount++;
                }
            }
        }
        TestHarness.Check(valid && paths.Count == 24 && uniqueArt.Count == 24 && upgradeCount == 72,
            "Tema: 24 ilustrações SVG distintas e válidas, 24 parceiros e 72 melhorias associados aos IDs econômicos");
        var events = theme["campaignEvents"]!.AsArray();
        TestHarness.Check(events.Count == 8 && definition.Politics!.Offices.All(office =>
            events.Count(item => item!["officeId"]!.GetValue<string>() == office.Id &&
                item["decisionIndex"]!.GetValue<int>() is 1 or 2) == 2),
            "Tema: dois eventos próprios para cada cargo, com escolhas editáveis");
    }
}
