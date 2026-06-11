using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json;
using Vcad.Core.Results;
using Vcad.Plugin.Config;
using Vcad.Plugin.Execution;
using Vcad.Plugin.Net;

namespace Vcad.Plugin.UI
{
    internal class SidebarControl : UserControl
    {
        public event EventHandler<bool> CollapsedChanged;

        private TabControl _tabs;
        private Button _btnCollapse;
        private Panel _collapsedStrip;
        private TableLayoutPanel _rootLayout;
        private bool _collapsed;

        // Chat Tab controls
        private FlowLayoutPanel _chatList;
        private Button _btnUseAgent;
        private TextBox _txtNaturalLanguage;
        private Label _lblChatStatus;

        // Settings Tab controls
        private ComboBox _cmbProvider;
        private TextBox _txtBaseUrl;
        private ComboBox _cmbModel;
        private TextBox _txtApiKey;
        private CheckBox _chkShowKey;
        private NumericUpDown _numPort;
        private CheckBox _chkStrictJson;
        private NumericUpDown _numTimeout;
        private CheckBox _chkAutoRun;
        private Button _btnTestConnection;
        private Button _btnSaveSettings;
        private ComboBox _cmbProfile;
        private Button _btnNewProfile;
        private Button _btnDeleteProfile;
        private Label _lblSettingsStatus;

        // Usage Tab controls
        private Label _lblUsageRequests;
        private Label _lblUsageSuccess;
        private Label _lblUsageFailed;
        private Label _lblUsageAvg;
        private Label _lblUsageProvider;
        private Label _lblUsageModel;
        private Label _lblStatus;

        private int _usageRequests;
        private int _usageSuccess;
        private int _usageFailed;
        private long _usageTotalMs;

        private static readonly Color CadBg = Color.FromArgb(0x13, 0x13, 0x13);
        private static readonly Color CadPanel = Color.FromArgb(0x1B, 0x1B, 0x1C);
        private static readonly Color CadPanelHigh = Color.FromArgb(0x2A, 0x2A, 0x2A);
        private static readonly Color CadInput = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color CadBorder = Color.FromArgb(0x3A, 0x4A, 0x49);
        private static readonly Color CadText = Color.FromArgb(0xE5, 0xE2, 0xE1);
        private static readonly Color CadMuted = Color.FromArgb(0xB9, 0xCA, 0xC9);
        private static readonly Color CadCyan = Color.FromArgb(0x00, 0xDD, 0xDD);
        private static readonly Color CadGreen = Color.FromArgb(0x4C, 0xE3, 0x46);
        private static readonly Color CadOrange = Color.FromArgb(0xFE, 0x94, 0x00);
        private static readonly Font UiFont = new Font("Microsoft YaHei UI", 9F);
        private static readonly Font UiFontSmall = new Font("Microsoft YaHei UI", 8F);
        private static readonly Font UiFontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font MonoFont = new Font("Consolas", 9F);

        public SidebarControl()
        {
            Dock = DockStyle.Fill;
            BackColor = CadBg;
            ForeColor = CadText;
            Font = UiFont;
            BuildLayout();
            LoadProfilesIntoUi();
        }

        private void BuildLayout()
        {
            _rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _rootLayout.Controls.Add(BuildHeader(), 0, 0);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Alignment = TabAlignment.Bottom,
                ItemSize = new Size(140, 42),
                SizeMode = TabSizeMode.Fixed,
            };
            _tabs.DrawItem += OnDrawTab;
            _tabs.Resize += (s, e) => ResizeTabItems();

            var chatTab = new TabPage("对话");
            chatTab.BackColor = CadBg;
            BuildChatTab(chatTab);
            _tabs.TabPages.Add(chatTab);

            var settingsTab = new TabPage("配置");
            settingsTab.BackColor = CadBg;
            BuildSettingsTab(settingsTab);
            _tabs.TabPages.Add(settingsTab);

            var usageTab = new TabPage("用量");
            usageTab.BackColor = CadBg;
            BuildUsageTab(usageTab);
            _tabs.TabPages.Add(usageTab);

            _rootLayout.Controls.Add(_tabs, 0, 1);
            Controls.Add(_rootLayout);

            _collapsedStrip = BuildCollapsedStrip();
            _collapsedStrip.Visible = false;
            Controls.Add(_collapsedStrip);
            ResizeTabItems();
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                Padding = new Padding(10, 8, 10, 8),
            };

            var title = new Label
            {
                Text = "CAD AI 助手",
                AutoSize = true,
                ForeColor = CadCyan,
                Font = UiFontBold,
                Location = new Point(12, 13),
            };
            var mark = new Label
            {
                Text = "▧",
                AutoSize = true,
                ForeColor = CadCyan,
                Font = new Font("Consolas", 13F, FontStyle.Bold),
                Location = new Point(2, 9),
            };
            var online = new Label
            {
                Text = "● ONLINE",
                AutoSize = true,
                ForeColor = CadGreen,
                Font = UiFontSmall,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(230, 15),
            };
            _btnCollapse = new Button
            {
                Text = "‹",
                Width = 24,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                ForeColor = CadCyan,
                BackColor = CadBg,
                Font = UiFontBold,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnCollapse.FlatAppearance.BorderColor = CadBorder;
            _btnCollapse.FlatAppearance.BorderSize = 1;
            _btnCollapse.Click += (s, e) => SetCollapsed(!_collapsed);
            header.Resize += (s, e) => online.Left = Math.Max(160, header.Width - online.Width - 12);
            header.Resize += (s, e) => _btnCollapse.Left = Math.Max(120, online.Left - _btnCollapse.Width - 8);
            header.Controls.Add(mark);
            header.Controls.Add(title);
            header.Controls.Add(online);
            header.Controls.Add(_btnCollapse);
            return header;
        }

        private Panel BuildCollapsedStrip()
        {
            var strip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                Padding = new Padding(6),
            };
            var expand = new Button
            {
                Text = "›",
                Width = 36,
                Height = 32,
                Left = 6,
                Top = 8,
                FlatStyle = FlatStyle.Flat,
                ForeColor = CadCyan,
                BackColor = CadBg,
                Font = UiFontBold,
            };
            expand.FlatAppearance.BorderColor = CadBorder;
            expand.FlatAppearance.BorderSize = 1;
            expand.Click += (s, e) => SetCollapsed(false);
            var label = new Label
            {
                Text = "V\r\nC\r\nA\r\nD",
                AutoSize = false,
                Width = 36,
                Height = 120,
                Left = 7,
                Top = 52,
                ForeColor = CadCyan,
                Font = UiFontBold,
                TextAlign = ContentAlignment.TopCenter,
            };
            strip.Controls.Add(expand);
            strip.Controls.Add(label);
            return strip;
        }

        private void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            if (_rootLayout != null) _rootLayout.Visible = !collapsed;
            if (_collapsedStrip != null) _collapsedStrip.Visible = collapsed;
            CollapsedChanged?.Invoke(this, collapsed);
        }

        private void BuildChatTab(TabPage tab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10, 8, 10, 8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            layout.Controls.Add(BuildChatMetrics(), 0, 0);

            _chatList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = CadBg,
                Padding = new Padding(0, 6, 0, 6),
            };
            _chatList.Resize += (s, e) => ResizeChatCards();
            layout.Controls.Add(_chatList, 0, 1);

            var inputPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CadPanel,
                Padding = new Padding(8),
            };
            _txtNaturalLanguage = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.None,
                BackColor = CadPanel,
                ForeColor = CadText,
                Font = UiFont,
                Text = "询问 CAD 助手...",
                Width = 250,
                Height = 40,
                Location = new Point(10, 12),
            };
            _txtNaturalLanguage.GotFocus += (s, e) =>
            {
                if (_txtNaturalLanguage.Text == "询问 CAD 助手...") _txtNaturalLanguage.Text = "";
            };
            _txtNaturalLanguage.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await OnUseAgentAsync();
                }
            };
            _btnUseAgent = new Button
            {
                Text = "▶",
                Width = 44,
                Height = 40,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnUseAgent.Click += async (s, e) => await OnUseAgentAsync();
            inputPanel.Resize += (s, e) =>
            {
                _btnUseAgent.Left = inputPanel.Width - _btnUseAgent.Width - 10;
                _txtNaturalLanguage.Width = Math.Max(120, _btnUseAgent.Left - 20);
            };
            inputPanel.Controls.Add(_txtNaturalLanguage);
            inputPanel.Controls.Add(_btnUseAgent);
            layout.Controls.Add(inputPanel, 0, 2);

            _lblChatStatus = new Label
            {
                Text = "就绪。",
                Dock = DockStyle.Fill,
                ForeColor = CadGreen,
                Font = UiFontSmall,
            };
            _lblStatus = _lblChatStatus;
            layout.Controls.Add(_lblChatStatus, 0, 3);

            tab.Controls.Add(layout);
            ApplyIndustrialStyle(tab);
            StylePrimaryButton(_btnUseAgent);
            AddAssistantCard("CAD 核心引擎", "描述你要画什么，我会生成并执行 CAD 命令。");
        }

        private Control BuildChatMetrics()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                ColumnCount = 3,
                RowCount = 1,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            panel.Controls.Add(MetricLabel("今日费用", "$0.00"), 0, 0);
            panel.Controls.Add(MetricLabel("会话请求", "0"), 1, 0);
            panel.Controls.Add(MetricLabel("状态", "接口已连接"), 2, 0);
            return panel;
        }

        private Control MetricLabel(string title, string value)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = CadBg };
            panel.Controls.Add(new Label
            {
                Text = title,
                Left = 0,
                Top = 0,
                Width = 100,
                Height = 16,
                ForeColor = CadMuted,
                Font = UiFontSmall,
            });
            panel.Controls.Add(new Label
            {
                Text = value,
                Left = 0,
                Top = 17,
                Width = 110,
                Height = 20,
                ForeColor = CadCyan,
                Font = UiFontBold,
            });
            return panel;
        }

        private void AddUserCard(string text)
        {
            AddChatCard(null, text, false);
        }

        private void AddAssistantCard(string title, string text)
        {
            AddChatCard(title, text, true);
        }

        private void AddChatCard(string title, string text, bool assistant)
        {
            if (_chatList == null) return;
            var width = Math.Max(220, _chatList.ClientSize.Width - 28);
            var label = new Label
            {
                Text = (string.IsNullOrEmpty(title) ? "" : title + "\r\n") + text,
                ForeColor = assistant ? CadText : Color.White,
                Font = assistant ? UiFontBold : UiFont,
                AutoSize = false,
                Width = width - 20,
                Left = 10,
                Top = 8,
            };
            var preferred = label.GetPreferredSize(new Size(width - 20, 0));
            label.Height = Math.Max(34, preferred.Height + 4);

            var card = new Panel
            {
                Width = width,
                Height = label.Height + 16,
                BackColor = assistant ? CadPanel : CadPanelHigh,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(8),
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(assistant ? CadBorder : CadCyan))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            card.Controls.Add(label);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private void ResizeChatCards()
        {
            if (_chatList == null) return;
            var width = Math.Max(220, _chatList.ClientSize.Width - 28);
            foreach (Control card in _chatList.Controls)
            {
                card.Width = width;
                if (card.Controls.Count > 0)
                {
                    var label = card.Controls[0] as Label;
                    if (label != null)
                    {
                        label.Width = width - 20;
                        var preferred = label.GetPreferredSize(new Size(width - 20, 0));
                        label.Height = Math.Max(34, preferred.Height + 4);
                        card.Height = label.Height + 16;
                    }
                }
            }
        }

        private void BuildSettingsTab(TabPage tab)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 14,
                Padding = new Padding(10),
                AutoSize = false,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            for (int i = 1; i < 14; i++)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            }

            int row = 0;
            var settingsHeader = new Panel { Dock = DockStyle.Fill, BackColor = CadBg };
            settingsHeader.Controls.Add(new Label
            {
                Text = "模型配置",
                Left = 0,
                Top = 8,
                Width = 160,
                Height = 28,
                ForeColor = CadText,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            });
            settingsHeader.Controls.Add(new Label
            {
                Text = "v0.1.0",
                Width = 78,
                Height = 26,
                Top = 8,
                Left = 260,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = CadMuted,
                BackColor = CadPanelHigh,
                Font = MonoFont,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            });
            settingsHeader.Resize += (s, e) =>
            {
                var badge = settingsHeader.Controls[1];
                badge.Left = Math.Max(160, settingsHeader.Width - badge.Width - 4);
            };
            panel.SetColumnSpan(settingsHeader, 2);
            panel.Controls.Add(settingsHeader, 0, row++);

            panel.Controls.Add(new Label { Text = Strings.LblProfile, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var profileRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _cmbProfile = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProfile.SelectedIndexChanged += (s, e) => OnProfileChanged();
            _btnNewProfile = new Button { Text = Strings.BtnNewProfile, Width = 60 };
            _btnNewProfile.Click += (s, e) => OnNewProfile();
            _btnDeleteProfile = new Button { Text = Strings.BtnDeleteProfile, Width = 60 };
            _btnDeleteProfile.Click += (s, e) => OnDeleteProfile();
            profileRow.Controls.Add(_cmbProfile);
            profileRow.Controls.Add(_btnNewProfile);
            profileRow.Controls.Add(_btnDeleteProfile);
            panel.Controls.Add(profileRow, 1, row++);

            panel.Controls.Add(new Label { Text = "模型厂家", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbProvider = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProvider.Items.AddRange(new object[] { "openai", "deepseek", "anthropic", "gemini", "ollama", "custom" });
            _cmbProvider.SelectedIndexChanged += (s, e) => OnProviderChanged();
            panel.Controls.Add(_cmbProvider, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblApiBaseUrl, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtBaseUrl = new TextBox { Width = 320 };
            panel.Controls.Add(_txtBaseUrl, 1, row++);

            panel.Controls.Add(new Label { Text = "模型型号", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbModel = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDown };
            panel.Controls.Add(_cmbModel, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblApiKey, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var keyRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _txtApiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
            _chkShowKey = new CheckBox { Text = Strings.BtnShowKey, AutoSize = true };
            _chkShowKey.CheckedChanged += (s, e) => _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
            keyRow.Controls.Add(_txtApiKey);
            keyRow.Controls.Add(_chkShowKey);
            panel.Controls.Add(keyRow, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblAgentPort, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _numPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 8765, Width = 90 };
            panel.Controls.Add(_numPort, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblStrictJson, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _chkStrictJson = new CheckBox { Checked = true, AutoSize = true };
            panel.Controls.Add(_chkStrictJson, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblTimeoutSec, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _numTimeout = new NumericUpDown { Minimum = 5, Maximum = 600, Value = 30, Width = 90 };
            panel.Controls.Add(_numTimeout, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblAutoRun, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _chkAutoRun = new CheckBox { Checked = false, AutoSize = true };
            panel.Controls.Add(_chkAutoRun, 1, row++);

            var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnTestConnection = new Button { Text = Strings.BtnTestConnection, Width = 140, Height = 28 };
            _btnTestConnection.Click += async (s, e) => await OnTestConnectionAsync();
            _btnSaveSettings = new Button { Text = Strings.BtnSave, Width = 100, Height = 28 };
            _btnSaveSettings.Click += (s, e) => OnSaveSettings();
            actionRow.Controls.Add(_btnTestConnection);
            actionRow.Controls.Add(_btnSaveSettings);
            panel.Controls.Add(actionRow, 1, row++);

            _lblSettingsStatus = new Label
            {
                Text = "系统完整性：等待测试",
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = CadMuted,
                Font = UiFontSmall,
            };
            panel.SetColumnSpan(_lblSettingsStatus, 2);
            panel.Controls.Add(_lblSettingsStatus, 0, row++);

            var notice = new Label
            {
                Text = Strings.PrivacyNotice,
                ForeColor = Color.DimGray,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 0),
            };
            panel.SetColumnSpan(notice, 2);
            panel.Controls.Add(notice, 0, row++);

            tab.Controls.Add(panel);
            ApplyIndustrialStyle(tab);
            StyleGhostButton(_btnNewProfile);
            StyleGhostButton(_btnDeleteProfile);
            StyleGhostButton(_btnTestConnection);
            StylePrimaryButton(_btnSaveSettings);
            notice.ForeColor = CadMuted;
        }

        private void BuildUsageTab(TabPage tab)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12),
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "本次会话用量",
                Dock = DockStyle.Fill,
                ForeColor = CadText,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            };
            panel.Controls.Add(title, 0, 0);

            panel.Controls.Add(UsageCard("请求统计", out _lblUsageRequests, out _lblUsageSuccess), 0, 1);
            panel.Controls.Add(UsageCard("失败与耗时", out _lblUsageFailed, out _lblUsageAvg), 0, 2);
            panel.Controls.Add(UsageCard("当前模型", out _lblUsageProvider, out _lblUsageModel), 0, 3);

            var barCard = new Panel { Dock = DockStyle.Fill, BackColor = CadPanel, Padding = new Padding(10) };
            barCard.Paint += (s, e) =>
            {
                var y = barCard.Height / 2;
                using (var bg = new Pen(CadPanelHigh, 3))
                using (var fg = new Pen(CadGreen, 3))
                {
                    e.Graphics.DrawLine(bg, 12, y, barCard.Width - 12, y);
                    var ratio = _usageRequests == 0 ? 0 : Math.Min(1.0, _usageSuccess / (double)Math.Max(1, _usageRequests));
                    e.Graphics.DrawLine(fg, 12, y, 12 + (int)((barCard.Width - 24) * ratio), y);
                }
            };
            panel.Controls.Add(barCard, 0, 4);

            tab.Controls.Add(panel);
            ApplyIndustrialStyle(tab);
            RefreshUsage();
        }

        private Control UsageCard(string title, out Label left, out Label right)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = CadPanel, Padding = new Padding(10) };
            panel.Paint += (s, e) =>
            {
                using (var pen = new Pen(CadBorder))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
                }
            };
            panel.Controls.Add(new Label
            {
                Text = title,
                Left = 10,
                Top = 8,
                Width = 220,
                Height = 20,
                ForeColor = CadMuted,
                Font = UiFontBold,
            });
            left = new Label
            {
                Left = 10,
                Top = 38,
                Width = 150,
                Height = 28,
                ForeColor = CadCyan,
                Font = new Font("Consolas", 14F, FontStyle.Bold),
            };
            right = new Label
            {
                Left = 170,
                Top = 38,
                Width = 180,
                Height = 28,
                ForeColor = CadText,
                Font = new Font("Consolas", 12F, FontStyle.Bold),
            };
            panel.Controls.Add(left);
            panel.Controls.Add(right);
            return panel;
        }

        private void RefreshUsage()
        {
            if (_lblUsageRequests == null) return;
            _lblUsageRequests.Text = _usageRequests.ToString();
            _lblUsageSuccess.Text = "成功 " + _usageSuccess;
            _lblUsageFailed.Text = _usageFailed.ToString();
            _lblUsageAvg.Text = _usageRequests == 0 ? "平均 0 ms" : "平均 " + (_usageTotalMs / Math.Max(1, _usageRequests)) + " ms";
            var active = AgentConfigStore.LoadActive();
            _lblUsageProvider.Text = string.IsNullOrEmpty(active.Provider) ? "openai" : active.Provider;
            _lblUsageModel.Text = string.IsNullOrEmpty(active.Model) ? "未设置" : active.Model;
        }

        private void OnDrawTab(object sender, DrawItemEventArgs e)
        {
            var selected = e.Index == _tabs.SelectedIndex;
            var rect = e.Bounds;
            using (var bg = new SolidBrush(selected ? CadPanelHigh : CadBg))
            using (var border = new Pen(selected ? CadCyan : CadBorder))
            {
                e.Graphics.FillRectangle(bg, rect);
                e.Graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                var caption = _tabs.TabPages[e.Index].Text;
                TextRenderer.DrawText(
                    e.Graphics,
                    caption,
                    UiFontBold,
                    rect,
                    selected ? CadCyan : CadMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void ResizeTabItems()
        {
            if (_tabs == null || _tabs.TabCount == 0 || _tabs.Width <= 0) return;
            var width = Math.Max(120, (_tabs.Width - 6) / _tabs.TabCount);
            if (_tabs.ItemSize.Width != width)
            {
                _tabs.ItemSize = new Size(width, 42);
            }
        }

        private static void ApplyIndustrialStyle(Control root)
        {
            foreach (Control c in root.Controls)
            {
                c.Font = UiFont;
                c.ForeColor = CadText;

                if (c is TabPage || c is TableLayoutPanel || c is FlowLayoutPanel || c is Panel)
                {
                    c.BackColor = CadBg;
                }
                else if (c is GroupBox)
                {
                    c.BackColor = CadBg;
                    c.ForeColor = CadCyan;
                    c.Font = UiFontBold;
                    c.Padding = new Padding(8, 6, 8, 8);
                }
                else if (c is TextBox tb)
                {
                    tb.BackColor = CadInput;
                    tb.ForeColor = CadText;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.Font = tb.Multiline ? MonoFont : UiFont;
                }
                else if (c is ComboBox cb)
                {
                    cb.BackColor = CadInput;
                    cb.ForeColor = CadText;
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.Font = UiFont;
                }
                else if (c is NumericUpDown nud)
                {
                    nud.BackColor = CadInput;
                    nud.ForeColor = CadText;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    nud.Font = UiFont;
                }
                else if (c is CheckBox chk)
                {
                    chk.BackColor = CadBg;
                    chk.ForeColor = CadMuted;
                    chk.FlatStyle = FlatStyle.Flat;
                }
                else if (c is Label lbl)
                {
                    lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = CadMuted;
                    lbl.Font = UiFontSmall;
                }

                ApplyIndustrialStyle(c);
            }
        }

        private static void StylePrimaryButton(Button button)
        {
            if (button == null) return;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = CadCyan;
            button.ForeColor = Color.Black;
            button.Font = UiFontBold;
            button.FlatAppearance.BorderColor = CadCyan;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x00, 0xFB, 0xFB);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x00, 0xB8, 0xB8);
        }

        private static void StyleGhostButton(Button button)
        {
            if (button == null) return;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = CadBg;
            button.ForeColor = CadCyan;
            button.Font = UiFontBold;
            button.FlatAppearance.BorderColor = CadBorder;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = CadPanelHigh;
            button.FlatAppearance.MouseDownBackColor = CadInput;
        }

        // --- Chat tab actions ---

        private async Task OnUseAgentAsync()
        {
            var text = _txtNaturalLanguage.Text;
            if (text == "询问 CAD 助手...") text = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                SetChatStatus("请输入要绘制或修改的内容。", CadOrange);
                return;
            }

            try
            {
                _btnUseAgent.Enabled = false;
                _txtNaturalLanguage.Text = "";
                AddUserCard(text);
                SetChatStatus("正在生成 CAD 命令...", CadCyan);
                System.Windows.Forms.Application.DoEvents();

                var sw = Stopwatch.StartNew();
                var settings = AgentConfigStore.LoadActive();
                var client = new AgentLiteClient(settings);
                var dsl = await client.ParseAsync(text);

                if (dsl == null)
                {
                    _usageRequests++;
                    _usageFailed++;
                    AddAssistantCard("CAD 核心引擎", "模型没有返回可执行命令。");
                    SetChatStatus("模型未返回 DSL。", CadOrange);
                    RefreshUsage();
                    return;
                }

                SetChatStatus("正在执行几何命令...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                var result = DslExecutor.Execute(dsl);
                sw.Stop();

                _usageRequests++;
                _usageTotalMs += sw.ElapsedMilliseconds;
                if (result.Success) _usageSuccess++; else _usageFailed++;
                RefreshUsage();

                var message = result.Success
                    ? "已完成。执行 " + result.Summary.Succeeded + " 条命令，生成 " + CountEntities(result) + " 个对象。"
                    : "执行失败。失败命令 " + result.Summary.Failed + " 条；" + FirstError(result);
                AddAssistantCard("CAD 核心引擎", message);
                SetChatStatus(result.Success ? "完成，用时 " + sw.ElapsedMilliseconds + " ms。" : "执行失败。", result.Success ? CadGreen : CadOrange);
            }
            catch (System.Exception ex)
            {
                _usageRequests++;
                _usageFailed++;
                RefreshUsage();
                var msg = SecretRedactor.Redact(ex.Message);
                AddAssistantCard("CAD 核心引擎", "连接或执行失败：" + msg);
                SetChatStatus("失败：" + msg, CadOrange);
            }
            finally
            {
                _btnUseAgent.Enabled = true;
            }
        }

        private static int CountEntities(VcadResult result)
        {
            var count = 0;
            foreach (var r in result.Results)
            {
                if (r.Entities != null) count += r.Entities.Count;
            }
            return count;
        }

        private static string FirstError(VcadResult result)
        {
            if (result.Errors != null && result.Errors.Count > 0)
            {
                return result.Errors[0].Message;
            }
            return "请检查模型输出或图形环境。";
        }

        private void SetChatStatus(string message, Color color)
        {
            if (_lblChatStatus != null)
            {
                _lblChatStatus.Text = message;
                _lblChatStatus.ForeColor = color;
            }
            if (_lblStatus != null)
            {
                _lblStatus.Text = message;
            }
        }

        // --- Settings tab actions ---

        private static readonly Dictionary<string, (string BaseUrl, string Model)> ProviderDefaults =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai",    ("https://api.openai.com", "gpt-4o-mini") },
                { "deepseek",  ("https://api.deepseek.com", "deepseek-v4-flash") },
                { "anthropic", ("https://api.anthropic.com", "claude-3-5-haiku-latest") },
                { "gemini",    ("https://generativelanguage.googleapis.com", "gemini-1.5-flash") },
                { "ollama",    ("http://localhost:11434", "llama3.2") },
                { "custom",    ("", "") },
            };

        private static readonly Dictionary<string, string[]> ProviderModels =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1" } },
                { "deepseek", new[] { "deepseek-v4-flash", "deepseek-v4-pro" } },
                { "anthropic", new[] { "claude-3-5-haiku-latest", "claude-3-5-sonnet-latest", "claude-sonnet-4-5" } },
                { "gemini", new[] { "gemini-1.5-flash", "gemini-1.5-pro" } },
                { "ollama", new[] { "llama3.2", "qwen2.5", "deepseek-r1" } },
                { "custom", new string[0] },
            };

        private bool _suppressProviderChange;

        private void OnProviderChanged()
        {
            if (_suppressProviderChange) return;
            var provider = _cmbProvider.SelectedItem as string;
            if (string.IsNullOrEmpty(provider)) return;
            if (!ProviderDefaults.TryGetValue(provider, out var def)) return;

            // Always fill defaults when the user picks a new provider,
            // unless the field clearly holds a non-default value from
            // a different provider's default (which we'd want to replace).
            if (string.IsNullOrWhiteSpace(_txtBaseUrl.Text) || IsAnyKnownDefaultBaseUrl(_txtBaseUrl.Text))
            {
                _txtBaseUrl.Text = def.BaseUrl;
            }
            FillModelChoices(provider, def.Model);
            if (string.IsNullOrWhiteSpace(_cmbModel.Text) || IsAnyKnownDefaultModel(_cmbModel.Text))
            {
                _cmbModel.Text = def.Model;
            }
        }

        private static bool IsAnyKnownDefaultBaseUrl(string s)
        {
            foreach (var kv in ProviderDefaults) if (kv.Value.BaseUrl == s) return true;
            return false;
        }

        private static bool IsAnyKnownDefaultModel(string s)
        {
            foreach (var kv in ProviderDefaults) if (kv.Value.Model == s) return true;
            return false;
        }

        private void FillModelChoices(string provider, string preferredModel)
        {
            if (_cmbModel == null) return;
            var current = preferredModel ?? _cmbModel.Text;
            _cmbModel.Items.Clear();
            if (!string.IsNullOrEmpty(provider) && ProviderModels.TryGetValue(provider, out var models))
            {
                foreach (var model in models)
                {
                    _cmbModel.Items.Add(model);
                }
            }
            if (!string.IsNullOrWhiteSpace(current))
            {
                _cmbModel.Text = current;
            }
        }

        private void LoadProfilesIntoUi()
        {
            var store = AgentConfigStore.LoadAll();
            _cmbProfile.Items.Clear();
            foreach (var name in store.ProfileNames())
            {
                _cmbProfile.Items.Add(name);
            }
            if (_cmbProfile.Items.Count == 0)
            {
                store.UpsertProfile(new AgentSettings { Name = "default" });
                AgentConfigStore.SaveAll(store);
                _cmbProfile.Items.Add("default");
            }
            _cmbProfile.SelectedItem = store.ActiveProfileName ?? _cmbProfile.Items[0];
            ApplyProfileToUi(store.GetActiveOrFirst());
        }

        private void OnProfileChanged()
        {
            var store = AgentConfigStore.LoadAll();
            var name = _cmbProfile.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            store.ActiveProfileName = name;
            AgentConfigStore.SaveAll(store);
            ApplyProfileToUi(store.GetProfile(name));
        }

        private void OnNewProfile()
        {
            var name = Prompt(Strings.PromptProfileName, "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var store = AgentConfigStore.LoadAll();
            store.UpsertProfile(new AgentSettings { Name = name });
            store.ActiveProfileName = name;
            AgentConfigStore.SaveAll(store);
            LoadProfilesIntoUi();
        }

        private void OnDeleteProfile()
        {
            var store = AgentConfigStore.LoadAll();
            var name = _cmbProfile.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            store.RemoveProfile(name);
            AgentConfigStore.SaveAll(store);
            LoadProfilesIntoUi();
        }

        private void ApplyProfileToUi(AgentSettings s)
        {
            if (s == null) return;
            _suppressProviderChange = true;
            try
            {
                var provider = string.IsNullOrEmpty(s.Provider) ? "openai" : s.Provider;
                _cmbProvider.SelectedItem = provider;
                var hasDefault = ProviderDefaults.TryGetValue(provider, out var def);
                FillModelChoices(provider, null);

                _txtBaseUrl.Text = !string.IsNullOrEmpty(s.ApiBaseUrl)
                    ? s.ApiBaseUrl
                    : (hasDefault ? def.BaseUrl : "");
                _cmbModel.Text = !string.IsNullOrEmpty(s.Model)
                    ? s.Model
                    : (hasDefault ? def.Model : "");
                _txtApiKey.Text = s.ApiKeyPlain ?? "";
                _numPort.Value = s.AgentPort == 0 ? 8765 : s.AgentPort;
                _chkStrictJson.Checked = s.StrictJson;
                _numTimeout.Value = s.TimeoutSeconds == 0 ? 30 : s.TimeoutSeconds;
                _chkAutoRun.Checked = s.AutoRunAfterParse;
            }
            finally
            {
                _suppressProviderChange = false;
            }
        }

        private void OnSaveSettings()
        {
            try
            {
                var store = AgentConfigStore.LoadAll();
                var name = _cmbProfile.SelectedItem as string ?? "default";
                var s = store.GetProfile(name) ?? new AgentSettings { Name = name };
                s.Provider = _cmbProvider.SelectedItem as string ?? "openai";
                s.ApiBaseUrl = _txtBaseUrl.Text.Trim();
                s.Model = _cmbModel.Text.Trim();
                s.ApiKeyPlain = _txtApiKey.Text;
                s.AgentPort = (int)_numPort.Value;
                s.StrictJson = _chkStrictJson.Checked;
                s.TimeoutSeconds = (int)_numTimeout.Value;
                s.AutoRunAfterParse = _chkAutoRun.Checked;
                store.UpsertProfile(s);
                store.ActiveProfileName = name;
                AgentConfigStore.SaveAll(store);
                SetSettingsStatus("配置已保存到 " + AgentConfigStore.ConfigPath, CadGreen);
                RefreshUsage();
            }
            catch (System.Exception ex)
            {
                SetSettingsStatus("保存配置失败: " + SecretRedactor.Redact(ex.Message), CadOrange);
            }
        }

        private async Task OnTestConnectionAsync()
        {
            try
            {
                _btnTestConnection.Enabled = false;
                SetSettingsStatus("正在测试 Agent Lite 和模型...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                OnSaveSettings();
                var settings = AgentConfigStore.LoadActive();
                var client = new AgentLiteClient(settings);
                var result = await client.TestModelAsync();
                SetSettingsStatus(result.Message, result.Success ? CadGreen : CadOrange);
            }
            catch (System.Exception ex)
            {
                SetSettingsStatus("测试失败: " + SecretRedactor.Redact(ex.Message), CadOrange);
            }
            finally
            {
                _btnTestConnection.Enabled = true;
            }
        }

        private void SetSettingsStatus(string message, Color color)
        {
            if (_lblSettingsStatus != null)
            {
                _lblSettingsStatus.Text = message;
                _lblSettingsStatus.ForeColor = color;
            }
            if (_lblStatus != null)
            {
                _lblStatus.Text = message;
            }
        }

        private static string Prompt(string title, string defaultValue)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ClientSize = new Size(320, 90);

                var tb = new TextBox { Left = 10, Top = 14, Width = 300, Text = defaultValue };
                var ok = new Button { Text = Strings.BtnOk, DialogResult = DialogResult.OK, Left = 150, Top = 50, Width = 70 };
                var cancel = new Button { Text = Strings.BtnCancel, DialogResult = DialogResult.Cancel, Left = 230, Top = 50, Width = 70 };
                f.Controls.AddRange(new Control[] { tb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }
    }

    internal static class SampleDsl
    {
        public const string RectangleAndText = @"{
  ""version"": ""vcad_dsl_v1"",
  ""unit"": ""mm"",
  ""coordinate_system"": ""WCS"",
  ""commands"": [
    {
      ""type"": ""create_layer"",
      ""id"": ""LAYER-A-WALL"",
      ""name"": ""A-WALL"",
      ""color"": 7
    },
    {
      ""type"": ""create_layer"",
      ""id"": ""LAYER-T-TEXT"",
      ""name"": ""T-TEXT"",
      ""color"": 2
    },
    {
      ""type"": ""draw_rectangle"",
      ""id"": ""RECT-001"",
      ""origin"": [0, 0],
      ""width"": 6000,
      ""height"": 4000,
      ""rotation"": 0,
      ""layer"": ""A-WALL""
    },
    {
      ""type"": ""draw_text"",
      ""id"": ""TEXT-001"",
      ""text"": ""VCAD DEMO"",
      ""position"": [1000, 500],
      ""height"": 250,
      ""rotation"": 0,
      ""alignment"": ""left"",
      ""text_style"": ""STANDARD"",
      ""layer"": ""T-TEXT""
    }
  ]
}";
    }
}
