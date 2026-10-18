using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameNet.TestLauncher;

internal sealed class MainForm : Form
{
    private readonly TextBox _port = new() { Text = "5432", Width = 110 };
    private readonly TextBox _adminUser = new() { Text = "postgres", Width = 220 };
    private readonly TextBox _adminPassword = new() { UseSystemPasswordChar = true, Width = 280 };
    private readonly TextBox _ownerUsername = new() { Text = "slice1-owner", Width = 220 };
    private readonly TextBox _ownerDisplay = new() { Text = "Slice 1 Test Owner", Width = 280 };
    private readonly TextBox _ownerPassword = new() { UseSystemPasswordChar = true, Width = 280 };
    private readonly TextBox _hourlyRate = new() { Text = "1000", Width = 130 };
    private readonly CheckBox _confirmDisposable = new()
    {
        Text = "I confirm this will create a NEW disposable local test database and apply GameNet test migrations.",
        AutoSize = true
    };
    private readonly CheckBox _rememberSettings = new()
    {
        Text = "Remember Owner/tariff settings encrypted for this Windows user (PostgreSQL admin password is never saved).",
        AutoSize = true,
        Checked = true
    };
    private readonly Button _startButton = new() { Text = "Create fresh test database and launch", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop test processes", AutoSize = true, Enabled = false };
    private readonly Button _reopenDesktopButton = new() { Text = "Reopen Desktop (same test data)", AutoSize = true, Enabled = false };
    private readonly Button _clearSettingsButton = new() { Text = "Forget saved settings", AutoSize = true };
    private readonly Label _status = new()
    {
        Text = "Ready. This test runs on this PC only and does not install Windows services.",
        AutoSize = true,
        MaximumSize = new Size(940, 0)
    };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9),
        BackColor = SystemColors.Window
    };

    private Process? _harnessProcess;
    private string? _stopSignalPath;
    private string? _reopenSignalPath;
    private bool _testWorkspaceReady;
    private bool _closing;

    public MainForm()
    {
        Text = "GameNet 5 — Slice 1 Test Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 720);
        Size = new Size(1060, 860);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new Label
        {
            Text = "GameNet 5 — temporary, isolated functional test",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        root.Controls.Add(heading, 0, 0);

        var dbGroup = new GroupBox
        {
            Text = "PostgreSQL on this PC (127.0.0.1 only)",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10)
        };
        var dbGrid = CreateFieldGrid();
        AddField(dbGrid, "PostgreSQL TCP port", _port);
        AddField(dbGrid, "PostgreSQL administrator user", _adminUser);
        AddField(dbGrid, "Administrator password (used only to create the disposable test DB)", _adminPassword);
        dbGroup.Controls.Add(dbGrid);
        root.Controls.Add(dbGroup, 0, 1);

        var testGroup = new GroupBox
        {
            Text = "Throwaway Owner and test tariff",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10)
        };
        var testGrid = CreateFieldGrid();
        AddField(testGrid, "Owner username", _ownerUsername);
        AddField(testGrid, "Owner display name", _ownerDisplay);
        AddField(testGrid, "Owner password (10–256 characters)", _ownerPassword);
        AddField(testGrid, "Hourly rate in whole Toman (1000 gives about 33 Toman for 2 minutes)", _hourlyRate);
        testGroup.Controls.Add(testGrid);
        root.Controls.Add(testGroup, 0, 2);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 8)
        };
        controls.Controls.Add(_confirmDisposable);
        controls.Controls.Add(_rememberSettings);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_stopButton);
        buttons.Controls.Add(_reopenDesktopButton);
        buttons.Controls.Add(_clearSettingsButton);
        controls.Controls.Add(buttons);
        controls.Controls.Add(_status);
        root.Controls.Add(controls, 0, 3);

        var logGroup = new GroupBox
        {
            Text = "Progress and diagnostics",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 4);

        _startButton.Click += async (_, _) => await StartTestAsync();
        _stopButton.Click += (_, _) => RequestStop();
        _reopenDesktopButton.Click += (_, _) => RequestReopenDesktop();
        _clearSettingsButton.Click += (_, _) => ForgetSettings();
        FormClosing += OnFormClosing;

        LoadSettings();
        AppendLog("No PowerShell window is shown. The test harness runs internally and logs its status here.");
        AppendLog("The test does not register services or change ProgramData, machine certificates, firewall, machine registry, system time, or LAN settings.");
    }

    private static TableLayoutPanel CreateFieldGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(2)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        return grid;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control editor)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 12, 7),
            MaximumSize = new Size(440, 0)
        };
        editor.Anchor = AnchorStyles.Left;
        editor.Margin = new Padding(3, 4, 3, 4);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(editor, 1, row);
    }

    private void LoadSettings()
    {
        try
        {
            var saved = SettingsStore.Load();
            if (saved is null)
                return;
            _port.Text = saved.PostgreSqlPort;
            _adminUser.Text = saved.PostgreSqlAdminUser;
            _ownerUsername.Text = saved.OwnerUsername;
            _ownerDisplay.Text = saved.OwnerDisplayName;
            _ownerPassword.Text = saved.OwnerPassword;
            _hourlyRate.Text = saved.HourlyRateToman;
            AppendLog("Saved test settings were restored using Windows CurrentUser DPAPI. The PostgreSQL administrator password was not saved.");
        }
        catch (Exception exception)
        {
            AppendLog($"Saved settings could not be read ({exception.GetType().Name}); enter test values again or use Forget saved settings.");
        }
    }

    private LauncherSettings ReadAndValidateSettings()
    {
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Enter a valid PostgreSQL port from 1 to 65535.");
        if (string.IsNullOrWhiteSpace(_adminUser.Text) ||
            !System.Text.RegularExpressions.Regex.IsMatch(_adminUser.Text.Trim(), @"^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
            throw new InvalidOperationException("The PostgreSQL administrator username must contain only letters, digits and underscores, and must not start with a digit.");
        if (_adminPassword.Text.Length == 0)
            throw new InvalidOperationException("Enter the PostgreSQL administrator password. It is used only during disposable database creation and is not saved.");
        if (string.IsNullOrWhiteSpace(_ownerUsername.Text) || _ownerUsername.Text.Trim().Length > 64)
            throw new InvalidOperationException("Enter a throwaway Owner username up to 64 characters.");
        if (string.IsNullOrWhiteSpace(_ownerDisplay.Text) || _ownerDisplay.Text.Trim().Length > 120)
            throw new InvalidOperationException("Enter a throwaway Owner display name up to 120 characters.");
        if (_ownerPassword.Text.Length is < 10 or > 256)
            throw new InvalidOperationException("The throwaway Owner password must be between 10 and 256 characters.");
        if (!long.TryParse(_hourlyRate.Text.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var rate) || rate < 1)
            throw new InvalidOperationException("Enter a positive whole-Toman hourly rate.");
        if (!_confirmDisposable.Checked)
            throw new InvalidOperationException("Confirm that this is a new disposable test database before starting.");
        return new LauncherSettings
        {
            PostgreSqlPort = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PostgreSqlAdminUser = _adminUser.Text.Trim(),
            OwnerUsername = _ownerUsername.Text.Trim(),
            OwnerDisplayName = _ownerDisplay.Text.Trim(),
            OwnerPassword = _ownerPassword.Text,
            HourlyRateToman = rate.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private void SaveOrClearSettings(LauncherSettings settings)
    {
        if (_rememberSettings.Checked)
        {
            SettingsStore.Save(settings);
            AppendLog("Owner/tariff settings saved encrypted for this Windows user.");
        }
        else
        {
            SettingsStore.Clear();
            AppendLog("Saved Owner/tariff settings cleared.");
        }
    }

    private async Task StartTestAsync()
    {
        if (_harnessProcess is not null)
        {
            MessageBox.Show(this, "A test is already running. Stop it before starting another run.",
                "GameNet 5 Test Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LauncherSettings settings;
        try
        {
            settings = ReadAndValidateSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Check test settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true, "Checking and creating a fresh disposable PostgreSQL database…");
        _log.Clear();
        AppendLog("Starting a new test run. No existing database is reused or dropped.");
        try
        {
            SaveOrClearSettings(settings);
            AssertProcessOnlyPreconditions();
            var port = int.Parse(settings.PostgreSqlPort, System.Globalization.CultureInfo.InvariantCulture);
            var psqlPath = FindPsql();
            var adminPassword = _adminPassword.Text;
            await ExecutePsqlAsync(psqlPath, port, settings.PostgreSqlAdminUser, adminPassword,
                "SELECT current_user, current_database();", "PostgreSQL login check");
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var databaseName = $"gamenet_slice1_test_{suffix}";
            var roleName = $"gamenet_slice1_app_{suffix}";
            var rolePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

            await ExecutePsqlAsync(psqlPath, port, settings.PostgreSqlAdminUser, adminPassword,
                $"CREATE ROLE {roleName} LOGIN PASSWORD '{rolePassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;",
                "Create restricted disposable database role");
            try
            {
                await ExecutePsqlAsync(psqlPath, port, settings.PostgreSqlAdminUser, adminPassword,
                    $"CREATE DATABASE {databaseName} OWNER {roleName};", "Create disposable test database");
            }
            catch
            {
                try
                {
                    await ExecutePsqlAsync(psqlPath, port, settings.PostgreSqlAdminUser, adminPassword,
                        $"DROP ROLE IF EXISTS {roleName};", "Clean up the newly created unused test role");
                }
                catch
                {
                    AppendLog("Warning: PostgreSQL could not remove the new unused test role after database creation failed. Keep this diagnostic with the test evidence.");
                }
                throw;
            }

            adminPassword = string.Empty;
            _adminPassword.Clear();

            var builder = new DbConnectionStringBuilder
            {
                ["Host"] = "127.0.0.1",
                ["Port"] = port,
                ["Database"] = databaseName,
                ["Username"] = roleName,
                ["Password"] = rolePassword,
                ["SSL Mode"] = "Disable",
                ["Pooling"] = false
            };
            var connectionString = builder.ConnectionString;
            builder.Clear();
            rolePassword = string.Empty;
            AppendLog($"Created disposable database '{databaseName}' and restricted role '{roleName}'.");
            AppendLog("Database credentials will be delivered to the short-lived harness over redirected stdin, never through command-line arguments.");
            var testedSha = ReadTestedCheckoutSha();
            var stopPath = Path.Combine(Path.GetTempPath(), "gamenet5-test-stop-" + Guid.NewGuid().ToString("N") + ".signal");
            var reopenPath = Path.Combine(Path.GetTempPath(), "gamenet5-test-reopen-" + Guid.NewGuid().ToString("N") + ".signal");
            _stopSignalPath = stopPath;
            _reopenSignalPath = reopenPath;
            var config = new Dictionary<string, object?>
            {
                ["testedCheckoutSha"] = testedSha,
                ["connectionString"] = connectionString,
                ["confirmedDisposableDatabase"] = true,
                ["ownerUsername"] = settings.OwnerUsername,
                ["ownerDisplayName"] = settings.OwnerDisplayName,
                ["ownerPassword"] = settings.OwnerPassword,
                ["hourlyRateToman"] = long.Parse(settings.HourlyRateToman, System.Globalization.CultureInfo.InvariantCulture)
            };
            LaunchHarness(config, stopPath, reopenPath);
            connectionString = string.Empty;
            settings.OwnerPassword = string.Empty;
            _ownerPassword.Clear();
            _adminPassword.Clear();
            _confirmDisposable.Checked = false;
            SetBusy(true, "Test processes are starting. The GameNet Desktop will open separately.");
            AppendLog("When the WPF Desktop opens, sign in with the throwaway Owner settings above and run Start → wait about 2 minutes → End.");
            AppendLog("To finish and stop only the test processes, click Stop test processes here.");
        }
        catch (Exception exception)
        {
            _adminPassword.Clear();
            AppendLog($"FAILED: {exception.Message}");
            SetBusy(false, "Test setup failed safely. Review the diagnostic log; no existing database was dropped.");
            MessageBox.Show(this, exception.Message, "GameNet 5 test could not start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void AssertProcessOnlyPreconditions()
    {
        var programDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager");
        var protectedMarkers = new[]
        {
            Path.Combine(programDataRoot, "Config", "server.json"),
            Path.Combine(programDataRoot, "Config", "server-secrets.bin"),
            Path.Combine(programDataRoot, "Secrets", "server-secrets.v1.dpapi")
        };
        if (protectedMarkers.Any(File.Exists))
            throw new InvalidOperationException("Existing GameNet Manager Server state was found under ProgramData. The test launcher will not overlap or modify it. Use a clean test environment or review the existing installation first.");

        var scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        foreach (var serviceName in new[] { "GameNet 5 Server", "GameNet 5 Agent" })
        {
            var start = new ProcessStartInfo(scPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("query");
            start.ArgumentList.Add(serviceName);
            using var process = new Process { StartInfo = start };
            if (!process.Start())
                throw new InvalidOperationException("Could not complete the read-only GameNet service preflight. No test database was created.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("The read-only service preflight timed out. No test database was created.");
            }
            var stdout = awaitIfCompleted(stdoutTask);
            var stderr = awaitIfCompleted(stderrTask);
            if (process.ExitCode == 0)
                throw new InvalidOperationException($"The service '{serviceName}' already exists. This process-only test will not install, stop, or modify it.");
            if (process.ExitCode != 1060)
                throw new InvalidOperationException($"The launcher could not reliably verify whether service '{serviceName}' exists. {SanitizePostgresError(stderr + " " + stdout)}");
        }

        static string awaitIfCompleted(Task<string> task) => task.GetAwaiter().GetResult();
    }

    private static string FindPsql()
    {
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL", "17", "bin", "psql.exe"),
            string.IsNullOrWhiteSpace(programFilesX86) ? string.Empty : Path.Combine(programFilesX86, "PostgreSQL", "17", "bin", "psql.exe")
        };
        var path = candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
        if (path is null)
            throw new FileNotFoundException("PostgreSQL 17 command-line tools (psql.exe) were not found. Install/repair PostgreSQL 17 on this PC; GameNet will not change the PostgreSQL service.");
        return path;
    }

    private async Task ExecutePsqlAsync(string psql, int port, string username, string password, string sql, string operation)
    {
        var start = new ProcessStartInfo(psql)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var arg in new[] { "-X", "-w", "-h", "127.0.0.1", "-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "-U", username, "-d", "postgres", "-v", "ON_ERROR_STOP=1" })
            start.ArgumentList.Add(arg);
        foreach (var key in start.Environment.Keys.ToArray())
        {
            if (key.StartsWith("PG", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        }
        start.Environment["PGPASSWORD"] = password;
        start.Environment["PGSSLMODE"] = "disable";
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"{operation} could not start.");
        start.Environment.Remove("PGPASSWORD");
        start.Environment.Remove("PGSSLMODE");
        await process.StandardInput.WriteLineAsync(sql);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        start.Environment.Remove("PGPASSWORD");
        if (process.ExitCode != 0)
        {
            var detail = SanitizePostgresError(stderr);
            throw new InvalidOperationException($"{operation} failed. Verify local PostgreSQL permissions/credentials. {detail}");
        }
        if (operation == "PostgreSQL login check")
            AppendSafeAdminCheck(stdout);
    }

    private static string SanitizePostgresError(string stderr)
    {
        var safe = stderr.ReplaceLineEndings(" ").Trim();
        foreach (var sensitive in new[] { "password", "Password", "pwd", "Pwd" })
        {
            var token = sensitive + "=";
            var start = safe.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
                safe = safe[..start] + "[redacted]";
        }
        return safe.Length > 450 ? safe[..450] : safe;
    }

    private void AppendSafeAdminCheck(string stdout)
    {
        // Intentionally don't print the returned data: it identifies the admin role and DB.
        AppendLog("Connected to local PostgreSQL. Administrative login check passed.");
    }

    private string ReadTestedCheckoutSha()
    {
        var root = GetInstallRoot();
        var manifestPath = Path.Combine(root, "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var sha = manifest.RootElement.GetProperty("source").GetProperty("testedCheckoutSha").GetString();
        if (sha is null || !System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{40}$"))
            throw new InvalidDataException("The installed test payload has no valid exact source SHA.");
        return sha;
    }

    private void LaunchHarness(Dictionary<string, object?> config, string stopPath, string reopenPath)
    {
        var root = GetInstallRoot();
        var pwsh = Path.Combine(root, "Setup", "PowerShell", "pwsh.exe");
        var script = Path.Combine(root, "Tools", "run-slice1-process-acceptance.ps1");
        if (!File.Exists(pwsh) || !File.Exists(script))
            throw new FileNotFoundException("The test payload is incomplete; required harness files are missing.");
        var start = new ProcessStartInfo(pwsh)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", script, "-GuiMode", "-StopSignalPath", stopPath, "-ReopenSignalPath", reopenPath
        })
            start.ArgumentList.Add(arg);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog("ERROR: " + e.Data); };
        process.Exited += (_, _) => BeginInvoke((MethodInvoker)(() =>
        {
            var exitCode = process.ExitCode;
            _harnessProcess?.Dispose();
            _harnessProcess = null;
            var signalPath = _stopSignalPath;
            var reopenSignalPath = _reopenSignalPath;
            _stopSignalPath = null;
            _reopenSignalPath = null;
            _testWorkspaceReady = false;
            _reopenDesktopButton.Enabled = false;
            foreach (var path in new[] { signalPath, reopenSignalPath })
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
            if (!_closing)
            {
                SetBusy(false, exitCode == 0
                    ? "Test processes stopped cleanly."
                    : $"Test harness exited with code {exitCode}. Review the diagnostic log.");
                AppendLog($"Harness exited (code {exitCode}).");
            }
        }));
        if (!process.Start())
            throw new InvalidOperationException("Could not start the hidden test harness.");
        _harnessProcess = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var json = JsonSerializer.Serialize(config);
        try
        {
            process.StandardInput.Write(json);
            process.StandardInput.Close();
        }
        finally
        {
            json = string.Empty;
            config["connectionString"] = null;
            config["ownerPassword"] = null;
        }
    }

    private static string GetInstallRoot() =>
        Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName
        ?? throw new InvalidOperationException("Could not determine the test installation folder.");

    private void RequestStop()
    {
        if (_harnessProcess is null || _stopSignalPath is null)
            return;
        try
        {
            File.WriteAllText(_stopSignalPath, "Stop requested by operator.");
            _stopButton.Enabled = false;
            _reopenDesktopButton.Enabled = false;
            _status.Text = "Stopping the Server, Agent and Desktop processes started for this test…";
            AppendLog("Stop requested. Existing business/legacy processes are not targeted.");
        }
        catch (Exception exception)
        {
            AppendLog($"Could not send a graceful stop signal: {exception.Message}");
        }
    }

    private void RequestReopenDesktop()
    {
        if (!_testWorkspaceReady || _harnessProcess is null || _harnessProcess.HasExited ||
            string.IsNullOrWhiteSpace(_reopenSignalPath))
            return;

        try
        {
            File.WriteAllText(_reopenSignalPath, "Reopen Desktop against the existing isolated test workspace.");
            _reopenDesktopButton.Enabled = false;
            _status.Text = "Checking whether the Desktop is closed, then reopening it against the same test database…";
            AppendLog("Desktop reopen requested. Server, Agent and database remain the same; no new database is created.");
        }
        catch (Exception exception)
        {
            _reopenDesktopButton.Enabled = true;
            AppendLog($"Could not send the Desktop reopen request: {exception.Message}");
        }
    }

    private void ForgetSettings()
    {
        if (MessageBox.Show(this, "Delete the encrypted saved Owner/tariff settings for this Windows user?",
                "Forget saved settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        SettingsStore.Clear();
        _ownerPassword.Clear();
        AppendLog("Saved encrypted settings removed.");
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_harnessProcess is not null && !_harnessProcess.HasExited)
        {
            var choice = MessageBox.Show(this,
                "A test is still running. Stop its temporary GameNet processes and close the launcher?",
                "GameNet 5 Test Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closing = true;
            RequestStop();
            e.Cancel = true;
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) =>
            {
                if (_harnessProcess is null || _harnessProcess.HasExited)
                {
                    timer.Stop();
                    timer.Dispose();
                    Close();
                }
            };
            timer.Start();
            return;
        }
        _closing = true;
    }

    private void SetBusy(bool busy, string status)
    {
        _startButton.Enabled = !busy;
        _stopButton.Enabled = busy && _harnessProcess is not null;
        _reopenDesktopButton.Enabled = busy && _testWorkspaceReady && _harnessProcess is not null;
        _clearSettingsButton.Enabled = !busy;
        _port.Enabled = !busy;
        _adminUser.Enabled = !busy;
        _adminPassword.Enabled = !busy;
        _ownerUsername.Enabled = !busy;
        _ownerDisplay.Enabled = !busy;
        _ownerPassword.Enabled = !busy;
        _hourlyRate.Enabled = !busy;
        _confirmDisposable.Enabled = !busy;
        _rememberSettings.Enabled = !busy;
        _status.Text = status;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)(() => AppendLog(message)));
            return;
        }
        if (message == "WPF_ACCEPTANCE_READY=true")
        {
            _testWorkspaceReady = true;
            _reopenDesktopButton.Enabled = _harnessProcess is not null && !_harnessProcess.HasExited;
            _status.Text = "Test ready. Close only the Desktop window, then use Reopen Desktop to check saved results.";
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] Test workspace ready. Keep this Launcher open; close the Desktop window and use Reopen Desktop (same test data) to verify persistence.{Environment.NewLine}");
            return;
        }
        if (message == "DESKTOP_REOPENED=true")
        {
            _reopenDesktopButton.Enabled = _testWorkspaceReady && _harnessProcess is not null && !_harnessProcess.HasExited;
            _status.Text = "Desktop reopened against the same isolated Server and database. Sign in again and refresh recent cash checkouts.";
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] Desktop reopened. Sign in again and verify the same paid invoice appears in recent cash checkouts.{Environment.NewLine}");
            return;
        }
        if (message.StartsWith("DESKTOP_REOPEN_BLOCKED=", StringComparison.Ordinal))
        {
            _reopenDesktopButton.Enabled = _testWorkspaceReady && _harnessProcess is not null && !_harnessProcess.HasExited;
            _status.Text = "Close the existing Desktop window before reopening it.";
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message.Substring("DESKTOP_REOPEN_BLOCKED=".Length)}{Environment.NewLine}");
            return;
        }
        if (message.StartsWith("DESKTOP_REOPEN_FAILED=", StringComparison.Ordinal))
        {
            _reopenDesktopButton.Enabled = _testWorkspaceReady && _harnessProcess is not null && !_harnessProcess.HasExited;
            _status.Text = "Desktop did not reopen. Review the diagnostic message; existing test data is unchanged.";
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message.Substring("DESKTOP_REOPEN_FAILED=".Length)}{Environment.NewLine}");
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
