using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BiliStreamAudio.Tui.Views;

internal sealed class PlaceholderWindow : Window
{
    public PlaceholderWindow(string title, string message)
    {
        Title = title;
        Add(new Terminal.Gui.Views.Label
        {
            Text = message,
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = Dim.Auto(),
            Height = Dim.Auto()
        });
    }
}
