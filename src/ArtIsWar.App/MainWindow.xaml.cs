using System.Windows;
using ArtIsWar.App.ViewModels;

namespace ArtIsWar.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.Page = 2;
        SourceInitialized += FitDesktop;
    }
    private void FitDesktop(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var desktop = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double availableWidth = desktop.Width / dpi.DpiScaleX;
        double availableHeight = desktop.Height / dpi.DpiScaleY;
        MinWidth = Math.Min(MinWidth, availableWidth * .9);
        MinHeight = Math.Min(MinHeight, availableHeight * .9);
        Width = Math.Max(MinWidth, Math.Min(Width, availableWidth * .9));
        Height = Math.Max(MinHeight, Math.Min(Height, availableHeight * .88));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = desktop.Left / dpi.DpiScaleX + (availableWidth - Width) / 2;
        Top = desktop.Top / dpi.DpiScaleY + (availableHeight - Height) / 2;
    }
    private void HeadphoneSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ArtIsWar.Core.HeadphoneProfile profile })
        {
            var model = (MainViewModel)DataContext;
            model.SelectedHeadphone = profile;
            model.LoadHeadphoneCommand.Execute(null);
        }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && int.TryParse(button.Tag?.ToString(), out int page))
            ((MainViewModel)DataContext).Page = page;
    }
}
