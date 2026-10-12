using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using KoaZa.Foreseer.Models;

namespace KoaZa.Foreseer.Services;

public sealed class ApiFootballProvider
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("https://v3.football.api-sports.io/"), Timeout = TimeSpan.FromSeconds(25) };

    private async Task<JsonDocument> GetAsync(string path, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("x-apisports-key", apiKey.Trim());
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"API-Football پاسخ {(int)response.StatusCode}: {body[..Math.Min(220, body.Length)]}");
        var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object && errors.EnumerateObject().Any())
            throw new InvalidOperationException("API-Football: " + errors.ToString());
        return doc;
    }

    public async Task<List<Competition>> GetCompetitionsAsync(string apiKey, CancellationToken ct)
    {
        using var doc = await GetAsync("leagues", apiKey, ct);
        var result = new List<Competition>();
        if (!doc.RootElement.TryGetProperty("response", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            var league = item.GetProperty("league"); var country = item.TryGetProperty("country", out var co) ? co.GetProperty("name").GetString() ?? "" : "";
            var seasons = item.TryGetProperty("seasons", out var se) ? se : default;
            var season = DateTime.UtcNow.Year;
            if (seasons.ValueKind == JsonValueKind.Array)
            {
                var entries = seasons.EnumerateArray().ToList();
                var current = entries.FirstOrDefault(s => s.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.True);
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("year", out var currentYear) && currentYear.TryGetInt32(out var currentValue)) season = currentValue;
                else
                {
                    var years = entries.Select(s => s.TryGetProperty("year", out var y) && y.TryGetInt32(out var n) ? n : 0).Where(y => y > 0).ToList();
                    if (years.Count > 0) season = years.Where(y => y <= DateTime.UtcNow.Year).DefaultIfEmpty(years.Max()).Max();
                }
            }
            result.Add(new Competition { Id=$"apif:{league.GetProperty("id").GetInt32()}:{season}", Name=league.GetProperty("name").GetString() ?? "Unknown", Country=country, Provider="API-Football", ProviderId=league.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture), Season=season });
        }
        return result;
    }

    public async Task<List<Fixture>> GetFixturesAsync(string apiKey, DateTime from, DateTime to, CancellationToken ct)
    {
        var path = $"fixtures?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var doc = await GetAsync(path, apiKey, ct);
        return ParseFixtures(doc);
    }

    public async Task<List<Fixture>> GetLiveFixturesAsync(string apiKey, CancellationToken ct)
    {
        using var doc = await GetAsync("fixtures?live=all", apiKey, ct);
        return ParseFixtures(doc);
    }

    private static List<Fixture> ParseFixtures(JsonDocument doc)
    {
        var result = new List<Fixture>();
        if (!doc.RootElement.TryGetProperty("response", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var fixture = item.GetProperty("fixture"); var league = item.GetProperty("league"); var teams = item.GetProperty("teams"); var goals = item.GetProperty("goals");
                var status = fixture.GetProperty("status"); var home = teams.GetProperty("home"); var away = teams.GetProperty("away");
                var id = fixture.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture); var leagueId = league.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
                var season = league.TryGetProperty("season", out var sy) && sy.TryGetInt32(out var ss) ? ss : DateTime.UtcNow.Year;
                int? hg = goals.TryGetProperty("home", out var hge) && hge.ValueKind == JsonValueKind.Number ? hge.GetInt32() : null;
                int? ag = goals.TryGetProperty("away", out var age) && age.ValueKind == JsonValueKind.Number ? age.GetInt32() : null;
                int? elapsed = status.TryGetProperty("elapsed", out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
                result.Add(new Fixture { Id=$"apif:{id}", Provider="API-Football", ProviderId=id, CompetitionId=$"apif:{leagueId}:{season}", Competition=league.GetProperty("name").GetString() ?? "", Country=league.GetProperty("country").GetString() ?? "", HomeTeam=home.GetProperty("name").GetString() ?? "", AwayTeam=away.GetProperty("name").GetString() ?? "", KickoffUtc=DateTime.Parse(fixture.GetProperty("date").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).ToUniversalTime(), Status=status.GetProperty("short").GetString() ?? "NS", HomeGoals=hg, AwayGoals=ag, Elapsed=elapsed });
            }
            catch (Exception) { /* Ignore malformed individual records; keep the remaining feed usable. */ }
        }
        return result;
    }
}
