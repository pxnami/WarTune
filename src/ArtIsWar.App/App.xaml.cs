using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtIsWar.App.Services;
using ArtIsWar.App.ViewModels;
using ArtIsWar.Core;
using Forms = System.Windows.Forms;

namespace ArtIsWar.App;

public partial class App : Application
{
    private MainViewModel? model;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Stream? trayIconStream;
    private Mutex? instanceMutex;
    private async void OnStartup(object sender, StartupEventArgs args)
    {
        if (args.Args.Contains("--smoke-test")) { SmokeTest(args.Args); return; }
        if (args.Args.Contains("--remove-config")) {
            try { string? apoPath = SystemServices.ApoConfigDirectory(); if (apoPath != null) new ApoConfiguration(apoPath, Path.Combine(AppContext.BaseDirectory, "ArtIsWar.Dsp.dll")).Remove(); Shutdown(0); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "WarTune configuration removal failed"); Shutdown(1); }
            return;
        }
        int handoff = Array.IndexOf(args.Args, "--wait-for-process");
        if (handoff >= 0 && handoff + 1 < args.Args.Length && int.TryParse(args.Args[handoff + 1], out int parent)) {
            try { using var previous = Process.GetProcessById(parent); await previous.WaitForExitAsync(); } catch (ArgumentException) { }
        }
        instanceMutex = new Mutex(true, "Local\\ArtIsWarAudio-" + Environment.UserName, out bool created);
        if (!created) { MessageBox.Show("WarTune is already running. Open it from the system tray."); Shutdown(); return; }
        model = new();
        var window = new MainWindow(model); MainWindow = window;
        trayIconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/App.ico")).Stream;
        trayIcon = new System.Drawing.Icon(trayIconStream);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "WarTune", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("WarTune ON", null, async (_, _) => { try { await model.ApplyAsync(true); } catch (Exception ex) { model.Error(ex); ShowWindow(); } });
        menu.Items.Add("WarTune OFF", null, (_, _) => { if (model.IsOn) model.ToggleCommand.Execute(null); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var preset in AudioProfile.Presets.Take(3)) menu.Items.Add(preset.Name, null, async (_, _) => { try { await model.SelectPresetAsync(preset); } catch (Exception ex) { model.Error(ex); ShowWindow(); } });
        menu.Items.Add("Open WarTune", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowWindow();
        window.Closing += (_, e) => { if (model.MinimizeToTray) { e.Cancel = true; window.Hide(); } };
        window.Show(); try { await model.ScanAsync(); } catch (Exception ex) { model.Error(ex); }
        if (args.Args.Contains("--tray")) window.Hide();
    }
    private void ShowWindow() { MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); trayIcon?.Dispose(); trayIconStream?.Dispose(); model?.Dispose(); instanceMutex?.Dispose(); base.OnExit(e); }
    private async void SmokeTest(string[] args)
    {
        try
        {
            var scan = new WindowsAudio().Scan();
            model = new(); var window = new MainWindow(model); MainWindow = window; window.Show(); await model.ScanAsync();
            if (args.Contains("--smoke-min-size")) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            string? output = args.SkipWhile(a => a != "--smoke-output").Skip(1).FirstOrDefault();
            bool controlsChecked = false;
            model.Page = 1; window.UpdateLayout();
            var sliders = Descendants<System.Windows.Controls.Slider>(window).ToArray();
            if (sliders.Length != 4) throw new InvalidOperationException("Warzone slider controls were not created.");
            int originalFootsteps = model.Footsteps;
            sliders[0].SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, 42.0);
            if (model.Footsteps != 42) throw new InvalidOperationException("Slider does not update its view model.");
            sliders[0].SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, (double)originalFootsteps);
            var preset = Descendants<System.Windows.Controls.ComboBox>(window).First();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            preset.Focus(); preset.IsDropDownOpen = true; window.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            if (preset.Template.FindName("PART_Popup", preset) is not System.Windows.Controls.Primitives.Popup { IsOpen: true })
                throw new InvalidOperationException("Preset dropdown did not open.");
            preset.IsDropDownOpen = false;
            var originalProfile = model.SelectedProfile;
            preset.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, 0);
            if (model.SelectedProfile != model.Profiles[0]) throw new InvalidOperationException("Preset selection did not update.");
            preset.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, originalProfile);
            model.Page = 0; window.UpdateLayout();
            var homeSliders = Descendants<System.Windows.Controls.Slider>(window).ToArray();
            if (homeSliders.Length != 4) throw new InvalidOperationException("Home controls were not created.");
            homeSliders[0].SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, 37.0);
            if (model.Footsteps != 37) throw new InvalidOperationException("Home slider binding failed.");
            homeSliders[0].SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, (double)originalFootsteps);
            var navigate = Descendants<System.Windows.Controls.Button>(window).First(b => b.Tag?.ToString() == "5");
            navigate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (model.Page != 5) throw new InvalidOperationException("Audio Setup navigation failed.");
            var maximize = Descendants<System.Windows.Controls.Button>(window).First(b => b.ToolTip?.ToString() == "Maximize or restore");
            maximize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); window.UpdateLayout();
            if (window.WindowState != WindowState.Maximized) throw new InvalidOperationException("Maximize button failed.");
            maximize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); window.UpdateLayout();
            if (window.WindowState != WindowState.Normal) throw new InvalidOperationException("Restore button failed.");
            var minimize = Descendants<System.Windows.Controls.Button>(window).First(b => b.ToolTip?.ToString() == "Minimize");
            minimize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (window.WindowState != WindowState.Minimized) throw new InvalidOperationException("Minimize button failed.");
            SystemCommands.RestoreWindow(window); window.UpdateLayout();
            model.Page = 5; window.UpdateLayout();
            var setupDetails = Descendants<System.Windows.Controls.Expander>(window).First();
            setupDetails.SetCurrentValue(System.Windows.Controls.Expander.IsExpandedProperty, true);
            window.UpdateLayout();
            var outputSelector = Descendants<System.Windows.Controls.ComboBox>(window).First();
            if (model.Devices.Count > 0)
            {
                outputSelector.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, 0);
                if (model.SelectedDevice != model.Devices[0]) throw new InvalidOperationException("Output selection binding failed.");
            }
            setupDetails.SetCurrentValue(System.Windows.Controls.Expander.IsExpandedProperty, false);
            await model.RunDiagnosticsAsync();
            if (model.DiagnosticRows.Count() < 5) throw new InvalidOperationException("Diagnostic rows did not load.");
            model.Page = 7; window.UpdateLayout();
            var categories = Descendants<System.Windows.Controls.TabControl>(window).First(t => t.Items.Count == 4);
            foreach (int category in Enumerable.Range(0, 4))
            {
                categories.SelectedIndex = category; window.UpdateLayout();
                if (categories.SelectedContent == null) throw new InvalidOperationException("Settings category is empty.");
            }
            categories.SelectedIndex = 2;
            model.Page = 2; window.UpdateLayout();
            model.ProfileSearch = model.Profiles[0].Name;
            if (!model.ProfileLibrary.Any() || model.ProfileLibrary.Any(p => !p.Name.Contains(model.ProfileSearch, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Profile search failed.");
            model.ProfileSearch = "";
            if (model.ProfileLibrary.Count() != model.Profiles.Count) throw new InvalidOperationException("Profile search did not restore the library.");
            model.SelectedProfile = originalProfile;
            window.UpdateLayout();
            var profileViewport = Descendants<System.Windows.Controls.ScrollViewer>(window).First(s => s.Name == "ProfileViewport");
            var profileActions = Descendants<System.Windows.Controls.Border>(window).First(b => b.Name == "ProfileActions");
            double viewportBottom = profileViewport.TransformToAncestor(window).Transform(new System.Windows.Point(0, profileViewport.ActualHeight)).Y;
            double actionsTop = profileActions.TransformToAncestor(window).Transform(new System.Windows.Point()).Y;
            if (viewportBottom > actionsTop + 1) throw new InvalidOperationException("Profile content overlaps the action bar.");
            if (args.Contains("--smoke-min-size") && profileViewport.ScrollableHeight <= 0)
                throw new InvalidOperationException("Profile overflow is not scrollable at minimum size.");
            var statusStrip = Descendants<System.Windows.Controls.Border>(window).First(b => b.Name == "StatusStrip");
            foreach (var button in Descendants<System.Windows.Controls.Button>(statusStrip).Where(b => b.IsVisible))
                if (button.TransformToAncestor(window).Transform(new System.Windows.Point(0, button.ActualHeight)).Y > window.ActualHeight - 4)
                    throw new InvalidOperationException("A status-strip button is clipped.");
            controlsChecked = true;
            foreach (int page in Enumerable.Range(0, 8)) {
                model.Page = page; window.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                if (output != null) {
                    var content = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"page-{page}.png")); png.Save(stream);
                }
                if (output != null)
                {
                    foreach (var scroller in Descendants<System.Windows.Controls.ScrollViewer>(window).Where(s => s.IsVisible && s.ScrollableHeight > 0)) scroller.ScrollToEnd();
                    window.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    var content = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"page-{page}-bottom.png")); png.Save(stream);
                }
            }
            if (output != null) AtomicFile.WriteText(output, JsonSerializer.Serialize(new { Launched = true, Pages = 8, ControlsChecked = controlsChecked, ProfileActionsDoNotOverlap = true, DpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX, Width = window.Width, Height = window.Height, DeviceCount = scan.Devices.Count, Formats = scan.Devices.Select(d => new { d.Channels, d.SampleRate }), ApoInstalled = SystemServices.ApoDirectory() != null }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
        }
        catch (Exception ex) { if (args.Length > 2) AtomicFile.WriteText(args[^1], ex.ToString()); Shutdown(1); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
