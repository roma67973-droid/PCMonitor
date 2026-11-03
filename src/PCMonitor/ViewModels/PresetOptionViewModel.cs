using PCMonitor.Core;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Кнопка быстрого режима на вкладке «Экран»: отмечена, пока текущие значения совпадают с режимом.</summary>
public sealed class PresetOptionViewModel(DisplayPreset preset, string title, string description, DisplayViewModel owner) : ObservableObject
{
    public DisplayPreset Preset { get; } = preset;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public bool IsActive
    {
        get => owner.ActivePreset == Preset;
        set
        {
            if (value)
            {
                owner.ApplyPreset(Preset);
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsActive));
}
