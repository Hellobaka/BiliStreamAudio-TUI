using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace BiliStreamAudio.Tui.Views;

/// <summary>Base window for application pages that must not use Escape to quit the application.</summary>
internal abstract class ApplicationWindow : Window
{
    protected ApplicationWindow()
    {
        KeyDown += (_, key) =>
        {
            if (SuppressEscape && key == Key.Esc)
            {
                key.Handled = true;
            }
        };
    }

    protected virtual bool SuppressEscape => true;
}
