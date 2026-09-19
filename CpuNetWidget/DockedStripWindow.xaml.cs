using System.Windows;
using System.Windows.Input;

namespace CpuNetWidget;

internal partial class DockedStripWindow : Window
{
    private const double Thickness = 7;
    private const double Length = 50;

    internal DockedStripWindow(DockEdge edge, double anchor, Rect workingArea, bool topmost)
    {
        InitializeComponent();
        Topmost = topmost;

        if (edge is DockEdge.Left or DockEdge.Right)
        {
            Width = Thickness;
            Height = Length;
            Left = edge == DockEdge.Left ? workingArea.Left : workingArea.Right - Thickness;
            Top = Math.Clamp(anchor - Length / 2, workingArea.Top, workingArea.Bottom - Length);
            StripBorder.CornerRadius = edge == DockEdge.Left
                ? new CornerRadius(0, 5, 5, 0)
                : new CornerRadius(5, 0, 0, 5);
        }
        else
        {
            Width = Length;
            Height = Thickness;
            Left = Math.Clamp(anchor - Length / 2, workingArea.Left, workingArea.Right - Length);
            Top = edge == DockEdge.Top ? workingArea.Top : workingArea.Bottom - Thickness;
            StripBorder.CornerRadius = edge == DockEdge.Top
                ? new CornerRadius(0, 0, 5, 5)
                : new CornerRadius(5, 5, 0, 0);
        }
    }

    public event EventHandler? RestoreRequested;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
