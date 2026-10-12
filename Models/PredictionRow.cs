namespace KoaZa.Foreseer.Models;

public sealed class PredictionRow
{
    public string Market { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Probability { get; set; } = "—";
    public string Confidence { get; set; } = "دادهٔ ناکافی";
    public string Note { get; set; } = "";
}
