using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace ytVideo
{
    public partial class Form1 : Form
    {
        private Panel topBar;
        private Button btnClose;
        private WebView2 webView;
        private System.Windows.Forms.Timer uiTimer;

        // --- Z-Order Kontrolü API'leri ---
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        // --- Sürükleme API'leri ---
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        // --- GLOBAL HOTKEY API'leri ---
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int MUTE_HOTKEY_ID = 1;
        const int EXIT_HOTKEY_ID = 2;
        const int BOSS_HOTKEY_ID = 3;
        const int MOD_NONE = 0x0000;

        private bool isBossModeActive = false;

        // Senin eski koddaki gibi sabit saydamlık
        private double defaultOpacity = 0.25;

        public Form1()
        {
            ArayuzuHazirla();
            TarayiciyiBaslat();

            // Kısayolları Sisteme Kaydet
            RegisterHotKey(this.Handle, MUTE_HOTKEY_ID, MOD_NONE, (int)Keys.M);
            RegisterHotKey(this.Handle, EXIT_HOTKEY_ID, MOD_NONE, (int)Keys.X); // Kapatma (X)
            RegisterHotKey(this.Handle, BOSS_HOTKEY_ID, MOD_NONE, (int)Keys.B); // Panik/Gizlenme (B)
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();

                if (id == MUTE_HOTKEY_ID)
                {
                    // M -> Sesi Aç/Kapat
                    webView?.CoreWebView2?.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v => v.muted = !v.muted);");
                }
                else if (id == EXIT_HOTKEY_ID)
                {
                    // X -> KOMPLE KAPAT
                    Application.Exit();
                }
                else if (id == BOSS_HOTKEY_ID)
                {
                    // B -> PANİK MODU (Tamamen görünmez yap, ekran dışına at ve videoyu durdur)
                    isBossModeActive = !isBossModeActive;

                    if (isBossModeActive)
                    {
                        this.Opacity = 0.0;
                        // Pencere handle'ını bozmadan ekranın çok uzak bir köşesine fırlatıyoruz
                        this.Location = new Point(-9999, -9999);
                        webView?.CoreWebView2?.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v => v.pause());");
                    }
                    else
                    {
                        this.Opacity = defaultOpacity;
                        // Geri çağrıldığında tekrar sağ alt köşeye yerleştir
                        Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
                        this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);
                        webView?.CoreWebView2?.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v => v.play());");
                    }
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(this.Handle, MUTE_HOTKEY_ID);
            UnregisterHotKey(this.Handle, EXIT_HOTKEY_ID);
            UnregisterHotKey(this.Handle, BOSS_HOTKEY_ID);
            base.OnFormClosing(e);
        }

        private void ArayuzuHazirla()
        {
            this.Text = "Background Worker"; // Senin eski isim

            // Yatay YouTube videosu için ufak boyut
            this.Size = new Size(256, 158);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(28, 28, 28);
            this.Opacity = defaultOpacity;
            this.ShowInTaskbar = true; // Hep açık kalsın ki Hotkey kopmasın

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);

            topBar = new Panel();
            topBar.Height = 14;
            topBar.Dock = DockStyle.Top;
            topBar.BackColor = Color.FromArgb(45, 45, 45);
            topBar.MouseDown += TopBar_MouseDown;

            btnClose = new Button();
            btnClose.Text = "✕";
            btnClose.Dock = DockStyle.Right;
            btnClose.Width = 18;
            btnClose.Font = new Font("Arial", 5, FontStyle.Bold);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.Red;
            btnClose.ForeColor = Color.LightGray;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => Application.Exit();

            topBar.Controls.Add(btnClose);
            this.Controls.Add(topBar);

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.NavigationCompleted += WebView_NavigationCompleted;
            this.Controls.Add(webView);

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 100;
            uiTimer.Tick += UiTimer_Tick;
            uiTimer.Start();
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            if (isBossModeActive) return;

            Point mousePos = Cursor.Position;
            bool isMouseOver = this.Bounds.Contains(mousePos);

            if (isMouseOver)
            {
                if (!this.TopMost)
                {
                    this.TopMost = true;
                    // Saydamlığı değiştirmedik, sadece öne çıkıyor! (Eski kodundaki gibi)
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            else
            {
                if (this.TopMost)
                {
                    this.TopMost = false;
                    SetWindowPos(this.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        private void TopBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }


        private async void TarayiciyiBaslat()
        {
            string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ytVideoPiP_Data");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                document.addEventListener('DOMContentLoaded', function() {
                    var style = document.createElement('style');
                    style.innerHTML = `
                        ytd-masthead, #secondary, #comments, #related, ytd-engagement-panel-section-list-renderer { 
                            display: none !important; 
                        }
                        #primary { 
                            max-width: 100% !important; 
                            width: 100% !important; 
                            padding: 0 !important; 
                        }
                    `;
                    document.head.appendChild(style);
                });
            ");

            webView.Source = new Uri("https://www.youtube.com");
        }

        private void WebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            webView.ZoomFactor = 0.50;
        }
    }
}