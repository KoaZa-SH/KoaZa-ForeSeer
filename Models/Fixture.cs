namespace KoaZa.Foreseer.Models;

public sealed class Fixture
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string CompetitionId { get; set; } = "";
    public string Competition { get; set; } = "";
    public string Country { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public DateTime KickoffUtc { get; set; }
    public string Status { get; set; } = "NS";
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public int? Elapsed { get; set; }
    public bool IsLive => Status is "1H" or "HT" or "2H" or "ET" or "P" or "LIVE" or "INT" or "BT";
    public string Score => HomeGoals.HasValue && AwayGoals.HasValue ? $"{HomeGoals} - {AwayGoals}" : "vs";
    public string KickoffLocal => KickoffUtc.ToLocalTime().ToString("dd MMM · HH:mm");
    public string StateLabel => IsLive ? (Elapsed.HasValue ? $"زنده · {Elapsed}'" : "زنده") : Status switch { "FT" => "پایان", "PST" => "به تعویق افتاده", "CANC" => "لغو", _ => KickoffLocal };
}
