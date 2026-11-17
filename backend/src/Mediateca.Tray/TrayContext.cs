using System.Diagnostics;

namespace Mediateca.Tray;

/// <summary>Ícono de la bandeja con su menú; controla la pila de Docker Compose de Mediateca.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const int BalloonMilliseconds = 5000;
    private const int MaxTooltipLength = 63;

    private readonly string _composeFolder;
    private readonly bool _startAutomatically;
    private readonly TrayIcons _icons = new TrayIcons();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new ContextMenuStrip();
    private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private readonly ToolStripMenuItem _autostartItem = new ToolStripMenuItem("Iniciar con Windows");
    private readonly ToolStripMenuItem _startItem = new ToolStripMenuItem("Iniciar Mediateca");
    private readonly ToolStripMenuItem _stopItem = new ToolStripMenuItem("Detener Mediateca");
    private readonly ToolStripMenuItem _openItem = new ToolStripMenuItem("Abrir Mediateca");
    private readonly ToolStripMenuItem _logsItem = new ToolStripMenuItem("Ver registros");
    private readonly ToolStripMenuItem _exitItem = new ToolStripMenuItem("Salir");

    private StackState _state = StackState.DockerUnavailable;
    private bool _webRunning;
    private bool _checking;
    private bool _busy;
    private bool _firstCheckDone;
    private bool _disposed;

    public TrayContext(string composeFolder, bool startAutomatically)
    {
        _composeFolder = composeFolder;
        _startAutomatically = startAutomatically;

        _autostartItem.Checked = ReadAutostart();
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _startItem.Click += async (_, _) => await StartStackAsync();
        _stopItem.Click += async (_, _) => await StopStackAsync();
        _openItem.Click += (_, _) => OpenWeb();
        _logsItem.Click += (_, _) => ShowLogs();
        _exitItem.Click += (_, _) => ExitApplication();

        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startItem);
        _menu.Items.Add(_stopItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_logsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitItem);
        _menu.Opening += (_, _) => _autostartItem.Checked = ReadAutostart();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons.Stopped,
            Text = StackStatus.Describe(_state),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenWeb();
            }
        };

        UpdateMenu();

        // La primera revisión ocurre casi de inmediato; después, cada 5 segundos.
        _timer.Interval = 300;
        _timer.Tick += async (_, _) => await OnTimerTickAsync();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cleanup();
        }

        base.Dispose(disposing);
    }

    private async Task OnTimerTickAsync()
    {
        _timer.Interval = 5000;
        await CheckStatusAsync();
    }

    /// <summary>Una sola revisión a la vez: si la anterior sigue, esta se omite.</summary>
    private async Task CheckStatusAsync()
    {
        if (_checking || _disposed)
        {
            return;
        }

        _checking = true;
        var startNow = false;
        try
        {
            var snapshot = await DockerCli.GetSnapshotAsync(_composeFolder, _lifetime.Token);
            if (_disposed)
            {
                return;
            }

            ApplyStatus(snapshot);

            if (!_firstCheckDone)
            {
                _firstCheckDone = true;
                startNow = _startAutomatically
                    && !_busy
                    && snapshot.State != StackState.Running
                    && snapshot.State != StackState.Starting;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo revisar el estado: " + ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _checking = false;
        }

        if (startNow)
        {
            await StartStackAsync();
        }
    }

    private void ApplyStatus(StackSnapshot snapshot)
    {
        _state = snapshot.State;
        _webRunning = snapshot.WebRunning;

        var icon = _state == StackState.Running ? _icons.Running : _icons.Stopped;
        if (!ReferenceEquals(_notifyIcon.Icon, icon))
        {
            _notifyIcon.Icon = icon;
        }

        var text = StackStatus.Describe(_state);
        if (text.Length > MaxTooltipLength)
        {
            text = text.Substring(0, MaxTooltipLength);
        }

        _notifyIcon.Text = text;
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        var canStart = !_busy && _state != StackState.Running && _state != StackState.Starting;
        var canStop = !_busy
            && (_state == StackState.Running || _state == StackState.Starting || _state == StackState.Partial);

        _startItem.Enabled = canStart;
        _stopItem.Enabled = canStop;
        _openItem.Enabled = _webRunning;
    }

    private async Task StartStackAsync()
    {
        if (_busy || _disposed)
        {
            return;
        }

        _busy = true;
        UpdateMenu();
        try
        {
            await StartCoreAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo iniciar Mediateca: " + ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }

        if (!_disposed)
        {
            UpdateMenu();
            await CheckStatusAsync();
        }
    }

    private async Task StartCoreAsync()
    {
        var dockerReady = await DockerCli.IsAvailableAsync(_lifetime.Token);
        if (!dockerReady)
        {
            var dockerDesktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Docker",
                "Docker",
                "Docker Desktop.exe");

            if (File.Exists(dockerDesktop))
            {
                ShowBalloon("Esperando a Docker Desktop…", ToolTipIcon.Info);
                Process.Start(new ProcessStartInfo(dockerDesktop) { UseShellExecute = true })?.Dispose();
                dockerReady = await DockerCli.WaitUntilAvailableAsync(TimeSpan.FromMinutes(5), _lifetime.Token);
            }
        }

        if (_disposed)
        {
            return;
        }

        if (!dockerReady)
        {
            ShowBalloon("Docker no está disponible. Instala o abre Docker Desktop e inténtalo de nuevo.", ToolTipIcon.Error);
            return;
        }

        // Iniciar y detener no se cancelan al salir: el proceso de docker termina por su cuenta.
        var result = await DockerCli.RunAsync("compose up -d", _composeFolder, CancellationToken.None);
        if (result.ExitCode == 0)
        {
            ShowBalloon("Mediateca está en marcha. Ábrela con doble clic en el ícono.", ToolTipIcon.Info);
        }
        else
        {
            ShowBalloon(
                DockerCli.FirstErrorLine(result.StdErr, "No se pudo iniciar Mediateca (código " + result.ExitCode + ")."),
                ToolTipIcon.Error);
        }
    }

    private async Task StopStackAsync()
    {
        if (_busy || _disposed)
        {
            return;
        }

        _busy = true;
        UpdateMenu();
        try
        {
            ShowBalloon("Deteniendo Mediateca…", ToolTipIcon.Info);
            var result = await DockerCli.RunAsync("compose stop", _composeFolder, CancellationToken.None);
            if (result.ExitCode == 0)
            {
                ShowBalloon("Mediateca se detuvo.", ToolTipIcon.Info);
            }
            else
            {
                ShowBalloon(
                    DockerCli.FirstErrorLine(result.StdErr, "No se pudo detener Mediateca (código " + result.ExitCode + ")."),
                    ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo detener Mediateca: " + ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }

        if (!_disposed)
        {
            UpdateMenu();
            await CheckStatusAsync();
        }
    }

    private void OpenWeb()
    {
        try
        {
            if (!_webRunning)
            {
                ShowBalloon("Mediateca no está en ejecución. Inicia desde el menú del ícono.", ToolTipIcon.Info);
                return;
            }

            var port = EnvFile.ReadWebPort(_composeFolder);
            var url = "http://localhost:" + port;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo abrir el navegador: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowLogs()
    {
        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe", "/k docker compose logs -f --tail 200")
            {
                UseShellExecute = true,
                WorkingDirectory = _composeFolder,
            };
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo abrir los registros: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            if (AutostartRegistry.IsEnabled())
            {
                AutostartRegistry.Disable();
            }
            else
            {
                AutostartRegistry.Enable(_composeFolder);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("No se pudo cambiar el inicio con Windows: " + ex.Message, ToolTipIcon.Error);
        }

        _autostartItem.Checked = ReadAutostart();
    }

    private static bool ReadAutostart()
    {
        try
        {
            return AutostartRegistry.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _notifyIcon.ShowBalloonTip(BalloonMilliseconds, "Mediateca", text, icon);
        }
        catch (Exception)
        {
            // Un aviso que falla no debe tumbar la app.
        }
    }

    private void ExitApplication()
    {
        // Los contenedores siguen corriendo a propósito: "Detener Mediateca" es lo que los apaga.
        Cleanup();
        ExitThread();
    }

    private void Cleanup()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _lifetime.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _timer.Dispose();
        _menu.Dispose();
        _icons.Dispose();
    }
}
