using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PCMonitor.Controls;

/// <summary>Поведение для полей «нажми и переименуй»: Enter применяет, Esc отменяет, оба снимают фокус.</summary>
public static class TextBoxBehavior
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(TextBoxBehavior), new PropertyMetadata(false, OnCommitOnEnterChanged));

    public static bool GetCommitOnEnter(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(CommitOnEnterProperty);
    }

    public static void SetCommitOnEnter(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CommitOnEnterProperty, value);
    }

    private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.KeyDown -= OnKeyDown;
        if ((bool)e.NewValue)
        {
            box.KeyDown += OnKeyDown;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        switch (e.Key)
        {
            case Key.Enter:
                binding?.UpdateSource();
                break;
            case Key.Escape:
                binding?.UpdateTarget();
                break;
            default:
                return;
        }

        e.Handled = true;
        Keyboard.ClearFocus();
        if (Window.GetWindow(box) is { } window)
        {
            FocusManager.SetFocusedElement(window, window);
        }
    }
}
