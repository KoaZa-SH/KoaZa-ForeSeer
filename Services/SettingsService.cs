using System.Text.Json;
using KoaZa.Foreseer.Models;

namespace KoaZa.Foreseer.Services;

public sealed class SettingsService
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KoaZa", "Foreseer", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, settings, Options);
    }
}
