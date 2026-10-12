using KoaZa.Foreseer.Models;
using Microsoft.Data.Sqlite;

namespace KoaZa.Foreseer.Services;

public sealed class FixtureRepository
{
    private readonly string _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KoaZa", "Foreseer", "foreseer.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS competitions (
 id TEXT PRIMARY KEY, name TEXT NOT NULL, country TEXT NOT NULL, provider TEXT NOT NULL,
 provider_id TEXT NOT NULL, season INTEGER NOT NULL, is_selected INTEGER NOT NULL DEFAULT 0,
 UNIQUE(provider, provider_id, season));
CREATE TABLE IF NOT EXISTS fixtures (
 id TEXT PRIMARY KEY, provider TEXT NOT NULL, provider_id TEXT NOT NULL, competition_id TEXT NOT NULL,
 competition TEXT NOT NULL, country TEXT NOT NULL, home_team TEXT NOT NULL, away_team TEXT NOT NULL,
 kickoff_utc TEXT NOT NULL, status TEXT NOT NULL, home_goals INTEGER NULL, away_goals INTEGER NULL,
 elapsed INTEGER NULL, updated_utc TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_fixtures_kickoff ON fixtures(kickoff_utc);
";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveCompetitionsAsync(IEnumerable<Competition> competitions)
    {
        await using var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        foreach (var c in competitions)
        {
            var cmd = connection.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"INSERT INTO competitions(id,name,country,provider,provider_id,season,is_selected)
VALUES($id,$name,$country,$provider,$providerId,$season,$selected)
ON CONFLICT(provider,provider_id,season) DO UPDATE SET name=excluded.name,country=excluded.country,is_selected=excluded.is_selected;";
            cmd.Parameters.AddWithValue("$id", c.Id); cmd.Parameters.AddWithValue("$name", c.Name);
            cmd.Parameters.AddWithValue("$country", c.Country); cmd.Parameters.AddWithValue("$provider", c.Provider);
            cmd.Parameters.AddWithValue("$providerId", c.ProviderId); cmd.Parameters.AddWithValue("$season", c.Season);
            cmd.Parameters.AddWithValue("$selected", c.IsSelected ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<Competition>> GetCompetitionsAsync()
    {
        var result = new List<Competition>();
        await using var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,name,country,provider,provider_id,season,is_selected FROM competitions ORDER BY country,name;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new Competition { Id=reader.GetString(0), Name=reader.GetString(1), Country=reader.GetString(2), Provider=reader.GetString(3), ProviderId=reader.GetString(4), Season=reader.GetInt32(5), IsSelected=reader.GetInt32(6) == 1 });
        return result;
    }

    public async Task SaveFixturesAsync(IEnumerable<Fixture> fixtures)
    {
        await using var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        foreach (var f in fixtures)
        {
            var cmd = connection.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"INSERT INTO fixtures(id,provider,provider_id,competition_id,competition,country,home_team,away_team,kickoff_utc,status,home_goals,away_goals,elapsed,updated_utc)
VALUES($id,$provider,$providerId,$competitionId,$competition,$country,$home,$away,$kickoff,$status,$hg,$ag,$elapsed,$updated)
ON CONFLICT(id) DO UPDATE SET status=excluded.status,home_goals=excluded.home_goals,away_goals=excluded.away_goals,elapsed=excluded.elapsed,updated_utc=excluded.updated_utc;";
            cmd.Parameters.AddWithValue("$id", f.Id); cmd.Parameters.AddWithValue("$provider", f.Provider);
            cmd.Parameters.AddWithValue("$providerId", f.ProviderId); cmd.Parameters.AddWithValue("$competitionId", f.CompetitionId);
            cmd.Parameters.AddWithValue("$competition", f.Competition); cmd.Parameters.AddWithValue("$country", f.Country);
            cmd.Parameters.AddWithValue("$home", f.HomeTeam); cmd.Parameters.AddWithValue("$away", f.AwayTeam);
            cmd.Parameters.AddWithValue("$kickoff", f.KickoffUtc.ToUniversalTime().ToString("O")); cmd.Parameters.AddWithValue("$status", f.Status);
            cmd.Parameters.AddWithValue("$hg", (object?)f.HomeGoals ?? DBNull.Value); cmd.Parameters.AddWithValue("$ag", (object?)f.AwayGoals ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$elapsed", (object?)f.Elapsed ?? DBNull.Value); cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<Fixture>> GetFixturesAsync(DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<Fixture>();
        await using var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,provider,provider_id,competition_id,competition,country,home_team,away_team,kickoff_utc,status,home_goals,away_goals,elapsed FROM fixtures WHERE kickoff_utc >= $from AND kickoff_utc <= $to ORDER BY kickoff_utc;";
        cmd.Parameters.AddWithValue("$from", fromUtc.ToString("O")); cmd.Parameters.AddWithValue("$to", toUtc.ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new Fixture { Id=reader.GetString(0), Provider=reader.GetString(1), ProviderId=reader.GetString(2), CompetitionId=reader.GetString(3), Competition=reader.GetString(4), Country=reader.GetString(5), HomeTeam=reader.GetString(6), AwayTeam=reader.GetString(7), KickoffUtc=DateTime.Parse(reader.GetString(8)).ToUniversalTime(), Status=reader.GetString(9), HomeGoals=reader.IsDBNull(10)?null:reader.GetInt32(10), AwayGoals=reader.IsDBNull(11)?null:reader.GetInt32(11), Elapsed=reader.IsDBNull(12)?null:reader.GetInt32(12) });
        }
        return result;
    }
}
