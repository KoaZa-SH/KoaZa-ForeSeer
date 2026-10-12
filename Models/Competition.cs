using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoaZa.Foreseer.Models;

public sealed class Competition : INotifyPropertyChanged
{
    private bool _isSelected;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public int Season { get; set; }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); } }
    public string DisplayName => string.IsNullOrWhiteSpace(Country) ? Name : $"{Name} · {Country}";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
