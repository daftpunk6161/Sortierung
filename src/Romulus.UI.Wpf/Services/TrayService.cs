using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Manages system tray icon lifecycle: creation, context menu, minimize-to-tray, disposal.
/// Extracted from MainWindow.xaml.cs (RF-007).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private IntPtr _trayIconHandle;
    // V2-WPF-M03: Guard against rapid Toggle calls during icon creation
    private bool _isCreating;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayService(Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
    }

    public bool IsActive => _trayIcon is not null;

    /// <summary>
    /// Toggle tray mode. If already active, minimizes to tray; otherwise creates tray icon and minimizes.
    /// </summary>
    public void Toggle()
    {
        if (_trayIcon is not null)
        {
            _window.WindowState = WindowState.Minimized;
            return;
        }
        if (_isCreating) return;
        _isCreating = true;

        IntPtr hicon;
        using (var bitmap = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(40, 100, 210));
                using var font = new Font("Segoe UI", 16, System.Drawing.FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                g.DrawString("R", font, brush, 2, 2);
            }
            hicon = bitmap.GetHicon();
        }

        _trayIconHandle = hicon;
        var icon = Icon.FromHandle(hicon);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Anzeigen", null, (_, _) => RestoreWindow());
        menu.Items.Add("DryRun starten", null, (_, _) =>
        {
            _window.Dispatcher.InvokeAsync(() =>
            {
                _vm.DryRun = true;
                _vm.RunCommand.Execute(null);
            });
        });
        menu.Items.Add("Status", null, (_, _) =>
        {
            _window.Dispatcher.InvokeAsync(() =>
            {
                var status = _vm.IsBusy ? "Lauf aktiv..." : "Bereit";
                _trayIcon?.ShowBalloonTip(3000, "Romulus Status", status, System.Windows.Forms.ToolTipIcon.Info);
            });
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) =>
        {
            _window.Dispatcher.InvokeAsync(() =>
            {
                if (_window is MainWindow mainWindow)
                {
                    mainWindow.RequestApplicationExit();
                    return;
                }

                _window.Close();
            });
        });

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Romulus",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        _window.StateChanged -= OnWindowStateChanged;
        _window.StateChanged += OnWindowStateChanged;

        _isCreating = false;
        _trayIcon.ShowBalloonTip(2000, "Romulus", "In den System-Tray minimiert.", System.Windows.Forms.ToolTipIcon.Info);
        _window.WindowState = WindowState.Minimized;
    }

    public void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized && _trayIcon is not null)
        {
            _window.Hide();
            _trayIcon.ShowBalloonTip(2000, "Romulus", "Anwendung läuft im Hintergrund.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>GUI-111: Show a balloon tip notification.</summary>
    public void ShowBalloonTip(string title, string message)
    {
        _trayIcon?.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info);
    }

    /// <summary>GUI-111: Update the tray icon tooltip text.</summary>
    public void UpdateTooltip(string text)
    {
        if (_trayIcon is not null)
            _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _isCreating = false;
        _window.StateChanged -= OnWindowStateChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_trayIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
    }

    private void RestoreWindow()
    {
        _window.Dispatcher.InvokeAsync(() =>
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        });
    }
}
