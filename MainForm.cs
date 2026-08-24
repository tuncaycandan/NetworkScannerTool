using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetworkScannerTool
{
    public sealed class MainForm : Form
    {
        private string T(string tr, string en)
        {
            return englishMode ? en : tr;
        }

        private string GetSshExePath()
        {
            string windows =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows);

            // 64-bit Windows üzerinde 32-bit uygulamaysa,
            // gerçek System32'ye Sysnative üzerinden eriş.
            if (Environment.Is64BitOperatingSystem &&
                !Environment.Is64BitProcess)
            {
                return Path.Combine(
                    windows,
                    "Sysnative",
                    "OpenSSH",
                    "ssh.exe");
            }

            return Path.Combine(
                windows,
                "System32",
                "OpenSSH",
                "ssh.exe");
        }

        private bool ShowOpenSshInstallDialog()
        {
            using (var form = new Form())
            {
                form.Text = T("SSH Kullanılamıyor", "SSH Unavailable");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(410, 165);
                form.Font = new Font("Segoe UI", 9F);

                var icon = new PictureBox
                {
                    Location = new Point(22, 28),
                    Size = new Size(32, 32),
                    Image = SystemIcons.Information.ToBitmap(),
                    SizeMode = PictureBoxSizeMode.StretchImage
                };

                var text = new Label
                {
                    Location = new Point(70, 22),
                    Size = new Size(315, 75),
                    Text =
                        T("Windows OpenSSH Client yüklü değil.\r\n\r\n",
                          "Windows OpenSSH Client is not installed.\r\n\r\n") +
                        T("OpenSSH İstemcisi şimdi yüklensin mi?\r\n\r\n",
                          "Install the OpenSSH Client now?\r\n\r\n") +
                        T("Bu işlem yönetici yetkisi gerektirir.",
                          "This operation requires administrator privileges.")
                };

                var installButton = new Button
                {
                    Text = T("OpenSSH Yükle", "Install OpenSSH"),
                    Location = new Point(155, 120),
                    Size = new Size(120, 30),
                    DialogResult = DialogResult.OK
                };

                var cancelButton = new Button
                {
                    Text = T("İptal", "Cancel"),
                    Location = new Point(285, 120),
                    Size = new Size(100, 30),
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.Add(icon);
                form.Controls.Add(text);
                form.Controls.Add(installButton);
                form.Controls.Add(cancelButton);

                form.AcceptButton = installButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog(this) == DialogResult.OK;
            }
        }

        private string ShowSshLoginDialog(string ip)
        {
            using (var form = new Form())
            {
                form.Text = T("SSH Bağlantısı", "SSH Connection");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(390, 155);
                form.Font = new Font("Segoe UI", 9F);

                var ipLabel = new Label
                {
                    Text = T("IP Adresi:", "IP Address:"),
                    Location = new Point(25, 25),
                    Size = new Size(90, 22)
                };

                var ipValue = new Label
                {
                    Text = ip,
                    Location = new Point(120, 25),
                    Size = new Size(235, 22),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };

                var userLabel = new Label
                {
                    Text = T("Kullanıcı Adı:", "Username:"),
                    Location = new Point(25, 62),
                    Size = new Size(90, 22)
                };

                var userText = new TextBox
                {
                    Location = new Point(120, 59),
                    Size = new Size(235, 24)
                };

                var connectButton = new Button
                {
                    Text = T("Bağlan", "Connect"),
                    Location = new Point(165, 105),
                    Size = new Size(90, 30),
                    DialogResult = DialogResult.OK
                };

                var cancelButton = new Button
                {
                    Text = T("İptal", "Cancel"),
                    Location = new Point(265, 105),
                    Size = new Size(90, 30),
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.Add(ipLabel);
                form.Controls.Add(ipValue);
                form.Controls.Add(userLabel);
                form.Controls.Add(userText);
                form.Controls.Add(connectButton);
                form.Controls.Add(cancelButton);

                form.AcceptButton = connectButton;
                form.CancelButton = cancelButton;

                form.Shown += (s, e) =>
                {
                    userText.Focus();
                };

                if (form.ShowDialog(this) != DialogResult.OK)
                    return null;

                string username = userText.Text.Trim();

                if (string.IsNullOrWhiteSpace(username))
                    return null;

                return username;
            }
        }

        private async Task InstallOpenSshClientAsync()
        {
            try
            {
                statusLabel.Text = T("OpenSSH Client yükleniyor...", "Installing OpenSSH Client...");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -ExecutionPolicy Bypass -Command " +
                        "\"$ErrorActionPreference='Stop'; " +
                        "Add-WindowsCapability -Online " +
                        "-Name OpenSSH.Client~~~~0.0.1.0\"",

                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process process = Process.Start(psi);

                if (process == null)
                {
                    MessageBox.Show(
                        T("OpenSSH kurulumu başlatılamadı.", "OpenSSH installation could not be started."),
                        T("Kurulum Hatası", "Installation Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }

                await Task.Run(() => process.WaitForExit());

                int exitCode = process.ExitCode;

                // Kurulum durumunu Windows Capability üzerinden doğrula
                var checkPsi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -Command " +
                        "\"(Get-WindowsCapability -Online " +
                        "-Name OpenSSH.Client~~~~0.0.1.0).State\"",

                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                string state = "";

                using (var checkProcess = Process.Start(checkPsi))
                {
                    if (checkProcess != null)
                    {
                        state = await checkProcess.StandardOutput.ReadToEndAsync();
                        await Task.Run(() => checkProcess.WaitForExit());
                    }
                }

                state = (state ?? "").Trim();

                if (state.Equals(
                    "Installed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    statusLabel.Text =
                        T("OpenSSH Client başarıyla yüklendi.", "OpenSSH Client installed successfully.");

                    MessageBox.Show(
                        T("OpenSSH Client başarıyla yüklendi.\r\n\r\n",
                          "OpenSSH Client installed successfully.\r\n\r\n") +
                        T("SSH artık kullanılabilir.", "SSH is now available."),
                        "OpenSSH",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                statusLabel.Text = T("OpenSSH kurulamadı.", "OpenSSH could not be installed.");

                MessageBox.Show(
    T("OpenSSH Client doğrulanamadı.\r\n\r\n",
      "OpenSSH Client could not be verified.\r\n\r\n") +
    T("PowerShell çıkış kodu: ", "PowerShell exit code: ") + exitCode + "\r\n" +
    T("Capability durumu: ", "Capability state: ") + state,
    T("OpenSSH Kurulum Hatası", "OpenSSH Installation Error"),
    MessageBoxButtons.OK,
    MessageBoxIcon.Error);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                statusLabel.Text = T("OpenSSH kurulumu iptal edildi.", "OpenSSH installation was cancelled.");
            }
            catch (Exception ex)
            {
                statusLabel.Text = T("OpenSSH kurulumu başarısız.", "OpenSSH installation failed.");

                MessageBox.Show(
                    T("OpenSSH kurulumu sırasında hata oluştu.\r\n\r\n",
                      "An error occurred while installing OpenSSH.\r\n\r\n") +
                    ex.Message,
                    T("Kurulum Hatası", "Installation Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(
            uint uFormat,
            IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(
            uint uFlags,
            UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private readonly ComboBox adapterCombo = new ComboBox();
        private readonly CheckBox scanAllCheck = new CheckBox();
        private readonly TextBox rangeStart = new TextBox();
        private readonly TextBox rangeEnd = new TextBox();
        private readonly Label localIpLabel = new Label();
        private readonly Label gatewayLabel = new Label();
        private readonly Label networkSummary = new Label();
        private readonly Button scanButton = new Button();
        private readonly Button exportButton = new Button();
        private readonly ListView devices = new ListView();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label progressLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TabControl bottomTabs = new TabControl();
        private readonly Label deviceInfo = new Label();
        private readonly ListView portsList = new ListView();
        private readonly Button scanPortsButton = new Button();
        private readonly ListView sharesList = new ListView();
        private readonly Button scanSharesButton = new Button();
        private readonly Label shareStatus = new Label();
        private readonly ListView historyList = new ListView();
        private readonly LinkLabel footer = new LinkLabel();
        private readonly ComboBox deviceTypeFilter = new ComboBox();
        private readonly ComboBox languageCombo = new ComboBox();
        private readonly Label languageLabel = new Label();
        private readonly ComboBox themeCombo = new ComboBox();
        private readonly Label themeLabel = new Label();
        private readonly LinkLabel versionLink = new LinkLabel();
        private readonly Button logButton = new Button();
        private bool englishMode = false;
        private bool darkMode = false;
        private bool startupUpdateCheckDone = false;

        // Sürüm AssemblyInfo.cs içindeki AssemblyVersion değerinden alınır.
        // Örnek: [assembly: AssemblyVersion("1.2.0.0")] -> 1.2
        private static string CurrentVersion
        {
            get
            {
                var version = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version;

                return version == null ? "0.0" : version.ToString(2);
            }
        }

        private const string GitHubOwner = "tuncaycandan";
        private const string GitHubRepo = "NetworkScannerTool";

        private readonly HttpClient updateHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private readonly List<AdapterInfo> adapters = new List<AdapterInfo>();
        private readonly List<DeviceInfo> scanResults = new List<DeviceInfo>();
        private readonly Dictionary<string, List<PortResult>> portCache = new Dictionary<string, List<PortResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<HistoryEntry>> history = new Dictionary<string, List<HistoryEntry>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> vendorCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly HttpClient vendorHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };


        private CancellationTokenSource scanCts;
        private string selectedIp = "";
        private readonly DeviceListComparer deviceListComparer = new DeviceListComparer();



        private string LocalizeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            switch (value)
            {
                case "Aktif":
                case "Active":
                    return T("Aktif", "Active");

                case "Pasif":
                case "Inactive":
                    return T("Pasif", "Inactive");

                case "Bilinmiyor":
                case "Unknown":
                    return T("Bilinmiyor", "Unknown");

                case "Aranıyor...":
                case "Searching...":
                    return T("Aranıyor...", "Searching...");

                case "Tespit ediliyor...":
                case "Detecting...":
                    return T("Tespit ediliyor...", "Detecting...");

                case "Diğer":
                case "Other":
                    return T("Diğer", "Other");

                case "Kamera / NVR":
                case "Camera / NVR":
                    return T("Kamera / NVR", "Camera / NVR");

                case "IP Kamera / NVR":
                case "IP Camera / NVR":
                    return T("IP Kamera / NVR", "IP Camera / NVR");

                case "IP Kamera":
                case "IP Camera":
                    return T("IP Kamera", "IP Camera");

                case "Yazıcı":
                case "Printer":
                    return T("Yazıcı", "Printer");

                case "NAS / Dosya Sunucusu":
                case "NAS / File Server":
                    return T("NAS / Dosya Sunucusu", "NAS / File Server");

                case "Windows Sunucu":
                case "Windows Server":
                    return T("Windows Sunucu", "Windows Server");

                case "Windows Cihazı":
                case "Windows Device":
                    return T("Windows Cihazı", "Windows Device");

                case "Ağ Cihazı":
                case "Network Device":
                    return T("Ağ Cihazı", "Network Device");

                case "Telefon / Tablet":
                case "Phone / Tablet":
                    return T("Telefon / Tablet", "Phone / Tablet");

                case "Telefon / Mobil Cihaz":
                case "Phone / Mobile Device":
                    return T("Telefon / Mobil Cihaz", "Phone / Mobile Device");

                case "Linux Cihazı":
                case "Linux Device":
                    return T("Linux Cihazı", "Linux Device");

                case "Linux Sunucu":
                case "Linux Server":
                    return T("Linux Sunucu", "Linux Server");

                case "Linux / Unix Cihazı":
                case "Linux / Unix Device":
                    return T("Linux / Unix Cihazı", "Linux / Unix Device");

                case "Mac / Apple Bilgisayar":
                case "Mac / Apple Computer":
                    return T("Mac / Apple Bilgisayar", "Mac / Apple Computer");

                case "Bilgisayar / Sunucu":
                case "Computer / Server":
                    return T("Bilgisayar / Sunucu", "Computer / Server");

                case "RTSP Cihazı":
                case "RTSP Device":
                    return T("RTSP Cihazı", "RTSP Device");

                case "Web Yönetimli Cihaz":
                case "Web Managed Device":
                    return englishMode
    ? "Web Managed Device"
    : "Web Yönetimli Cihaz";

                default:
                    return value;
            }
        }

        private void LocalizeExistingData()
        {
            foreach (var d in scanResults)
            {
                d.Status = LocalizeValue(d.Status);
                d.DeviceType = LocalizeValue(d.DeviceType);
                d.Vendor = LocalizeValue(d.Vendor);
            }

            foreach (var pair in history)
            {
                foreach (var h in pair.Value)
                    h.Status = LocalizeValue(h.Status);
            }
        }

        private void UpdateDeviceInfoText(DeviceInfo d)
        {
            if (d == null)
                return;

            deviceInfo.Text =
                T("IP Adresi: ", "IP Address: ") + d.Ip +
                "    |    " + T("Hostname: ", "Hostname: ") + d.Hostname +
                Environment.NewLine +
                T("MAC Adresi: ", "MAC Address: ") + d.Mac +
                "    |    " + T("Üretici: ", "Vendor: ") + d.Vendor +
                Environment.NewLine +
                T("Cihaz Türü: ", "Device Type: ") + d.DeviceType +
                "    |    " + T("Ağ: ", "Network: ") + d.Network +
                Environment.NewLine +
                T("Yanıt: ", "Response: ") + d.Response +
                "    |    " + T("Durum: ", "Status: ") + d.Status;
        }

        private void ApplyLanguage()
        {
            LocalizeExistingData();

            scanAllCheck.Text = T("Tüm ağları tara", "Scan all networks");
            scanAllCheck.Location = new Point(375, 13);
            scanAllCheck.Size = new Size(125, 28);
            scanAllCheck.CheckAlign = ContentAlignment.MiddleLeft;
            scanAllCheck.TextAlign = ContentAlignment.MiddleLeft;
            scanButton.Text = T("Tara", "Scan");
            exportButton.Text = T("HTML Rapor", "HTML Report");
            scanPortsButton.Text = T("Portları Tara", "Scan Ports");
            scanSharesButton.Text = T("Paylaşımları Tara", "Scan Shares");
            versionLink.Text = "v" + CurrentVersion;

            themeLabel.Text = T("Tema:", "Theme:");
            themeCombo.Items.Clear();
            themeCombo.Items.Add(T("Açık", "Light"));
            themeCombo.Items.Add(T("Koyu", "Dark"));
            themeCombo.SelectedIndex = darkMode ? 1 : 0;

            languageLabel.Text = T("Dil:", "Language:");
            languageLabel.Location = new Point(765, 83);
            languageLabel.Size = new Size(70, 22);
            languageLabel.TextAlign = ContentAlignment.MiddleRight;
            languageCombo.Location = new Point(840, 82);
            languageCombo.Size = new Size(120, 24);

            var networkLabels = Controls.Find("networkLabel", true);
            if (networkLabels.Length > 0)
            {
                networkLabels[0].Text = T("Ağ:", "Network:");
                networkLabels[0].Size = englishMode ? new Size(60, 22) : new Size(35, 22);
            }

            adapterCombo.Location = englishMode ? new Point(80, 14) : new Point(55, 14);
            adapterCombo.Size = new Size(285, 24);

            var rangeLabels = Controls.Find("rangeLabel", true);
            if (rangeLabels.Length > 0)
                rangeLabels[0].Text = T("Aralık:", "Range:");

            var filterLabels = Controls.Find("typeFilterLabel", true);
            if (filterLabels.Length > 0)
                filterLabels[0].Text = T("Filtre:", "Filter:");

            var refreshButtons = Controls.Find("refreshBtn", true);
            if (refreshButtons.Length > 0)
                refreshButtons[0].Text = T("Ağ Bilgisini Yenile", "Refresh Network Info");

            if (bottomTabs.TabPages.Count >= 4)
            {
                bottomTabs.TabPages[0].Text = T("Cihaz Bilgileri", "Device Information");
                bottomTabs.TabPages[1].Text = T("Açık Portlar", "Open Ports");
                bottomTabs.TabPages[2].Text = T("Paylaşımlar", "Shares");
                bottomTabs.TabPages[3].Text = T("Geçmiş", "History");
            }

            if (devices.Columns.Count >= 8)
            {
                devices.Columns[0].Text = T("IP Adresi", "IP Address");
                devices.Columns[1].Text = "Hostname";
                devices.Columns[2].Text = T("MAC Adresi", "MAC Address");
                devices.Columns[3].Text = T("Üretici", "Vendor");
                devices.Columns[4].Text = T("Cihaz Türü", "Device Type");
                devices.Columns[5].Text = T("Yanıt", "Response");
                devices.Columns[6].Text = T("Durum", "Status");
                devices.Columns[7].Text = T("Ağ", "Network");
            }

            if (portsList.Columns.Count >= 3)
            {
                portsList.Columns[0].Text = "Port";
                portsList.Columns[1].Text = T("Servis", "Service");
                portsList.Columns[2].Text = T("Durum", "Status");
            }

            if (sharesList.Columns.Count >= 3)
            {
                sharesList.Columns[0].Text = T("Paylaşım", "Share");
                sharesList.Columns[1].Text = T("Tür", "Type");
                sharesList.Columns[2].Text = T("Yol", "Path");
            }

            if (historyList.Columns.Count >= 4)
            {
                historyList.Columns[0].Text = T("Tarih / Saat", "Date / Time");
                historyList.Columns[1].Text = T("Durum", "Status");
                historyList.Columns[2].Text = "Hostname";
                historyList.Columns[3].Text = "MAC";
            }

            BuildContextMenu();

            networkSummary.Text = adapters.Count + T(" aktif ağ", " active network(s)");
            ApplySelectedAdapter();

            RefreshDeviceTypeFilterItems();
            ApplyDeviceTypeFilter();

            ApplyTheme();

            var selected = GetSelectedDevice();
            if (selected != null)
            {
                UpdateDeviceInfoText(selected);
                shareStatus.Text = T("Seçili cihaz: ", "Selected device: ") + selected.Ip;
                RefreshPortsTab(selected.Ip);
                RefreshHistoryTab(selected.Ip);
            }
            else
            {
                deviceInfo.Text = T("Listeden bir cihaz seçin.", "Select a device from the list.");
                shareStatus.Text = T("Bir cihaz seçin.", "Select a device.");

                if (scanResults.Count == 0)
                    statusLabel.Text = T("Hazır.", "Ready.");
                else
                    statusLabel.Text = scanResults.Count + T(" cihaz gösteriliyor.", " device(s) shown.");
            }
        }

        public MainForm()
        {
            Text = "Network Scanner Tool v" + CurrentVersion;

            // Türkçe Windows -> Türkçe, diğer tüm Windows dilleri -> English
            englishMode = !CultureInfo.CurrentUICulture.Name.StartsWith(
                "tr",
                StringComparison.OrdinalIgnoreCase);

            // EXE içine gömülü ikonu pencere ve görev çubuğunda da kullan.
            using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("NetworkScannerTool.network_server_icon_191508.ico"))
            {
                if (iconStream != null)
                    this.Icon = new Icon(iconStream);
            }
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 655);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(247, 248, 250);

            // DPI ölçeklendirme: %125 / %150 gibi Windows ölçeklerinde
            // WinForms kontrollerinin daha düzgün ölçeklenmesini sağlar.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            MinimumSize = new Size(996, 694);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
            BuildUi();
            AppLogger.Initialize();

            // Sağ alt bölüm: mevcut tuncay_gokturk.png imzası.
            var gokturkLogo = new PictureBox
            {
                Image = Properties.Resources.tuncay_gokturk,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Size = new Size(90, 20),
                Location = new Point(880, 620),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Cursor = Cursors.Hand
            };
            gokturkLogo.Click += (s, e) => ShowAboutDialog();
            Controls.Add(gokturkLogo);
            gokturkLogo.BringToFront();

            LoadAdapters();
            ApplyLanguage();
            AppLogger.Info("Application", "UI initialized; version v" + CurrentVersion);

            // Program açıldıktan 3 saniye sonra sessizce güncelleme kontrolü yap.
            Shown += async (s, e) =>
            {
                if (startupUpdateCheckDone)
                    return;

                startupUpdateCheckDone = true;

                await Task.Delay(3000);
                await CheckForUpdatesAsync(true);
            };
        }

        private void BuildUi()
        {
            var networkLabel = new Label
            {
                Name = "networkLabel",
                Text = "Ağ:",
                Location = new Point(18, 18),
                Size = new Size(35, 22),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(networkLabel);

            adapterCombo.Location = new Point(55, 14);
            adapterCombo.Size = new Size(285, 24);
            adapterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            adapterCombo.SelectedIndexChanged += (s, e) => ApplySelectedAdapter();
            Controls.Add(adapterCombo);

            networkSummary.Location = new Point(505, 18);
            networkSummary.Size = new Size(110, 22);
            networkSummary.Font = new Font(Font, FontStyle.Bold);
            Controls.Add(networkSummary);

            scanAllCheck.Text = "Tüm ağları tara";
            scanAllCheck.Location = new Point(350, 13);
            scanAllCheck.Size = new Size(125, 28);
            scanAllCheck.AutoSize = false;
            scanAllCheck.CheckAlign = ContentAlignment.MiddleLeft;
            scanAllCheck.TextAlign = ContentAlignment.MiddleLeft;
            scanAllCheck.FlatStyle = FlatStyle.Standard;
            scanAllCheck.UseVisualStyleBackColor = true;
            Controls.Add(scanAllCheck);

            scanButton.Text = "Tara";
            scanButton.Location = new Point(700, 10);
            scanButton.Size = new Size(85, 32);
            scanButton.BackColor = Color.FromArgb(0, 120, 215);
            scanButton.ForeColor = Color.White;
            scanButton.FlatStyle = FlatStyle.Flat;
            scanButton.FlatAppearance.BorderSize = 0;
            scanButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 140, 235);
            scanButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 90, 175);
            scanButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            scanButton.Font = new Font(Font, FontStyle.Bold);
            scanButton.Click += async (s, e) => await ScanButton_ClickAsync();
            Controls.Add(scanButton);


            exportButton.Text = "HTML Rapor";
            exportButton.Location = new Point(795, 10);
            exportButton.Size = new Size(100, 32);
            exportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            exportButton.Enabled = false;
            exportButton.Click += (s, e) => ExportHtmlReport();
            Controls.Add(exportButton);

            logButton.Text = "Log";
            logButton.Location = new Point(905, 10);
            logButton.Size = new Size(55, 32);
            logButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            logButton.Click += (s, e) => OpenLogFolder();
            Controls.Add(logButton);

            localIpLabel.Location = new Point(18, 50);
            localIpLabel.Size = new Size(210, 20);
            localIpLabel.ForeColor = Color.DimGray;
            Controls.Add(localIpLabel);

            gatewayLabel.Location = new Point(235, 50);
            gatewayLabel.Size = new Size(190, 20);
            gatewayLabel.ForeColor = Color.DimGray;
            Controls.Add(gatewayLabel);

            var rangeLabel = new Label { Name = "rangeLabel", Text = "Aralık:", Location = new Point(430, 50), Size = new Size(62, 20), ForeColor = Color.DimGray };
            Controls.Add(rangeLabel);

            rangeStart.Location = new Point(493, 46);
            rangeStart.Size = new Size(118, 24);
            rangeStart.Leave += (s, e) => UpdateRangeEndFromStart();
            rangeStart.KeyDown += RangeTextBox_KeyDown;
            Controls.Add(rangeStart);

            var dash = new Label { Text = "-", Location = new Point(616, 50), Size = new Size(12, 20), TextAlign = ContentAlignment.MiddleCenter };
            Controls.Add(dash);

            rangeEnd.Location = new Point(632, 46);
            rangeEnd.Size = new Size(118, 24);
            rangeEnd.KeyDown += RangeTextBox_KeyDown;
            Controls.Add(rangeEnd);

            var refreshBtn = new Button { Name = "refreshBtn", Text = "Ağ Bilgisini Yenile", Location = new Point(760, 46), Size = new Size(200, 26) };
            refreshBtn.Click += (s, e) => LoadAdapters();
            Controls.Add(refreshBtn);

            var typeFilterLabel = new Label
            {
                Name = "typeFilterLabel",
                Text = "Filtre:",
                Location = new Point(18, 86),
                Size = new Size(40, 22),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(typeFilterLabel);

            deviceTypeFilter.Location = new Point(60, 82);
            deviceTypeFilter.Size = new Size(150, 24);
            deviceTypeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            deviceTypeFilter.Enabled = false;

            deviceTypeFilter.Items.Clear();
            deviceTypeFilter.Items.Add(T("Tümü", "All"));
            deviceTypeFilter.SelectedIndex = 0;
            deviceTypeFilter.SelectedIndexChanged +=
                (s, e) => ApplyDeviceTypeFilter();

            Controls.Add(deviceTypeFilter);

            themeLabel.Text = "Tema:";
            themeLabel.Location = new Point(600, 83);
            themeLabel.Size = new Size(55, 22);
            themeLabel.TextAlign = ContentAlignment.MiddleRight;
            themeLabel.Font = new Font(Font, FontStyle.Bold);
            Controls.Add(themeLabel);

            themeCombo.Location = new Point(660, 82);
            themeCombo.Size = new Size(95, 24);
            themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            themeCombo.Items.Add("Açık");
            themeCombo.Items.Add("Koyu");
            themeCombo.SelectedIndex = darkMode ? 1 : 0;
            themeCombo.SelectedIndexChanged += (s, e) =>
            {
                darkMode = themeCombo.SelectedIndex == 1;
                ApplyTheme();
            };
            Controls.Add(themeCombo);

            languageLabel.Text = "Dil:";
            languageLabel.Location = new Point(765, 83);
            languageLabel.Size = new Size(70, 22);
            languageLabel.TextAlign = ContentAlignment.MiddleRight;
            languageLabel.Font = new Font(Font, FontStyle.Bold);
            Controls.Add(languageLabel);

            languageCombo.Location = new Point(840, 82);
            languageCombo.Size = new Size(120, 24);
            languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            languageCombo.Items.Add("Türkçe");
            languageCombo.Items.Add("English");
            languageCombo.SelectedIndex = englishMode ? 1 : 0;
            languageCombo.SelectedIndexChanged += (s, e) =>
            {
                englishMode = languageCombo.SelectedIndex == 1;
                ApplyLanguage();
            };
            Controls.Add(languageCombo);

            devices.Location = new Point(18, 114);
            devices.Size = new Size(942, 305);
            devices.View = View.Details;
            devices.FullRowSelect = true;
            devices.GridLines = true;
            devices.HideSelection = false;
            devices.Columns.Add("IP Adresi", 110);
            devices.Columns.Add("Hostname", 150);
            devices.Columns.Add("MAC Adresi", 140);
            devices.Columns.Add("Üretici", 150);
            devices.Columns.Add("Cihaz Türü", 125);
            devices.Columns.Add("Yanıt", 70);
            devices.Columns.Add("Durum", 65);
            devices.Columns.Add("Ağ", 175);
            devices.SelectedIndexChanged += Devices_SelectedIndexChanged;
            devices.MouseUp += Devices_MouseUp;

            // Sütun başlığına tıklayarak sıralama
            devices.ListViewItemSorter = deviceListComparer;
            devices.ColumnClick += Devices_ColumnClick;

            Controls.Add(devices);

            BuildContextMenu();

            bottomTabs.Location = new Point(18, 430);
            bottomTabs.Size = new Size(942, 150);

            var infoTab = new TabPage("Cihaz Bilgileri");
            deviceInfo.Location = new Point(16, 16);
            deviceInfo.Size = new Size(880, 92);
            deviceInfo.Text = T("Listeden bir cihaz seçin.", "Select a device from the list.");
            infoTab.Controls.Add(deviceInfo);

            var portsTab = new TabPage("Açık Portlar");
            portsList.Location = new Point(16, 12);
            portsList.Size = new Size(650, 88);
            portsList.View = View.Details;
            portsList.GridLines = true;
            portsList.Scrollable = true;
            portsList.Columns.Add("Port", 90);
            portsList.Columns.Add("Servis", 280);
            portsList.Columns.Add("Durum", 110);
            portsTab.Controls.Add(portsList);

            scanPortsButton.Text = T("Portları Tara", "Scan Ports");
            scanPortsButton.Location = new Point(680, 12);
            scanPortsButton.Size = new Size(190, 32);
            scanPortsButton.Font = new Font(Font, FontStyle.Bold);
            scanPortsButton.Click += async (s, e) => await ScanSelectedPortsAsync();
            portsTab.Controls.Add(scanPortsButton);

            var sharesTab = new TabPage("Paylaşımlar");
            sharesList.Location = new Point(16, 12);
            sharesList.Size = new Size(650, 88);
            sharesList.View = View.Details;
            sharesList.GridLines = true;
            sharesList.Columns.Add("Paylaşım", 200);
            sharesList.Columns.Add("Tür", 120);
            sharesList.Columns.Add("Yol", 300);
            sharesTab.Controls.Add(sharesList);

            scanSharesButton.Text = "Paylaşımları Tara";
            scanSharesButton.Location = new Point(680, 12);
            scanSharesButton.Size = new Size(190, 30);
            scanSharesButton.Font = new Font(Font, FontStyle.Bold);
            scanSharesButton.Click += async (s, e) => await ScanSelectedSharesAsync();
            sharesTab.Controls.Add(scanSharesButton);

            shareStatus.Location = new Point(680, 50);
            shareStatus.Size = new Size(220, 42);
            shareStatus.ForeColor = Color.DimGray;
            shareStatus.Text = T("Bir cihaz seçin.", "Select a device.");
            sharesTab.Controls.Add(shareStatus);

            var historyTab = new TabPage("Geçmiş");
            historyList.Location = new Point(16, 12);
            historyList.Size = new Size(880, 88);
            historyList.View = View.Details;
            historyList.GridLines = true;
            historyList.Columns.Add("Tarih / Saat", 180);
            historyList.Columns.Add("Durum", 100);
            historyList.Columns.Add("Hostname", 260);
            historyList.Columns.Add("MAC", 180);
            historyTab.Controls.Add(historyList);

            bottomTabs.TabPages.Add(infoTab);
            bottomTabs.TabPages.Add(portsTab);
            bottomTabs.TabPages.Add(sharesTab);
            bottomTabs.TabPages.Add(historyTab);
            Controls.Add(bottomTabs);

            // Progress bar
            progress.Location = new Point(18, 590);
            progress.Size = new Size(520, 16);
            Controls.Add(progress);

            // Tarama sayacı
            progressLabel.Location = new Point(550, 586);
            progressLabel.Size = new Size(90, 22);
            progressLabel.TextAlign = ContentAlignment.MiddleRight;
            progressLabel.ForeColor = Color.DimGray;
            progressLabel.Text = "0 / 0";
            Controls.Add(progressLabel);

            // Durum
            statusLabel.Location = new Point(655, 586);
            statusLabel.Size = new Size(305, 22);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = Color.DimGray;
            statusLabel.Text = T("Hazır.", "Ready.");
            Controls.Add(statusLabel);

            // En öne getir
            progressLabel.BringToFront();
            statusLabel.BringToFront();

            // ================================
            // FOOTER
            // ================================

            // Üst ayırıcı çizgi
            var footerLine = new Label();
            footerLine.Location = new Point(18, 614);
            footerLine.Size = new Size(942, 1);
            footerLine.BackColor = Color.FromArgb(220, 224, 230);
            Controls.Add(footerLine);

            // Mevcut sürüm bağlantısı - sol alt
            versionLink.Text = "v" + CurrentVersion;
            versionLink.Location = new Point(18, 618);
            versionLink.Size = new Size(70, 27);
            versionLink.Font = new Font("Segoe UI", 8.5F, FontStyle.Underline);
            versionLink.LinkColor = Color.RoyalBlue;
            versionLink.ActiveLinkColor = Color.DarkBlue;
            versionLink.VisitedLinkColor = Color.RoyalBlue;
            versionLink.Cursor = Cursors.Hand;
            versionLink.TextAlign = ContentAlignment.MiddleLeft;
            versionLink.AutoSize = false;
            versionLink.Click += async (s, e) => await CheckForUpdatesAsync(false);
            Controls.Add(versionLink);
            versionLink.BringToFront();

            // Footer
            footer.Location = new Point(18, 620);
            footer.Size = new Size(942, 24);

            footer.Text =
                "tuncay.net.tr";

            footer.TextAlign = ContentAlignment.MiddleCenter;

            footer.Font = new Font(
                "Segoe UI",
                8.5F,
                FontStyle.Regular);

            footer.ForeColor =
                Color.FromArgb(105, 110, 120);

            footer.LinkColor =
                Color.FromArgb(55, 105, 170);

            footer.ActiveLinkColor =
                Color.FromArgb(35, 80, 140);

            footer.VisitedLinkColor =
                footer.LinkColor;

            footer.LinkBehavior =
                LinkBehavior.HoverUnderline;

            // Sadece tuncay.net.tr tıklanabilir
            int linkStart =
                footer.Text.IndexOf("tuncay.net.tr");

            footer.Links.Clear();

            footer.Links.Add(
                linkStart,
                "tuncay.net.tr".Length,
                "https://tuncay.net.tr");

            footer.LinkClicked += (s, e) =>
            {
                Process.Start(
                    e.Link.LinkData.ToString());
            };

            Controls.Add(footer);
            versionLink.BringToFront();
        }
        // =========================================================
        // GITHUB OTOMATİK GÜNCELLEME
        // =========================================================

        private async Task CheckForUpdatesAsync(bool silent)
        {
            AppLogger.Info("Update check", silent ? "started silently" : "started by user");
            string previousStatus = statusLabel.Text;

            try
            {
                if (!silent)
                    statusLabel.Text = T(
                        "Güncellemeler denetleniyor...",
                        "Checking for updates...");

                versionLink.Enabled = false;

                string apiUrl =
                    "https://api.github.com/repos/" +
                    GitHubOwner + "/" +
                    GitHubRepo +
                    "/releases/latest";

                using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                {
                    request.Headers.UserAgent.ParseAdd(
                        "NetworkScannerTool/" + CurrentVersion);

                    request.Headers.Accept.ParseAdd(
                        "application/vnd.github+json");

                    using (HttpResponseMessage response =
                        await updateHttp.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (!silent)
                            {
                                MessageBox.Show(
                                    T(
                                        "GitHub sürüm bilgisi alınamadı.\r\n\r\nHTTP: ",
                                        "GitHub release information could not be retrieved.\r\n\r\nHTTP: ") +
                                    (int)response.StatusCode,
                                    T("Güncelleme", "Update"),
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }

                            statusLabel.Text = previousStatus;
                            return;
                        }

                        string json = await response.Content.ReadAsStringAsync();

                        string tag = ExtractJsonString(json, "tag_name");
                        string assetName = FindExeAssetName(json);
                        string downloadUrl = FindExeDownloadUrl(json);

                        if (string.IsNullOrWhiteSpace(tag))
                            throw new InvalidOperationException(
                                T(
                                    "GitHub sürüm etiketi okunamadı.",
                                    "GitHub release tag could not be read."));

                        Version latestVersion;
                        Version currentVersion;

                        if (!TryParseVersion(tag, out latestVersion))
                            throw new InvalidOperationException(
                                T(
                                    "GitHub sürüm numarası geçersiz: ",
                                    "Invalid GitHub version number: ") + tag);

                        if (!TryParseVersion(CurrentVersion, out currentVersion))
                            currentVersion = new Version(0, 0);

                        if (latestVersion <= currentVersion)
                        {
                            statusLabel.Text = T(
                                "Program güncel.",
                                "Application is up to date.");

                            if (!silent)
                            {
                                MessageBox.Show(
                                    T(
                                        "En güncel sürümü kullanıyorsunuz.\r\n\r\nSürüm: v",
                                        "You are using the latest version.\r\n\r\nVersion: v") +
                                    CurrentVersion,
                                    T("Güncelleme", "Update"),
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                            }

                            return;
                        }

                        if (string.IsNullOrWhiteSpace(downloadUrl))
                        {
                            MessageBox.Show(
                                T(
                                    "Yeni sürüm bulundu ancak Release Assets bölümünde indirilebilir .exe dosyası bulunamadı.\r\n\r\nYeni sürüm: ",
                                    "A new version was found, but no downloadable .exe file exists in Release Assets.\r\n\r\nNew version: ") +
                                tag,
                                T("Yeni Sürüm", "New Version"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);

                            statusLabel.Text = previousStatus;
                            return;
                        }

                        DialogResult result = MessageBox.Show(
                            T(
                                "Yeni sürüm bulundu.\r\n\r\nMevcut sürüm: v",
                                "A new version is available.\r\n\r\nCurrent version: v") +
                            CurrentVersion +
                            T("\r\nYeni sürüm: ", "\r\nNew version: ") +
                            tag +
                            T(
                                "\r\n\r\nŞimdi indirip güncellemek ister misiniz?",
                                "\r\n\r\nWould you like to download and install it now?"),
                            T("Yeni Sürüm", "New Version"),
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (result != DialogResult.Yes)
                        {
                            statusLabel.Text = previousStatus;
                            return;
                        }

                        await DownloadAndInstallUpdateAsync(
                            downloadUrl,
                            string.IsNullOrWhiteSpace(assetName)
                                ? "NetworkScannerTool.exe"
                                : assetName,
                            tag);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Update check", "", ex);
                statusLabel.Text = T(
                    "Güncelleme kontrolü başarısız.",
                    "Update check failed.");

                if (!silent)
                {
                    MessageBox.Show(
                        T(
                            "Güncelleme kontrolü sırasında hata oluştu.\r\n\r\n",
                            "An error occurred while checking for updates.\r\n\r\n") +
                        ex.Message,
                        T("Güncelleme Hatası", "Update Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                versionLink.Enabled = true;
            }
        }

        private async Task DownloadAndInstallUpdateAsync(
            string downloadUrl,
            string assetName,
            string releaseTag)
        {
            string tempExe = Path.Combine(
                Path.GetTempPath(),
                "NetworkScannerTool_Update_" +
                Guid.NewGuid().ToString("N") +
                ".exe");

            string currentExe = Application.ExecutablePath;

            try
            {
                statusLabel.Text = T(
                    "Yeni sürüm indiriliyor...",
                    "Downloading new version...");

                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    downloadUrl))
                {
                    request.Headers.UserAgent.ParseAdd(
                        "NetworkScannerTool/" + CurrentVersion);

                    using (HttpResponseMessage response =
                        await updateHttp.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();

                        byte[] data =
                            await response.Content.ReadAsByteArrayAsync();

                        if (data == null ||
                            data.Length < 2 ||
                            data[0] != 0x4D ||
                            data[1] != 0x5A)
                        {
                            throw new InvalidDataException(
                                T(
                                    "İndirilen dosya geçerli bir Windows EXE dosyası değil.",
                                    "The downloaded file is not a valid Windows EXE file."));
                        }

                        File.WriteAllBytes(tempExe, data);
                        if (!HasAuthenticodeSignature(tempExe))
                        {
                            TryDeleteFile(tempExe);
                            throw new InvalidDataException(T("Güncelleme dosyasının dijital imzası doğrulanamadı.", "The update file's digital signature could not be verified."));
                        }
                    }
                }

                string updaterBat = Path.Combine(
                    Path.GetTempPath(),
                    "NetworkScannerTool_Updater_" +
                    Guid.NewGuid().ToString("N") +
                    ".cmd");

                string script =
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    ":waitloop\r\n" +
                    "tasklist /FI \"PID eq " +
                    Process.GetCurrentProcess().Id +
                    "\" 2>nul | find \"" +
                    Process.GetCurrentProcess().Id +
                    "\" >nul\r\n" +
                    "if not errorlevel 1 (\r\n" +
                    "  timeout /t 1 /nobreak >nul\r\n" +
                    "  goto waitloop\r\n" +
                    ")\r\n" +
                    "copy /y \"" +
                    tempExe +
                    "\" \"" +
                    currentExe +
                    "\" >nul\r\n" +
                    "if errorlevel 1 (\r\n" +
                    "  echo Update failed.\r\n" +
                    "  pause\r\n" +
                    "  exit /b 1\r\n" +
                    ")\r\n" +
                    "start \"\" \"" +
                    currentExe +
                    "\"\r\n" +
                    "del /q \"" +
                    tempExe +
                    "\" >nul 2>&1\r\n" +
                    "del /q \"%~f0\" >nul 2>&1\r\n";

                File.WriteAllText(
                    updaterBat,
                    script,
                    Encoding.Default);

                statusLabel.Text = T(
                    "Güncelleme hazır. Program yeniden başlatılıyor...",
                    "Update is ready. Restarting application...");

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"" + updaterBat + "\"";
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                // Program Files gibi korumalı klasörlerde eski EXE'nin
                // üzerine yazabilmek için updater yönetici olarak çalışır.
                if (!CanWriteToApplicationDirectory())
                    psi.Verb = "runas";

                Process.Start(psi);

                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                statusLabel.Text = T(
                    "Güncelleme iptal edildi.",
                    "Update was cancelled.");

                TryDeleteFile(tempExe);
            }
            catch (Exception ex)
            {
                TryDeleteFile(tempExe);

                statusLabel.Text = T(
                    "Güncelleme yüklenemedi.",
                    "Update could not be installed.");

                MessageBox.Show(
                    T(
                        "Yeni sürüm indirilirken veya kurulurken hata oluştu.\r\n\r\n",
                        "An error occurred while downloading or installing the new version.\r\n\r\n") +
                    ex.Message,
                    T("Güncelleme Hatası", "Update Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool HasAuthenticodeSignature(string path)
        {
            try
            {
                var certificate = X509Certificate.CreateFromSignedFile(path);
                return certificate != null && !string.IsNullOrWhiteSpace(certificate.Subject);
            }
            catch
            {
                return false;
            }
        }

        private bool CanWriteToApplicationDirectory()
        {
            try
            {
                string dir = Path.GetDirectoryName(
                    Application.ExecutablePath);

                if (string.IsNullOrWhiteSpace(dir))
                    return false;

                string testFile = Path.Combine(
                    dir,
                    ".nst_write_test_" +
                    Guid.NewGuid().ToString("N") +
                    ".tmp");

                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static bool TryParseVersion(
            string value,
            out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            if (value.StartsWith(
                "v",
                StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }

            int dash = value.IndexOf('-');
            if (dash >= 0)
                value = value.Substring(0, dash);

            int plus = value.IndexOf('+');
            if (plus >= 0)
                value = value.Substring(0, plus);

            return Version.TryParse(value, out version);
        }

        private static string ExtractJsonString(
            string json,
            string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) ||
                string.IsNullOrWhiteSpace(propertyName))
            {
                return "";
            }

            string marker = "\"" + propertyName + "\"";
            int keyIndex = json.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);

            if (keyIndex < 0)
                return "";

            int colon = json.IndexOf(':', keyIndex + marker.Length);
            if (colon < 0)
                return "";

            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0)
                return "";

            int i = firstQuote + 1;
            StringBuilder sb = new StringBuilder();
            bool escaped = false;

            while (i < json.Length)
            {
                char c = json[i++];

                if (escaped)
                {
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(c); break;
                    }

                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    break;

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string FindExeAssetName(string json)
        {
            return FindExeAssetProperty(json, "name");
        }

        private static string FindExeDownloadUrl(string json)
        {
            return FindExeAssetProperty(
                json,
                "browser_download_url");
        }

        private static string FindExeAssetProperty(
            string json,
            string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            int assetsIndex = json.IndexOf(
                "\"assets\"",
                StringComparison.OrdinalIgnoreCase);

            if (assetsIndex < 0)
                return "";

            int searchFrom = assetsIndex;

            while (searchFrom < json.Length)
            {
                int urlMarker = json.IndexOf(
                    "\"browser_download_url\"",
                    searchFrom,
                    StringComparison.OrdinalIgnoreCase);

                if (urlMarker < 0)
                    break;

                string url = ExtractJsonString(
                    json.Substring(urlMarker),
                    "browser_download_url");

                if (!string.IsNullOrWhiteSpace(url) &&
                    url.EndsWith(
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (propertyName.Equals(
                        "browser_download_url",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }

                    // Aynı asset objesindeki name alanını geriye doğru bul.
                    int objectStart = json.LastIndexOf(
                        '{',
                        urlMarker);

                    if (objectStart >= 0)
                    {
                        int nameMarker = json.IndexOf(
                            "\"name\"",
                            objectStart,
                            StringComparison.OrdinalIgnoreCase);

                        if (nameMarker >= 0 &&
                            nameMarker < urlMarker)
                        {
                            return ExtractJsonString(
                                json.Substring(nameMarker),
                                "name");
                        }
                    }

                    return Path.GetFileName(
                        new Uri(url).LocalPath);
                }

                searchFrom = urlMarker + 24;
            }

            return "";
        }

        // =========================================================
        // LIGHT / DARK TEMA
        // =========================================================

        private void ApplyTheme()
        {
            Color formBack = darkMode
                ? Color.FromArgb(24, 27, 32)
                : Color.FromArgb(247, 248, 250);

            Color panelBack = darkMode
                ? Color.FromArgb(31, 35, 41)
                : Color.White;

            Color inputBack = darkMode
                ? Color.FromArgb(38, 43, 50)
                : Color.White;

            Color textColor = darkMode
                ? Color.FromArgb(230, 234, 239)
                : Color.FromArgb(31, 41, 55);

            Color mutedColor = darkMode
                ? Color.FromArgb(165, 174, 185)
                : Color.DimGray;

            Color lineColor = darkMode
                ? Color.FromArgb(72, 79, 88)
                : Color.FromArgb(220, 224, 230);

            Color buttonBack = darkMode
                ? Color.FromArgb(47, 53, 61)
                : SystemColors.Control;

            Color buttonText = darkMode
                ? Color.White
                : SystemColors.ControlText;

            BackColor = formBack;
            ForeColor = textColor;

            ApplyThemeRecursive(
                this,
                formBack,
                panelBack,
                inputBack,
                textColor,
                mutedColor,
                lineColor,
                buttonBack,
                buttonText);

            // Footer link renkleri
            footer.ForeColor = mutedColor;
            footer.LinkColor = darkMode
                ? Color.FromArgb(110, 168, 255)
                : Color.FromArgb(55, 105, 170);

            footer.ActiveLinkColor = darkMode
                ? Color.FromArgb(145, 190, 255)
                : Color.FromArgb(35, 80, 140);

            footer.VisitedLinkColor = footer.LinkColor;
        }

        private void ApplyThemeRecursive(
            Control parent,
            Color formBack,
            Color panelBack,
            Color inputBack,
            Color textColor,
            Color mutedColor,
            Color lineColor,
            Color buttonBack,
            Color buttonText)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TabPage)
                {
                    c.BackColor = panelBack;
                    c.ForeColor = textColor;
                }
                else if (c is TabControl)
                {
                    c.BackColor = formBack;
                    c.ForeColor = textColor;
                }
                else if (c is TextBox)
                {
                    TextBox tb = (TextBox)c;
                    tb.BackColor = inputBack;
                    tb.ForeColor = textColor;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ComboBox)
                {
                    ComboBox cb = (ComboBox)c;
                    cb.BackColor = inputBack;
                    cb.ForeColor = textColor;
                    cb.FlatStyle = FlatStyle.Flat;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 0;
                    b.UseVisualStyleBackColor = false;
                    if (ReferenceEquals(b, scanButton))
                    {
                        bool scanning = scanCts != null;
                        b.BackColor = scanning
                            ? Color.FromArgb(210, 45, 45)
                            : Color.FromArgb(0, 120, 215);
                        b.ForeColor = Color.White;
                        b.FlatAppearance.MouseOverBackColor = scanning
                            ? Color.FromArgb(230, 65, 65)
                            : Color.FromArgb(30, 140, 235);
                        b.FlatAppearance.MouseDownBackColor = scanning
                            ? Color.FromArgb(175, 30, 30)
                            : Color.FromArgb(0, 90, 175);
                    }
                    else
                    {
                        b.BackColor = buttonBack;
                        b.ForeColor = buttonText;
                    }
                }
                else if (c is ListView)
                {
                    ListView lv = (ListView)c;
                    lv.BackColor = inputBack;
                    lv.ForeColor = textColor;
                }
                else if (c is Label)
                {
                    Label l = (Label)c;

                    if (l.Height <= 2)
                    {
                        l.BackColor = lineColor;
                    }
                    else
                    {
                        l.BackColor = Color.Transparent;

                        if (l.ForeColor == Color.DimGray ||
                            l.ForeColor == Color.FromArgb(105, 110, 120))
                        {
                            l.ForeColor = mutedColor;
                        }
                        else
                        {
                            l.ForeColor = textColor;
                        }
                    }
                }
                else if (c is LinkLabel)
                {
                    LinkLabel ll = (LinkLabel)c;
                    ll.BackColor = Color.Transparent;
                    ll.ForeColor = mutedColor;
                    ll.LinkColor = darkMode
                        ? Color.FromArgb(110, 168, 255)
                        : Color.FromArgb(55, 105, 170);
                    ll.ActiveLinkColor = darkMode
                        ? Color.FromArgb(145, 190, 255)
                        : Color.FromArgb(35, 80, 140);
                    ll.VisitedLinkColor = ll.LinkColor;
                }
                else if (c is CheckBox)
                {
                    CheckBox ch = (CheckBox)c;
                    ch.BackColor = Color.Transparent;
                    ch.ForeColor = textColor;
                }
                else if (c is ProgressBar)
                {
                    c.BackColor = inputBack;
                }
                else if (!(c is PictureBox))
                {
                    c.BackColor = c.Parent is TabPage
                        ? panelBack
                        : formBack;
                    c.ForeColor = textColor;
                }

                if (c.HasChildren)
                {
                    ApplyThemeRecursive(
                        c,
                        formBack,
                        panelBack,
                        inputBack,
                        textColor,
                        mutedColor,
                        lineColor,
                        buttonBack,
                        buttonText);
                }
            }

            // ContextMenuStrip'ler Controls ağacında değildir.
            ApplyContextMenuTheme();
        }

        private void ApplyContextMenuTheme()
        {
            if (devices.ContextMenuStrip == null)
                return;

            ContextMenuStrip menu = devices.ContextMenuStrip;

            menu.BackColor = darkMode
                ? Color.FromArgb(35, 39, 45)
                : SystemColors.Control;

            menu.ForeColor = darkMode
                ? Color.White
                : SystemColors.ControlText;

            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = menu.BackColor;
                item.ForeColor = menu.ForeColor;
            }
        }

        // =========================================================
        // DPI YARDIMCILARI
        // =========================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Form ilk kez görünür olduğunda yerleşimi DPI'ya göre
            // yeniden hesaplat ve tüm kontrolleri aynı font ailesine çek.
            NormalizeLayoutForDpi(this);

            ApplyTheme();
        }

        private void NormalizeLayoutForDpi(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button)
                {
                    Button b = (Button)c;
                    b.TextAlign = ContentAlignment.MiddleCenter;
                    b.AutoEllipsis = true;
                }
                else if (c is Label)
                {
                    Label l = (Label)c;

                    if (l.AutoSize)
                        l.AutoSize = false;

                    l.AutoEllipsis = true;
                }
                else if (c is ComboBox)
                {
                    ((ComboBox)c).IntegralHeight = false;
                }

                if (c.HasChildren)
                    NormalizeLayoutForDpi(c);
            }
        }

        private void SafeCopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Clipboard kısa süreli kilitliyse tekrar dene.
            bool opened = false;

            for (int i = 0; i < 20; i++)
            {
                if (OpenClipboard(this.Handle))
                {
                    opened = true;
                    break;
                }

                Application.DoEvents();
                Thread.Sleep(50);
            }

            if (!opened)
            {
                MessageBox.Show(
                    T("Windows panosu açılamadı.", "Windows clipboard could not be opened."),
                    T("Pano Hatası", "Clipboard Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            IntPtr hGlobal = IntPtr.Zero;

            try
            {
                EmptyClipboard();

                byte[] bytes =
                    Encoding.Unicode.GetBytes(text + "\0");

                hGlobal = GlobalAlloc(
                    GMEM_MOVEABLE,
                    (UIntPtr)bytes.Length);

                if (hGlobal == IntPtr.Zero)
                    return;

                IntPtr target = GlobalLock(hGlobal);

                if (target == IntPtr.Zero)
                    return;

                try
                {
                    Marshal.Copy(
                        bytes,
                        0,
                        target,
                        bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                IntPtr result =
                    SetClipboardData(
                        CF_UNICODETEXT,
                        hGlobal);

                if (result != IntPtr.Zero)
                {
                    // Başarılı olduğunda belleğin sahibi Windows olur.
                    hGlobal = IntPtr.Zero;
                }
            }
            finally
            {
                CloseClipboard();

                if (hGlobal != IntPtr.Zero)
                    GlobalFree(hGlobal);
            }
        }
        private void Devices_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (deviceListComparer.Column == e.Column)
            {
                deviceListComparer.Ascending = !deviceListComparer.Ascending;
            }
            else
            {
                deviceListComparer.Column = e.Column;
                deviceListComparer.Ascending = true;
            }

            devices.Sort();

            string columnName =
                e.Column >= 0 && e.Column < devices.Columns.Count
                    ? devices.Columns[e.Column].Text
                    : T("Sütun", "Column");

            statusLabel.Text =
                columnName + T(" sütununa göre ", " column sorted ") +
                (deviceListComparer.Ascending
                    ? T("artan", "ascending")
                    : T("azalan", "descending")) +
                T(" sıralandı.", ".");
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ping", null, (s, e) => RunCmd("ping " + selectedIp));
            menu.Items.Add(T("IP Kopyala", "Copy IP"), null, (s, e) => { if (!string.IsNullOrEmpty(selectedIp)) SafeCopyToClipboard(selectedIp); });
            menu.Items.Add(T("MAC Kopyala", "Copy MAC"), null, (s, e) =>
            {
                var d = GetSelectedDevice();
                if (d != null && !string.IsNullOrWhiteSpace(d.Mac) && d.Mac != "-") SafeCopyToClipboard(d.Mac);

            });

            menu.Items.Add(T("Hostname Kopyala", "Copy Hostname"), null, (s, e) =>
            {
                var d = GetSelectedDevice();

                if (d != null &&
                    !string.IsNullOrWhiteSpace(d.Hostname))
                {
                    SafeCopyToClipboard(d.Hostname);
                }
            });

            menu.Items.Add(T("Tüm Bilgileri Kopyala", "Copy All Information"), null, (s, e) =>
            {
                var d = GetSelectedDevice();

                if (d == null)
                    return;

                string info =
                    T("IP Adresi: ", "IP Address: ") + d.Ip + Environment.NewLine +
                    "Hostname: " + d.Hostname + Environment.NewLine +
                    T("MAC Adresi: ", "MAC Address: ") + d.Mac + Environment.NewLine +
                    T("Üretici: ", "Vendor: ") + d.Vendor + Environment.NewLine +
                    T("Cihaz Türü: ", "Device Type: ") + d.DeviceType + Environment.NewLine +
                    T("Yanıt: ", "Response: ") + d.Response + Environment.NewLine +
                    T("Durum: ", "Status: ") + d.Status + Environment.NewLine +
                    T("Ağ: ", "Network: ") + d.Network;

                SafeCopyToClipboard(info);
            });


            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(T("Cihazı Yeniden Tara", "Rescan Device"), null,
    async (s, e) => await RescanSelectedDeviceAsync());
            menu.Items.Add(T("Portları Tara", "Scan Ports"), null, async (s, e) => await ScanSelectedPortsAsync());
            menu.Items.Add(T("Ağ Paylaşımını Aç", "Open Network Share"), null, (s, e) =>
            {
                if (!string.IsNullOrEmpty(selectedIp)) Process.Start("explorer.exe", @"\\" + selectedIp);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Tracert", null, (s, e) => RunCmd("tracert " + selectedIp));
            menu.Items.Add("HTTP", null, (s, e) => OpenUrl("http://" + selectedIp));
            menu.Items.Add("HTTPS", null, (s, e) => OpenUrl("https://" + selectedIp));
            menu.Items.Add("RDP", null, (s, e) => Process.Start("mstsc.exe", "/v:" + selectedIp));
            menu.Items.Add("SSH", null, async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(selectedIp))
                    return;

                string sshPath = GetSshExePath();

                // OpenSSH kurulu değil
                if (!File.Exists(sshPath))
                {
                    if (ShowOpenSshInstallDialog())
                    {
                        await InstallOpenSshClientAsync();
                    }

                    return;
                }

                // OpenSSH kurulu
                string username = ShowSshLoginDialog(selectedIp);

                if (string.IsNullOrWhiteSpace(username))
                    return;

                try
                {
                    Process.Start(
                        "cmd.exe",
                        "/k \"\"" + sshPath + "\" " +
                        username + "@" + selectedIp + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        T("SSH başlatılamadı.\r\n\r\n", "SSH could not be started.\r\n\r\n") +
                        ex.Message,
                        T("SSH Hatası", "SSH Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Wake-On-LAN", null, (s, e) =>
            {
                var d = GetSelectedDevice();
                if (d == null || d.Mac == "-" || string.IsNullOrWhiteSpace(d.Mac))
                {
                    MessageBox.Show(T("MAC adresi bulunamadı.", "MAC address was not found."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SendWakeOnLan(d.Mac);
            });
            devices.ContextMenuStrip = menu;
        }

        private async Task RescanSelectedDeviceAsync()
        {
            var d = GetSelectedDevice();

            if (d == null)
            {
                MessageBox.Show(
                    T("Önce bir cihaz seçin.", "Select a device first."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            string ip = d.Ip;

            statusLabel.Text =
                ip + T(" yeniden taranıyor...", " is being rescanned...");

            try
            {
                // -----------------------------
                // 1. PING
                // -----------------------------

                PingReply reply = null;

                using (var ping = new Ping())
                {
                    try
                    {
                        reply = await ping.SendPingAsync(ip, 400);
                    }
                    catch
                    {
                        reply = null;
                    }
                }

                if (reply == null ||
                    reply.Status != IPStatus.Success)
                {
                    d.Status = T("Pasif", "Inactive");
                    d.Response = "-";
                    d.Seen = DateTime.Now;

                    AddHistory(d);

                    UpdateDeviceRow(d);

                    statusLabel.Text =
                        ip + T(" yanıt vermiyor.", " is not responding.");

                    return;
                }

                // -----------------------------
                // 2. MAC
                // -----------------------------

                var localMacByIp = adapters
                    .Where(a =>
                        !string.IsNullOrWhiteSpace(a.Ip) &&
                        !string.IsNullOrWhiteSpace(a.Mac))
                    .GroupBy(a => a.Ip)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().Mac,
                        StringComparer.OrdinalIgnoreCase);

                string mac =
                    GetMacFastParallel(
                        ip,
                        localMacByIp);

                // -----------------------------
                // 3. HOSTNAME + ÜRETİCİ
                // -----------------------------

                Task<string> hostnameTask =
                    ResolveHostnameFastAsync(ip, 450);

                Task<string> vendorTask =
                    GuessVendorAsync(mac, ip);

                string hostname = "-";
                string vendor = T("Bilinmiyor", "Unknown");

                try
                {
                    hostname = await hostnameTask;
                }
                catch
                {
                }

                try
                {
                    vendor = await vendorTask;
                }
                catch
                {
                    vendor = T("Bilinmiyor", "Unknown");
                }

                if (IsUnknownVendor(vendor))
                {
                    string hostnameVendor = GuessVendorFromHostname(hostname);

                    if (!string.IsNullOrWhiteSpace(hostnameVendor))
                        vendor = hostnameVendor;
                }

                // -----------------------------
                // 4. CİHAZ TÜRÜ
                // -----------------------------

                string deviceType = T("Diğer", "Other");

                try
                {
                    deviceType =
                        await DetectDeviceTypeAsync(
                            ip,
                            hostname,
                            vendor);
                }
                catch
                {
                }

                // -----------------------------
                // 5. BİLGİLERİ GÜNCELLE
                // -----------------------------

                d.Mac =
                    string.IsNullOrWhiteSpace(mac)
                        ? "-"
                        : mac;

                d.Hostname =
                    string.IsNullOrWhiteSpace(hostname) ||
                    hostname == "-"
                        ? ip
                        : hostname;

                d.Vendor =
                    string.IsNullOrWhiteSpace(vendor)
                        ? T("Bilinmiyor", "Unknown")
                        : vendor;

                d.DeviceType =
                    string.IsNullOrWhiteSpace(deviceType)
                        ? T("Diğer", "Other")
                        : deviceType;

                d.Response =
                    reply.RoundtripTime + " ms";

                d.Status = T("Aktif", "Active");
                d.Seen = DateTime.Now;

                AddHistory(d);

                // Eski port sonucu geçersiz olabilir.
                portCache.Remove(ip);

                UpdateDeviceRow(d);

                RefreshDeviceTypeFilterItems();
                RefreshHistoryTab(ip);
                RefreshPortsTab(ip);

                statusLabel.Text =
                    ip + T(" yeniden tarandı.", " rescanned.");
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    T("Yeniden tarama hatası: ", "Rescan error: ") +
                    ex.Message;
            }
        }
        private void UpdateDeviceRow(DeviceInfo d)
        {
            foreach (ListViewItem item in devices.Items)
            {
                if (!ReferenceEquals(item.Tag, d))
                    continue;

                item.SubItems[0].Text = d.Ip;
                item.SubItems[1].Text = d.Hostname;
                item.SubItems[2].Text = d.Mac;
                item.SubItems[3].Text = d.Vendor;
                item.SubItems[4].Text = d.DeviceType;
                item.SubItems[5].Text = d.Response;
                item.SubItems[6].Text = d.Status;
                item.SubItems[7].Text = d.Network;

                break;
            }

            var selected = GetSelectedDevice();

            if (selected != null &&
                ReferenceEquals(selected, d))
            {
                UpdateDeviceInfoText(d);
            }
        }

        private void Devices_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = devices.HitTest(e.Location);
            if (hit.Item != null)
            {
                devices.SelectedItems.Clear();
                hit.Item.Selected = true;
            }
        }

        private void LoadAdapters()
        {
            adapters.Clear();
            adapterCombo.Items.Clear();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                var uni = props.UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                if (uni == null) continue;

                var gw = props.GatewayAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

                var info = new AdapterInfo
                {
                    Name = ni.Description,
                    Ip = uni.Address.ToString(),
                    Mask = uni.IPv4Mask != null ? uni.IPv4Mask.ToString() : "255.255.255.0",
                    Gateway = gw != null ? gw.Address.ToString() : "",
                    Mac = FormatMac(ni.GetPhysicalAddress().GetAddressBytes())
                };
                adapters.Add(info);
                adapterCombo.Items.Add(info.Name + " [" + info.Ip + "]");
            }

            networkSummary.Text = adapters.Count + T(" aktif ağ", " active network(s)");
            if (adapters.Count > 0)
            {
                adapterCombo.SelectedIndex = 0;
                ApplySelectedAdapter();
            }
            else
            {
                localIpLabel.Text = T("Yerel IP: -", "Local IP: -");
                gatewayLabel.Text = "Gateway: -";
            }
        }

        private void ApplySelectedAdapter()
        {
            if (adapterCombo.SelectedIndex < 0 || adapterCombo.SelectedIndex >= adapters.Count) return;
            var a = adapters[adapterCombo.SelectedIndex];
            localIpLabel.Text = T("Yerel IP: ", "Local IP: ") + a.Ip;
            gatewayLabel.Text = "Gateway: " + (string.IsNullOrEmpty(a.Gateway) ? "-" : a.Gateway);

            var range = GetSuggestedRange(a.Ip, a.Mask);
            rangeStart.Text = range.Item1;
            rangeEnd.Text = range.Item2;
        }




        private const int MaxConcurrentScans = 40;
        private const int MaxScanTargets = 65536;
        private const int MaxConcurrentDetailScans = 16;
        private static readonly SemaphoreSlim DetailScanLimiter =
            new SemaphoreSlim(MaxConcurrentDetailScans, MaxConcurrentDetailScans);

        private async Task ScanButton_ClickAsync()
        {
            if (scanCts != null)
            {
                scanCts.Cancel();
                return;
            }

            await StartScanAsync();
        }

        private void SetScanButtonState(bool scanning)
        {
            scanButton.Text = scanning ? T("Durdur", "Stop") : T("Tara", "Scan");
            scanButton.BackColor = scanning
                ? Color.FromArgb(210, 45, 45)
                : Color.FromArgb(0, 120, 215);
            scanButton.ForeColor = Color.White;
            scanButton.FlatStyle = FlatStyle.Flat;
            scanButton.FlatAppearance.BorderSize = 0;
            scanButton.FlatAppearance.MouseOverBackColor = scanning
                ? Color.FromArgb(230, 65, 65)
                : Color.FromArgb(30, 140, 235);
            scanButton.FlatAppearance.MouseDownBackColor = scanning
                ? Color.FromArgb(175, 30, 30)
                : Color.FromArgb(0, 90, 175);
            scanButton.Enabled = true;
        }

        private async Task StartScanAsync()
        {
            if (scanCts != null)
                return;

            AppLogger.Info("Network scan", "started");
            var targets = new List<Tuple<string, string>>();
            ulong estimatedTargets = 0;

            if (scanAllCheck.Checked)
            {
                foreach (var a in adapters)
                {
                    var r = GetSuggestedRange(a.Ip, a.Mask);
                    estimatedTargets += CountRange(r.Item1, r.Item2);
                }

                if (estimatedTargets > MaxScanTargets)
                {
                    MessageBox.Show(T("Tarama aralığı çok geniş. En fazla " + MaxScanTargets + " hedef taranabilir.", "The scan range is too large. At most " + MaxScanTargets + " targets can be scanned."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (var a in adapters)
                {
                    var r = GetSuggestedRange(a.Ip, a.Mask);
                    targets.AddRange(BuildRange(r.Item1, r.Item2).Select(ip => Tuple.Create(ip, a.Name)));
                }
            }
            else
            {
                if (adapterCombo.SelectedIndex < 0)
                    return;

                IPAddress startIp;
                IPAddress endIp;

                if (!IPAddress.TryParse(rangeStart.Text, out startIp) ||
                    !IPAddress.TryParse(rangeEnd.Text, out endIp) ||
                    startIp.AddressFamily != AddressFamily.InterNetwork ||
                    endIp.AddressFamily != AddressFamily.InterNetwork)
                {
                    MessageBox.Show(
                        T("Geçerli bir IPv4 aralığı girin.", "Enter a valid IPv4 range."),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                estimatedTargets = CountRange(rangeStart.Text, rangeEnd.Text);
                if (estimatedTargets > MaxScanTargets)
                {
                    MessageBox.Show(T("Tarama aralığı çok geniş. En fazla " + MaxScanTargets + " hedef taranabilir.", "The scan range is too large. At most " + MaxScanTargets + " targets can be scanned."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                targets.AddRange(
                    BuildRange(rangeStart.Text, rangeEnd.Text)
                    .Select(ip => Tuple.Create(ip, adapters[adapterCombo.SelectedIndex].Name))
                );
            }

            if (targets.Count == 0)
                return;

            // Tarama başlamadan yerel adaptör bilgilerini kopyala.
            // Arka plan thread'lerinden WinForms kontrollerine erişmeyelim.
            var localMacByIp = adapters
                .Where(a =>
                    !string.IsNullOrWhiteSpace(a.Ip) &&
                    !string.IsNullOrWhiteSpace(a.Mac))
                .GroupBy(a => a.Ip)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().Mac,
                    StringComparer.OrdinalIgnoreCase);

            devices.Items.Clear();
            scanResults.Clear();

            deviceTypeFilter.Enabled = false;
            deviceTypeFilter.Items.Clear();
            deviceTypeFilter.Items.Add(T("Tümü", "All"));
            deviceTypeFilter.SelectedIndex = 0;

            progress.Value = 0;
            progress.Maximum = Math.Max(1, targets.Count);
            progressLabel.Text = "0 / " + targets.Count;

            statusLabel.Text =
                T("Tarama başladı • ", "Scan started • ") +
                MaxConcurrentScans +
                T(" eşzamanlı tarama", " concurrent scans");

            SetScanButtonState(true);
            exportButton.Enabled = false;

            scanCts = new CancellationTokenSource();
            CancellationToken ct = scanCts.Token;

            int done = 0;
            int found = 0;

            var limiter =
                new SemaphoreSlim(
                    MaxConcurrentScans,
                    MaxConcurrentScans);

            try
            {
                var scanTasks = targets.Select(async target =>
                {
                    bool entered = false;

                    try
                    {
                        await limiter
                            .WaitAsync(ct)
                            .ConfigureAwait(false);

                        entered = true;

                        ct.ThrowIfCancellationRequested();

                        string ip = target.Item1;
                        PingReply reply = null;

                        try
                        {
                            using (var ping = new Ping())
                            {
                                reply = await ping
                                    .SendPingAsync(ip, 220)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            reply = null;
                        }

                        ct.ThrowIfCancellationRequested();

                        DeviceInfo device = null;

                        if (reply != null &&
                            reply.Status == IPStatus.Success)
                        {
                            string mac =
                                GetMacFastParallel(
                                    ip,
                                    localMacByIp);

                            device = new DeviceInfo
                            {
                                Ip = ip,
                                Hostname = ip,
                                Mac = mac,
                                Vendor = T("Aranıyor...", "Searching..."),
                                DeviceType = T("Tespit ediliyor...", "Detecting..."),
                                Response =
                                    reply.RoundtripTime + " ms",
                                Status = T("Aktif", "Active"),
                                Network = target.Item2,
                                Seen = DateTime.Now
                            };

                                    Interlocked.Increment(ref found);
                                    AppLogger.Info("Device discovered", ip);
                                }

                                int current =
                            Interlocked.Increment(ref done);

                        if (IsDisposed || Disposing)
                            return;

                        try
                        {
                            Invoke((Action)(() =>
                            {
                                if (device != null)
                                {
                                    scanResults.Add(device);

                                    AddHistory(device);
                                    AddDeviceRow(device);

                                    // Hostname, üretici ve cihaz türü
                                    // taramayı bekletmeden devam etsin.
                                    _ = CompleteDeviceDetailsAsync(device);
                                }

                                progress.Value =
                                    Math.Min(
                                        progress.Maximum,
                                        current);

                                progressLabel.Text =
                                    current +
                                    " / " +
                                    targets.Count;

                                statusLabel.Text =
                                    T("Taranıyor • ", "Scanning • ") +
                                    found +
                                    T(" cihaz bulundu", " device(s) found");
                            }));
                        }
                        catch
                        {
                            // Form kapanıyorsa UI güncellemesini atla.
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Ana metodun cancellation yönetmesine izin ver.
                        throw;
                    }
                    catch
                    {
                        // Tek bir IP'deki hata tüm taramayı durdurmasın.
                        int current =
                            Interlocked.Increment(ref done);

                        if (!IsDisposed && !Disposing)
                        {
                            try
                            {
                                Invoke((Action)(() =>
                                {
                                    progress.Value =
                                        Math.Min(
                                            progress.Maximum,
                                            current);

                                    progressLabel.Text =
                                        current +
                                        " / " +
                                        targets.Count;
                                }));
                            }
                            catch
                            {
                            }
                        }
                    }
                    finally
                    {
                        if (entered)
                            limiter.Release();
                    }
                }).ToArray();

                await Task.WhenAll(scanTasks);

                statusLabel.Text =
                    scanResults.Count +
                    T(" cihaz bulundu • Tarama tamamlandı.", " device(s) found • Scan completed.");
                AppLogger.Info("Network scan", "completed; devices=" + scanResults.Count);

                exportButton.Enabled =
                    scanResults.Count > 0;

                RefreshDeviceTypeFilterItems();
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text =
                    T("Tarama durduruldu • ", "Scan stopped • ") +
                    scanResults.Count +
                    T(" cihaz bulundu.", " device(s) found.");
                AppLogger.Warning("Network scan", "cancelled; devices=" + scanResults.Count);

                exportButton.Enabled =
                    scanResults.Count > 0;

                RefreshDeviceTypeFilterItems();
            }
            finally
            {
                limiter.Dispose();

                if (scanCts != null)
                {
                    scanCts.Dispose();
                    scanCts = null;
                }

                SetScanButtonState(false);
            }
        }

        private string GetMacFastParallel(
        string ip,
        Dictionary<string, string> localMacByIp)
        {
            string localMac;

            if (localMacByIp != null &&
                localMacByIp.TryGetValue(ip, out localMac))
            {
                return localMac;
            }

            try
            {
                byte[] dst =
                    IPAddress.Parse(ip).GetAddressBytes();

                uint dest =
                    BitConverter.ToUInt32(dst, 0);

                byte[] mac =
                    new byte[6];

                int len = mac.Length;

                int result =
                    SendARP(
                        dest,
                        0,
                        mac,
                        ref len);

                if (result == 0 && len > 0)
                {
                    return FormatMac(
                        mac.Take(len).ToArray());
                }
            }
            catch
            {
            }

            return "-";
        }

        private async Task CompleteDeviceDetailsAsync(DeviceInfo d)
        {
            string hostname = "-";
            string vendor = T("Bilinmiyor", "Unknown");

            try
            {
                Task<string> hostnameTask =
                    ResolveHostnameFastAsync(d.Ip, 450);

                Task<string> vendorTask =
                    GuessVendorAsync(d.Mac, d.Ip);

                try
                {
                    hostname = await hostnameTask;
                }
                catch
                {
                    hostname = "-";
                }

                try
                {
                    vendor = await vendorTask;
                }
                catch
                {
                    vendor = T("Bilinmiyor", "Unknown");
                }

                if (IsUnknownVendor(vendor))
                {
                    string hostnameVendor = GuessVendorFromHostname(hostname);

                    if (!string.IsNullOrWhiteSpace(hostnameVendor))
                        vendor = hostnameVendor;
                }
            }
            catch
            {
            }

            string deviceType = T("Diğer", "Other");
            try
            {
                deviceType = await DetectDeviceTypeAsync(
    d.Ip,
    hostname,
    vendor);
            }
            catch
            {
                deviceType = T("Diğer", "Other");
            }

            if (IsDisposed || Disposing)
                return;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!scanResults.Contains(d))
                        return;

                    d.Hostname =
                       string.IsNullOrWhiteSpace(hostname) || hostname == "-"
                            ? d.Ip
                            : hostname;

                    UpdateLatestHistoryDetails(d);

                    d.Vendor =
                        string.IsNullOrWhiteSpace(vendor)
                            ? T("Bilinmiyor", "Unknown")
                            : vendor;

                    d.DeviceType =
                        string.IsNullOrWhiteSpace(deviceType)
                            ? T("Diğer", "Other")
                            : deviceType;

                    RefreshDeviceTypeFilterItems();

                    // Cihaz türü arka planda tamamlandığında aktif filtreyi yenile.
                    if (deviceTypeFilter.SelectedIndex > 0)
                    {
                        ApplyDeviceTypeFilter();
                    }

                    foreach (ListViewItem item in devices.Items)
                    {
                        if (ReferenceEquals(item.Tag, d))
                        {
                            if (item.SubItems.Count > 1)
                                item.SubItems[1].Text = d.Hostname;

                            if (item.SubItems.Count > 3)
                                item.SubItems[3].Text = d.Vendor;

                            if (item.SubItems.Count > 4)
                                item.SubItems[4].Text = d.DeviceType;

                            break;
                        }
                    }

                    var selected = GetSelectedDevice();

                    if (selected != null && ReferenceEquals(selected, d))
                    {
                        UpdateDeviceInfoText(d);
                    }
                }));
            }
            catch
            {
            }
        }

        private async Task<string> DetectDeviceTypeAsync(string ip, string hostname, string vendor)
        {
            await DetailScanLimiter.WaitAsync();
            try
            {
                return await DetectDeviceTypeCoreAsync(ip, hostname, vendor);
            }
            finally
            {
                DetailScanLimiter.Release();
            }
        }

        private async Task<string> DetectDeviceTypeCoreAsync(
            string ip,
            string hostname,
            string vendor)
        {
            string v =
                (vendor ?? "")
                .ToUpperInvariant();

            string h =
                (hostname ?? "")
                .ToUpperInvariant();

            // ---------------------------------------
            // 1. GATEWAY / ROUTER TESPİTİ
            // ---------------------------------------

            foreach (var a in adapters)
            {
                if (!string.IsNullOrWhiteSpace(a.Gateway) &&
                    string.Equals(
                        a.Gateway,
                        ip,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return T("Router / Gateway", "Router / Gateway");
                }
            }

            // ---------------------------------------
            // 2. GEREKLİ PORTLARI PARALEL TARA
            // ---------------------------------------

            var p22 = TestTcpPortAsync(ip, 22, 180);    // SSH
            var p23 = TestTcpPortAsync(ip, 23, 180);    // Telnet
            var p53 = TestTcpPortAsync(ip, 53, 180);    // DNS
            var p80 = TestTcpPortAsync(ip, 80, 180);    // HTTP
            var p135 = TestTcpPortAsync(ip, 135, 180);   // RPC
            var p139 = TestTcpPortAsync(ip, 139, 180);   // NetBIOS
            var p443 = TestTcpPortAsync(ip, 443, 180);   // HTTPS
            var p445 = TestTcpPortAsync(ip, 445, 180);   // SMB
            var p515 = TestTcpPortAsync(ip, 515, 180);   // LPD Printer
            var p548 = TestTcpPortAsync(ip, 548, 180);   // AFP
            var p554 = TestTcpPortAsync(ip, 554, 180);   // RTSP
            var p631 = TestTcpPortAsync(ip, 631, 180);   // IPP Printer
            var p8000 = TestTcpPortAsync(ip, 8000, 180);  // Hikvision
            var p8080 = TestTcpPortAsync(ip, 8080, 180);  // HTTP Alt
            var p8443 = TestTcpPortAsync(ip, 8443, 180);  // HTTPS Alt
            var p9100 = TestTcpPortAsync(ip, 9100, 180);  // RAW Printer
            var p3389 = TestTcpPortAsync(ip, 3389, 180);  // RDP
            var p5900 = TestTcpPortAsync(ip, 5900, 180);  // VNC

            var p2049 = TestTcpPortAsync(ip, 2049, 180);  // NFS
            var p32400 = TestTcpPortAsync(ip, 32400, 180); // Plex

            var p37777 =
                TestTcpPortAsync(ip, 37777, 180);          // Dahua

            var p34567 =
                TestTcpPortAsync(ip, 34567, 180);          // DVR/NVR

            var p8899 =
                TestTcpPortAsync(ip, 8899, 180);           // Kamera

            await Task.WhenAll(
                p22,
                p23,
                p53,
                p80,
                p135,
                p139,
                p443,
                p445,
                p515,
                p548,
                p554,
                p631,
                p8000,
                p8080,
                p8443,
                p9100,
                p3389,
                p5900,
                p2049,
                p32400,
                p37777,
                p34567,
                p8899);

            bool ssh = p22.Result;
            bool telnet = p23.Result;
            bool dns = p53.Result;
            bool http = p80.Result;
            bool rpc = p135.Result;
            bool netbios = p139.Result;
            bool https = p443.Result;
            bool smb = p445.Result;
            bool lpd = p515.Result;
            bool afp = p548.Result;
            bool rtsp = p554.Result;
            bool ipp = p631.Result;
            bool hikPort = p8000.Result;
            bool httpAlt = p8080.Result;
            bool httpsAlt = p8443.Result;
            bool printer = p9100.Result;
            bool rdp = p3389.Result;
            bool vnc = p5900.Result;
            bool nfs = p2049.Result;
            bool plex = p32400.Result;
            bool dahuaPort = p37777.Result;
            bool dvrPort = p34567.Result;
            bool camPort = p8899.Result;

            // ---------------------------------------
            // 3. KAMERA / NVR
            // ---------------------------------------

            bool hikvision =
                v.Contains("HIKVISION");

            bool dahua =
                v.Contains("DAHUA");

            bool cameraVendor =
                hikvision ||
                dahua ||
                v.Contains("AXIS") ||
                v.Contains("VIVOTEK") ||
                v.Contains("UNIVIEW") ||
                v.Contains("XM") ||
                v.Contains("HANGZHOU");

            if (hikvision)
            {
                if (hikPort && rtsp)
                    return T("Kamera / NVR", "Camera / NVR");

                if (hikPort)
                    return T("NVR / DVR", "NVR / DVR");

                if (rtsp)
                    return T("IP Kamera / NVR", "IP Camera / NVR");

                return T("Kamera / NVR", "Camera / NVR");
            }

            if (dahua)
            {
                if (dahuaPort && rtsp)
                    return T("Kamera / NVR", "Camera / NVR");

                if (dahuaPort)
                    return T("NVR / DVR", "NVR / DVR");

                if (rtsp)
                    return T("IP Kamera / NVR", "IP Camera / NVR");

                return T("Kamera / NVR", "Camera / NVR");
            }

            if (dahuaPort || dvrPort)
            {
                return rtsp
                    ? T("Kamera / NVR", "Camera / NVR")
                    : T("NVR / DVR", "NVR / DVR");
            }

            if (hikPort && rtsp)
                return T("Kamera / NVR", "Camera / NVR");

            if ((camPort && rtsp) ||
                (cameraVendor && rtsp))
            {
                return T("IP Kamera", "IP Camera");
            }

            // ---------------------------------------
            // 4. YAZICI
            // ---------------------------------------

            bool printerVendor =
                v.Contains("HP") ||
                v.Contains("HEWLETT") ||
                v.Contains("EPSON") ||
                v.Contains("CANON") ||
                v.Contains("BROTHER") ||
                v.Contains("RICOH") ||
                v.Contains("KYOCERA") ||
                v.Contains("XEROX") ||
                v.Contains("LEXMARK");

            bool printerHostname =
                h.Contains("PRINTER") ||
                h.Contains("YAZICI") ||
                h.StartsWith("HP") ||
                h.StartsWith("EPSON") ||
                h.StartsWith("BROTHER");

            if (printer ||
                lpd ||
                ipp ||
                (printerVendor &&
                 (http || https || httpAlt)))
            {
                return T("Yazıcı", "Printer");
            }

            if (printerVendor && printerHostname)
                return T("Yazıcı", "Printer");

            // ---------------------------------------
            // 5. NAS
            // ---------------------------------------

            bool nasVendor =
                v.Contains("SYNOLOGY") ||
                v.Contains("QNAP") ||
                v.Contains("ASUSTOR") ||
                v.Contains("BUFFALO") ||
                v.Contains("WESTERN DIGITAL");

            bool nasHostname =
                h.Contains("NAS") ||
                h.Contains("SYNOLOGY") ||
                h.Contains("QNAP") ||
                h.Contains("DISKSTATION");

            if (nasVendor ||
                nasHostname ||
                (nfs && smb) ||
                (plex && smb))
            {
                return T("NAS / Dosya Sunucusu", "NAS / File Server");
            }

            // ---------------------------------------
            // 6. WINDOWS PC / WINDOWS SERVER
            // ---------------------------------------

            bool windowsPorts =
                rpc &&
                (smb || netbios);

            bool windowsHostname =
                h.StartsWith("DESKTOP-") ||
                h.StartsWith("LAPTOP-") ||
                h.Contains("WINDOWS") ||
                h.Contains("WIN-");

            if (windowsPorts)
            {
                if (rdp &&
                    (h.Contains("SERVER") ||
                     h.StartsWith("SRV") ||
                     h.StartsWith("DC")))
                {
                    return T("Windows Sunucu", "Windows Server");
                }

                if (rdp)
                    return T("Windows PC", "Windows PC");

                if (windowsHostname)
                    return T("Windows PC", "Windows PC");

                return T("Windows Cihazı", "Windows Device");
            }

            // ---------------------------------------
            // 7. ROUTER / ACCESS POINT / SWITCH
            // ---------------------------------------

            bool networkVendor =
                v.Contains("TP-LINK") ||
                v.Contains("TP LINK") ||
                v.Contains("UBIQUITI") ||
                v.Contains("MIKROTIK") ||
                v.Contains("CISCO") ||
                v.Contains("ARUBA") ||
                v.Contains("NETGEAR") ||
                v.Contains("ZYXEL") ||
                v.Contains("HUAWEI") ||
                v.Contains("ZTE") ||
                v.Contains("ASUS") ||
                v.Contains("D-LINK");

            bool networkHostname =
                h.Contains("ROUTER") ||
                h.Contains("SWITCH") ||
                h.Contains("ACCESSPOINT") ||
                h.Contains("ACCESS-POINT") ||
                h.StartsWith("AP-");

            if (networkVendor &&
                (dns ||
                 telnet ||
                 ssh ||
                 http ||
                 https))
            {
                if (h.Contains("SWITCH"))
                    return T("Network Switch", "Network Switch");

                if (networkHostname)
                    return T("Access Point / Router", "Access Point / Router");

                return T("Ağ Cihazı", "Network Device");
            }

            // ---------------------------------------
            // 8. APPLE / TELEFON / TABLET
            // ---------------------------------------

            bool apple =
                v.Contains("APPLE");

            bool mobileVendor =
                apple ||
                v.Contains("SAMSUNG") ||
                v.Contains("XIAOMI") ||
                v.Contains("OPPO") ||
                v.Contains("VIVO") ||
                v.Contains("ONEPLUS") ||
                v.Contains("REALME");

            bool mobileHostname =
                h.Contains("IPHONE") ||
                h.Contains("IPAD") ||
                h.Contains("ANDROID") ||
                h.Contains("GALAXY") ||
                h.Contains("XIAOMI");

            if (mobileHostname)
                return T("Telefon / Tablet", "Phone / Tablet");

            if (mobileVendor &&
                !smb &&
                !rpc &&
                !printer &&
                !rtsp)
            {
                return T("Telefon / Mobil Cihaz", "Phone / Mobile Device");
            }

            // ---------------------------------------
            // 9. LINUX / UNIX
            // ---------------------------------------

            bool linuxHostname =
                h.Contains("LINUX") ||
                h.Contains("UBUNTU") ||
                h.Contains("DEBIAN") ||
                h.Contains("CENTOS") ||
                h.Contains("FEDORA") ||
                h.Contains("RASPBERRY");

            if (linuxHostname)
                return T("Linux Cihazı", "Linux Device");

            if (ssh &&
                !rpc &&
                !rdp &&
                !rtsp)
            {
                if (smb || nfs)
                    return T("Linux Sunucu", "Linux Server");

                return T("Linux / Unix Cihazı", "Linux / Unix Device");
            }

            // ---------------------------------------
            // 10. MAC / MACBOOK
            // ---------------------------------------

            if (apple && (afp || smb))
                return T("Mac / Apple Bilgisayar", "Mac / Apple Computer");

            // ---------------------------------------
            // 11. UZAK MASAÜSTÜ / VNC
            // ---------------------------------------

            if (rdp)
                return T("Windows PC", "Windows PC");

            if (vnc)
                return T("Bilgisayar / Sunucu", "Computer / Server");

            // ---------------------------------------
            // 12. SADECE RTSP
            // ---------------------------------------

            if (rtsp)
                return T("RTSP Cihazı", "RTSP Device");

            // ---------------------------------------
            // 13. WEB ARAYÜZLÜ CİHAZ
            // ---------------------------------------

            if (http ||
                https ||
                httpAlt ||
                httpsAlt)
            {
                return "Web Yönetimli Cihaz";
            }

            return "Diğer";
        }

        private void AddDeviceRow(DeviceInfo d)
        {
            if (!MatchesDeviceTypeFilter(d))
                return;

            AddDeviceRowCore(d);
        }

        private void AddDeviceRowCore(DeviceInfo d)
        {
            var item = new ListViewItem(d.Ip);

            item.SubItems.Add(d.Hostname);
            item.SubItems.Add(d.Mac);
            item.SubItems.Add(d.Vendor);
            item.SubItems.Add(d.DeviceType);
            item.SubItems.Add(d.Response);
            item.SubItems.Add(d.Status);
            item.SubItems.Add(d.Network);

            item.Tag = d;

            devices.Items.Add(item);
        }

        private void RefreshDeviceTypeFilterItems()
        {
            string currentSelection =
                deviceTypeFilter.SelectedItem as string ?? T("Tümü", "All");

            currentSelection = LocalizeValue(currentSelection);

            var types = scanResults
                .Select(d => d.DeviceType)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    !string.Equals(
                        x,
                        T("Tespit ediliyor...", "Detecting..."),
                        StringComparison.CurrentCultureIgnoreCase))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            deviceTypeFilter.BeginUpdate();

            try
            {
                deviceTypeFilter.Items.Clear();
                deviceTypeFilter.Items.Add(T("Tümü", "All"));

                foreach (string type in types)
                    deviceTypeFilter.Items.Add(type);

                int selectedIndex = 0;

                for (int i = 0;
                     i < deviceTypeFilter.Items.Count;
                     i++)
                {
                    if (string.Equals(
                        deviceTypeFilter.Items[i].ToString(),
                        currentSelection,
                        StringComparison.CurrentCultureIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                deviceTypeFilter.SelectedIndex =
                    selectedIndex;
            }
            finally
            {
                deviceTypeFilter.EndUpdate();
            }

            deviceTypeFilter.Enabled =
                scanResults.Count > 0 &&
                types.Count > 0;
        }

        private bool MatchesDeviceTypeFilter(DeviceInfo d)
        {
            if (deviceTypeFilter.SelectedIndex <= 0)
                return true;

            string selected =
                deviceTypeFilter.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selected))
                return true;

            return string.Equals(
                d.DeviceType ?? "",
                selected,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private void ApplyDeviceTypeFilter()
        {
            if (IsDisposed || Disposing)
                return;

            string selectedIpBeforeFilter =
                selectedIp;

            devices.BeginUpdate();

            try
            {
                devices.Items.Clear();

                foreach (var d in scanResults)
                {
                    if (MatchesDeviceTypeFilter(d))
                        AddDeviceRowCore(d);
                }

                devices.Sort();

                if (!string.IsNullOrWhiteSpace(
                    selectedIpBeforeFilter))
                {
                    foreach (ListViewItem item
                        in devices.Items)
                    {
                        var d =
                            item.Tag as DeviceInfo;

                        if (d != null &&
                            string.Equals(
                                d.Ip,
                                selectedIpBeforeFilter,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            item.Selected = true;
                            item.Focused = true;

                            break;
                        }
                    }
                }
            }
            finally
            {
                devices.EndUpdate();
            }

            string selected =
                deviceTypeFilter.SelectedItem as string
                ?? T("Tümü", "All");

            if (deviceTypeFilter.SelectedIndex <= 0)
            {
                statusLabel.Text =
                    scanResults.Count +
                    T(" cihaz gösteriliyor.", " device(s) shown.");
            }
            else
            {
                statusLabel.Text =
                    selected +
                    ": " +
                    devices.Items.Count +
                    T(" cihaz", " device(s)");
            }
        }

        private void Devices_SelectedIndexChanged(object sender, EventArgs e)
        {
            var d = GetSelectedDevice();
            if (d == null) return;
            selectedIp = d.Ip;

            UpdateDeviceInfoText(d);

            RefreshPortsTab(d.Ip);
            RefreshHistoryTab(d.Ip);
            shareStatus.Text = T("Seçili cihaz: ", "Selected device: ") + d.Ip;
        }

        private DeviceInfo GetSelectedDevice()
        {
            if (devices.SelectedItems.Count == 0) return null;
            return devices.SelectedItems[0].Tag as DeviceInfo;
        }

        private async Task ScanSelectedPortsAsync()
        {
            var d = GetSelectedDevice();

            if (d == null)
            {
                MessageBox.Show(
                    T("Önce bir cihaz seçin.", "Select a device first."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            bottomTabs.SelectedIndex = 1;
            AppLogger.Info("Port scan", "started; target=" + d.Ip);

            portsList.Items.Clear();

            portsList.Items.Add(
                new ListViewItem(
                    new[]
                    {
                "...",
                T("Portlar taranıyor...", "Scanning ports..."),
                T("Bekleyin", "Please wait")
                    }));

            scanPortsButton.Enabled = false;
            scanPortsButton.Text = T("Taranıyor...", "Scanning...");

            var common = new[]
            {
        Tuple.Create(21, "FTP"),
        Tuple.Create(22, "SSH"),
        Tuple.Create(23, "Telnet"),
        Tuple.Create(25, "SMTP"),
        Tuple.Create(53, "DNS"),
        Tuple.Create(80, "HTTP"),
        Tuple.Create(110, "POP3"),
        Tuple.Create(135, "MS RPC"),
        Tuple.Create(139, "NetBIOS"),
        Tuple.Create(143, "IMAP"),
        Tuple.Create(443, "HTTPS"),
        Tuple.Create(445, "SMB"),
        Tuple.Create(465, "SMTPS"),
        Tuple.Create(587, "SMTP Submission"),
        Tuple.Create(993, "IMAPS"),
        Tuple.Create(995, "POP3S"),
        Tuple.Create(1433, "MS SQL"),
        Tuple.Create(3306, "MySQL"),
        Tuple.Create(3389, "RDP"),
        Tuple.Create(5900, "VNC"),
        Tuple.Create(554, "RTSP"),
        Tuple.Create(8000, "Hikvision SDK"),
        Tuple.Create(37777, "Dahua Service"),
        Tuple.Create(34567, "DVR/NVR Service"),
        Tuple.Create(8899, "Camera Service"),
        Tuple.Create(8080, "HTTP Alt"),
        Tuple.Create(8443, "HTTPS Alt")
    };

            try
            {
                var tasks = common.Select(async p =>
                {
                    bool open =
                        await TestTcpPortAsync(
                            d.Ip,
                            p.Item1,
                            400);

                    return new PortResult
                    {
                        Port = p.Item1,
                        Service = p.Item2,
                        Open = open
                    };
                });

                PortResult[] results =
                    await Task.WhenAll(tasks);

                var list =
                    results
                        .OrderBy(x => x.Port)
                        .ToList();

                portCache[d.Ip] = list;

                portsList.BeginUpdate();

                try
                {
                    portsList.Items.Clear();

                    foreach (var r in list.Where(x => x.Open))
                    {
                        var item =
                            new ListViewItem(
                                r.Port.ToString());

                        item.SubItems.Add(r.Service);
                        item.SubItems.Add(T("Açık", "Open"));

                        portsList.Items.Add(item);
                    }

                    if (portsList.Items.Count == 0)
                    {
                        portsList.Items.Add(
                            new ListViewItem(
                                new[]
                                {
                            "-",
                            T("Açık port bulunamadı.", "No open ports found."),
                            "-"
                                }));
                    }
                }
                finally
                {
                    portsList.EndUpdate();
                }

                int openCount =
                    list.Count(x => x.Open);

                statusLabel.Text =
                    d.Ip +
                    " • " +
                    openCount +
                    T(" açık port bulundu.", " open port(s) found.");
                AppLogger.Info("Port scan", "completed; target=" + d.Ip + "; open=" + openCount);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Port scan", d.Ip, ex);
                portsList.Items.Clear();

                portsList.Items.Add(
                    new ListViewItem(
                        new[]
                        {
                    "-",
                    T("Port taraması başarısız", "Port scan failed"),
                    "-"
                        }));

                statusLabel.Text =
                    T("Port tarama hatası: ", "Port scan error: ") +
                    ex.Message;
            }
            finally
            {
                scanPortsButton.Enabled = true;
                scanPortsButton.Text = T("Portları Tara", "Scan Ports");
            }
        }



        private void RefreshPortsTab(string ip)
        {
            portsList.Items.Clear();
            if (!portCache.TryGetValue(ip, out var list))
            {
                portsList.Items.Add(new ListViewItem(new[] { "-", T("Henüz port taraması yapılmadı.", "Port scan has not been run yet."), "-" }));
                return;
            }
            foreach (var r in list.Where(x => x.Open))
                portsList.Items.Add(new ListViewItem(new[] { r.Port.ToString(), r.Service, T("Açık", "Open") }));

            if (portsList.Items.Count == 0)
                portsList.Items.Add(new ListViewItem(new[] { "-", T("Açık port bulunamadı.", "No open ports found."), "-" }));
        }

        private async Task ScanSelectedSharesAsync()
        {
            var d = GetSelectedDevice();
            if (d == null)
            {
                MessageBox.Show(T("Önce bir cihaz seçin.", "Select a device first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            sharesList.Items.Clear();
            shareStatus.Text = T("SMB kontrol ediliyor...", "Checking SMB...");

            bool smb445 = await TestTcpPortAsync(d.Ip, 445, 400);
            bool smb139 = await TestTcpPortAsync(d.Ip, 139, 400);
            if (!smb445 && !smb139)
            {
                sharesList.Items.Add(new ListViewItem(new[] { "-", "-", T("SMB servisi kapalı", "SMB service is closed") }));
                shareStatus.Text = T("445 ve 139 portları kapalı.", "Ports 445 and 139 are closed.");
                return;
            }

            try
            {
                var shares = NetShareEnumerator.GetShares(d.Ip);
                if (shares.Count == 0)
                {
                    sharesList.Items.Add(new ListViewItem(new[] { "-", "-", T("Paylaşım bulunamadı", "No share found") }));
                    shareStatus.Text = T("SMB açık • Paylaşım bulunamadı.", "SMB open • No share found.");
                    return;
                }

                foreach (var sh in shares)
                    sharesList.Items.Add(new ListViewItem(new[] { sh.Name, LocalizeValue(sh.Type), @"\\" + d.Ip + "\\" + sh.Name }));

                shareStatus.Text = shares.Count + T(" paylaşım bulundu.", " share(s) found.");
            }
            catch (UnauthorizedAccessException)
            {
                sharesList.Items.Add(new ListViewItem(new[] { "-", "-", T("Kimlik doğrulama gerekli", "Authentication required") }));
                shareStatus.Text = T("SMB açık • Kullanıcı adı/parola gerekli.", "SMB open • Username/password required.");
            }
            catch (Exception ex)
            {
                sharesList.Items.Add(new ListViewItem(new[] { "-", "-", T("Paylaşımlar okunamadı", "Shares could not be read") }));
                shareStatus.Text = ex.Message;
            }
        }

        private async Task<bool> TestTcpPortAsync(string ip, int port, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var task = client.ConnectAsync(ip, port);
                    var winner = await Task.WhenAny(task, Task.Delay(timeoutMs));
                    return winner == task && client.Connected;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("TCP port test", ip + ":" + port, ex);
                    return false;
                }
            }
        }

        private async Task<string> ResolveHostnameFastAsync(string ip, int timeoutMs)
        {
            // Ana taramayı bekletmez; CompleteDeviceDetailsAsync içinden
            // arka planda çağrılır. Birkaç Windows isim çözüm yöntemini aynı
            // anda başlatıp ilk geçerli sonucu kullanıyoruz.

            var tasks = new List<Task<string>>
            {
                ResolveHostnameSmbNtlmAsync(ip),
                ResolveHostnameNbnsNodeStatusAsync(ip),
                ResolveHostnameMdnsAsync(ip),
                ResolveHostnameDnsAsync(ip),
                ResolveHostnamePingAAsync(ip),
                ResolveHostnameNetBiosAsync(ip),
                Task.Run(() => GetServerNameFromSmb(ip))
            };

            int maxWait = Math.Max(1200, Math.Min(timeoutMs * 4, 1900));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(maxWait);

            while (tasks.Count > 0)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                    break;

                Task timeoutTask = Task.Delay(remaining);
                Task finished = await Task.WhenAny(
                    tasks.Cast<Task>().Concat(new[] { timeoutTask }));

                if (finished == timeoutTask)
                    break;

                var completed = finished as Task<string>;
                tasks.Remove(completed);

                try
                {
                    string name = await completed;

                    if (IsUsefulHostname(name, ip))
                        return CleanHostname(name);
                }
                catch
                {
                }
            }

            return "-";
        }

        private async Task<string> ResolveHostnameSmbNtlmAsync(string ip)
        {
            // Windows SMB2/NTLM sunucusunun CHALLENGE_MESSAGE TargetInfo
            // alanından bilgisayar adını okur. Kimlik bilgisi gönderilmez.
            TcpClient client = null;

            try
            {
                client = new TcpClient();

                Task connectTask = client.ConnectAsync(ip, 445);
                Task connectWinner = await Task.WhenAny(
                    connectTask,
                    Task.Delay(450));

                if (connectWinner != connectTask || !client.Connected)
                    return "";

                using (NetworkStream stream = client.GetStream())
                {
                    // 1) SMB2 NEGOTIATE
                    byte[] negotiate =
                        BuildSmb2NegotiatePacket();

                    await stream.WriteAsync(
                        negotiate,
                        0,
                        negotiate.Length);

                    byte[] negotiateResponse =
                        await ReadSmbFrameAsync(stream, 650);

                    if (negotiateResponse == null ||
                        negotiateResponse.Length < 64)
                        return "";

                    // 2) SMB2 SESSION_SETUP + SPNEGO/NTLM Type 1
                    byte[] type1 =
                        BuildNtlmType1Message();

                    byte[] spnego =
                        BuildSpnegoNtlmToken(type1);

                    byte[] sessionSetup =
                        BuildSmb2SessionSetupPacket(spnego);

                    await stream.WriteAsync(
                        sessionSetup,
                        0,
                        sessionSetup.Length);

                    byte[] response =
                        await ReadSmbFrameAsync(stream, 800);

                    if (response == null ||
                        response.Length < 80)
                        return "";

                    // SMB2 response içindeki SPNEGO blobunu ayrı çözmeye gerek yok;
                    // NTLMSSP imzasını bulup Type 2 mesajını doğrudan ayrıştırıyoruz.
                    int ntlmOffset =
                        FindAsciiSequence(
                            response,
                            "NTLMSSP\0");

                    if (ntlmOffset < 0)
                        return "";

                    return ParseNtlmType2ComputerName(
                        response,
                        ntlmOffset);
                }
            }
            catch
            {
                return "";
            }
            finally
            {
                if (client != null)
                {
                    try { client.Close(); }
                    catch { }
                }
            }
        }

        private byte[] BuildSmb2NegotiatePacket()
        {
            var body = new List<byte>();

            AddUInt16LE(body, 36);  // StructureSize
            AddUInt16LE(body, 2);   // DialectCount
            AddUInt16LE(body, 1);   // SecurityMode: signing enabled
            AddUInt16LE(body, 0);   // Reserved
            AddUInt32LE(body, 0);   // Capabilities

            byte[] guid =
                Guid.NewGuid().ToByteArray();

            body.AddRange(guid);

            AddUInt32LE(body, 0);   // NegotiateContextOffset
            AddUInt16LE(body, 0);   // NegotiateContextCount
            AddUInt16LE(body, 0);   // Reserved2

            // SMB 2.0.2 ve SMB 2.1.
            // Windows 7+ ve modern Windows sunucularıyla uyumludur.
            AddUInt16LE(body, 0x0202);
            AddUInt16LE(body, 0x0210);

            byte[] header =
                BuildSmb2Header(
                    0x0000, // NEGOTIATE
                    0);

            return WrapNetbiosSession(
                CombineBytes(
                    header,
                    body.ToArray()));
        }

        private byte[] BuildSmb2SessionSetupPacket(
            byte[] securityBlob)
        {
            var body = new List<byte>();

            AddUInt16LE(body, 25);  // StructureSize
            body.Add(0x00);         // Flags
            body.Add(0x01);         // SecurityMode
            AddUInt32LE(body, 0);   // Capabilities
            AddUInt32LE(body, 0);   // Channel
            AddUInt16LE(body, 88);  // SecurityBufferOffset = 64 + 24
            AddUInt16LE(
                body,
                (ushort)securityBlob.Length);
            AddUInt64LE(body, 0);   // PreviousSessionId

            body.AddRange(securityBlob);

            byte[] header =
                BuildSmb2Header(
                    0x0001, // SESSION_SETUP
                    1);

            return WrapNetbiosSession(
                CombineBytes(
                    header,
                    body.ToArray()));
        }

        private byte[] BuildSmb2Header(
            ushort command,
            ulong messageId)
        {
            var h = new List<byte>();

            h.Add(0xFE);
            h.Add((byte)'S');
            h.Add((byte)'M');
            h.Add((byte)'B');

            AddUInt16LE(h, 64);        // StructureSize
            AddUInt16LE(h, 0);         // CreditCharge
            AddUInt32LE(h, 0);         // Status / ChannelSequence
            AddUInt16LE(h, command);
            AddUInt16LE(h, 1);         // CreditRequest
            AddUInt32LE(h, 0);         // Flags
            AddUInt32LE(h, 0);         // NextCommand
            AddUInt64LE(h, messageId);
            AddUInt32LE(h, 0x0000FEFF); // ProcessId
            AddUInt32LE(h, 0);         // TreeId
            AddUInt64LE(h, 0);         // SessionId

            for (int i = 0; i < 16; i++)
                h.Add(0);

            return h.ToArray();
        }

        private byte[] BuildNtlmType1Message()
        {
            var b = new List<byte>();

            b.AddRange(
                Encoding.ASCII.GetBytes(
                    "NTLMSSP\0"));

            AddUInt32LE(b, 1); // NEGOTIATE_MESSAGE

            // Unicode + OEM + RequestTarget + NTLM +
            // AlwaysSign + ExtendedSessionSecurity +
            // TargetInfo + 128-bit + 56-bit.
            AddUInt32LE(b, 0xA0888207);

            // Domain security buffer: empty, offset 32
            AddUInt16LE(b, 0);
            AddUInt16LE(b, 0);
            AddUInt32LE(b, 32);

            // Workstation security buffer: empty, offset 32
            AddUInt16LE(b, 0);
            AddUInt16LE(b, 0);
            AddUInt32LE(b, 32);

            return b.ToArray();
        }

        private byte[] BuildSpnegoNtlmToken(
            byte[] ntlmToken)
        {
            byte[] spnegoOid =
            {
                0x2B, 0x06, 0x01,
                0x05, 0x05, 0x02
            };

            byte[] ntlmOid =
            {
                0x2B, 0x06, 0x01, 0x04,
                0x01, 0x82, 0x37, 0x02,
                0x02, 0x0A
            };

            byte[] mechOid =
                DerTlv(0x06, ntlmOid);

            byte[] mechList =
                DerTlv(
                    0x30,
                    mechOid);

            byte[] mechTypes =
                DerTlv(
                    0xA0,
                    mechList);

            byte[] octetToken =
                DerTlv(
                    0x04,
                    ntlmToken);

            byte[] mechToken =
                DerTlv(
                    0xA2,
                    octetToken);

            byte[] negTokenInitSeq =
                DerTlv(
                    0x30,
                    CombineBytes(
                        mechTypes,
                        mechToken));

            byte[] negTokenInit =
                DerTlv(
                    0xA0,
                    negTokenInitSeq);

            return DerTlv(
                0x60,
                CombineBytes(
                    DerTlv(0x06, spnegoOid),
                    negTokenInit));
        }

        private byte[] DerTlv(
            byte tag,
            byte[] value)
        {
            var result = new List<byte>();

            result.Add(tag);
            result.AddRange(
                DerLength(value.Length));
            result.AddRange(value);

            return result.ToArray();
        }

        private byte[] DerLength(int length)
        {
            if (length < 0x80)
                return new[]
                {
                    (byte)length
                };

            if (length <= 0xFF)
                return new[]
                {
                    (byte)0x81,
                    (byte)length
                };

            return new[]
            {
                (byte)0x82,
                (byte)(length >> 8),
                (byte)(length & 0xFF)
            };
        }

        private async Task<byte[]> ReadSmbFrameAsync(
            NetworkStream stream,
            int timeoutMs)
        {
            byte[] nbss =
                await ReadExactAsync(
                    stream,
                    4,
                    timeoutMs);

            if (nbss == null ||
                nbss.Length != 4)
                return null;

            int length =
                (nbss[1] << 16) |
                (nbss[2] << 8) |
                nbss[3];

            if (length <= 0 ||
                length > 1024 * 1024)
                return null;

            return await ReadExactAsync(
                stream,
                length,
                timeoutMs);
        }

        private async Task<byte[]> ReadExactAsync(
            NetworkStream stream,
            int count,
            int timeoutMs)
        {
            byte[] buffer =
                new byte[count];

            int offset = 0;

            DateTime deadline =
                DateTime.UtcNow.AddMilliseconds(
                    timeoutMs);

            while (offset < count)
            {
                int remaining =
                    (int)(deadline - DateTime.UtcNow)
                    .TotalMilliseconds;

                if (remaining <= 0)
                    return null;

                Task<int> readTask =
                    stream.ReadAsync(
                        buffer,
                        offset,
                        count - offset);

                Task winner =
                    await Task.WhenAny(
                        readTask,
                        Task.Delay(remaining));

                if (winner != readTask)
                    return null;

                int read =
                    await readTask;

                if (read <= 0)
                    return null;

                offset += read;
            }

            return buffer;
        }

        private string ParseNtlmType2ComputerName(
            byte[] packet,
            int ntlmOffset)
        {
            try
            {
                // Signature + MessageType doğrulaması.
                if (ntlmOffset < 0 ||
                    ntlmOffset + 48 > packet.Length)
                    return "";

                uint messageType =
                    ReadUInt32LE(
                        packet,
                        ntlmOffset + 8);

                if (messageType != 2)
                    return "";

                ushort targetInfoLength =
                    ReadUInt16LE(
                        packet,
                        ntlmOffset + 40);

                uint targetInfoOffset =
                    ReadUInt32LE(
                        packet,
                        ntlmOffset + 44);

                int start =
                    ntlmOffset +
                    (int)targetInfoOffset;

                int end =
                    start +
                    targetInfoLength;

                if (start < ntlmOffset ||
                    end > packet.Length ||
                    start >= end)
                    return "";

                string nbComputerName = "";
                string dnsComputerName = "";

                int pos = start;

                while (pos + 4 <= end)
                {
                    ushort avId =
                        ReadUInt16LE(
                            packet,
                            pos);

                    ushort avLen =
                        ReadUInt16LE(
                            packet,
                            pos + 2);

                    pos += 4;

                    if (avId == 0)
                        break;

                    if (pos + avLen > end)
                        break;

                    // MsvAvNbComputerName = 1
                    // MsvAvDnsComputerName = 3
                    if ((avId == 1 ||
                         avId == 3) &&
                        avLen > 0)
                    {
                        string value =
                            Encoding.Unicode.GetString(
                                packet,
                                pos,
                                avLen).TrimEnd('\0');

                        if (avId == 1)
                            nbComputerName = value;
                        else if (avId == 3)
                            dnsComputerName = value;
                    }

                    pos += avLen;
                }

                if (IsUsefulHostname(
                    nbComputerName,
                    ""))
                {
                    return CleanHostname(
                        nbComputerName);
                }

                if (IsUsefulHostname(
                    dnsComputerName,
                    ""))
                {
                    return CleanHostname(
                        dnsComputerName);
                }
            }
            catch
            {
            }

            return "";
        }

        private int FindAsciiSequence(
            byte[] data,
            string text)
        {
            if (data == null ||
                string.IsNullOrEmpty(text))
                return -1;

            byte[] needle =
                Encoding.ASCII.GetBytes(text);

            for (int i = 0;
                 i <= data.Length - needle.Length;
                 i++)
            {
                bool match = true;

                for (int j = 0;
                     j < needle.Length;
                     j++)
                {
                    if (data[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }

            return -1;
        }

        private byte[] WrapNetbiosSession(
            byte[] smbPayload)
        {
            int len = smbPayload.Length;

            var result =
                new byte[len + 4];

            result[0] = 0x00;
            result[1] =
                (byte)((len >> 16) & 0xFF);
            result[2] =
                (byte)((len >> 8) & 0xFF);
            result[3] =
                (byte)(len & 0xFF);

            Buffer.BlockCopy(
                smbPayload,
                0,
                result,
                4,
                len);

            return result;
        }

        private byte[] CombineBytes(
            params byte[][] arrays)
        {
            int length =
                arrays.Sum(
                    a => a == null
                        ? 0
                        : a.Length);

            byte[] result =
                new byte[length];

            int offset = 0;

            foreach (byte[] a in arrays)
            {
                if (a == null ||
                    a.Length == 0)
                    continue;

                Buffer.BlockCopy(
                    a,
                    0,
                    result,
                    offset,
                    a.Length);

                offset += a.Length;
            }

            return result;
        }

        private void AddUInt16LE(
            List<byte> list,
            ushort value)
        {
            list.Add(
                (byte)(value & 0xFF));
            list.Add(
                (byte)(value >> 8));
        }

        private void AddUInt32LE(
            List<byte> list,
            uint value)
        {
            list.Add(
                (byte)(value & 0xFF));
            list.Add(
                (byte)((value >> 8) & 0xFF));
            list.Add(
                (byte)((value >> 16) & 0xFF));
            list.Add(
                (byte)((value >> 24) & 0xFF));
        }

        private void AddUInt64LE(
            List<byte> list,
            ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                list.Add(
                    (byte)(
                        (value >> (i * 8)) &
                        0xFF));
            }
        }

        private ushort ReadUInt16LE(
            byte[] data,
            int offset)
        {
            return (ushort)(
                data[offset] |
                (data[offset + 1] << 8));
        }

        private uint ReadUInt32LE(
            byte[] data,
            int offset)
        {
            return
                (uint)data[offset] |
                ((uint)data[offset + 1] << 8) |
                ((uint)data[offset + 2] << 16) |
                ((uint)data[offset + 3] << 24);
        }

        private async Task<string> ResolveHostnameNbnsNodeStatusAsync(string ip)
        {
            // Doğrudan NetBIOS Node Status (NBSTAT) sorgusu.
            // nbtstat.exe çağırmadan UDP/137 üzerinden cihazın kayıtlı
            // NetBIOS bilgisayar adını ister.
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(ip, out address))
                    return "";

                byte[] packet = BuildNbnsNodeStatusPacket();

                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 700;
                    udp.Connect(address, 137);

                    await udp.SendAsync(packet, packet.Length);

                    Task<UdpReceiveResult> receiveTask = udp.ReceiveAsync();
                    Task winner = await Task.WhenAny(
                        receiveTask,
                        Task.Delay(700));

                    if (winner != receiveTask)
                        return "";

                    UdpReceiveResult response = await receiveTask;
                    return ParseNbnsNodeStatusName(response.Buffer);
                }
            }
            catch
            {
                return "";
            }
        }

        private byte[] BuildNbnsNodeStatusPacket()
        {
            var data = new List<byte>();

            ushort id = (ushort)new Random().Next(1, 65535);

            // Header
            data.Add((byte)(id >> 8));
            data.Add((byte)(id & 0xFF));
            data.Add(0x00); data.Add(0x00); // Flags
            data.Add(0x00); data.Add(0x01); // Questions
            data.Add(0x00); data.Add(0x00); // Answers
            data.Add(0x00); data.Add(0x00); // Authority
            data.Add(0x00); data.Add(0x00); // Additional

            // Wildcard NetBIOS name "*" padded to 16 bytes.
            byte[] netbiosName = new byte[16];
            netbiosName[0] = 0x2A; // '*'

            data.Add(0x20); // 32 encoded chars

            foreach (byte b in netbiosName)
            {
                data.Add((byte)('A' + ((b >> 4) & 0x0F)));
                data.Add((byte)('A' + (b & 0x0F)));
            }

            data.Add(0x00);       // End of name
            data.Add(0x00);
            data.Add(0x21);       // QTYPE NBSTAT
            data.Add(0x00);
            data.Add(0x01);       // QCLASS IN

            return data.ToArray();
        }

        private string ParseNbnsNodeStatusName(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 20)
                    return "";

                int offset = 12;

                // Question name
                SkipDnsLikeName(data, ref offset);

                if (offset + 4 > data.Length)
                    return "";

                offset += 4; // QTYPE + QCLASS

                // Answer name
                SkipDnsLikeName(data, ref offset);

                // TYPE + CLASS + TTL + RDLENGTH
                if (offset + 10 > data.Length)
                    return "";

                ushort type = ReadUInt16BE(data, offset);
                offset += 2;

                offset += 2; // class
                offset += 4; // ttl

                ushort rdLength = ReadUInt16BE(data, offset);
                offset += 2;

                if (type != 0x0021 ||
                    offset >= data.Length ||
                    offset + rdLength > data.Length)
                    return "";

                int count = data[offset++];
                string serverName = "";

                for (int i = 0; i < count; i++)
                {
                    if (offset + 18 > data.Length)
                        break;

                    string name =
                        Encoding.ASCII.GetString(data, offset, 15).Trim();

                    byte suffix = data[offset + 15];
                    ushort flags = ReadUInt16BE(data, offset + 16);

                    bool isGroup = (flags & 0x8000) != 0;

                    // <00> UNIQUE = workstation/computer name.
                    if (!isGroup &&
                        suffix == 0x00 &&
                        IsUsefulHostname(name, ""))
                    {
                        return name;
                    }

                    // <20> UNIQUE = server service; iyi fallback.
                    if (!isGroup &&
                        suffix == 0x20 &&
                        string.IsNullOrWhiteSpace(serverName) &&
                        IsUsefulHostname(name, ""))
                    {
                        serverName = name;
                    }

                    offset += 18;
                }

                return serverName;
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> ResolveHostnameMdnsAsync(string ip)
        {
            // mDNS reverse PTR sorgusu.
            // Özellikle NAS, macOS/Linux ve bazı IoT cihazlarında .local adını bulabilir.
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(ip, out address))
                    return "";

                byte[] b = address.GetAddressBytes();

                string reverseName =
                    b[3] + "." + b[2] + "." + b[1] + "." + b[0] +
                    ".in-addr.arpa";

                byte[] query = BuildDnsPtrQuery(reverseName, true);

                using (var udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.Client.SetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.ReuseAddress,
                        true);

                    udp.Client.Bind(
                        new IPEndPoint(IPAddress.Any, 0));

                    await udp.SendAsync(
                        query,
                        query.Length,
                        new IPEndPoint(
                            IPAddress.Parse("224.0.0.251"),
                            5353));

                    DateTime end =
                        DateTime.UtcNow.AddMilliseconds(650);

                    while (DateTime.UtcNow < end)
                    {
                        int remaining =
                            (int)(end - DateTime.UtcNow).TotalMilliseconds;

                        if (remaining <= 0)
                            break;

                        Task<UdpReceiveResult> recv = udp.ReceiveAsync();
                        Task winner = await Task.WhenAny(
                            recv,
                            Task.Delay(remaining));

                        if (winner != recv)
                            break;

                        UdpReceiveResult result = await recv;

                        string name =
                            ParseDnsPtrResponse(
                                result.Buffer,
                                reverseName);

                        if (IsUsefulHostname(name, ip))
                            return CleanHostname(name);
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private byte[] BuildDnsPtrQuery(
            string name,
            bool requestUnicast)
        {
            var data = new List<byte>();

            // mDNS transaction ID = 0
            data.Add(0x00); data.Add(0x00);
            data.Add(0x00); data.Add(0x00); // flags
            data.Add(0x00); data.Add(0x01); // QDCOUNT
            data.Add(0x00); data.Add(0x00);
            data.Add(0x00); data.Add(0x00);
            data.Add(0x00); data.Add(0x00);

            foreach (string label in name.Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                data.Add((byte)bytes.Length);
                data.AddRange(bytes);
            }

            data.Add(0x00);
            data.Add(0x00); data.Add(0x0C); // PTR

            // QU bit asks responder for unicast reply.
            if (requestUnicast)
            {
                data.Add(0x80);
                data.Add(0x01);
            }
            else
            {
                data.Add(0x00);
                data.Add(0x01);
            }

            return data.ToArray();
        }

        private string ParseDnsPtrResponse(
            byte[] data,
            string expectedQuestion)
        {
            try
            {
                if (data == null || data.Length < 12)
                    return "";

                int qdCount = ReadUInt16BE(data, 4);
                int anCount = ReadUInt16BE(data, 6);
                int nsCount = ReadUInt16BE(data, 8);
                int arCount = ReadUInt16BE(data, 10);

                int offset = 12;

                for (int i = 0; i < qdCount; i++)
                {
                    ReadDnsName(data, ref offset);

                    if (offset + 4 > data.Length)
                        return "";

                    offset += 4;
                }

                int rrCount = anCount + nsCount + arCount;

                for (int i = 0; i < rrCount; i++)
                {
                    string owner =
                        ReadDnsName(data, ref offset);

                    if (offset + 10 > data.Length)
                        return "";

                    ushort type = ReadUInt16BE(data, offset);
                    offset += 2;

                    offset += 2; // class
                    offset += 4; // ttl

                    ushort rdLength =
                        ReadUInt16BE(data, offset);

                    offset += 2;

                    if (offset + rdLength > data.Length)
                        return "";

                    int rdataOffset = offset;

                    if (type == 12) // PTR
                    {
                        int temp = rdataOffset;

                        string ptr =
                            ReadDnsName(data, ref temp);

                        if (!string.IsNullOrWhiteSpace(ptr))
                        {
                            if (string.IsNullOrWhiteSpace(expectedQuestion) ||
                                owner.Equals(
                                    expectedQuestion,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return ptr;
                            }
                        }
                    }

                    offset = rdataOffset + rdLength;
                }
            }
            catch
            {
            }

            return "";
        }

        private ushort ReadUInt16BE(byte[] data, int offset)
        {
            return (ushort)(
                (data[offset] << 8) |
                data[offset + 1]);
        }

        private void SkipDnsLikeName(
            byte[] data,
            ref int offset)
        {
            int safety = 0;

            while (offset < data.Length &&
                   safety++ < 256)
            {
                byte len = data[offset++];

                if (len == 0)
                    return;

                if ((len & 0xC0) == 0xC0)
                {
                    if (offset < data.Length)
                        offset++;

                    return;
                }

                offset += len;
            }
        }

        private string ReadDnsName(
            byte[] data,
            ref int offset)
        {
            var labels = new List<string>();

            int pos = offset;
            bool jumped = false;
            int nextOffset = offset;
            int safety = 0;

            while (pos < data.Length &&
                   safety++ < 128)
            {
                byte len = data[pos++];

                if (len == 0)
                {
                    if (!jumped)
                        nextOffset = pos;

                    break;
                }

                if ((len & 0xC0) == 0xC0)
                {
                    if (pos >= data.Length)
                        break;

                    int pointer =
                        ((len & 0x3F) << 8) |
                        data[pos++];

                    if (!jumped)
                        nextOffset = pos;

                    pos = pointer;
                    jumped = true;
                    continue;
                }

                if (pos + len > data.Length)
                    break;

                labels.Add(
                    Encoding.UTF8.GetString(
                        data,
                        pos,
                        len));

                pos += len;

                if (!jumped)
                    nextOffset = pos;
            }

            offset = nextOffset;

            return string.Join(".", labels);
        }

        private async Task<string> ResolveHostnameDnsAsync(string ip)
        {
            try
            {
                var task = Dns.GetHostEntryAsync(ip);
                var winner = await Task.WhenAny(task, Task.Delay(700));

                if (winner == task && !task.IsFaulted)
                {
                    var entry = await task;

                    if (entry != null)
                        return entry.HostName;
                }
            }
            catch
            {
            }

            return "";
        }

        private async Task<string> ResolveHostnamePingAAsync(string ip)
        {
            // ping -a Windows'un kendi isim çözüm zincirini kullanır.
            // Yerel ağda DNS/LLMNR/NetBIOS üzerinden isim bulunabildiğinde
            // "Pinging HOSTNAME [10.0.0.x]" biçiminde çıktı üretir.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ping.exe",
                    Arguments = "-a -n 1 -w 250 " + ip,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.Default
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return "";

                    Task<string> readTask =
                        process.StandardOutput.ReadToEndAsync();

                    Task winner =
                        await Task.WhenAny(readTask, Task.Delay(650));

                    if (winner != readTask)
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill();
                        }
                        catch { }

                        return "";
                    }

                    string output = await readTask;

                    // Dil bağımsız yaklaşım:
                    // Satırda [IP] varsa, köşeli parantezin hemen önündeki
                    // son kelime hostname'dir.
                    string marker = "[" + ip + "]";

                    foreach (string raw in output.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = raw.Trim();
                        int pos = line.IndexOf(
                            marker,
                            StringComparison.OrdinalIgnoreCase);

                        if (pos <= 0)
                            continue;

                        string before =
                            line.Substring(0, pos).Trim();

                        if (before.Length == 0)
                            continue;

                        string[] parts =
                            before.Split(
                                new[] { ' ', '\t' },
                                StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length == 0)
                            continue;

                        string candidate =
                            parts[parts.Length - 1].Trim();

                        if (IsUsefulHostname(candidate, ip))
                            return candidate;
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private async Task<string> ResolveHostnameNetBiosAsync(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nbtstat.exe",
                    Arguments = "-A " + ip,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.Default
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return "";

                    Task<string> readTask =
                        process.StandardOutput.ReadToEndAsync();

                    Task winner =
                        await Task.WhenAny(readTask, Task.Delay(750));

                    if (winner != readTask)
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill();
                        }
                        catch { }

                        return "";
                    }

                    string output = await readTask;

                    foreach (string raw in output.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = raw.Trim();

                        int pos = line.IndexOf(
                            "<00>",
                            StringComparison.OrdinalIgnoreCase);

                        if (pos <= 0)
                            continue;

                        string upper = line.ToUpperInvariant();

                        // Grup kaydı bilgisayar adı değildir.
                        if (upper.Contains("GROUP") ||
                            upper.Contains("GRUP"))
                            continue;

                        string name =
                            line.Substring(0, pos).Trim();

                        if (IsUsefulHostname(name, ip))
                            return name;
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private bool IsUsefulHostname(string name, string ip)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            name = name.Trim().TrimEnd('.');

            if (name == "-" ||
                name.Equals(ip, StringComparison.OrdinalIgnoreCase))
                return false;

            IPAddress parsed;
            if (IPAddress.TryParse(name, out parsed))
                return false;

            if (name.Length > 255)
                return false;

            return true;
        }

        private string CleanHostname(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "-";

            name = name.Trim().TrimEnd('.');

            // FQDN geldiyse ekranda ilk etiketi göster.
            // Örn: TRKOCA-A003.local -> TRKOCA-A003
            int dot = name.IndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);

            return string.IsNullOrWhiteSpace(name) ? "-" : name;
        }

        private string GetServerNameFromSmb(string ip)
        {
            IntPtr buffer = IntPtr.Zero;

            try
            {
                // NetServerGetInfo sunucu adı olarak \\IP kabul eder.
                int result = NetServerGetInfo(
                    @"\\" + ip,
                    100,
                    out buffer);

                if (result != 0 || buffer == IntPtr.Zero)
                    return "";

                SERVER_INFO_100 info =
                    (SERVER_INFO_100)Marshal.PtrToStructure(
                        buffer,
                        typeof(SERVER_INFO_100));

                if (!string.IsNullOrWhiteSpace(info.sv100_name))
                    return info.sv100_name.Trim();
            }
            catch
            {
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    try { NetApiBufferFree(buffer); }
                    catch { }
                }
            }

            return "";
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVER_INFO_100
        {
            public uint sv100_platform_id;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string sv100_name;
        }

        [DllImport("Netapi32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern int NetServerGetInfo(
            string servername,
            int level,
            out IntPtr bufptr);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(
            IntPtr Buffer);

        private async Task<string> GetMacAsync(string ip)
        {
            if (adapterCombo.SelectedIndex >= 0)
            {
                var a = adapters[adapterCombo.SelectedIndex];

                if (a.Ip == ip)
                    return a.Mac;
            }

            // SendARP'yi birkaç kez dene
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    byte[] dst =
                        IPAddress.Parse(ip).GetAddressBytes();

                    uint dest =
                        BitConverter.ToUInt32(dst, 0);

                    byte[] mac = new byte[6];
                    int len = mac.Length;

                    int result =
                        SendARP(
                            dest,
                            0,
                            mac,
                            ref len);

                    if (result == 0 && len > 0)
                    {
                        return FormatMac(
                            mac.Take(len).ToArray());
                    }
                }
                catch
                {
                }

                await Task.Delay(80);
            }

            // SendARP bulamazsa Windows ARP tablosuna bak
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp.exe",
                    Arguments = "-a " + ip,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string output =
                            await process.StandardOutput.ReadToEndAsync();

                        var match =
                            System.Text.RegularExpressions.Regex.Match(
                                output,
                                @"\b" +
                                System.Text.RegularExpressions.Regex.Escape(ip) +
                                @"\s+([0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})\b");

                        if (match.Success)
                        {
                            string found =
                                match.Groups[1].Value;

                            return found
                                .Replace("-", ":")
                                .ToUpperInvariant();
                        }
                    }
                }
            }
            catch
            {
            }

            return "-";
        }


        private bool IsUnknownVendor(string vendor)
        {
            return string.IsNullOrWhiteSpace(vendor) ||
                   vendor == "-" ||
                   vendor.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase) ||
                   vendor.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                   vendor.Equals("*NO COMPANY*", StringComparison.OrdinalIgnoreCase);
        }

        private string GuessVendorFromHostname(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname) || hostname == "-")
                return "";

            string h = hostname.Trim().ToUpperInvariant();

            if (h.Contains("HIKVISION")) return "Hikvision";
            if (h.Contains("DAHUA")) return "Dahua";
            if (h.Contains("UNIVIEW") || h.Contains("UNV-")) return "Uniview";
            if (h.Contains("VIVOTEK")) return "Vivotek";
            if (h.Contains("AXIS")) return "Axis Communications";

            if (h.Contains("XIAOMI") || h.Contains("REDMI") || h.Contains("POCO")) return "Xiaomi";
            if (h.Contains("SAMSUNG") || h.Contains("GALAXY")) return "Samsung";
            if (h.Contains("HUAWEI")) return "Huawei";
            if (h.Contains("HONOR")) return "Honor";
            if (h.Contains("OPPO")) return "OPPO";
            if (h.Contains("ONEPLUS")) return "OnePlus";
            if (h.Contains("REALME")) return "realme";
            if (h.Contains("VIVO")) return "vivo";
            if (h.Contains("IPHONE") || h.Contains("IPAD") ||
                h.Contains("MACBOOK") || h.Contains("IMAC") ||
                h.Contains("MAC-MINI")) return "Apple";

            if (h.Contains("TP-LINK") || h.Contains("TPLINK")) return "TP-Link";
            if (h.Contains("UBIQUITI") || h.Contains("UNIFI")) return "Ubiquiti";
            if (h.Contains("MIKROTIK")) return "MikroTik";
            if (h.Contains("NETGEAR")) return "NETGEAR";
            if (h.Contains("ZYXEL")) return "Zyxel";
            if (h.Contains("D-LINK") || h.Contains("DLINK")) return "D-Link";
            if (h.Contains("ARUBA")) return "Aruba / HPE";
            if (h.Contains("CISCO")) return "Cisco";
            if (h.Contains("TENDANET") || h.Contains("TENDA")) return "Tenda";

            if (h.Contains("SYNOLOGY") || h.Contains("DISKSTATION")) return "Synology";
            if (h.Contains("QNAP")) return "QNAP";
            if (h.Contains("ASUSTOR")) return "ASUSTOR";
            if (h.Contains("MYCLOUD")) return "Western Digital";

            if (h.Contains("EPSON")) return "Epson";
            if (h.Contains("BROTHER")) return "Brother";
            if (h.Contains("CANON")) return "Canon";
            if (h.Contains("KYOCERA")) return "Kyocera";
            if (h.Contains("RICOH")) return "Ricoh";
            if (h.Contains("XEROX")) return "Xerox";
            if (h.Contains("LEXMARK")) return "Lexmark";
            if (h.StartsWith("HP-") || h.StartsWith("HP_") || h.Contains("HEWLETT")) return "HP";

            if (h.Contains("ASUS")) return "ASUS";
            if (h.Contains("LENOVO")) return "Lenovo";
            if (h.Contains("DELL")) return "Dell";
            if (h.Contains("ACER")) return "Acer";
            if (h.Contains("MSI")) return "MSI";

            return "";
        }

        private async Task<string> GuessVendorAsync(string mac, string ip)
        {
            if (string.IsNullOrWhiteSpace(mac) || mac == "-")
                return "-";

            // Kendi makinemizin adaptör üreticisini doğrudan adaptör adından al.
            if (adapterCombo.SelectedIndex >= 0 &&
                adapters[adapterCombo.SelectedIndex].Ip == ip)
            {
                string n = adapters[adapterCombo.SelectedIndex].Name.ToUpperInvariant();

                if (n.Contains("MEDIATEK")) return "MediaTek";
                if (n.Contains("INTEL")) return "Intel";
                if (n.Contains("REALTEK")) return "Realtek";
                if (n.Contains("QUALCOMM") || n.Contains("ATHEROS")) return "Qualcomm";
                if (n.Contains("BROADCOM")) return "Broadcom";
            }

            string clean = new string(
                mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

            if (clean.Length < 6)
                return T("Bilinmiyor", "Unknown");

            // Yerel yönetilen / randomized MAC.
            try
            {
                int firstByte = Convert.ToInt32(clean.Substring(0, 2), 16);
                if ((firstByte & 0x02) != 0)
                    return "Private";
            }
            catch { }

            string oui = clean.Substring(0, 6);

            // Önce cache.
            string cached;
            if (vendorCache.TryGetValue(oui, out cached))
                return LocalizeValue(cached);

            // Ekrandaki cihazlarda doğruladığımız ve sık kullanılan bazı kayıtlar.
            // Ağ olmasa bile bunlar bulunur.
            var localMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["14DAE9"] = "ASUSTek COMPUTER INC.",
                ["001761"] = "Private",
                ["3C970E"] = "Wistron InfoComm(Kunshan)Co.,Ltd.",
                ["3037B3"] = "HUAWEI TECHNOLOGIES CO.,LTD",

                ["FCB0DE"] = "MediaTek",
                ["000CE7"] = "MediaTek",
                ["001B21"] = "Intel",
                ["001CC0"] = "Intel",
                ["00E04C"] = "Realtek",
                ["50C7BF"] = "TP-Link",
                ["001E8C"] = "ASUS",
                ["001CC1"] = "Hikvision",
                ["C056E3"] = "Hikvision",
                ["3C46D8"] = "Dahua",
                ["286C07"] = "Xiaomi",
                ["34CE00"] = "Xiaomi",
                ["001632"] = "Samsung",
                ["001E10"] = "Huawei",
                ["001D0F"] = "Ubiquiti",
                ["00140B"] = "Aruba / HPE",
                ["000C29"] = "VMware",
                ["005056"] = "VMware",
                ["080027"] = "VirtualBox",
                ["525400"] = "QEMU / KVM",
                ["B827EB"] = "Raspberry Pi"
            };

            string localVendor;
            if (localMap.TryGetValue(oui, out localVendor))
            {
                vendorCache[oui] = localVendor;
                return localVendor;
            }

            // MACLookup API v2:
            // GET /v2/macs/{mac}/company/name
            // Yanıt doğrudan şirket adıdır.
            try
            {
                string lookupMac =
                    clean.Substring(0, 2) + ":" +
                    clean.Substring(2, 2) + ":" +
                    clean.Substring(4, 2);

                string url =
                    "https://api.maclookup.app/v2/macs/" +
                    Uri.EscapeDataString(lookupMac) +
                    "/company/name";

                using (var response = await vendorHttp.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string vendor =
                            (await response.Content.ReadAsStringAsync()).Trim();

                        if (vendor == "*PRIVATE*")
                            vendor = "Private";
                        else if (vendor == "*NO COMPANY*" ||
                                 string.IsNullOrWhiteSpace(vendor))
                            vendor = "Bilinmiyor";

                        vendorCache[oui] = vendor;
                        return LocalizeValue(vendor);
                    }
                }
            }
            catch
            {
                // İnternet/API problemi taramayı durdurmasın.
            }

            vendorCache[oui] = "Bilinmiyor";
            return T("Bilinmiyor", "Unknown");
        }

        private void AddHistory(DeviceInfo d)
        {
            if (!history.TryGetValue(d.Ip, out var list))
            {
                list = new List<HistoryEntry>();
                history[d.Ip] = list;
            }
            list.Add(new HistoryEntry
            {
                Time = d.Seen,
                Status = d.Status,
                Hostname = d.Hostname,
                Mac = d.Mac
            });
            while (list.Count > 50) list.RemoveAt(0);
        }

        private void UpdateLatestHistoryDetails(DeviceInfo d)
        {
            try
            {
                List<HistoryEntry> list;
                if (!history.TryGetValue(d.Ip, out list) ||
                    list == null ||
                    list.Count == 0)
                    return;

                var last = list[list.Count - 1];

                last.Hostname = d.Hostname;
                last.Mac = d.Mac;
            }
            catch
            {
            }
        }

        private void RefreshHistoryTab(string ip)
        {
            historyList.Items.Clear();
            if (!history.TryGetValue(ip, out var list)) return;
            foreach (var h in list.OrderByDescending(x => x.Time))
                historyList.Items.Add(new ListViewItem(new[] {
                    h.Time.ToString("dd.MM.yyyy HH:mm:ss"), h.Status, h.Hostname, h.Mac
                }));
        }

        private void ExportHtmlReport()
        {
            if (scanResults.Count == 0)
                return;

            try
            {
                string directory = AppLogger.LogDirectory;
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(
                    directory,
                    "network_scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");

                var reportDevices = scanResults.ToList();
                var typeCounts = reportDevices
                    .GroupBy(d => string.IsNullOrWhiteSpace(d.DeviceType) ? T("Diğer", "Other") : d.DeviceType)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .ToList();
    
                var html = new StringBuilder();
                html.AppendLine("<!doctype html>");
                html.AppendLine("<html lang=\"" + (englishMode ? "en" : "tr") + "\"><head>");
                html.AppendLine("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
                html.AppendLine("<title>Network Scanner Report</title>");
                html.AppendLine("<style>:root{--blue:#1464c4;--cyan:#06b6d4;--ink:#172033;--muted:#64748b;--line:#e2e8f0}*{box-sizing:border-box}body{font-family:Segoe UI,Arial,sans-serif;background:linear-gradient(135deg,#eaf4ff 0%,#f8fbff 45%,#eef7f5 100%);color:var(--ink);margin:0;padding:36px}.card{max-width:1500px;margin:auto;background:rgba(255,255,255,.94);border:1px solid #fff;border-radius:22px;padding:32px;box-shadow:0 18px 50px #1e3a5f22}.hero{display:flex;justify-content:space-between;align-items:center;gap:20px;background:linear-gradient(120deg,#0d4fa8,#1686cf 55%,#08a6a6);color:#fff;border-radius:16px;padding:26px 30px;margin-bottom:24px}.hero h1{margin:0 0 6px;font-size:30px}.hero .meta{color:#dbeafe;margin:0}.actions{display:flex;gap:10px;align-items:center}.print{border:0;border-radius:10px;background:#fff;color:#0d4fa8;padding:11px 16px;font-weight:700;cursor:pointer;box-shadow:0 4px 12px #001b3d33}.print:hover{background:#e0f2fe}.summary{display:flex;gap:14px;flex-wrap:wrap;margin:0 0 24px}.stat{min-width:150px;background:#f0f7ff;border:1px solid #cfe5ff;border-radius:14px;padding:13px 16px}.stat strong{display:block;color:var(--blue);font-size:22px}.stat span{color:var(--muted);font-size:12px}.charts{display:grid;grid-template-columns:repeat(2,minmax(260px,1fr));gap:18px;margin:0 0 24px}.chart-card{border:1px solid var(--line);border-radius:16px;padding:18px;background:#fbfdff}.chart-card h2{font-size:16px;margin:0 0 15px}.chart-row{display:flex;align-items:center;gap:22px}.pie{width:150px;height:150px;border-radius:50%;background:conic-gradient(var(--gradient));box-shadow:inset 0 0 0 22px #fff,0 5px 15px #123f7a22;flex:0 0 auto}.legend{display:grid;gap:7px}.legend-item{font-size:12px;color:var(--muted)}.dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:6px}.filters{background:#f8fbff;border:1px solid #cfe5ff;border-radius:14px;padding:16px;margin:0 0 20px}.filters h2{font-size:16px;margin:0 0 12px}.filter-list{display:flex;gap:10px;flex-wrap:wrap}.filter-list label{background:#fff;border:1px solid var(--line);border-radius:999px;padding:7px 11px;font-size:13px;cursor:pointer}.filter-list input{accent-color:var(--blue);margin-right:6px}table{border-collapse:separate;border-spacing:0;width:100%;font-size:14px;overflow:hidden;border:1px solid var(--line);border-radius:14px}th{background:#123f7a;color:#fff;text-align:left;padding:13px 12px;font-weight:600}td{padding:12px;border-bottom:1px solid var(--line);background:#fff}tr:nth-child(even) td{background:#f8fbff}tr:hover td{background:#e7f5ff}tr:last-child td{border-bottom:0}.footer{text-align:center;color:var(--muted);font-size:13px;margin-top:24px;padding-top:18px;border-top:1px solid var(--line)}@media(max-width:800px){body{padding:14px}.card{padding:16px}.hero{display:block;padding:22px}.actions{margin-top:18px}.charts{grid-template-columns:1fr}.chart-row{align-items:flex-start;flex-direction:column}table{font-size:12px}th,td{padding:8px}}@media print{@page{size:A4 portrait;margin:5mm}body{background:#fff;padding:0}.card{box-shadow:none;border:0;padding:0;max-width:none}.actions,.filters{display:none}.hero{color:#000;background:#fff;border-bottom:3px solid #1464c4;border-radius:0;padding:0 0 15px}.hero .meta{color:#64748b} .summary{margin-bottom:8px}.stat{padding:4px 7px;min-width:90px}.stat strong{font-size:14px}.stat span{font-size:9px}.charts{display:none}table{table-layout:fixed;width:100%;font-size:7.2px;border-radius:0}thead{display:table-header-group}tr{page-break-inside:avoid}th,td{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;padding:3px 2px;line-height:1.05}th{background:#123f7a!important;-webkit-print-color-adjust:exact;print-color-adjust:exact}th:nth-child(1),td:nth-child(1){width:12%}th:nth-child(2),td:nth-child(2){width:12%}th:nth-child(3),td:nth-child(3){width:14%}th:nth-child(4),td:nth-child(4){width:14%}th:nth-child(5),td:nth-child(5){width:12%}th:nth-child(6),td:nth-child(6){width:8%}th:nth-child(7),td:nth-child(7){width:8%}th:nth-child(8),td:nth-child(8){width:20%}.pie{-webkit-print-color-adjust:exact;print-color-adjust:exact}}</style>");
                html.AppendLine("</head><body><div class=\"card\">");
                html.AppendLine("<header class=\"hero\"><div><h1>" + Html(T("Ağ Tarama Raporu", "Network Scan Report")) + "</h1><p class=\"meta\">" + Html(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")) + "</p></div><div class=\"actions\"><button class=\"print\" onclick=\"window.print()\">" + Html(T("Yazdır", "Print")) + "</button></div></header>");
                html.AppendLine("<section class=\"summary\"><div class=\"stat\"><strong>" + reportDevices.Count + "</strong><span>" + Html(T("Toplam cihaz", "Total devices")) + "</span></div><div class=\"stat\"><strong>" + reportDevices.Count(d => d.Status == T("Aktif", "Active")) + "</strong><span>" + Html(T("Aktif cihaz", "Active devices")) + "</span></div><div class=\"stat\"><strong>" + DateTime.Now.ToString("HH:mm") + "</strong><span>" + Html(T("Rapor saati", "Report time")) + "</span></div></section>");
                html.AppendLine("<section class=\"filters\"><h2>" + Html(T("Yazdırılacak cihaz türleri", "Device types to print")) + "</h2><div class=\"filter-list\">");
                foreach (var type in typeCounts)
                    html.AppendLine("<label><input type=\"checkbox\" class=\"type-filter\" value=\"" + Html(type.Name) + "\" checked onchange=\"applyFilters()\">" + Html(type.Name) + "</label>");
                html.AppendLine("</div></section>");
                html.AppendLine("<table><thead><tr>");
                string[] headers = {
                    T("IP Adresi", "IP Address"), T("Hostname", "Hostname"),
                    T("MAC Adresi", "MAC Address"), T("Üretici", "Vendor"),
                    T("Cihaz Türü", "Device Type"), T("Yanıt", "Response"),
                    T("Durum", "Status"), T("Ağ", "Network")
                };
                foreach (string header in headers)
                    html.Append("<th>").Append(Html(header)).Append("</th>");
                html.AppendLine("</tr></thead><tbody>");

                foreach (var d in reportDevices)
                {
                    html.AppendLine("<tr data-type=\"" + Html(d.DeviceType) + "\"><td>" + Html(d.Ip) + "</td><td>" + Html(d.Hostname) + "</td><td>" + Html(d.Mac) + "</td><td>" + Html(d.Vendor) + "</td><td>" + Html(d.DeviceType) + "</td><td>" + Html(d.Response) + "</td><td>" + Html(d.Status) + "</td><td>" + Html(d.Network) + "</td></tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("<script>function applyFilters(){const selected=Array.from(document.querySelectorAll('.type-filter:checked')).map(x=>x.value);document.querySelectorAll('tbody tr[data-type]').forEach(row=>{row.style.display=selected.indexOf(row.dataset.type)>=0?'':'none';});}applyFilters();</script>");
                html.AppendLine("<footer class=\"footer\">NetworkScanner v" + Html(CurrentVersion) + "</footer>");
                html.AppendLine("</div></body></html>");
                File.WriteAllText(filePath, html.ToString(), new UTF8Encoding(false));
                AppLogger.Info("HTML report", filePath);
                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error("HTML report", "", ex);
                MessageBox.Show(
                    T("HTML raporu oluşturulamadı.\\r\\n\\r\\n", "HTML report could not be created.\\r\\n\\r\\n") + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string BuildConicGradient(List<int> counts, string[] colors)
        {
            if (counts == null || counts.Count == 0)
                return "#cbd5e1 0 100%";

            int total = counts.Sum();
            if (total == 0)
                return "#cbd5e1 0 100%";

            var parts = new List<string>();
            double start = 0;
            for (int i = 0; i < counts.Count; i++)
            {
                double end = start + (counts[i] * 100.0 / total);
                string color = colors[i % colors.Length];
                parts.Add(color + " " + start.ToString("0.##", CultureInfo.InvariantCulture) + "% " + end.ToString("0.##", CultureInfo.InvariantCulture) + "%");
                start = end;
            }
            return string.Join(",", parts);
        }

        private string BuildChartCard<T>(string title, string gradient, List<T> groups, string[] colors)
        {
            var html = new StringBuilder();
            html.Append("<div class=\"chart-card\"><h2>").Append(Html(title)).Append("</h2><div class=\"chart-row\"><div class=\"pie\" style=\"--gradient:").Append(gradient).Append("\"></div><div class=\"legend\">");
            int index = 0;
            foreach (var group in groups)
            {
                var name = (string)group.GetType().GetProperty("Name").GetValue(group, null);
                var count = (int)group.GetType().GetProperty("Count").GetValue(group, null);
                html.Append("<div class=\"legend-item\"><span class=\"dot\" style=\"background:").Append(colors[index % colors.Length]).Append("\"></span>").Append(Html(name)).Append(" <strong>").Append(count).Append("</strong></div>");
                index++;
            }
            html.Append("</div></div></div>");
            return html.ToString();
        }

        private static string Html(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? "");
        }

                private void ShowAboutDialog()
        {
            using (var about = new Form())
            {
                about.Text = T("Hakkında", "About");
                about.StartPosition = FormStartPosition.CenterParent;
                about.FormBorderStyle = FormBorderStyle.FixedDialog;
                about.ClientSize = new Size(460, 330);
                about.MinimizeBox = false;
                about.MaximizeBox = false;
                about.ShowInTaskbar = false;
                about.BackColor = Color.White;
                about.Padding = new Padding(0);

                var header = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(460, 112),
                    BackColor = Color.FromArgb(13, 79, 168)
                };
                about.Controls.Add(header);

                var logo = new PictureBox
                {
                    Image = Properties.Resources.networkscanner_about,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(24, 16),
                    Size = new Size(80, 80),
                    BackColor = Color.Transparent
                };
                header.Controls.Add(logo);

                var title = new Label
                {
                    Text = "NetworkScanner",
                    Location = new Point(125, 25),
                    Size = new Size(305, 32),
                    Font = new Font("Segoe UI", 17, FontStyle.Bold),
                    ForeColor = Color.White
                };
                header.Controls.Add(title);

                var version = new Label
                {
                    Text = "v" + CurrentVersion + "  •  " + T("Ağ keşif ve analiz aracı", "Network discovery and analysis tool"),
                    Location = new Point(127, 63),
                    Size = new Size(305, 25),
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = Color.FromArgb(215, 235, 255)
                };
                header.Controls.Add(version);

                var information = new Label
                {
                    Text = T("Uygulama bilgileri", "Application information"),
                    Location = new Point(28, 132),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = Color.FromArgb(20, 80, 145)
                };
                about.Controls.Add(information);

                var separator = new Panel
                {
                    Location = new Point(28, 158),
                    Size = new Size(404, 1),
                    BackColor = Color.FromArgb(220, 228, 238)
                };
                about.Controls.Add(separator);

                var developer = new Label
                {
                    Text = T("Programcı", "Developer") + ":  Tuncay Candan",
                    Location = new Point(28, 174),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10),
                    ForeColor = Color.FromArgb(40, 48, 60)
                };
                about.Controls.Add(developer);

                var github = new LinkLabel
                {
                    Text = "GitHub:  github.com/tuncaycandan/NetworkScannerTool",
                    Location = new Point(28, 207),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10),
                    LinkColor = Color.FromArgb(20, 90, 180),
                    ActiveLinkColor = Color.FromArgb(10, 55, 130),
                    VisitedLinkColor = Color.FromArgb(20, 90, 180),
                    Cursor = Cursors.Hand
                };
                github.Links.Clear();
                github.Links.Add(
                    "GitHub:  ".Length,
                    "github.com/tuncaycandan/NetworkScannerTool".Length,
                    "https://github.com/tuncaycandan/NetworkScannerTool");
                github.LinkClicked += (s, e) => OpenUrl(e.Link.LinkData.ToString());
                about.Controls.Add(github);

                var web = new LinkLabel
                {
                    Text = "Web:  www.tuncay.net.tr",
                    Location = new Point(28, 240),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10),
                    LinkColor = Color.FromArgb(20, 90, 180),
                    ActiveLinkColor = Color.FromArgb(10, 55, 130),
                    VisitedLinkColor = Color.FromArgb(20, 90, 180),
                    Cursor = Cursors.Hand
                };
                web.Links.Clear();
                web.Links.Add(
                    "Web:  ".Length,
                    "www.tuncay.net.tr".Length,
                    "https://www.tuncay.net.tr");
                web.LinkClicked += (s, e) => OpenUrl(e.Link.LinkData.ToString());
                about.Controls.Add(web);

                var ok = new Button
                {
                    Text = T("Tamam", "OK"),
                    Location = new Point(352, 282),
                    Size = new Size(80, 30),
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(20, 100, 190),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                ok.FlatAppearance.BorderColor = Color.FromArgb(15, 75, 145);
                ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 125, 215);
                ok.FlatAppearance.MouseDownBackColor = Color.FromArgb(12, 70, 145);
                about.AcceptButton = ok;
                about.CancelButton = ok;
                about.Controls.Add(ok);
                about.ShowDialog(this);
            }
        }


        private void OpenLogFolder()
        {
            try
            {
                string directory = AppLogger.LogDirectory;
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + directory + "\"",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Open log folder", "", ex);
                MessageBox.Show(
                    T("Log klasörü açılamadı.\r\n\r\n", "Could not open the log folder.\r\n\r\n") + ex.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static void RunCmd(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return;
            Process.Start(new ProcessStartInfo("cmd.exe", "/k " + args) { UseShellExecute = true });
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
        }

        private async void RangeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            await StartScanAsync();
        }

        private void UpdateRangeEndFromStart()
        {
            IPAddress start;
            if (!IPAddress.TryParse(rangeStart.Text.Trim(), out start) ||
                start.AddressFamily != AddressFamily.InterNetwork)
                return;

            byte[] bytes = start.GetAddressBytes();
            bytes[3] = 254;
            rangeEnd.Text = new IPAddress(bytes).ToString();
        }

        private static Tuple<string, string> GetSuggestedRange(string ip, string mask)
        {
            var ipb = IPAddress.Parse(ip).GetAddressBytes();
            var mb = IPAddress.Parse(mask).GetAddressBytes();
            var nb = new byte[4];
            var bb = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                nb[i] = (byte)(ipb[i] & mb[i]);
                bb[i] = (byte)(nb[i] | (byte)~mb[i]);
            }

            uint n = ToUInt(nb);
            uint b = ToUInt(bb);
            if (b <= n + 1) return Tuple.Create(ip, ip);
            return Tuple.Create(FromUInt(n + 1), FromUInt(b - 1));
        }

        private static ulong CountRange(string start, string end)
        {
            uint s = ToUInt(IPAddress.Parse(start).GetAddressBytes());
            uint e = ToUInt(IPAddress.Parse(end).GetAddressBytes());
            return e < s ? 0UL : (ulong)e - s + 1UL;
        }

        private static IEnumerable<string> BuildRange(string start, string end)
        {
            uint s = ToUInt(IPAddress.Parse(start).GetAddressBytes());
            uint e = ToUInt(IPAddress.Parse(end).GetAddressBytes());
            if (e < s) yield break;
            for (uint i = s; i <= e; i++)
            {
                yield return FromUInt(i);
                if (i == uint.MaxValue) break;
            }
        }

        private static uint ToUInt(byte[] b) =>
            ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

        private static string FromUInt(uint n) =>
            ((n >> 24) & 255) + "." + ((n >> 16) & 255) + "." + ((n >> 8) & 255) + "." + (n & 255);

        private static string FormatMac(byte[] bytes) =>
            bytes == null || bytes.Length == 0 ? "-" : string.Join(":", bytes.Select(x => x.ToString("X2")));

        private void SendWakeOnLan(string mac)
        {
            try
            {
                var clean = mac.Replace(":", "").Replace("-", "");
                var macBytes = Enumerable.Range(0, 6)
                    .Select(i => Convert.ToByte(clean.Substring(i * 2, 2), 16)).ToArray();

                var packet = new byte[102];
                for (int i = 0; i < 6; i++) packet[i] = 0xFF;
                for (int i = 1; i <= 16; i++)
                    Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);

                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
                }
                MessageBox.Show(T("Wake-On-LAN paketi gönderildi.", "Wake-On-LAN packet sent."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("Wake-On-LAN başarısız.\n\n", "Wake-On-LAN failed.\n\n") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint DestIP, uint SrcIP, [Out] byte[] pMacAddr, ref int PhyAddrLen);

        private sealed class DeviceListComparer : System.Collections.IComparer
        {
            public int Column { get; set; } = 0;
            public bool Ascending { get; set; } = true;

            public int Compare(object x, object y)
            {
                var a = x as ListViewItem;
                var b = y as ListViewItem;

                if (a == null || b == null)
                    return 0;

                string av = GetSubItemText(a, Column);
                string bv = GetSubItemText(b, Column);

                int result;

                // IP Adresi sütunu
                if (Column == 0)
                {
                    result = CompareIpAddresses(av, bv);
                }
                // Yanıt sütunu: "12 ms" gibi değerleri sayısal sırala
                else if (Column == 5)
                {
                    result = CompareResponse(av, bv);
                }
                else
                {
                    result = string.Compare(
                        av,
                        bv,
                        StringComparison.CurrentCultureIgnoreCase);
                }

                return Ascending ? result : -result;
            }

            private static string GetSubItemText(ListViewItem item, int column)
            {
                if (item.SubItems.Count <= column)
                    return "";

                return item.SubItems[column].Text ?? "";
            }

            private static int CompareIpAddresses(string a, string b)
            {
                IPAddress ipa;
                IPAddress ipb;

                bool okA = IPAddress.TryParse(a, out ipa);
                bool okB = IPAddress.TryParse(b, out ipb);

                if (!okA && !okB)
                    return string.Compare(
                        a,
                        b,
                        StringComparison.OrdinalIgnoreCase);

                if (!okA) return 1;
                if (!okB) return -1;

                byte[] ba = ipa.GetAddressBytes();
                byte[] bb = ipb.GetAddressBytes();

                int len = Math.Min(ba.Length, bb.Length);

                for (int i = 0; i < len; i++)
                {
                    int c = ba[i].CompareTo(bb[i]);
                    if (c != 0)
                        return c;
                }

                return ba.Length.CompareTo(bb.Length);
            }

            private static int CompareResponse(string a, string b)
            {
                int va = ExtractNumber(a);
                int vb = ExtractNumber(b);

                return va.CompareTo(vb);
            }

            private static int ExtractNumber(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return int.MaxValue;

                var digits = new string(
                    value.Where(char.IsDigit).ToArray());

                int n;
                return int.TryParse(digits, out n)
                    ? n
                    : int.MaxValue;
            }
        }

        

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }
    }

}

