using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using PCMonitor.Core;
using PCMonitor.Theming;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Страница «Темы»: превью всех тем, применение по клику.</summary>
public sealed class ThemesViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public ThemesViewModel(AppSettings settings)
    {
        _settings = settings;
        SelectCommand = new RelayCommand<ThemeTileViewModel>(Select);
        foreach (var theme in ThemeManager.All)
        {
            Tiles.Add(new ThemeTileViewModel(theme) { IsSelected = theme == ThemeManager.Current });
        }
    }

    public ObservableCollection<ThemeTileViewModel> Tiles { get; } = [];

    public ICommand SelectCommand { get; }

    private void Select(ThemeTileViewModel tile)
    {
        ThemeManager.Apply(tile.Theme);
        _settings.Theme = tile.Theme.Name;
        _settings.SaveSoon();
        foreach (var t in Tiles)
        {
            t.IsSelected = t == tile;
        }
    }
}

/// <summary>Плитка-превью темы. Кисти собственные (не из ресурсов), чтобы показывать тему, а не текущую.</summary>
public sealed class ThemeTileViewModel(ThemeDefinition theme) : ObservableObject
{
    private bool _isSelected;

    public ThemeDefinition Theme { get; } = theme;

    public string Name => Theme.Name;

    public Brush Background { get; } = ThemeManager.CreateBrush(theme.Background);

    public Brush Card { get; } = ThemeManager.CreateBrush(theme.Card);

    public Brush Text { get; } = ThemeManager.CreateBrush(theme.Text);

    public Brush SubText { get; } = ThemeManager.CreateBrush(theme.SubText);

    public Brush Accent { get; } = ThemeManager.CreateAccentGradient(theme);

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
