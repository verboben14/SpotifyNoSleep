using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpotifyNoSleep.Installer
{
    internal static class InstallerCore
    {
        public const string AppName = "SpotifyNoSleep";
        public const string AppVersion = "1.0.0";
        public const string Publisher = "verboben14";
        public const string EmbeddedResourceName = "SpotifyNoSleep.exe";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpotifyNoSleep";

        public static string GetInstallDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            }
            return Path.Combine(localAppData, AppName);
        }

        public static string GetInstalledExePath()
        {
            return Path.Combine(GetInstallDirectory(), "SpotifyNoSleep.exe");
        }

        public static string GetInstalledUninstallerPath()
        {
            return Path.Combine(GetInstallDirectory(), "SpotifyNoSleep-Uninstaller.exe");
        }

        public static string GetStartMenuFolder()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(programs, AppName);
        }

        public static bool IsInstalled()
        {
            return File.Exists(GetInstalledExePath());
        }

        public static void StopRunningInstances()
        {
            Process[] procs = Process.GetProcessesByName("SpotifyNoSleep");
            for (int i = 0; i < procs.Length; i++)
            {
                try
                {
                    if (!procs[i].HasExited)
                    {
                        procs[i].Kill();
                        procs[i].WaitForExit(3000);
                    }
                }
                catch
                {
                }
                finally
                {
                    procs[i].Dispose();
                }
            }

            Thread.Sleep(250);
        }

        public static void Install(bool enableStartup, bool createStartMenu, bool launchAfter)
        {
            StopRunningInstances();

            string installDir = GetInstallDirectory();
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            string exePath = GetInstalledExePath();
            ExtractEmbeddedExe(exePath);

            // Copy installer as uninstaller (skip if currently running from the exact same path)
            string currentSetupExe = Assembly.GetExecutingAssembly().Location;
            string uninstallerPath = GetInstalledUninstallerPath();
            if (!string.Equals(Path.GetFullPath(currentSetupExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentSetupExe, uninstallerPath, true);
            }

            // Configure Windows Startup (current user, no admin needed)
            SetStartupRegistry(enableStartup, exePath);

            // Register in Windows Settings -> Apps -> Installed Apps
            RegisterUninstallEntry(installDir, exePath, uninstallerPath);

            if (createStartMenu)
            {
                CreateStartMenuShortcuts(exePath, uninstallerPath);
            }

            if (launchAfter)
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.WorkingDirectory = installDir;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
        }

        public static void Uninstall(bool removeUserData)
        {
            StopRunningInstances();
            SetStartupRegistry(false, string.Empty);
            RemoveScheduledTaskIfPresent();
            RemoveUninstallEntry();
            RemoveStartMenuShortcuts();

            string installDir = GetInstallDirectory();
            string installedExe = GetInstalledExePath();
            if (File.Exists(installedExe))
            {
                try
                {
                    File.Delete(installedExe);
                }
                catch
                {
                }
            }

            if (removeUserData)
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dataDir = Path.Combine(appData, AppName);
                if (Directory.Exists(dataDir))
                {
                    try
                    {
                        Directory.Delete(dataDir, true);
                    }
                    catch
                    {
                    }
                }
            }

            string currentExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string uninstallerPath = Path.GetFullPath(GetInstalledUninstallerPath());
            if (string.Equals(currentExe, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                // Schedule self-deletion after process exits cleanly
                ScheduleSelfDirectoryCleanup(installDir);
            }
            else if (Directory.Exists(installDir))
            {
                try
                {
                    Directory.Delete(installDir, true);
                }
                catch
                {
                }
            }
        }

        private static void ExtractEmbeddedExe(string destinationPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded SpotifyNoSleep.exe payload was not found inside this installer.");
                }

                using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        private static void SetStartupRegistry(bool enabled, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(AppName, string.Format("\"{0}\"", exePath), RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
        }

        private static void RegisterUninstallEntry(string installDir, string exePath, string uninstallerPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (key == null)
                {
                    return;
                }

                key.SetValue("DisplayName", AppName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue("DisplayIcon", exePath, RegistryValueKind.String);
                key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                key.SetValue("UninstallString", string.Format("\"{0}\" --uninstall", uninstallerPath), RegistryValueKind.String);
                key.SetValue("QuietUninstallString", string.Format("\"{0}\" --uninstall --silent", uninstallerPath), RegistryValueKind.String);
                key.SetValue("URLInfoAbout", "https://github.com/verboben14/SpotifyNoSleep", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", 128, RegistryValueKind.DWord);
            }
        }

        private static void RemoveUninstallEntry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
            }
            catch
            {
            }
        }

        private static void CreateStartMenuShortcuts(string exePath, string uninstallerPath)
        {
            try
            {
                string folder = GetStartMenuFolder();
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                CreateShortcut(
                    Path.Combine(folder, "SpotifyNoSleep.lnk"),
                    exePath,
                    string.Empty,
                    Path.GetDirectoryName(exePath),
                    "Keep Windows awake while Spotify is playing music");

                CreateShortcut(
                    Path.Combine(folder, "Uninstall SpotifyNoSleep.lnk"),
                    uninstallerPath,
                    "--uninstall",
                    Path.GetDirectoryName(uninstallerPath),
                    "Uninstall SpotifyNoSleep");
            }
            catch
            {
                // Shortcuts are convenience items; ignore if WScript.Shell is disabled.
            }
        }

        private static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string arguments,
            string workingDirectory,
            string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.ReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static void RemoveStartMenuShortcuts()
        {
            try
            {
                string folder = GetStartMenuFolder();
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch
            {
            }
        }

        private static void RemoveScheduledTaskIfPresent()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "schtasks.exe";
                psi.Arguments = "/Delete /TN \"SpotifyNoSleep\" /F";
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(3000);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ScheduleSelfDirectoryCleanup(string dir)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = string.Format("/C ping 127.0.0.1 -n 3 > nul & rmdir /S /Q \"{0}\"", dir);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch
            {
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly CheckBox _chkStartup;
        private readonly CheckBox _chkStartMenu;
        private readonly CheckBox _chkLaunch;
        private readonly Label _lblStatus;
        private readonly Button _btnInstall;
        private readonly Button _btnUninstall;
        private readonly Button _btnClose;

        public SetupForm(bool uninstallFocus)
        {
            Text = "SpotifyNoSleep Setup v" + InstallerCore.AppVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 325);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
            }

            // Green header banner
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 78;
            header.BackColor = Color.FromArgb(24, 24, 24);

            Panel accentBar = new Panel();
            accentBar.Dock = DockStyle.Bottom;
            accentBar.Height = 4;
            accentBar.BackColor = Color.FromArgb(29, 185, 84);
            header.Controls.Add(accentBar);

            Label lblTitle = new Label();
            lblTitle.Text = "SpotifyNoSleep";
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(20, 14);
            header.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Prevent Windows sleep, screen-off & lock screen while Spotify plays";
            lblSubtitle.ForeColor = Color.FromArgb(205, 212, 218);
            lblSubtitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblSubtitle.AutoSize = true;
            lblSubtitle.Location = new Point(22, 46);
            header.Controls.Add(lblSubtitle);

            Controls.Add(header);

            Label lblDesc = new Label();
            lblDesc.Text = "Installs SpotifyNoSleep to your user folder (no admin rights required) so it runs silently in the system tray whenever Windows is on.";
            lblDesc.Location = new Point(22, 92);
            lblDesc.Size = new Size(416, 42);
            lblDesc.ForeColor = Color.FromArgb(55, 65, 81);
            Controls.Add(lblDesc);

            _chkStartup = new CheckBox();
            _chkStartup.Text = "Start automatically when Windows starts (recommended)";
            _chkStartup.Checked = true;
            _chkStartup.AutoSize = true;
            _chkStartup.Location = new Point(24, 144);
            Controls.Add(_chkStartup);

            _chkStartMenu = new CheckBox();
            _chkStartMenu.Text = "Create Start Menu shortcut";
            _chkStartMenu.Checked = true;
            _chkStartMenu.AutoSize = true;
            _chkStartMenu.Location = new Point(24, 172);
            Controls.Add(_chkStartMenu);

            _chkLaunch = new CheckBox();
            _chkLaunch.Text = "Launch SpotifyNoSleep immediately after install";
            _chkLaunch.Checked = true;
            _chkLaunch.AutoSize = true;
            _chkLaunch.Location = new Point(24, 200);
            Controls.Add(_chkLaunch);

            _lblStatus = new Label();
            _lblStatus.Location = new Point(22, 234);
            _lblStatus.Size = new Size(416, 24);
            _lblStatus.ForeColor = Color.FromArgb(80, 90, 100);
            Controls.Add(_lblStatus);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 56;
            footer.BackColor = Color.FromArgb(245, 247, 249);

            _btnUninstall = new Button();
            _btnUninstall.Text = "Uninstall";
            _btnUninstall.Size = new Size(96, 32);
            _btnUninstall.Location = new Point(22, 12);
            _btnUninstall.FlatStyle = FlatStyle.System;
            _btnUninstall.Click += OnUninstallClick;
            footer.Controls.Add(_btnUninstall);

            _btnInstall = new Button();
            _btnInstall.Text = "Install";
            _btnInstall.Size = new Size(124, 32);
            _btnInstall.Location = new Point(226, 12);
            _btnInstall.BackColor = Color.FromArgb(29, 185, 84);
            _btnInstall.ForeColor = Color.White;
            _btnInstall.FlatStyle = FlatStyle.Flat;
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnInstall.Click += OnInstallClick;
            footer.Controls.Add(_btnInstall);

            _btnClose = new Button();
            _btnClose.Text = "Close";
            _btnClose.Size = new Size(82, 32);
            _btnClose.Location = new Point(358, 12);
            _btnClose.FlatStyle = FlatStyle.System;
            _btnClose.Click += delegate { Close(); };
            footer.Controls.Add(_btnClose);

            Controls.Add(footer);

            RefreshInstallState();
            if (uninstallFocus && _btnUninstall.Enabled)
            {
                AcceptButton = _btnUninstall;
            }
            else
            {
                AcceptButton = _btnInstall;
            }
        }

        private void RefreshInstallState()
        {
            bool installed = InstallerCore.IsInstalled();
            _btnUninstall.Enabled = installed;
            _btnInstall.Text = installed ? "Reinstall / Update" : "Install";
            _lblStatus.Text = installed
                ? "Status: Installed in " + InstallerCore.GetInstallDirectory()
                : "Install location: " + InstallerCore.GetInstallDirectory();
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            SetBusy(true);
            try
            {
                InstallerCore.Install(_chkStartup.Checked, _chkStartMenu.Checked, _chkLaunch.Checked);
                RefreshInstallState();
                MessageBox.Show(
                    this,
                    "SpotifyNoSleep has been installed successfully!\n\nLook for the SpotifyNoSleep icon in your Windows system tray (or the ^ hidden icons menu near the clock).",
                    "SpotifyNoSleep Installed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Installation failed:\n\n" + ex.Message,
                    "SpotifyNoSleep Setup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "Stop SpotifyNoSleep and remove it from your PC and Windows startup?",
                "Uninstall SpotifyNoSleep",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true);
            try
            {
                InstallerCore.Uninstall(true);
                RefreshInstallState();
                MessageBox.Show(
                    this,
                    "SpotifyNoSleep has been uninstalled.",
                    "Uninstall Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Uninstall failed:\n\n" + ex.Message,
                    "SpotifyNoSleep Uninstall Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _btnInstall.Enabled = !busy;
            _btnUninstall.Enabled = !busy && InstallerCore.IsInstalled();
            _btnClose.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
    }

    internal static class InstallerProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = false;
            bool installArg = false;
            bool uninstallArg = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i].Trim().ToLowerInvariant();
                    if (arg == "--silent" || arg == "/s" || arg == "-s")
                    {
                        silent = true;
                    }
                    else if (arg == "--install" || arg == "/i")
                    {
                        installArg = true;
                    }
                    else if (arg == "--uninstall" || arg == "/u")
                    {
                        uninstallArg = true;
                    }
                }
            }

            if (silent && uninstallArg)
            {
                try
                {
                    InstallerCore.Uninstall(true);
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            if (silent && installArg)
            {
                try
                {
                    InstallerCore.Install(true, true, true);
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(uninstallArg));
            return 0;
        }
    }
}
