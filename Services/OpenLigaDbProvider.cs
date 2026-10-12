using System.Net.Http;
using System.Text.Json;
using KoaZa.Foreseer.Models;

namespace KoaZa.Foreseer.Services;

public sealed class OpenLigaDbProvider
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("https://api.openligadb.de/"), Timeout = TimeSpan.FromSeconds(20) };

    public async Task<List<Competition>> GetCompetitionsAsync(CancellationToken ct)
    {
        using var response = await Client.GetAsync("getavailableleagues", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = new List<Competition>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var shortcut = item.TryGetProperty("leagueShortcut", out var sh) ? sh.GetString() : null;
            var name = item.TryGetProperty("leagueName", out var nm) ? nm.GetString() : null;
            if (string.IsNullOrWhiteSpace(shortcut) || string.IsNullOrWhiteSpace(name)) continue;

            var season = item.TryGetProperty("leagueSeason", out var sy) && int.TryParse(sy.ToString(), out var year)
                ? year
                : DateTime.UtcNow.Year;

            // OpenLigaDB's available-leagues response does not reliably expose a country field.
            // Do not infer a country from the league name; leave it blank when the provider omits it.
            result.Add(new Competition
            {
                Id = $"oldb:{shortcut}:{season}",
                Name = name,
                Country = "",
                Provider = "OpenLigaDB",
                ProviderId = shortcut,
                Season = season
            });
        }

        return result;
    }

    public async Task<List<Fixture>> GetFixturesAsync(IEnumerable<Competition> selected, CancellationToken ct)
    {
        var result = new List<Fixture>();
        foreach (var c in selected.Where(x => x.Provider == "OpenLigaDB"))
        {
            try
            {
                using var response = await Client.GetAsync($"getmatchdata/{Uri.EscapeDataString(c.ProviderId)}/{c.Season}", ct);
                if (!response.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var m in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        var matchId = m.GetProperty("matchID").ToString();
                        var dateText = m.GetProperty("matchDateTimeUTC").GetString();
                        if (string.IsNullOrWhiteSpace(dateText)) continue;
                        var home = m.GetProperty("team1");
                        var away = m.GetProperty("team2");
                        var results = m.GetProperty("matchResults");
                        int? hg = null, ag = null;
                        var status = "NS";

                        if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                        {
                            var final = results.EnumerateArray().Last();
                            if (final.TryGetProperty("pointsTeam1", out var p1) && p1.ValueKind == JsonValueKind.Number
                                && final.TryGetProperty("pointsTeam2", out var p2) && p2.ValueKind == JsonValueKind.Number)
                            {
                                hg = p1.GetInt32();
                                ag = p2.GetInt32();
                                status = "FT";
                            }
                        }

                        result.Add(new Fixture
                        {
                            Id = $"oldb:{matchId}",
                            Provider = "OpenLigaDB",
                            ProviderId = matchId,
                            CompetitionId = c.Id,
                            Competition = c.Name,
                            Country = c.Country,
                            HomeTeam = home.GetProperty("teamName").GetString() ?? "",
                            AwayTeam = away.GetProperty("teamName").GetString() ?? "",
                            KickoffUtc = DateTime.Parse(dateText, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
                            Status = status,
                            HomeGoals = hg,
                            AwayGoals = ag
                        });
                    }
                    catch (Exception) { /* Ignore malformed individual records. */ }
                }
            }
            catch (Exception) { /* An unavailable competition must not break other providers. */ }
        }

        return result;
    }
}
