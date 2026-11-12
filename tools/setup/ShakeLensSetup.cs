// ============================================================================
//  ShakeLens Setup - installer for ShakeLens
//
//  Built with the .NET Framework compiler that ships with Windows, so the
//  installer itself needs no runtime of its own.  The application is stored
//  inside as a compressed resource and unpacked on the first run.
//
//    ShakeLens Setup.exe             install
//    ShakeLens Setup.exe --uninstall remove
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ShakeLensSetup
{
    internal static class Program
    {
        private const string AppName = "ShakeLens";
        private const string Version = "1.0.0";
        private const string ResourceName = "ShakeLens.Payload";

        internal static string InstallDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", AppName);
            }
        }

        internal static string StartMenuDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs");
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            bool uninstall = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (args[i].StartsWith("--delete", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    DeleteFolder(args[i + 1].Trim('"'));
                    return;
                }
            }

            if (uninstall)
            {
                Uninstall();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        // ------------------------------------------------------------ unpack --

        internal static void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "ShakeLensSetup.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        private static void Extract(string target)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidOperationException("The payload is missing.");

                using (DeflateStream deflate = new DeflateStream(stream, CompressionMode.Decompress))
                using (BinaryReader reader = new BinaryReader(deflate, Encoding.UTF8))
                {
                    byte[] magic = reader.ReadBytes(4);
                    if (magic.Length < 4 || Encoding.ASCII.GetString(magic) != "SLP1")
                        throw new InvalidDataException("The payload is damaged.");

                    int count = reader.ReadInt32();
                    WriteLog("unpacking " + count + " files into " + target);

                    for (int i = 0; i < count; i++)
                    {
                        int nameLength = reader.ReadInt32();
                        string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                        long size = reader.ReadInt64();
                        int compressed = reader.ReadInt32();

                        byte[] data = reader.ReadBytes(compressed);
                        if (data.Length != compressed)
                            throw new EndOfStreamException("The payload ends at file " + name);

                        string path = Path.Combine(target, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));

                        using (FileStream file = File.Create(path))
                        using (MemoryStream memory = new MemoryStream(data))
                        using (DeflateStream inner = new DeflateStream(memory, CompressionMode.Decompress))
                        {
                            inner.CopyTo(file);
                        }
                    }
                    WriteLog("unpacked " + count + " files");
                }
            }
        }

        // ----------------------------------------------------------- install --

        internal static void Install(string target, Action<int, string> progress)
        {
            WriteLog("install started");
            progress(5, "Preparing...");
            Directory.CreateDirectory(target);

            progress(20, "Unpacking ShakeLens...");
            Extract(target);

            progress(70, "Creating shortcuts...");
            string exe = Path.Combine(target, AppName + ".exe");
            CreateShortcut(Path.Combine(StartMenuDirectory, AppName + ".lnk"), exe);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), AppName + ".lnk"),
                exe);

            progress(85, "Writing the uninstall information...");
            WriteUninstallInfo(target);

            progress(100, "Done.");
            WriteLog("install finished");
        }

        private static void WriteUninstallInfo(string target)
        {
            string uninstaller = Path.Combine(target, "Uninstall " + AppName + ".exe");
            File.Copy(Application.ExecutablePath, uninstaller, true);

            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                if (key == null) return;
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "ShakeLens");
                key.SetValue("DisplayIcon", Path.Combine(target, AppName + ".exe"));
                key.SetValue("InstallLocation", target);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" --uninstall --quiet");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                key.SetValue("EstimatedSize", 150000);
            }
        }

        private static void CreateShortcut(string linkPath, string target)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
                IShellLinkW link = (IShellLinkW)new ShellLinkClass();
                link.SetPath(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(AppName + " - shake the cursor to open Google Lens");
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(linkPath, true);
            }
            catch
            {
                // a missing shortcut must never abort the installation
            }
        }

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        key.SetValue(AppName, "\"" + Path.Combine(InstallDirectory, AppName + ".exe") + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }

        // ---------------------------------------------------------- uninstall --

        private static void Uninstall()
        {
            bool quiet = false;
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
            }

            try
            {
                try
                {
                    string link = Path.Combine(StartMenuDirectory, AppName + ".lnk");
                    if (File.Exists(link)) File.Delete(link);
                    string desktop = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), AppName + ".lnk");
                    if (File.Exists(desktop)) File.Delete(desktop);
                }
                catch { }

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
                    using (Microsoft.Win32.RegistryKey run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (run != null) run.DeleteValue(AppName, false);
                    }
                }
                catch { }

                string directory = InstallDirectory;
                string self = Application.ExecutablePath;

                if (directory.EndsWith(AppName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(self) &&
                    self.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    // the running file cannot be deleted, so a copy does the job
                    string helper = Path.Combine(Path.GetTempPath(),
                        "ShakeLensUninstall_" + Guid.NewGuid().ToString("N") + ".exe");
                    try
                    {
                        File.Copy(self, helper, true);
                        Process.Start(new ProcessStartInfo(helper,
                            "--delete \"" + directory + "\"") { UseShellExecute = false });
                    }
                    catch { }
                }
                else
                {
                    try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
                }

                if (!quiet)
                {
                    MessageBox.Show(AppName + " has been removed.", AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show("The removal did not finish:\n\n" + ex.Message, AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>Called by the temporary helper to delete the folder.</summary>
        public static void DeleteFolder(string directory)
        {
            try
            {
                for (int attempt = 0; attempt < 10 && Directory.Exists(directory); attempt++)
                {
                    try
                    {
                        Directory.Delete(directory, true);
                    }
                    catch
                    {
                        Thread.Sleep(400);
                    }
                }
            }
            catch { }
        }
    }

    // ============================================================== the UI ===

    internal sealed class SetupForm : Form
    {
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Button _finish;
        private readonly CheckBox _autoStart;
        private readonly CheckBox _launch;
        private readonly PictureBox _logo;
        private bool _installed;

        public SetupForm()
        {
            Text = "ShakeLens Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 230);
            BackColor = Color.FromArgb(0x12, 0x15, 0x1C);
            ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5);
            MaximizeBox = false;
            MinimizeBox = false;

            _logo = new PictureBox
            {
                Size = new Size(56, 56),
                Location = new Point(24, 24),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(0x1E, 0x23, 0x2E)
            };
            Controls.Add(_logo);

            Label title = new Label
            {
                Text = "ShakeLens",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(96, 24),
                AutoSize = true
            };
            Controls.Add(title);

            Label sub = new Label
            {
                Text = "Shake the cursor to send any part of the screen to Google Lens.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(0x9A, 0xA4, 0xB6),
                Location = new Point(97, 58),
                AutoSize = true
            };
            Controls.Add(sub);

            _status = new Label
            {
                Text = "Preparing...",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5),
                Location = new Point(26, 108),
                AutoSize = true
            };
            Controls.Add(_status);

            _progress = new ProgressBar
            {
                Location = new Point(26, 132),
                Size = new Size(378, 6),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };
            Controls.Add(_progress);

            _autoStart = new CheckBox
            {
                Text = "Start ShakeLens automatically with Windows",
                Checked = true,
                Location = new Point(26, 152),
                ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5),
                AutoSize = true,
                Visible = false
            };
            Controls.Add(_autoStart);

            _launch = new CheckBox
            {
                Text = "Launch ShakeLens now",
                Checked = true,
                Location = new Point(26, 178),
                ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5),
                AutoSize = true,
                Visible = false
            };
            Controls.Add(_launch);

            _finish = new Button
            {
                Text = "Finish",
                Location = new Point(324, 186),
                Size = new Size(80, 28),
                Visible = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0x4C, 0x8D, 0xFF),
                ForeColor = Color.White
            };
            _finish.Click += OnFinish;
            Controls.Add(_finish);

            Shown += (s, e) => StartInstall();
        }

        private void StartInstall()
        {
            Thread worker = new Thread(() =>
            {
                try
                {
                    Program.Install(Program.InstallDirectory, Report);
                }
                catch (Exception ex)
                {
                    Program.WriteLog("install failed: " + ex);
                    BeginInvoke(new Action(() =>
                    {
                        _status.Text = "The installation failed: " + ex.Message;
                        _progress.Style = ProgressBarStyle.Marquee;
                    }));
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    _status.Text = "ShakeLens has been installed.";
                    _progress.Value = 100;
                    _autoStart.Visible = true;
                    _launch.Visible = true;
                    _finish.Visible = true;
                    _finish.Focus();
                }));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Report(int percent, string message)
        {
            BeginInvoke(new Action(() =>
            {
                _status.Text = message;
                _progress.Value = Math.Max(0, Math.Min(100, percent));
            }));
        }

        private void OnFinish(object sender, EventArgs e)
        {
            try
            {
                Program.SetAutoStart(_autoStart.Checked);
            }
            catch { }

            if (_launch.Checked)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(
                        Path.Combine(Program.InstallDirectory, "ShakeLens.exe")) { UseShellExecute = true });
                }
                catch { }
            }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // the little lens logo in the corner
            using (Graphics g = e.Graphics)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF box = new RectangleF(24, 24, 56, 56);
                float cx = box.Left + box.Width * 0.43f;
                float cy = box.Top + box.Height * 0.42f;
                float ro = box.Width * 0.34f;
                float ri = box.Width * 0.19f;
                float w = ro - ri;

                DrawArc(g, box, cx, cy, ro, ri, 0, -90, Color.FromArgb(234, 67, 53));
                DrawArc(g, box, cx, cy, ro, ri, -90, -90, Color.FromArgb(251, 188, 5));
                DrawArc(g, box, cx, cy, ro, ri, 180, -90, Color.FromArgb(52, 168, 83));
                DrawArc(g, box, cx, cy, ro, ri, 90, -90, Color.FromArgb(66, 133, 244));

                float angle = 45f * (float)Math.PI / 180f;
                using (Pen pen = new Pen(Color.FromArgb(66, 133, 244), w))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen,
                        cx + (float)Math.Cos(angle) * ((ro + ri) / 2f),
                        cy + (float)Math.Sin(angle) * ((ro + ri) / 2f),
                        cx + (float)Math.Cos(angle) * (box.Width * 0.70f),
                        cy + (float)Math.Sin(angle) * (box.Width * 0.70f));
                }
            }
        }

        private static void DrawArc(Graphics g, RectangleF box, float cx, float cy,
                                    float ro, float ri, float from, float sweep, Color color)
        {
            PointF[] points = new PointF[42];
            for (int i = 0; i <= 20; i++)
            {
                float a = (from + sweep * i / 20f) * (float)Math.PI / 180f;
                points[i] = new PointF(cx + (float)Math.Cos(a) * ro, cy + (float)Math.Sin(a) * ro);
            }
            for (int i = 20; i >= 0; i--)
            {
                float a = (from + sweep * i / 20f) * (float)Math.PI / 180f;
                points[21 + (20 - i)] = new PointF(cx + (float)Math.Cos(a) * ri, cy + (float)Math.Sin(a) * ri);
            }
            using (Brush brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, points);
            }
        }
    }

    // =========================================================== shell link ===

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkClass { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch,
                     IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short piHotkey);
        void SetHotkey(short iHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                  [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
