using System.Collections.Generic;

namespace Idle.Game;

// Presentation IDs point to the economy definitions; this file never changes rules.
public sealed class PresentationDefinition
{
    public string GameTitle { get; set; } = "Proletariado Simulator";
    public string Tagline { get; set; } = "Do corre ao poder. A contradição é sua.";
    public string ProtagonistName { get; set; } = "Zé do Corre";
    public List<PortraitPresentation> Portraits { get; set; } = new();
    public List<CampaignEventPresentation> CampaignEvents { get; set; } = new();
    public List<WorldPresentation> Worlds { get; set; } = new();
}

public sealed class WorldPresentation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrencyName { get; set; } = "recursos";
    public string PrestigeName { get; set; } = "catalisadores";
    public string Description { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public string AccentColor { get; set; } = "#b7ef83";
    public string Glyph { get; set; } = "";
    public string IconPath { get; set; } = "";
    public List<ProducerPresentation> Producers { get; set; } = new();
}

public sealed class ProducerPresentation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string PartnerName { get; set; } = "Parceiro do corre";
    public string PartnerDescription { get; set; } = "Repete a atividade automaticamente, inclusive offline.";
    public List<UpgradePresentation> Upgrades { get; set; } = new();
}

public sealed class PortraitPresentation
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class UpgradePresentation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Effect { get; set; } = "";
}

public sealed class CampaignEventPresentation
{
    public string OfficeId { get; set; } = "";
    public int DecisionIndex { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string BaseChoice { get; set; } = "Fortalecer a base";
    public string PersonalChoice { get; set; } = "Investir na própria imagem";
}
