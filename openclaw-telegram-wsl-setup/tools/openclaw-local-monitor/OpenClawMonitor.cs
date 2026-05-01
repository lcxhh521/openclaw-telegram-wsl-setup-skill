using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OpenClawLocalMonitor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MonitorForm());
        }
    }

    sealed class CommandResult
    {
        public bool Ok;
        public int ExitCode;
        public string Stdout = "";
        public string Stderr = "";
        public string Error = "";
    }

    enum ClosePreference
    {
        Ask,
        MinimizeToTray,
        Exit
    }

    sealed class CloseChoice
    {
        public bool Cancelled = true;
        public ClosePreference Preference = ClosePreference.MinimizeToTray;
        public bool Remember;
    }

    sealed class CloseChoiceDialog : Form
    {
        readonly CheckBox remember;
        public CloseChoice Choice = new CloseChoice();

        public CloseChoiceDialog(Icon icon)
        {
            Text = "关闭 OpenClaw 控制中心";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 190);
            BackColor = Color.White;
            ForeColor = Color.FromArgb(31, 41, 55);
            Font = new Font("Microsoft YaHei UI", 9f);
            if (icon != null) Icon = icon;

            var title = new Label
            {
                Text = "关闭控制中心？",
                Left = 22,
                Top = 18,
                Width = 360,
                Height = 26,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold)
            };
            var body = new Label
            {
                Text = "可以让它留在系统托盘继续监控，也可以直接退出。",
                Left = 22,
                Top = 52,
                Width = 370,
                Height = 48
            };
            remember = new CheckBox
            {
                Text = "记住我的选择",
                Left = 22,
                Top = 106,
                Width = 220,
                Height = 24
            };

            var trayButton = MakeDialogButton("最小化到托盘", 22, 144, 130);
            trayButton.Click += (s, e) => Choose(ClosePreference.MinimizeToTray);
            var exitButton = MakeDialogButton("关闭程序", 160, 144, 110);
            exitButton.Click += (s, e) => Choose(ClosePreference.Exit);
            var cancelButton = MakeDialogButton("取消", 282, 144, 90);
            cancelButton.Click += (s, e) => { Choice.Cancelled = true; DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, body, remember, trayButton, exitButton, cancelButton });
            AcceptButton = trayButton;
            CancelButton = cancelButton;
        }

        static Button MakeDialogButton(string text, int left, int top, int width)
        {
            return new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.System
            };
        }

        void Choose(ClosePreference preference)
        {
            Choice.Cancelled = false;
            Choice.Preference = preference;
            Choice.Remember = remember.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    sealed class Snapshot
    {
        public DateTime GeneratedAt = DateTime.Now;
        public string State = "Idle";
        public bool GatewayOk;
        public bool GatewaySoftFailure;
        public bool TelegramOk;
        public int RunningTasks;
        public int AuditWarnings;
        public int AuditErrors;
        public string GatewayText = "-";
        public string TelegramText = "-";
        public string RecentSessionAge = "-";
        public string StatusLine = "";
        public string Error = "";
        public long TokenTotal;
        public long TokenInput;
        public long TokenOutput;
        public long TokenCacheRead;
        public string TokenContext = "-";
        public string CostText = "-";
        public string CostState = "warn";
        public long LastSessionAgeMs = -1;
        public string LastSessionSource = "-";
        public string LastSessionModel = "-";
        public int FlowActive;
        public int FlowBlocked;
        public int FlowCancelRequested;
        public int LocalWorkItems;
        public bool LocalDaemonActive;
        public string LocalWorkAge = "-";
        public readonly List<string[]> Tasks = new List<string[]>();
        public readonly List<string> Sessions = new List<string>();
        public readonly List<string> Logs = new List<string>();
        public readonly List<string> TokenFlows = new List<string>();
    }

    sealed class CostSummary
    {
        public DateTime UpdatedAt = DateTime.MinValue;
        public bool Available;
        public double TotalCost;
        public string Error = "";
        public readonly List<string> Lines = new List<string>();
    }

    sealed class MonitorForm : Form
    {
        const string WslDistro = "Ubuntu";
        const string OpenClawCommand = "openclaw";
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        readonly Timer timer = new Timer();
        readonly object costLock = new object();
        readonly object artifactLock = new object();
        readonly long monitorStartedAtMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        readonly long activeTaskEventWindowMs = 20L * 60L * 1000L;
        readonly long freshTaskEventWindowMs = 2L * 60L * 1000L;
        Dictionary<string, long> previousArtifactMtimes = new Dictionary<string, long>();
        CostSummary cachedCost = new CostSummary();
        bool artifactBaselineReady;
        bool refreshing;
        int gatewayProbeFailures;
        ClosePreference closePreference = ClosePreference.Ask;

        Label updated;
        Label statusLine;
        Label tokenHeader;
        Label taskHeader;
        Label sessionHeader;
        Label logHeader;
        Button refreshButton;
        RoundedPanel hoverTip;
        Label hoverTipText;
        Card overall;
        Card gateway;
        Card telegram;
        Card tasks;
        Card audit;
        Card session;
        Card tokenTotal;
        Card tokenInput;
        Card tokenOutput;
        Card tokenCache;
        Card tokenCost;
        RoundedPanel costHintPopup;
        Label heroTitle;
        Label heroDetail;
        Label legendLine;
        DataGridView taskGrid;
        ListBox sessionList;
        ListBox logList;
        Button openControlButton;
        NotifyIcon trayIcon;
        ContextMenuStrip trayMenu;
        bool allowExit;
        bool trayNoticeShown;
        bool startAttempted;
        bool startingOpenClaw;
        bool wasMinimized;
        bool smoothRestorePending;
        string startupNote = "";

        public MonitorForm()
        {
            Text = "OpenClaw 控制中心";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 760);
            ClientSize = new Size(1220, 900);
            AutoScroll = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            BackColor = Color.FromArgb(246, 248, 252);
            ForeColor = Color.FromArgb(31, 41, 55);
            Font = new Font("Microsoft YaHei UI", 9f);
            var iconPath = Path.Combine(Application.StartupPath, "OpenClawMonitor.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);

            BuildUi();
            Resize += (s, e) => OnMonitorResize();
            SetupTray();
            closePreference = LoadClosePreference();
            timer.Interval = 12000;
            timer.Tick += async (s, e) => await RefreshAsync(false);
            timer.Start();
            Shown += async (s, e) =>
            {
                await EnsureOpenClawStartedAsync(false);
                await RefreshAsync(false);
            };
            FormClosing += OnFormClosing;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: paint child controls into one frame.
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_RESTORE = 0xF120;

            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_RESTORE)
                BeginSmoothRestore();

            base.WndProc(ref m);

            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_RESTORE)
                FinishSmoothRestore(true);
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowExit)
            {
                if (trayIcon != null) trayIcon.Visible = false;
                return;
            }

            var preference = closePreference;
            if (preference == ClosePreference.Ask)
            {
                using (var dialog = new CloseChoiceDialog(Icon))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Choice.Cancelled)
                    {
                        e.Cancel = true;
                        return;
                    }
                    preference = dialog.Choice.Preference;
                    if (dialog.Choice.Remember)
                    {
                        closePreference = preference;
                        SaveClosePreference(preference);
                    }
                }
            }

            if (preference == ClosePreference.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            allowExit = true;
            if (trayIcon != null) trayIcon.Visible = false;
        }

        ClosePreference LoadClosePreference()
        {
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path)) return ClosePreference.Ask;
                var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                object value;
                if (!data.TryGetValue("closePreference", out value)) return ClosePreference.Ask;
                var text = Convert.ToString(value);
                if (text == "tray") return ClosePreference.MinimizeToTray;
                if (text == "exit") return ClosePreference.Exit;
            }
            catch
            {
            }
            return ClosePreference.Ask;
        }

        void SaveClosePreference(ClosePreference preference)
        {
            try
            {
                var path = SettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = preference == ClosePreference.MinimizeToTray ? "tray" : preference == ClosePreference.Exit ? "exit" : "ask";
                var data = new Dictionary<string, string> { { "closePreference", text } };
                File.WriteAllText(path, json.Serialize(data), Encoding.UTF8);
            }
            catch
            {
            }
        }

        static string SettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenClawMonitor",
                "settings.json");
        }

        void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示面板", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add("打开 Control", null, (s, e) => OpenControl());
            trayMenu.Items.Add("重新检测", null, async (s, e) =>
            {
                ShowFromTray();
                await RefreshAsync(true);
            });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出控制中心", null, (s, e) =>
            {
                allowExit = true;
                trayIcon.Visible = false;
                Close();
            });

            trayIcon = new NotifyIcon
            {
                Text = "OpenClaw 控制中心",
                Icon = Icon,
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            if (!trayNoticeShown)
            {
                trayNoticeShown = true;
                trayIcon.ShowBalloonTip(1800, "OpenClaw 控制中心", "已在后台托盘运行。双击图标可打开。", ToolTipIcon.Info);
            }
        }

        void ShowFromTray()
        {
            BeginSmoothRestore();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Show();
            FinishSmoothRestore(true);
        }

        void OnMonitorResize()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                wasMinimized = true;
                return;
            }

            LayoutUi();
            if (wasMinimized)
            {
                BeginSmoothRestore();
                FinishSmoothRestore(false);
                wasMinimized = false;
            }
        }

        void BeginSmoothRestore()
        {
            if (smoothRestorePending) return;
            smoothRestorePending = true;
            Opacity = 0;
        }

        void FinishSmoothRestore(bool activate)
        {
            BeginInvoke(new Action(() =>
            {
                LayoutUi();
                Invalidate(true);
                Update();
                Opacity = 1;
                smoothRestorePending = false;
                if (activate) Activate();
            }));
        }

        void BuildUi()
        {
            Controls.Add(MakeLabel("OpenClaw 控制中心", 28, 20, 360, 34, 20f, Color.FromArgb(15, 23, 42), true));
            Controls.Add(MakeLabel("本机状态中心：启动、运行、Telegram、任务、Token 和成本流向", 30, 56, 720, 24, 9f, Color.FromArgb(100, 116, 139), false));

            updated = MakeLabel("", 840, 28, 230, 24, 9f, Color.FromArgb(100, 116, 139), false);
            Controls.Add(updated);
            openControlButton = new Button
            {
                Text = "打开 Control",
                Location = new Point(962, 20),
                Size = new Size(112, 36),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            openControlButton.FlatAppearance.BorderSize = 0;
            openControlButton.Click += (s, e) => OpenControl();
            AddBoundedHoverTip(openControlButton, "打开浏览器版 Control。");
            Controls.Add(openControlButton);

            refreshButton = new Button
            {
                Text = "重新检测",
                Location = new Point(1090, 20),
                Size = new Size(92, 36),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            refreshButton.FlatAppearance.BorderSize = 0;
            refreshButton.Click += async (s, e) => await RefreshAsync(true);
            AddBoundedHoverTip(refreshButton, "唤醒 WSL 并重查网关状态，不改配置。");
            Controls.Add(refreshButton);

            var hero = new RoundedPanel
            {
                Location = new Point(28, 92),
                Size = new Size(1154, 118),
                BackColor = Color.White,
                BorderColor = Color.FromArgb(226, 232, 240),
                Radius = 18
            };
            heroTitle = MakeLabel("正在检查 OpenClaw...", 28, 18, 520, 38, 22f, Color.FromArgb(15, 23, 42), true);
            heroDetail = MakeLabel("正在等待首次刷新。", 30, 60, 1000, 28, 10f, Color.FromArgb(71, 85, 105), false);
            hero.Controls.AddRange(new Control[] { heroTitle, heroDetail });
            Controls.Add(hero);

            overall = new Card("状态", 28, 232, 176, 88);
            gateway = new Card("网关", 220, 232, 176, 88);
            telegram = new Card("Telegram", 412, 232, 176, 88);
            tasks = new Card("后台任务", 604, 232, 176, 88);
            audit = new Card("提醒", 796, 232, 176, 88);
            session = new Card("最近活动", 988, 232, 194, 88);
            Controls.AddRange(new Control[] { overall.Panel, gateway.Panel, telegram.Panel, tasks.Panel, audit.Panel, session.Panel });

            Controls.Add(MakeLabel("Token / 成本流向", 28, 344, 260, 24, 12f, Color.FromArgb(15, 23, 42), true));
            tokenTotal = new Card("上下文占用", 28, 376, 142, 84);
            tokenInput = new Card("输入 Token", 184, 376, 142, 84);
            tokenOutput = new Card("输出 Token", 340, 376, 142, 84);
            tokenCache = new Card("缓存读取", 496, 376, 142, 84);
            tokenCost = new Card("已记录成本", 652, 376, 128, 84);
            Controls.AddRange(new Control[] { tokenTotal.Panel, tokenInput.Panel, tokenOutput.Panel, tokenCache.Panel, tokenCost.Panel });
            AddCostHint();

            Controls.Add(MakeLabel("现在在做什么", 28, 486, 260, 24, 12f, Color.FromArgb(15, 23, 42), true));
            taskGrid = new SmoothDataGridView
            {
                Location = new Point(28, 516),
                Size = new Size(1154, 150),
                BackgroundColor = Color.White,
                GridColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(31, 41, 55),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false
            };
            taskGrid.DefaultCellStyle.BackColor = Color.White;
            taskGrid.DefaultCellStyle.ForeColor = Color.FromArgb(31, 41, 55);
            taskGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            taskGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
            taskGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            taskGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);
            taskGrid.Columns.Add("label", "任务");
            taskGrid.Columns.Add("runtime", "类型");
            taskGrid.Columns.Add("status", "状态");
            taskGrid.Columns.Add("age", "持续");
            taskGrid.Columns.Add("last", "最近事件");
            Controls.Add(taskGrid);

            Controls.Add(MakeLabel("最近会话", 28, 692, 240, 24, 12f, Color.FromArgb(15, 23, 42), true));
            sessionList = MakeList(28, 722, 560, 120);
            Controls.Add(sessionList);

            Controls.Add(MakeLabel("最近提醒", 622, 692, 330, 24, 12f, Color.FromArgb(15, 23, 42), true));
            logList = MakeList(622, 722, 560, 120);
            Controls.Add(logList);

            statusLine = MakeLabel("", 28, 852, 1154, 24, 9f, Color.FromArgb(100, 116, 139), false);
            Controls.Add(statusLine);
            legendLine = MakeLabel("绿色=就绪，蓝色=正在工作，黄色=需要留意，红色=需要处理。", 28, 874, 1154, 22, 8.5f, Color.FromArgb(148, 163, 184), false);
            Controls.Add(legendLine);
            BuildHoverTip();
            LayoutUi();
        }

        void BuildHoverTip()
        {
            hoverTip = new RoundedPanel
            {
                Size = new Size(180, 34),
                BackColor = Color.White,
                BorderColor = Color.FromArgb(203, 213, 225),
                Radius = 10,
                Visible = false
            };
            hoverTipText = new Label
            {
                Location = new Point(10, 8),
                Size = new Size(160, 18),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BackColor = Color.Transparent
            };
            hoverTip.Controls.Add(hoverTipText);
            Controls.Add(hoverTip);
            hoverTip.BringToFront();
        }

        void AddBoundedHoverTip(Control target, string text)
        {
            target.MouseEnter += (s, e) => ShowBoundedHoverTip(target, text);
            target.MouseLeave += (s, e) => HideBoundedHoverTip();
        }

        void ShowBoundedHoverTip(Control target, string text)
        {
            if (hoverTip == null || hoverTipText == null) return;
            hoverTipText.Text = text;
            var measured = TextRenderer.MeasureText(text, hoverTipText.Font);
            var width = Math.Min(Math.Max(measured.Width + 24, 150), Math.Max(150, ClientSize.Width - 56));
            var height = 34;
            var screenPoint = target.PointToScreen(new Point(0, target.Height + 8));
            var local = PointToClient(screenPoint);
            var x = Math.Max(28, Math.Min(local.X, ClientSize.Width - width - 28));
            var y = local.Y;
            if (y + height > ClientSize.Height - 28)
                y = PointToClient(target.PointToScreen(new Point(0, -height - 8))).Y;
            y = Math.Max(28, y);
            hoverTip.SetBounds(x, y, width, height);
            hoverTipText.SetBounds(10, 8, width - 20, 18);
            hoverTip.Visible = true;
            hoverTip.BringToFront();
        }

        void HideBoundedHoverTip()
        {
            if (hoverTip != null) hoverTip.Visible = false;
        }

        void LayoutUi()
        {
            if (refreshButton == null || taskGrid == null) return;
            SuspendLayout();
            try
            {
                const int margin = 28;
                const int gap = 16;
                var contentWidth = Math.Max(900, ClientSize.Width - margin * 2);
                var clientHeight = Math.Max(680, ClientSize.Height);

                refreshButton.SetBounds(margin + contentWidth - 92, 20, 92, 36);
                openControlButton.SetBounds(margin + contentWidth - 230, 20, 112, 36);
                updated.SetBounds(Math.Max(margin, margin + contentWidth - 470), 28, 230, 24);

                var hero = Controls.OfType<RoundedPanel>().FirstOrDefault(p => p.Controls.Contains(heroTitle));
                if (hero != null)
                {
                    hero.SetBounds(margin, 92, contentWidth, 118);
                    heroTitle.SetBounds(28, 18, Math.Max(420, contentWidth - 56), 38);
                    heroDetail.SetBounds(30, 60, Math.Max(420, contentWidth - 60), 44);
                }

                var topCards = new[] { overall, gateway, telegram, tasks, audit, session };
                var topColumns = 6;
                var topCardWidth = (contentWidth - gap * (topColumns - 1)) / topColumns;
                var y = 232;
                for (var i = 0; i < topCards.Length; i++)
                {
                    var row = i / topColumns;
                    var col = i % topColumns;
                    topCards[i].SetBounds(margin + col * (topCardWidth + gap), y + row * 104, topCardWidth, 88);
                }
                y += ((topCards.Length + topColumns - 1) / topColumns) * 104 + 8;

                MoveDirectLabelFromOriginalY(344, margin, y, 260, 24);
                y += 32;
                var tokenCards = new[] { tokenTotal, tokenInput, tokenOutput, tokenCache, tokenCost };
                var tokenGap = contentWidth >= 1120 ? gap : 12;
                var tokenCardWidth = (contentWidth - tokenGap * (tokenCards.Length - 1)) / tokenCards.Length;
                for (var i = 0; i < tokenCards.Length; i++)
                    tokenCards[i].SetBounds(margin + i * (tokenCardWidth + tokenGap), y, tokenCardWidth, 84);
                y += 110;

                if (costHintPopup != null)
                {
                    var hintWidth = Math.Min(530, contentWidth);
                    costHintPopup.SetBounds(Math.Min(tokenCost.Panel.Left, margin + contentWidth - hintWidth), tokenCost.Panel.Bottom + 8, hintWidth, 56);
                }

                MoveDirectLabelFromOriginalY(486, margin, y, 260, 24);
                y += 30;
                var lowerArea = 178;
                var bottomArea = 58;
                var gridHeight = Math.Max(130, Math.Min(260, clientHeight - y - lowerArea - bottomArea));
                taskGrid.SetBounds(margin, y, contentWidth, gridHeight);
                y += gridHeight + 34;

                var halfWidth = (contentWidth - gap) / 2;
                MoveDirectLabelFromOriginalY(692, margin, y, halfWidth, 24);
                MoveDirectLabelFromOriginalX(622, margin + halfWidth + gap, y, halfWidth, 24);
                sessionList.SetBounds(margin, y + 30, halfWidth, 120);
                logList.SetBounds(margin + halfWidth + gap, y + 30, halfWidth, 120);
                y += 164;

                statusLine.SetBounds(margin, y, contentWidth, 24);
                legendLine.SetBounds(margin, y + 22, contentWidth, 22);
                AutoScrollMinSize = new Size(margin * 2 + contentWidth, y + 58);
            }
            finally
            {
                ResumeLayout();
            }
        }

        void MoveDirectLabelFromOriginalY(int originalY, int x, int y, int w, int h)
        {
            Label label = null;
            if (originalY == 344)
            {
                if (tokenHeader == null) tokenHeader = Controls.OfType<Label>().FirstOrDefault(l => l.Location.Y == originalY);
                label = tokenHeader;
            }
            else if (originalY == 486)
            {
                if (taskHeader == null) taskHeader = Controls.OfType<Label>().FirstOrDefault(l => l.Location.Y == originalY);
                label = taskHeader;
            }
            else if (originalY == 692)
            {
                if (sessionHeader == null) sessionHeader = Controls.OfType<Label>().FirstOrDefault(l => l.Location.Y == originalY && l.Location.X < 100);
                label = sessionHeader;
            }
            else
            {
                label = Controls.OfType<Label>().FirstOrDefault(l => l.Location.Y == originalY);
            }
            if (label != null) label.SetBounds(x, y, w, h);
        }

        void MoveDirectLabelFromOriginalX(int originalX, int x, int y, int w, int h)
        {
            if (logHeader == null) logHeader = Controls.OfType<Label>().FirstOrDefault(l => l.Location.X == originalX && l != updated);
            var label = logHeader;
            if (label != null) label.SetBounds(x, y, w, h);
        }

        ListBox MakeList(int x, int y, int w, int h)
        {
            return new ListBox
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(31, 41, 55),
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
        }

        Label MakeLabel(string text, int x, int y, int w, int h, float size, Color color, bool bold)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        async Task RefreshAsync(bool manualRecovery)
        {
            if (refreshing) return;
            refreshing = true;
            if (manualRecovery)
                updated.Text = "重新检测中...";
            else if (startingOpenClaw)
                updated.Text = "启动中...";
            refreshButton.Enabled = false;
            try
            {
                if (manualRecovery)
                    await EnsureOpenClawStartedAsync(true);
                var snapshot = await Task.Run(() => BuildSnapshot());
                Render(snapshot);
            }
            catch (Exception ex)
            {
                updated.Text = "刷新失败";
                statusLine.Text = ex.Message;
            }
            finally
            {
                refreshing = false;
                refreshButton.Enabled = true;
            }
        }

        async Task EnsureOpenClawStartedAsync(bool force)
        {
            if ((!force && startAttempted) || startingOpenClaw) return;
            startAttempted = true;
            startingOpenClaw = true;
            startupNote = force ? "正在重新检测并唤醒 OpenClaw..." : "正在启动 OpenClaw...";
            updated.Text = startupNote;
            try
            {
                var result = await Task.Run(() => StartOpenClawGateway());
                startupNote = result.Ok
                    ? (force ? "已重新检测：OpenClaw gateway 有响应。" : "OpenClaw 已启动，正在检查 Telegram。")
                    : (force ? "已重新检测：gateway 仍未响应，请查看状态卡片。" : "已尝试启动 OpenClaw；如果仍异常，请查看状态卡片。");
            }
            finally
            {
                startingOpenClaw = false;
            }
        }

        CommandResult StartOpenClawGateway()
        {
            var script =
                "systemctl --user start openclaw-gateway.service >/dev/null 2>&1 || true\n" +
                "pgrep -af 'openclaw-manual-keepalive' >/dev/null 2>&1 || (nohup bash -lc 'exec -a openclaw-manual-keepalive sleep infinity' >/dev/null 2>&1 &)\n" +
                "for i in $(seq 1 45); do openclaw gateway probe >/dev/null 2>&1 && exit 0; sleep 1; done\n" +
                "exit 1";
            return RunProcess("wsl.exe", new[] { "-d", WslDistro, "--", "bash", "-lc", script }, 60000);
        }

        void OpenControl()
        {
            try
            {
                var script = Path.Combine(Application.StartupPath, "Start-OpenClaw.ps1");
                if (File.Exists(script))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArg(script),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    return;
                }
                Process.Start("http://127.0.0.1:18789/");
            }
            catch (Exception ex)
            {
                statusLine.Text = "打开 Control 失败：" + ex.Message;
            }
        }

        Snapshot BuildSnapshot()
        {
            var probeTask = Task.Run(() => RunOpenClawJson(new[] { "gateway", "probe", "--json", "--timeout", "30000" }, 45000));
            var statusTask = Task.Run(() => RunOpenClawJson(new[] { "status", "--json" }, 35000));
            var tasksTask = Task.Run(() => RunOpenClawJson(new[] { "tasks", "list", "--json" }, 35000));
            var flowsTask = Task.Run(() => RunOpenClawText(new[] { "tasks", "flow", "list" }, 35000));
            var auditTask = Task.Run(() => RunOpenClawJson(new[] { "tasks", "audit", "--json" }, 35000));
            var logsTask = Task.Run(() => RunLogs());
            var workspaceTask = Task.Run(() => RunWorkspaceActivity());
            Task.WaitAll(probeTask, statusTask, tasksTask, flowsTask, auditTask, logsTask, workspaceTask);

            var probe = probeTask.Result;
            var status = statusTask.Result;
            var taskData = tasksTask.Result;
            var flowData = flowsTask.Result;
            var auditData = auditTask.Result;
            var logs = logsTask.Result;
            var workspaceActivity = workspaceTask.Result;

            var snapshot = new Snapshot();
            if (!probe.Item1)
            {
                gatewayProbeFailures++;
                snapshot.Error = probe.Item3;
                var serviceLooksAlive = StatusShowsGatewayServiceRunning(status.Item2) || status.Item1 || taskData.Item1 || auditData.Item1;
                if (serviceLooksAlive && gatewayProbeFailures < 3)
                {
                    snapshot.State = "Degraded";
                    snapshot.GatewayText = "探针不稳定";
                    snapshot.GatewaySoftFailure = true;
                    snapshot.StatusLine = "本轮 gateway 探针超时，但 OpenClaw 服务仍有响应；面板会继续自动重试。";
                }
                else
                {
                    snapshot.State = "Problem";
                    snapshot.GatewayText = "探针失败";
                    snapshot.StatusLine = string.IsNullOrWhiteSpace(probe.Item3) ? "gateway 探针连续失败。" : probe.Item3;
                }
            }
            else
            {
                gatewayProbeFailures = 0;
                FillFromProbe(snapshot, probe.Item2);
            }
            FillTokenUsage(snapshot, status.Item2);
            FillCostUsage(snapshot);
            FillTasks(snapshot, taskData.Item2);
            FillFlows(snapshot, flowData);
            FillWorkspaceActivity(snapshot, workspaceActivity);
            FillAudit(snapshot, auditData.Item2);
            FillLogs(snapshot, logs);

            if ((snapshot.RunningTasks > 0 || snapshot.FlowActive > 0 || snapshot.FlowBlocked > 0 || snapshot.FlowCancelRequested > 0 || snapshot.LocalWorkItems > 0) && snapshot.State != "Problem" && snapshot.State != "Degraded")
                snapshot.State = "Working";

            snapshot.StatusLine = string.IsNullOrWhiteSpace(snapshot.StatusLine)
                ? snapshot.GatewayText
                : snapshot.StatusLine;
            if (!string.IsNullOrWhiteSpace(startupNote) && snapshot.GatewayOk)
                snapshot.StatusLine = startupNote + " | " + snapshot.StatusLine;
            return snapshot;
        }

        bool StatusShowsGatewayServiceRunning(object statusObj)
        {
            var status = AsDict(statusObj);
            var service = AsDict(Get(status, "gatewayService"));
            var runtime = AsDict(Get(service, "runtime"));
            var statusText = Convert.ToString(Get(runtime, "status")) ?? "";
            var stateText = Convert.ToString(Get(runtime, "state")) ?? "";
            return statusText.Equals("running", StringComparison.OrdinalIgnoreCase)
                || stateText.Equals("active", StringComparison.OrdinalIgnoreCase);
        }

        void FillFromProbe(Snapshot s, object probeObj)
        {
            var probe = AsDict(probeObj);
            s.GatewayOk = ToBool(Get(probe, "ok"));
            var target = First(AsList(Get(probe, "targets")));
            var targetDict = AsDict(target);
            var connect = AsDict(Get(targetDict, "connect"));
            var health = AsDict(Get(targetDict, "health"));
            var channels = AsDict(Get(health, "channels"));
            var telegramChannel = AsDict(Get(channels, "telegram"));
            var network = AsDict(Get(probe, "network"));

            var rpcOk = ToBool(Get(connect, "rpcOk"));
            var latency = ToLong(Get(connect, "latencyMs"));
            s.GatewayOk = s.GatewayOk && rpcOk;
            s.GatewayText = s.GatewayOk
                ? "可连接 " + (latency >= 0 ? latency + "毫秒" : "")
                : "需检查";

            var tgConfigured = ToBool(Get(telegramChannel, "configured"));
            var tgRunning = ToBool(Get(telegramChannel, "running"));
            var tgConnected = ToBool(Get(telegramChannel, "connected"));
            s.TelegramOk = tgConfigured && tgRunning && tgConnected;
            s.TelegramText = !tgConfigured ? "未配置" : (s.TelegramOk ? "已连接" : "需检查");

            var summary = AsDict(Get(targetDict, "summary"));
            var summaryTasks = AsDict(Get(summary, "tasks"));
            if (summaryTasks.Count > 0)
                s.RunningTasks = Math.Max(s.RunningTasks, (int)Math.Max(0, ToLong(Get(summaryTasks, "active"))));

            var sessions = AsDict(Get(health, "sessions"));
            var recent = AsList(Get(sessions, "recent"));
            foreach (var item in recent.Cast<object>().Take(8))
            {
                var row = AsDict(item);
                var age = ToLong(Get(row, "age"));
                var key = Convert.ToString(Get(row, "key") ?? "");
                s.Sessions.Add(Pad(Age(age), 7) + " " + Trim(key, 70));
            }
            if (recent.Count > 0)
            {
                var age = ToLong(Get(AsDict(recent[0]), "age"));
                s.RecentSessionAge = Age(age);
                s.LastSessionAgeMs = age;
                s.LastSessionSource = TokenSource(Convert.ToString(Get(AsDict(recent[0]), "key") ?? ""));
                s.LastSessionModel = Convert.ToString(Get(AsDict(recent[0]), "model") ?? "-");
            }

            var url = Convert.ToString(Get(network, "localLoopbackUrl") ?? "ws://127.0.0.1:18789");
            s.StatusLine = url + " | Telegram " + s.TelegramText;
            if (!s.GatewayOk || (tgConfigured && !s.TelegramOk)) s.State = "Problem";
            if (s.GatewayOk && s.TelegramOk && s.State == "Idle") s.State = "Ready";
        }

        void FillTasks(Snapshot s, object tasksObj)
        {
            var data = AsDict(tasksObj);
            var items = AsList(Get(data, "tasks"));
            var activeItems = items.Cast<object>()
                .Select(item => AsDict(item))
                .Where(row =>
                {
                    var status = Convert.ToString(Get(row, "status") ?? "").ToLowerInvariant();
                    return status == "running" || status == "queued";
                })
                .ToList();

            s.RunningTasks = activeItems.Count;
            foreach (var row in activeItems.Take(20))
            {
                var label = Convert.ToString(Get(row, "label") ?? Get(row, "taskId") ?? "任务");
                var runtime = Convert.ToString(Get(row, "runtime") ?? "-");
                var rawStatus = Convert.ToString(Get(row, "status") ?? "-");
                var status = TranslateTaskStatus(rawStatus);
                var created = AgeSince(ToLong(Get(row, "createdAt")));
                var lastEventAt = ToLong(Get(row, "lastEventAt"));
                var last = AgeSince(lastEventAt);
                var lastAgeMs = MillisecondsSince(lastEventAt);
                if (lastAgeMs <= freshTaskEventWindowMs)
                    last += " · 活跃";
                else if (lastAgeMs > activeTaskEventWindowMs)
                {
                    status += "（静默）";
                    last += " · 事件偏旧";
                }
                s.Tasks.Add(new[] { Trim(label, 42), runtime, status, created, last });
            }
        }

        void FillFlows(Snapshot s, Tuple<bool, string, string> flowData)
        {
            if (flowData == null || !flowData.Item1 || string.IsNullOrWhiteSpace(flowData.Item2)) return;
            var match = System.Text.RegularExpressions.Regex.Match(
                flowData.Item2,
                @"TaskFlow pressure:\s*(\d+)\s+active\s+.\s+(\d+)\s+blocked\s+.\s+(\d+)\s+cancel-requested");
            if (!match.Success) return;

            s.FlowActive = (int)ToLong(match.Groups[1].Value);
            s.FlowBlocked = (int)ToLong(match.Groups[2].Value);
            s.FlowCancelRequested = (int)ToLong(match.Groups[3].Value);

            if (s.FlowActive > 0 || s.FlowBlocked > 0 || s.FlowCancelRequested > 0)
            {
                var status = s.FlowActive > 0 ? "运行中" : s.FlowBlocked > 0 ? "阻塞" : "取消中";
                var last = "active " + s.FlowActive + " / blocked " + s.FlowBlocked + " / cancel " + s.FlowCancelRequested;
                s.Tasks.Add(new[] { "TaskFlow 后台流程", "flow", status, "-", last });
            }
        }

        void FillTokenUsage(Snapshot s, object statusObj)
        {
            var status = AsDict(statusObj);
            var sessionsRoot = AsDict(Get(status, "sessions"));
            var recent = AsList(Get(sessionsRoot, "recent"));
            foreach (var item in recent.Cast<object>().Take(12))
            {
                var row = AsDict(item);
                var input = Math.Max(0, ToLong(Get(row, "inputTokens")));
                var output = Math.Max(0, ToLong(Get(row, "outputTokens")));
                var cacheRead = Math.Max(0, ToLong(Get(row, "cacheRead")));
                var cacheWrite = Math.Max(0, ToLong(Get(row, "cacheWrite")));
                var total = Math.Max(0, ToLong(Get(row, "totalTokens")));
                var context = Math.Max(0, ToLong(Get(row, "contextTokens")));
                var percent = ToLong(Get(row, "percentUsed"));
                var key = Convert.ToString(Get(row, "key") ?? "");
                var model = Convert.ToString(Get(row, "model") ?? "-");
                var age = ToLong(Get(row, "age"));
                var source = TokenSource(key);

                if (s.LastSessionAgeMs < 0 || (age >= 0 && age < s.LastSessionAgeMs))
                {
                    s.LastSessionAgeMs = age;
                    s.LastSessionSource = source;
                    s.LastSessionModel = model;
                    s.RecentSessionAge = Age(age);
                }

                s.TokenInput += input;
                s.TokenOutput += output;
                s.TokenCacheRead += cacheRead;
                s.TokenTotal += total;

                if (s.TokenContext == "-" && total > 0 && context > 0)
                    s.TokenContext = FormatTokens(total) + " / " + FormatTokens(context) + (percent >= 0 ? " (" + percent + "%)" : "");

                var bits = source + " · " + model + " · " + Age(age);
                var usage = "总 " + FormatTokens(total) + "｜入 " + FormatTokens(input) + "｜出 " + FormatTokens(output) + "｜缓存 " + FormatTokens(cacheRead + cacheWrite);
                s.TokenFlows.Add(bits + "    " + usage);
            }

            if (s.TokenFlows.Count == 0)
                s.TokenFlows.Add("暂时没有可用的 Token 会话快照。");
        }

        void FillCostUsage(Snapshot s)
        {
            var summary = GetCostSummary();
            if (summary.Available)
            {
                s.CostText = FormatUsd(summary.TotalCost);
                s.CostState = summary.TotalCost > 0 ? "work" : "good";
                foreach (var line in summary.Lines.Take(6))
                    s.TokenFlows.Add(line);
            }
            else
            {
                s.CostText = "未记录";
                s.CostState = "warn";
                s.TokenFlows.Add(string.IsNullOrWhiteSpace(summary.Error)
                    ? "成本 · 本地 session 日志里暂时没有 usage.cost；API-key 模型更容易留下成本记录。"
                    : "成本 · 读取失败：" + Trim(summary.Error, 100));
            }
        }

        CostSummary GetCostSummary()
        {
            lock (costLock)
            {
                if (cachedCost.Available && (DateTime.Now - cachedCost.UpdatedAt).TotalSeconds < 60)
                    return cachedCost;
            }

            var fresh = ReadCostSummary();
            lock (costLock)
            {
                cachedCost = fresh;
                return cachedCost;
            }
        }

        CostSummary ReadCostSummary()
        {
            var summary = new CostSummary { UpdatedAt = DateTime.Now };
            try
            {
                var script =
                    "const fs=require('fs');const path=require('path');const root=(process.env.HOME||'')+'/.openclaw/agents/main/sessions';const now=new Date();const monthStartMs=new Date(now.getFullYear(),now.getMonth(),1).getTime();const out={available:false,totalCost:0,buckets:[],error:'',monthStart:monthStartMs};const toMs=v=>{if(v==null)return 0;if(typeof v==='number')return v>1e12?v:v*1000;const n=Number(v);if(Number.isFinite(n)&&n>0)return n>1e12?n:n*1000;const d=Date.parse(String(v));return Number.isFinite(d)?d:0;};try{if(!fs.existsSync(root)){out.error='找不到 session 目录';}else{const map={};const seen=new Set();const add=(o,u,base)=>{const c=u&&u.cost;const value=Number(c&&c.total||0);if(!(value>0))return;const eventMs=toMs(o.timestamp||o.ts||o.createdAt||o.updatedAt||base.timestamp||base.ts)||base.fileM||0;if(eventMs<monthStartMs)return;const provider=String(o.provider||base.provider||'-');const model=String(o.model||base.model||'-');const dedupe=String(o.responseId||o.id||'')||[eventMs,provider,model,value,u.input,u.output,u.cacheRead,u.cacheWrite].join('|');if(seen.has(dedupe))return;seen.add(dedupe);const key=provider+'/'+model;const b=map[key]||(map[key]={key,cost:0,input:0,output:0,cacheRead:0,cacheWrite:0,totalTokens:0,replies:0});b.cost+=value;b.input+=Number(u.input||0);b.output+=Number(u.output||0);b.cacheRead+=Number(u.cacheRead||0);b.cacheWrite+=Number(u.cacheWrite||0);b.totalTokens+=Number(u.totalTokens||0);b.replies+=1;};const visit=(o,base)=>{if(!o||typeof o!=='object')return;if(o.usage&&o.usage.cost)add(o,o.usage,base);if(Array.isArray(o)){for(const v of o)visit(v,base);return;}for(const k of Object.keys(o)){if(k==='config'||k==='redacted')continue;visit(o[k],base);}};const files=fs.readdirSync(root).filter(f=>f.includes('.jsonl')).map(f=>{const p=path.join(root,f);return{f,p,m:fs.statSync(p).mtimeMs};}).filter(f=>f.m>=monthStartMs).sort((a,b)=>b.m-a.m).slice(0,500);for(const file of files){const text=fs.readFileSync(file.p,'utf8');for(const line of text.split(/\\r?\\n/)){if(!line||line.indexOf('usage')<0||line.indexOf('cost')<0)continue;let row;try{row=JSON.parse(line);}catch{continue;}visit(row,{provider:row.provider,model:row.model,timestamp:row.timestamp,ts:row.ts,fileM:file.m});}}out.buckets=Object.values(map).sort((a,b)=>b.cost-a.cost);out.totalCost=out.buckets.reduce((n,b)=>n+b.cost,0);out.available=out.buckets.length>0;}}catch(e){out.error=e&&e.message?e.message:String(e);}console.log(JSON.stringify(out));";
                var result = RunProcess("wsl.exe", new[] { "-d", WslDistro, "--", "node", "-e", script }, 60000);
                if (!result.Ok)
                {
                    summary.Error = Trim(result.Stderr + result.Error, 160);
                    return summary;
                }

                var payload = AsDict(json.DeserializeObject(result.Stdout));
                summary.TotalCost = ToDouble(Get(payload, "totalCost"));
                summary.Available = ToBool(Get(payload, "available"));
                summary.Error = Convert.ToString(Get(payload, "error") ?? "");
                foreach (var item in AsList(Get(payload, "buckets")).Cast<object>().Take(8))
                {
                    var b = AsDict(item);
                    summary.Lines.Add(
                        "成本 · " + Convert.ToString(Get(b, "key") ?? "-") +
                        " · " + FormatUsd(ToDouble(Get(b, "cost"))) +
                        " · " + Math.Max(0, ToLong(Get(b, "replies"))) + " 次回复" +
                        " · 入 " + FormatTokens(Math.Max(0, ToLong(Get(b, "input")))) +
                        "｜出 " + FormatTokens(Math.Max(0, ToLong(Get(b, "output")))) +
                        "｜缓存 " + FormatTokens(Math.Max(0, ToLong(Get(b, "cacheRead"))) + Math.Max(0, ToLong(Get(b, "cacheWrite")))));
                }
                return summary;
            }
            catch (Exception ex)
            {
                summary.Error = ex.Message;
                return summary;
            }
        }

        string TokenSource(string key)
        {
            key = key ?? "";
            if (key.Contains(":telegram:")) return "Telegram";
            if (key.Contains(":subagent:")) return "子任务";
            if (key.EndsWith(":main") || key.Contains(":main:main")) return "主会话";
            if (key.Contains(":slash:")) return "命令";
            return "直接会话";
        }

        void FillAudit(Snapshot s, object auditObj)
        {
            var audit = AsDict(auditObj);
            var findings = AsList(Get(audit, "findings"));
            var warnings = 0;
            var errors = 0;
            foreach (var findingObj in findings)
            {
                var finding = AsDict(findingObj);
                var findingTimeMs = AuditFindingTimestampMs(finding);
                if (findingTimeMs > 0 && findingTimeMs < monitorStartedAtMs) continue;

                var severity = (Convert.ToString(Get(finding, "severity")) ?? "").Trim().ToLowerInvariant();
                if (severity == "error") errors++;
                else if (severity == "warn" || severity == "warning") warnings++;
            }

            s.AuditWarnings = warnings;
            s.AuditErrors = errors;
            if (s.AuditErrors > 0) s.State = "Problem";
        }

        long AuditFindingTimestampMs(Dictionary<string, object> finding)
        {
            var task = AsDict(Get(finding, "task"));
            var flow = AsDict(Get(finding, "flow"));
            var timestamp = Math.Max(
                Math.Max(ToLong(Get(task, "lastEventAt")), ToLong(Get(task, "endedAt"))),
                Math.Max(ToLong(Get(flow, "updatedAt")), ToLong(Get(flow, "endedAt"))));
            timestamp = Math.Max(timestamp, Math.Max(ToLong(Get(task, "startedAt")), ToLong(Get(flow, "createdAt"))));
            timestamp = Math.Max(timestamp, Math.Max(ToLong(Get(task, "createdAt")), ToLong(Get(finding, "updatedAt"))));
            var ageMs = ToLong(Get(finding, "ageMs"));
            if (timestamp <= 0 && ageMs >= 0)
                timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds - ageMs;
            return timestamp;
        }

        void FillLogs(Snapshot s, List<Dictionary<string, object>> logs)
        {
            var interesting = logs
                .Where(l =>
                {
                    var sub = Convert.ToString(Get(l, "subsystem") ?? "");
                    var lvl = Convert.ToString(Get(l, "level") ?? "");
                    return sub.Contains("telegram") || lvl == "error" || lvl == "warn";
                })
                .TakeLastCompat(16)
                .ToList();

            foreach (var log in interesting)
            {
                var time = Convert.ToString(Get(log, "time") ?? "");
                var clock = time.Length >= 19 ? time.Substring(11, 8) : "--:--:--";
                var level = TranslateLogLevel(Convert.ToString(Get(log, "level") ?? ""));
                var subsystem = Trim(TranslateSubsystem(Convert.ToString(Get(log, "subsystem") ?? "")), 30);
                var message = Trim(System.Text.RegularExpressions.Regex.Replace(Convert.ToString(Get(log, "message") ?? ""), "\\s+", " "), 115);
                s.Logs.Add(clock + " " + Pad(level, 5) + " " + Pad(subsystem, 30) + " " + message);
            }
            if (s.Logs.Count == 0) s.Logs.Add("最近没有 Telegram 或错误日志。");
        }

        Tuple<bool, object, string> RunOpenClawJson(string[] args, int timeoutMs)
        {
            var all = new List<string> { "-d", WslDistro, "--", "bash", "-lc", BashCommand(args) };
            var result = RunProcess("wsl.exe", all.ToArray(), timeoutMs);
            if (!result.Ok) return Tuple.Create(false, (object)null, result.Stderr + result.Error);
            try
            {
                return Tuple.Create(true, json.DeserializeObject(result.Stdout), "");
            }
            catch (Exception ex)
            {
                return Tuple.Create(false, (object)null, "JSON 解析失败：" + ex.Message);
            }
        }

        Tuple<bool, string, string> RunOpenClawText(string[] args, int timeoutMs)
        {
            var all = new List<string> { "-d", WslDistro, "--", "bash", "-lc", BashCommand(args) };
            var result = RunProcess("wsl.exe", all.ToArray(), timeoutMs);
            return Tuple.Create(result.Ok, result.Stdout, result.Stderr + result.Error);
        }

        List<Dictionary<string, object>> RunLogs()
        {
            var result = RunProcess("wsl.exe", new[] { "-d", WslDistro, "--", "bash", "-lc", BashCommand(new[] { "logs", "--json", "--limit", "80", "--timeout", "10000" }) }, 20000);
            var items = new List<Dictionary<string, object>>();
            if (!result.Ok) return items;
            foreach (var line in result.Stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var dict = AsDict(json.DeserializeObject(line));
                    if (dict.Count > 0) items.Add(dict);
                }
                catch { }
            }
            return items;
        }

        Tuple<bool, string, string> RunWorkspaceActivity()
        {
            var script =
                "cd \"$HOME/.openclaw/workspace\" 2>/dev/null || exit 0\n" +
                "pidfile=\"memory/continuous-task-status/steinsgate-kurisu.pid\"\n" +
                "seen=' '\n" +
                "emit_proc() { role=\"$1\"; pid=\"$2\"; [ -n \"$pid\" ] || return; case \"$seen\" in *\" $pid \"*) return;; esac; seen=\"$seen$pid \"; etime=$(ps -p \"$pid\" -o etime= 2>/dev/null | awk '{$1=$1;print}'); args=$(ps -p \"$pid\" -o args= 2>/dev/null | cut -c1-160); [ -n \"$args\" ] && echo \"LOCALPROC\t$role\t$pid\t$etime\t$args\"; }\n" +
                "if [ -f \"$pidfile\" ]; then pid=$(tr -dc '0-9' < \"$pidfile\" 2>/dev/null); if [ -n \"$pid\" ] && ps -p \"$pid\" >/dev/null 2>&1; then emit_proc \"学习 daemon\" \"$pid\"; fi; fi\n" +
                "for pid in $(ps -eo pid=,args= | awk '/continuous_learning_daemon\\.py/ && !/awk/ {print $1}'); do emit_proc \"学习 daemon\" \"$pid\"; done\n" +
                "for pid in $(ps -eo pid=,args= | awk '/steinsgate_visible_supervisor\\.py/ && !/awk/ {print $1}'); do emit_proc \"可见 supervisor\" \"$pid\"; done\n" +
                "svc=$(systemctl --user is-active openclaw-netwatch.service 2>/dev/null || true); [ \"$svc\" = \"active\" ] && echo \"SERVICE\tOpenClaw 网络 watchdog\tactive\topenclaw-netwatch.service\"\n" +
                "watchlog=\"memory/continuous-task-status/steinsgate-kurisu-watchdog.log\"; [ -f \"$watchlog\" ] && printf 'WATCHDOG\\t%s\\t%s\\n' \"$(stat -c '%Y' \"$watchlog\" 2>/dev/null)\" \"$(tail -1 \"$watchlog\" 2>/dev/null | cut -c1-160)\"\n" +
                "find steinsgate memory/continuous-task-status -maxdepth 1 -type f \\( -name 'material_coverage_rotation*.md' -o -name 'material_coverage_rotation*.json' -o -name 'learning_synthesis_visible*.md' -o -name 'learning_synthesis_visible*.json' -o -name 'audio_performance_aux_notes_batch*.md' -o -name 'audio_performance_aux_notes_batch*.json' -o -name 'steinsgate-kurisu.json' \\) -mmin -120 -printf 'ARTIFACT\\t%T@\\t%p\\n' 2>/dev/null | sort -k2,2nr | head -12\n";
            var result = RunProcess("wsl.exe", new[] { "-d", WslDistro, "--", "bash", "-lc", script }, 15000);
            return Tuple.Create(result.Ok, result.Stdout, result.Stderr + result.Error);
        }

        void FillWorkspaceActivity(Snapshot s, Tuple<bool, string, string> data)
        {
            if (data == null || !data.Item1 || string.IsNullOrWhiteSpace(data.Item2)) return;

            var artifactRows = 0;
            long latestArtifactMs = 0;
            var currentArtifacts = new Dictionary<string, long>();
            foreach (var line in data.Item2.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { '\t' });
                if (parts.Length >= 2 && parts[0] == "DAEMON")
                {
                    s.LocalDaemonActive = true;
                    s.LocalWorkItems = Math.Max(s.LocalWorkItems, 1);
                    var pid = parts[1];
                    var detail = parts.Length >= 3 ? Trim(parts[2], 70) : "";
                    s.Tasks.Add(new[] { "本地学习 daemon", "本地进程", "运行中", "-", "PID " + pid + (string.IsNullOrWhiteSpace(detail) ? "" : " · " + detail) });
                    continue;
                }

                if (parts.Length >= 5 && parts[0] == "LOCALPROC")
                {
                    s.LocalDaemonActive = true;
                    s.LocalWorkItems = Math.Max(s.LocalWorkItems, 1);
                    var role = parts[1];
                    var pid = parts[2];
                    var etime = parts[3];
                    var detail = Trim(parts[4], 70);
                    s.Tasks.Add(new[] { "本地" + role, "OS 进程", "运行中", string.IsNullOrWhiteSpace(etime) ? "-" : etime, "PID " + pid + (string.IsNullOrWhiteSpace(detail) ? "" : " · " + detail) });
                    continue;
                }

                if (parts.Length >= 4 && parts[0] == "SERVICE")
                {
                    s.LocalDaemonActive = true;
                    s.LocalWorkItems = Math.Max(s.LocalWorkItems, 1);
                    s.Tasks.Add(new[] { parts[1], "systemd 服务", "运行中", "-", parts[3] });
                    continue;
                }

                if (parts.Length >= 3 && parts[0] == "WATCHDOG")
                {
                    double seconds = 0;
                    if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                    {
                        var watchdogMs = (long)(seconds * 1000);
                        var text = "watchdog 最近检查 " + AgeSince(watchdogMs);
                        if (!string.IsNullOrWhiteSpace(parts[2])) text += " · " + Trim(parts[2], 80);
                        s.StatusLine = string.IsNullOrWhiteSpace(s.StatusLine) ? text : s.StatusLine + " | " + text;
                    }
                    continue;
                }

                if (parts.Length >= 3 && parts[0] == "ARTIFACT")
                {
                    double seconds = 0;
                    if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                    {
                        var artifactMs = (long)(seconds * 1000);
                        latestArtifactMs = Math.Max(latestArtifactMs, artifactMs);
                        currentArtifacts[parts[2]] = artifactMs;
                    }

                    artifactRows++;
                }
            }

            var changedArtifacts = new List<Tuple<string, long>>();
            lock (artifactLock)
            {
                if (artifactBaselineReady)
                {
                    foreach (var item in currentArtifacts)
                    {
                        long previous;
                        if (!previousArtifactMtimes.TryGetValue(item.Key, out previous) || item.Value > previous + 500)
                            changedArtifacts.Add(Tuple.Create(item.Key, item.Value));
                    }
                }
                previousArtifactMtimes = currentArtifacts;
                artifactBaselineReady = true;
            }

            foreach (var item in changedArtifacts.OrderByDescending(x => x.Item2).Take(4))
            {
                var name = Path.GetFileName(item.Item1);
                s.Tasks.Add(new[] { Trim(name, 42), "产物写入", "刚更新", AgeSince(item.Item2), "连续刷新检测到写入" });
            }

            if (artifactRows > 0)
            {
                s.LocalWorkAge = AgeSince(latestArtifactMs);
                if (changedArtifacts.Count > 0)
                    s.LocalWorkItems = Math.Max(s.LocalWorkItems, 1);
                var label = changedArtifacts.Count > 0
                    ? "检测到产物写入"
                    : s.LocalDaemonActive ? "本地 daemon + 最近产物" : "最近产物";
                var suffix = changedArtifacts.Count > 0
                    ? ""
                    : s.LocalDaemonActive ? "" : "（不计为正在运行任务）";
                s.StatusLine = string.IsNullOrWhiteSpace(s.StatusLine)
                    ? label + " " + s.LocalWorkAge + suffix
                    : s.StatusLine + " | " + label + " " + s.LocalWorkAge + suffix;
            }
            else if (s.LocalDaemonActive)
            {
                s.StatusLine = string.IsNullOrWhiteSpace(s.StatusLine)
                    ? "本地 daemon 运行中"
                    : s.StatusLine + " | 本地 daemon 运行中";
            }
        }

        string BashCommand(string[] args)
        {
            var parts = new List<string> { OpenClawCommand };
            parts.AddRange(args.Select(ShellQuote));
            return string.Join(" ", parts);
        }

        string ShellQuote(string arg)
        {
            if (arg == null) return "''";
            return "'" + arg.Replace("'", "'\"'\"'") + "'";
        }

        CommandResult RunProcess(string file, string[] args, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = string.Join(" ", args.Select(QuoteArg)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return new CommandResult { Ok = false, ExitCode = -1, Error = "命令超时" };
                }
                return new CommandResult
                {
                    Ok = proc.ExitCode == 0,
                    ExitCode = proc.ExitCode,
                    Stdout = stdout.Result,
                    Stderr = stderr.Result
                };
            }
        }

        string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        void Render(Snapshot s)
        {
            updated.Text = "";
            heroTitle.Text = HeroTitle(s);
            heroDetail.Text = HeroDetail(s);
            heroTitle.ForeColor = HeroColor(s);
            overall.Value.Text = DisplayState(s.State);
            SetCard(overall, s.State == "Problem" ? "bad" : s.State == "Working" ? "work" : s.State == "Ready" ? "good" : "warn");
            gateway.Value.Text = s.GatewayText;
            SetCard(gateway, s.GatewayOk ? "good" : s.GatewaySoftFailure ? "warn" : "bad");
            telegram.Value.Text = s.TelegramText;
            SetCard(telegram, s.TelegramOk ? "good" : "bad");
            var registeredWork = Math.Max(s.RunningTasks, s.FlowActive);
            var backgroundTotal = registeredWork + s.FlowBlocked + s.FlowCancelRequested + s.LocalWorkItems;
            tasks.Value.Text = backgroundTotal.ToString();
            SetCard(tasks, backgroundTotal > 0 ? "work" : "good");
            audit.Value.Text = s.AuditWarnings + " 提醒 / " + s.AuditErrors + " 错误";
            SetCard(audit, s.AuditErrors > 0 ? "bad" : s.AuditWarnings > 0 ? "warn" : "good");
            session.Value.Text = s.RecentSessionAge;
            SetCard(session, s.RecentSessionAge == "-" ? "warn" : "good");

            tokenTotal.Value.Text = s.TokenContext != "-" ? s.TokenContext : FormatTokens(s.TokenTotal);
            SetCard(tokenTotal, s.TokenTotal > 0 ? "work" : "warn");
            tokenInput.Value.Text = FormatTokens(s.TokenInput);
            SetCard(tokenInput, s.TokenInput > 0 ? "good" : "warn");
            tokenOutput.Value.Text = FormatTokens(s.TokenOutput);
            SetCard(tokenOutput, s.TokenOutput > 0 ? "good" : "warn");
            tokenCache.Value.Text = FormatTokens(s.TokenCacheRead);
            SetCard(tokenCache, s.TokenCacheRead > 0 ? "good" : "warn");
            tokenCost.Value.Text = s.CostText;
            SetCard(tokenCost, s.CostState);

            taskGrid.Rows.Clear();
            foreach (var row in s.Tasks) taskGrid.Rows.Add(row);

            sessionList.Items.Clear();
            foreach (var row in s.Sessions) sessionList.Items.Add(row);
            if (s.Sessions.Count == 0) sessionList.Items.Add("探针没有返回会话数据。");

            logList.Items.Clear();
            foreach (var row in s.Logs) logList.Items.Add(row);

            statusLine.Text = s.StatusLine;
        }

        string HeroTitle(Snapshot s)
        {
            if (s.State == "Problem") return "需要处理";
            if (s.State == "Degraded") return "探针不稳定";
            if (s.State == "Working") return "OpenClaw 正在工作";
            if (s.State == "Ready") return "OpenClaw 已就绪";
            return "OpenClaw 当前安静";
        }

        string HeroDetail(Snapshot s)
        {
            if (s.State == "Problem")
            {
                if (!s.GatewayOk) return "控制中心连不上网关。请检查 WSL 或 OpenClaw gateway。";
                if (!s.TelegramOk) return "网关可连接，但 Telegram 未连接或未配置。";
                if (s.AuditErrors > 0) return "任务审计有错误。请查看提醒和日志。";
                return "有项目需要处理。";
            }
            if (s.State == "Degraded") return "OpenClaw 服务仍有响应，但本轮 gateway 探针超时。面板会自动重试，连续失败才标红。";
            if (s.State == "Working") return "检测到 OpenClaw 注册任务、活跃 TaskFlow、仍在运行的本地 daemon，或连续刷新之间的新产物写入。可以在下方表格看进展。";
            if (s.State == "Ready") return "网关和 Telegram 已连接；后台没有 queued/running 任务、活跃 TaskFlow 或仍在运行的本地 daemon。";
            return "后台没有 queued/running 任务、活跃 TaskFlow 或仍在运行的本地 daemon。";
        }

        Color HeroColor(Snapshot s)
        {
            if (s.State == "Problem") return Color.FromArgb(238, 96, 96);
            if (s.State == "Degraded") return Color.FromArgb(229, 176, 75);
            if (s.State == "Working") return Color.FromArgb(86, 160, 220);
            if (s.State == "Ready") return Color.FromArgb(84, 190, 130);
            return Color.FromArgb(229, 176, 75);
        }

        string DisplayState(string state)
        {
            if (state == "Problem") return "需要处理";
            if (state == "Degraded") return "需观察";
            if (state == "Working") return "正在工作";
            if (state == "Ready") return "就绪";
            return "空闲";
        }

        static string TranslateTaskStatus(string status)
        {
            var key = (status ?? "").Trim().ToLowerInvariant();
            if (key == "running") return "运行中";
            if (key == "pending") return "等待中";
            if (key == "queued") return "排队中";
            if (key == "complete" || key == "completed" || key == "done") return "已完成";
            if (key == "failed" || key == "error") return "失败";
            if (key == "cancelled" || key == "canceled") return "已取消";
            return string.IsNullOrWhiteSpace(status) ? "-" : status;
        }

        static string TranslateLogLevel(string level)
        {
            var key = (level ?? "").Trim().ToLowerInvariant();
            if (key == "error") return "错误";
            if (key == "warn" || key == "warning") return "提醒";
            if (key == "info") return "信息";
            if (key == "debug") return "调试";
            return string.IsNullOrWhiteSpace(level) ? "-" : level.ToUpperInvariant();
        }

        static string TranslateSubsystem(string subsystem)
        {
            var key = subsystem ?? "";
            if (key.Contains("gateway/channels/telegram")) return "Telegram 通道";
            if (key.Contains("diagnostic")) return "诊断";
            if (key.Contains("gateway")) return "网关";
            if (key.Contains("telegram")) return "Telegram";
            return key;
        }

        void AddCostHint()
        {
            const string text = "这是 OpenClaw 根据本月本地 usage.cost 记录汇总的估算成本，每月 1 号自然重新开始。它不等同于服务商最终账单，实际扣费以 OpenAI / Gemini / DeepSeek 等后台账单为准。";
            var info = new InfoBadge
            {
              Location = new Point(86, 12),
                Size = new Size(15, 15)
            };
            tokenCost.Panel.Controls.Add(info);
            info.BringToFront();

            costHintPopup = new RoundedPanel
            {
                Location = new Point(tokenCost.Panel.Left, tokenCost.Panel.Bottom + 8),
                Size = new Size(530, 56),
                BackColor = Color.FromArgb(248, 250, 252),
                BorderColor = Color.FromArgb(203, 213, 225),
                Radius = 12,
                Visible = false
            };
            var hintText = new Label
            {
                Text = text,
                Location = new Point(14, 9),
                Size = new Size(502, 38),
                AutoEllipsis = false,
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Microsoft YaHei UI", 9f),
                BackColor = Color.Transparent
            };
            costHintPopup.Controls.Add(hintText);
            Controls.Add(costHintPopup);

            EventHandler show = (s, e) =>
            {
                costHintPopup.Visible = true;
                costHintPopup.BringToFront();
            };
            EventHandler hide = (s, e) => BeginInvoke(new Action(() =>
            {
                var p = PointToClient(Cursor.Position);
                if (!tokenCost.Panel.Bounds.Contains(p) && !costHintPopup.Bounds.Contains(p))
                    costHintPopup.Visible = false;
            }));
            tokenCost.Panel.MouseEnter += show;
            tokenCost.Value.MouseEnter += show;
            info.MouseEnter += show;
            costHintPopup.MouseEnter += show;
            tokenCost.Panel.MouseLeave += hide;
            tokenCost.Value.MouseLeave += hide;
            info.MouseLeave += hide;
            costHintPopup.MouseLeave += hide;
        }

        void SetCard(Card card, string state)
        {
            if (state == "good") card.Value.ForeColor = Color.FromArgb(84, 190, 130);
            else if (state == "bad") card.Value.ForeColor = Color.FromArgb(238, 96, 96);
            else if (state == "warn") card.Value.ForeColor = Color.FromArgb(229, 176, 75);
            else if (state == "work") card.Value.ForeColor = Color.FromArgb(86, 160, 220);
            else card.Value.ForeColor = Color.White;
        }

        static Dictionary<string, object> AsDict(object value)
        {
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        static ArrayList AsList(object value)
        {
            if (value is ArrayList) return (ArrayList)value;
            var array = value as object[];
            if (array != null)
            {
                var list = new ArrayList();
                list.AddRange(array);
                return list;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                var list = new ArrayList();
                foreach (var item in enumerable) list.Add(item);
                return list;
            }
            return new ArrayList();
        }

        static object Get(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        static object First(ArrayList list)
        {
            return list != null && list.Count > 0 ? list[0] : null;
        }

        static bool ToBool(object value)
        {
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        static long ToLong(object value)
        {
            if (value == null) return -1;
            try { return Convert.ToInt64(value); } catch { return -1; }
        }

        static double ToDouble(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToDouble(value); } catch { return 0; }
        }

        static string AgeSince(long epochMs)
        {
            if (epochMs <= 0) return "-";
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;
            return Age((long)Math.Max(0, (DateTime.Now - dt).TotalMilliseconds));
        }

        static long MillisecondsSince(long epochMs)
        {
            if (epochMs <= 0) return long.MaxValue;
            return Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - epochMs);
        }

        static string Age(long ms)
        {
            if (ms < 0) return "-";
            if (ms < 1000) return ms + "毫秒";
            var sec = ms / 1000;
            if (sec < 60) return sec + "秒";
            var min = sec / 60;
            if (min < 60) return min + "分";
            var hr = min / 60;
            if (hr < 48) return hr + "小时";
            return (hr / 24) + "天";
        }

        static string FormatTokens(long value)
        {
            if (value <= 0) return "-";
            if (value >= 1000000) return (value / 1000000d).ToString("0.#") + "M";
            if (value >= 1000) return (value / 1000d).ToString("0.#") + "K";
            return value.ToString();
        }

        static string FormatUsd(double value)
        {
            if (value <= 0) return "$0.00";
            if (value < 0.01) return "$" + value.ToString("0.0000");
            return "$" + value.ToString("0.00");
        }

        static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        static string Pad(string text, int width)
        {
            text = text ?? "";
            return text.Length >= width ? text : text + new string(' ', width - text.Length);
        }
    }

    sealed class Card
    {
        public RoundedPanel Panel { get; private set; }
        public Label Value { get; private set; }
        readonly Label titleLabel;

        public Card(string title, int x, int y, int w, int h)
        {
            Panel = new RoundedPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.White,
                BorderColor = Color.FromArgb(226, 232, 240),
                Radius = 16
            };
            titleLabel = new Label
            {
                Text = title,
                Location = new Point(12, 10),
                Size = new Size(w - 24, 22),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            Value = new Label
            {
                Text = "-",
                Location = new Point(12, 38),
                Size = new Size(w - 24, h - 44),
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            Panel.Controls.Add(titleLabel);
            Panel.Controls.Add(Value);
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            Panel.SetBounds(x, y, w, h);
            titleLabel.SetBounds(12, 10, Math.Max(20, w - 24), 22);
            Value.SetBounds(12, 38, Math.Max(20, w - 24), Math.Max(20, h - 44));
        }
    }

    sealed class InfoBadge : Control
    {
        public InfoBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var fill = new SolidBrush(Color.FromArgb(239, 246, 255)))
            using (var border = new Pen(Color.FromArgb(96, 165, 250)))
            using (var text = new SolidBrush(Color.FromArgb(37, 99, 235)))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                e.Graphics.FillEllipse(fill, rect);
                e.Graphics.DrawEllipse(border, rect);
                e.Graphics.DrawString("i", font, text, rect, format);
            }
        }
    }

    sealed class SmoothDataGridView : DataGridView
    {
        public SmoothDataGridView()
        {
            DoubleBuffered = true;
        }
    }

    sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }

        public RoundedPanel()
        {
            Radius = 14;
            BorderColor = Color.FromArgb(226, 232, 240);
            DoubleBuffered = true;
            Padding = new Padding(1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var brush = new SolidBrush(BackColor))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    static class EnumerableCompat
    {
        public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count)
        {
            var queue = new Queue<T>();
            foreach (var item in source)
            {
                queue.Enqueue(item);
                while (queue.Count > count) queue.Dequeue();
            }
            return queue.ToArray();
        }
    }
}
