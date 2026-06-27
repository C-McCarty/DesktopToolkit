namespace Toolkit.Common.Catalog;

/// <summary>One installable module as listed in a catalog's <c>catalog.json</c>.</summary>
public sealed class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";

    /// <summary>URL of the module package (a .zip of a deployed module folder).</summary>
    public string Package { get; set; } = "";
}

/// <summary>The catalog document fetched from the configured source URL.</summary>
public sealed class CatalogDocument
{
    public List<CatalogEntry> Modules { get; set; } = new();
}
