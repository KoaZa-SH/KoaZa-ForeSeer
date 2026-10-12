namespace KoaZa.Foreseer.Models;

public sealed class AppSettings
{
    public string ApiFootballKey { get; set; } = "";
    public bool EnableApiFootball { get; set; } = true;
    public bool EnableOpenLigaDb { get; set; } = true;
    public int RefreshMinutes { get; set; } = 20;
    public List<string> SelectedCompetitionIds { get; set; } = new();
    public DateTime? LastCatalogRefreshUtc { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public DateTime? LastFixturesRefreshUtc { get; set; }
}
