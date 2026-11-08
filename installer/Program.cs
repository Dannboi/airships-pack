using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AirshipsInstaller
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--prelaunch")
            {
                try { return Core.PreLaunch(args[1], args.Length > 2 ? args[2] : "javaw"); }
                catch (Exception e) { Console.WriteLine("[Airships] pre-launch failed: " + e); return 2; }
            }
            if (args.Length >= 1 && args[0] == "--silent")   // --silent [dir] [maxMemMb] [--no-desktop]
            {
                try
                {
                    Core.Install(new Core.Options
                    {
                        Dir = args.Length > 1 ? args[1] : Config.DefaultInstallDir,
                        MaxMemMb = args.Length > 2 ? int.Parse(args[2]) : Core.RecommendedMb(Core.TotalRamGb()),
                        DesktopShortcut = !args.Contains("--no-desktop")
                    }, (m, p) => { if (m != null) Console.WriteLine(m); });
                    return 0;
                }
                catch (Exception e) { Console.WriteLine("install failed: " + e); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 1 && args[0] == "--uninstall")
            {
                string dir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                var r = MessageBox.Show("Uninstall " + Config.PackName + "?\n\nYour singleplayer worlds will be copied to\n" +
                                        "Documents\\Airships worlds backup first.\n\n(Click 'No' to delete them too.)",
                                        "Uninstall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return 0;
                if (Process.GetProcessesByName("prismlauncher").Any() || Process.GetProcessesByName("javaw").Any())
                    MessageBox.Show("Close the launcher and Minecraft first, then click OK.", "Uninstall");
                Core.Uninstall(dir, r == DialogResult.Yes);
                MessageBox.Show(Config.PackName + " was removed.", "Uninstall");
                return 0;
            }
            Application.Run(new MainForm());
            return 0;
        }
    }

    class MainForm : Form
    {
        readonly TextBox dirBox = new TextBox();
        readonly ComboBox ramBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox desktop = new CheckBox { Text = "Put a shortcut on my desktop", Checked = true, AutoSize = true };
        readonly Button installBtn = new Button { Text = "Install", Height = 40, Width = 150 };
        readonly ProgressBar bar = new ProgressBar { Height = 22, Maximum = 100 };
        readonly Label status = new Label { AutoSize = false, Height = 40, Text = "" };
        readonly Panel options = new Panel();

        public MainForm()
        {
            Text = Config.PackName + " - Installer";
            Icon = new Icon(new MemoryStream(Core.Resource("app.ico")));
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 560);
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.White;

            var logo = new PictureBox { Image = Image.FromStream(new MemoryStream(Core.Resource("logo.png"))), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(20, 12, 480, 150) };
            var intro = new Label
            {
                Bounds = new Rectangle(20, 170, 480, 70),
                Text = "This sets up everything you need to play: the launcher, Java and the modpack. " +
                       "The pack keeps itself up to date - every time you press Play it grabs whatever changed."
            };
            Controls.Add(logo); Controls.Add(intro);

            options.Bounds = new Rectangle(20, 245, 480, 180);
            var dirLbl = new Label { Text = "Install folder:", Bounds = new Rectangle(0, 0, 480, 22) };
            dirBox.Bounds = new Rectangle(0, 24, 380, 26); dirBox.Text = Config.DefaultInstallDir;
            var browse = new Button { Text = "Browse...", Bounds = new Rectangle(388, 22, 92, 30) };
            browse.Click += (s, e) =>
            {
                using (var f = new FolderBrowserDialog { Description = "Choose where to install", SelectedPath = dirBox.Text })
                    if (f.ShowDialog(this) == DialogResult.OK) dirBox.Text = Path.Combine(f.SelectedPath, "AirshipsConquerTheMines");
            };

            double gb = Core.TotalRamGb();
            int rec = Core.RecommendedMb(gb);
            var ramLbl = new Label { Text = $"Memory for Minecraft (your PC has {gb:0} GB):", Bounds = new Rectangle(0, 64, 480, 22) };
            ramBox.Bounds = new Rectangle(0, 88, 220, 26);
            foreach (int g in new[] { 4, 5, 6, 7, 8, 10, 12, 14, 16 })
                if (g * 1024 <= Math.Max(gb * 1024 - 2048, 4096)) ramBox.Items.Add(g + " GB" + (g * 1024 == rec ? "  (recommended)" : ""));
            ramBox.SelectedIndex = Math.Max(0, ramBox.Items.Cast<string>().ToList().FindIndex(x => x.Contains("recommended")));
            var warn = new Label { ForeColor = Color.DarkRed, Bounds = new Rectangle(230, 86, 250, 44), Text = gb < 11 ? "This pack is big - with under 12 GB of RAM it may run slowly." : "" };
            desktop.Location = new Point(0, 135);
            options.Controls.AddRange(new Control[] { dirLbl, dirBox, browse, ramLbl, ramBox, warn, desktop });
            Controls.Add(options);

            bar.Bounds = new Rectangle(20, 435, 480, 22);
            status.Bounds = new Rectangle(20, 460, 480, 40);
            installBtn.Location = new Point(350, 505);
            installBtn.Click += async (s, e) => await OnInstallClicked();
            Controls.AddRange(new Control[] { bar, status, installBtn });
        }

        bool finished;

        async Task OnInstallClicked()
        {
            if (finished) { Play(); return; }
            var o = new Core.Options
            {
                Dir = dirBox.Text.Trim(),
                MaxMemMb = int.Parse(((string)ramBox.SelectedItem).Split(' ')[0]) * 1024,
                DesktopShortcut = desktop.Checked
            };
            installBtn.Enabled = false; options.Enabled = false;
            try
            {
                await Task.Run(() => Core.Install(o, (msg, pct) => BeginInvoke((Action)(() =>
                {
                    if (msg != null) status.Text = msg;
                    bar.Value = Math.Max(0, Math.Min(100, pct));
                }))));
                finished = true;
                status.Text = "All set! Press Play. The first time, you'll sign in with your Microsoft account, " +
                              "then the mods download (about 1.5 GB, only once).";
                installBtn.Text = "Play"; installBtn.Enabled = true;
                installDir = o.Dir;
            }
            catch (Exception ex)
            {
                status.Text = "Something went wrong.";
                MessageBox.Show(this, "Install failed:\n\n" + ex.Message + "\n\nCheck your internet connection and try again.",
                                "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                installBtn.Enabled = true; options.Enabled = true;
            }
        }

        string installDir;

        void Play()
        {
            Process.Start(new ProcessStartInfo(Path.Combine(installDir, "prismlauncher.exe"), "--launch " + Config.InstanceId)
            { WorkingDirectory = installDir, UseShellExecute = false });
            Close();
        }
    }
}
