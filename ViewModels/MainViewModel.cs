using System.Collections.ObjectModel;
using System.Windows;
using KoaZa.Foreseer.Models;
using KoaZa.Foreseer.Services;

namespace KoaZa.Foreseer.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly FixtureRepository _repository = new();
    private readonly ApiFootballProvider _apiFootball = new();
    private readonly OpenLigaDbProvider _openLiga = new();
    private readonly PredictionService _predictionService = new();
    private AppSettings _settings = new();
    private Fixture? _selectedFixture;
    private string _status = "شروع برنامه…";
    private bool _isBusy;
    private string _apiKey = "";
    private string _searchText = "";

    public ObservableCollection<Competition> Competitions { get; } = new();
    public ObservableCollection<Fixture> Fixtures { get; } = new();
    public ObservableCollection<PredictionRow> Predictions { get; } = new();
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    public MainViewModel()
    {
        Competitions.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (Competition c in e.NewItems) c.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Competition.IsSelected)) OnPropertyChanged(nameof(SelectedCount)); };
        };
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);
        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync(), () => !IsBusy);
        SelectAllCommand = new RelayCommand(() => { foreach (var c in Competitions) c.IsSelected = true; OnPropertyChanged(nameof(SelectedCount)); });
        ClearSelectionCommand = new RelayCommand(() => { foreach (var c in Competitions) c.IsSelected = false; OnPropertyChanged(nameof(SelectedCount)); });
    }

    public string ApiKey { get => _apiKey; set { _apiKey=value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status=value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; set { _isBusy=value; OnPropertyChanged(); RefreshCommand?.RaiseCanExecuteChanged(); SaveSettingsCommand?.RaiseCanExecuteChanged(); } }
    public Fixture? SelectedFixture { get => _selectedFixture; set { _selectedFixture=value; OnPropertyChanged(); LoadPredictions(); } }
    public string SearchText { get => _searchText; set { _searchText=value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredCompetitions)); } }
    public IEnumerable<Competition> FilteredCompetitions => string.IsNullOrWhiteSpace(SearchText) ? Competitions : Competitions.Where(c => c.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    public int SelectedCount => Competitions.Count(c => c.IsSelected);
    public int LiveCount => Fixtures.Count(f => f.IsLive);
    public int UpcomingCount => Fixtures.Count(f => !f.IsLive && f.KickoffUtc >= DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        try
        {
            await _repository.InitializeAsync();
            _settings = await _settingsService.LoadAsync(); ApiKey = _settings.ApiFootballKey;
            var storedCompetitions = await _repository.GetCompetitionsAsync();
            var selectedIds = _settings.SelectedCompetitionIds.ToHashSet(StringComparer.Ordinal);
            foreach (var c in storedCompetitions) c.IsSelected = selectedIds.Contains(c.Id);
            MergeCompetitions(storedCompetitions);
            var cached = await _repository.GetFixturesAsync(DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(7));
            var initialSelected = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            ReplaceFixtures(cached.Where(f => initialSelected.Contains(f.CompetitionId) && (f.IsLive || f.KickoffUtc >= DateTime.UtcNow.AddMinutes(-30))));
            Status = cached.Count == 0 ? "آماده؛ برای دریافت داده، کلید API را وارد و تازه‌سازی کنید." : $"دادهٔ ذخیره‌شده بارگذاری شد · {cached.Count} مسابقه";
            OnPropertyChanged(nameof(SelectedCount));
            if (string.IsNullOrWhiteSpace(ApiKey)) await LoadOpenLigaCatalogAsync();
        }
        catch (Exception ex) { Status = "خطا در راه‌اندازی: " + ex.Message; }
    }

    private async Task LoadOpenLigaCatalogAsync()
    {
        try
        {
            var list = await _openLiga.GetCompetitionsAsync(CancellationToken.None);
            var selectedIds = _settings.SelectedCompetitionIds.ToHashSet(StringComparer.Ordinal);
            foreach (var c in list) c.IsSelected = selectedIds.Contains(c.Id);
            MergeCompetitions(list);
            await _repository.SaveCompetitionsAsync(list);
            Status = $"فهرست OpenLigaDB دریافت شد · {list.Count} رقابت؛ برای دادهٔ گسترده‌تر کلید API-Football را وارد کنید.";
        }
        catch (Exception ex) { Status = "OpenLigaDB در دسترس نیست؛ می‌توانید بعداً دوباره تلاش کنید. " + ex.Message; }
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _settings.ApiFootballKey = ApiKey.Trim();
            _settings.SelectedCompetitionIds = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToList();
            if (_settings.LastSyncUtc.HasValue && DateTime.UtcNow - _settings.LastSyncUtc.Value < TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes)))
            {
                var cachedOnly = await _repository.GetFixturesAsync(DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(7));
                var cachedSelectedIds = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                ReplaceFixtures(cachedOnly.Where(f => cachedSelectedIds.Contains(f.CompetitionId) && (f.IsLive || f.KickoffUtc >= DateTime.UtcNow.AddMinutes(-30))));
                Status = $"برای رعایت سهمیهٔ API، تازه‌سازی بعدی پس از {_settings.RefreshMinutes} دقیقه مجاز است. دادهٔ ذخیره‌شده نمایش داده شد.";
                return;
            }
            var token = CancellationToken.None;
            var allFixtures = new List<Fixture>();
            if (_settings.EnableApiFootball && !string.IsNullOrWhiteSpace(ApiKey))
            {
                if (!_settings.LastCatalogRefreshUtc.HasValue || DateTime.UtcNow - _settings.LastCatalogRefreshUtc.Value >= TimeSpan.FromHours(24) || Competitions.Count == 0)
                {
                    Status = "دریافت فهرست رقابت‌ها از API-Football…";
                    var catalog = await _apiFootball.GetCompetitionsAsync(ApiKey, token);
                    var selectedIds = _settings.SelectedCompetitionIds.ToHashSet(StringComparer.Ordinal);
                    foreach (var c in catalog) c.IsSelected = selectedIds.Contains(c.Id);
                    MergeCompetitions(catalog);
                    await _repository.SaveCompetitionsAsync(catalog);
                    _settings.LastCatalogRefreshUtc = DateTime.UtcNow;
                }
                if (!_settings.LastFixturesRefreshUtc.HasValue || DateTime.UtcNow - _settings.LastFixturesRefreshUtc.Value >= TimeSpan.FromHours(6))
                {
                    Status = "دریافت برنامه و تاریخچهٔ اخیر مسابقات…";
                    // One date-range request per six hours minimizes use of the free quota.
                    var range = await _apiFootball.GetFixturesAsync(ApiKey, DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(7), token);
                    allFixtures.AddRange(range);
                    _settings.LastFixturesRefreshUtc = DateTime.UtcNow;
                }
                // One live-feed request on each permitted manual refresh.
                allFixtures.AddRange(await _apiFootball.GetLiveFixturesAsync(ApiKey, token));
            }
            if (_settings.EnableOpenLigaDb && !Competitions.Any(c => c.Provider == "OpenLigaDB"))
            {
                try
                {
                    var openCatalog = await _openLiga.GetCompetitionsAsync(token);
                    var savedSelection = _settings.SelectedCompetitionIds.ToHashSet(StringComparer.Ordinal);
                    foreach (var c in openCatalog) c.IsSelected = savedSelection.Contains(c.Id);
                    MergeCompetitions(openCatalog);
                    await _repository.SaveCompetitionsAsync(openCatalog);
                }
                catch { /* API-Football remains usable if OpenLigaDB is unavailable. */ }
            }
            var selectedOpenLiga = Competitions.Where(c => c.IsSelected && c.Provider == "OpenLigaDB").ToList();
            if (_settings.EnableOpenLigaDb && selectedOpenLiga.Count > 0)
                allFixtures.AddRange(await _openLiga.GetFixturesAsync(selectedOpenLiga, token));

            var distinct = allFixtures.GroupBy(f => f.Id, StringComparer.Ordinal).Select(g => g.Last()).ToList();
            await _repository.SaveFixturesAsync(distinct);
            _settings.LastSyncUtc = DateTime.UtcNow;
            _settings.SelectedCompetitionIds = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToList();
            await _settingsService.SaveAsync(_settings);
            var visibleIds = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var cachedAll = await _repository.GetFixturesAsync(DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(7));
            var visible = cachedAll.Where(f => visibleIds.Contains(f.CompetitionId)).ToList();
            // Keep completed recent matches in storage for modeling, but show only live and the next seven days.
            ReplaceFixtures(visible.Where(f => f.IsLive || f.KickoffUtc >= DateTime.UtcNow.AddMinutes(-30)).OrderBy(f => f.KickoffUtc));
            Status = $"تازه‌سازی انجام شد · {Fixtures.Count} مسابقهٔ انتخاب‌شده · {LiveCount} زنده · {SelectedCount} رقابت فعال";
            OnPropertyChanged(nameof(SelectedCount));
        }
        catch (Exception ex)
        {
            Status = "تازه‌سازی ناموفق بود: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            _settings.ApiFootballKey = ApiKey.Trim();
            _settings.SelectedCompetitionIds = Competitions.Where(c => c.IsSelected).Select(c => c.Id).ToList();
            await _settingsService.SaveAsync(_settings);
            await _repository.SaveCompetitionsAsync(Competitions);
            OnPropertyChanged(nameof(SelectedCount));
            Status = "تنظیمات ذخیره شد.";
            if (Competitions.Count == 0) await LoadOpenLigaCatalogAsync();
        }
        catch (Exception ex) { Status = "ذخیرهٔ تنظیمات ناموفق بود: " + ex.Message; }
    }

    private void MergeCompetitions(IEnumerable<Competition> incoming)
    {
        var existing = Competitions.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var item in incoming)
        {
            if (existing.TryGetValue(item.Id, out var old))
            {
                old.Name=item.Name; old.Country=item.Country; old.ProviderId=item.ProviderId; old.Season=item.Season; old.IsSelected=item.IsSelected;
            }
            else Competitions.Add(item);
        }
        OnPropertyChanged(nameof(FilteredCompetitions)); OnPropertyChanged(nameof(SelectedCount));
    }

    private void ReplaceFixtures(IEnumerable<Fixture> fixtures)
    {
        Fixtures.Clear(); foreach (var fixture in fixtures) Fixtures.Add(fixture);
        OnPropertyChanged(nameof(LiveCount)); OnPropertyChanged(nameof(UpcomingCount));
        if (SelectedFixture is not null) LoadPredictions();
    }

    private async void LoadPredictions()
    {
        Predictions.Clear();
        if (SelectedFixture is null) return;
        try
        {
            var history = await _repository.GetFixturesAsync(DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(7));
            foreach (var row in _predictionService.Predict(SelectedFixture, history)) Predictions.Add(row);
        }
        catch (Exception ex) { Predictions.Add(new PredictionRow { Market="خطا", Outcome=ex.Message, Confidence="—" }); }
    }
}
