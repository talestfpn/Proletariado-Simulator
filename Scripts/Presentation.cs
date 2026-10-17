using System.Collections.Generic;

namespace Idle.Game;

// Presentation IDs point to the economy definitions; this file never changes rules.
public sealed class PresentationDefinition
{
    public string GameTitle { get; set; } = "IDLE / LAB";
    public string Tagline { get; set; } = "Um pequeno começo. Possibilidades infinitas.";
    public List<WorldPresentation> Worlds { get; set; } = new();
}

public sealed class WorldPresentation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrencyName { get; set; } = "recursos";
    public string PrestigeName { get; set; } = "catalisadores";
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
}
