using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
#if NETFRAMEWORK
using System.Globalization;
using System.Speech.Recognition;
#endif
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UglyToad.PdfPig;
using Vcad.Core.Results;
using Vcad.Plugin.Config;
using Vcad.Plugin.Context;
using Vcad.Plugin.Execution;
using Vcad.Plugin.Net;
using Vcad.Plugin.Pipeline;

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
        private Button _btnAttachFile;
        private Button _btnVoiceInput;
        private TextBox _txtNaturalLanguage;
        private Label _lblChatStatus;
        private Label _lblChatCost;
        private Label _lblChatRequests;
        private Label _lblChatConnection;
        private readonly List<string> _attachedFiles = new List<string>();
        private ToolTip _toolTip;
#if NETFRAMEWORK
        private SpeechRecognitionEngine _speechEngine;
        private bool _voiceListening;
#endif

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

        private int _chatRequests;
        private int _usageRequests;
        private int _usageSuccess;
        private int _usageFailed;
        private long _usageTotalMs;
        private CadPipelineCandidate _pendingCandidate;

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
        private const int MaxTextAttachmentChars = 20000;
        private const int MaxAttachmentCount = 12;
        private const long MaxInlineImageBytes = 4 * 1024 * 1024;
        private const long MaxInlineImageBytesTotal = 5 * 1024 * 1024;

        public SidebarControl()
        {
            Dock = DockStyle.Fill;
            BackColor = CadBg;
            ForeColor = CadText;
            Font = UiFont;
            BuildLayout();
            LoadProfilesIntoUi();
            StartAgentLiteOnOpen();
        }

        private void StartAgentLiteOnOpen()
        {
            Task.Run(async () =>
            {
                try
                {
                    var settings = AgentConfigStore.LoadActive();
                    NormalizeRuntimeSettings(settings);
                    var result = await AgentLiteProcessManager.EnsureStartedAsync(settings).ConfigureAwait(false);
                    PostToUi(() =>
                    {
                        if (result.Success)
                        {
                            SetChatStatus(result.Message, CadGreen);
                        }
                        else
                        {
                            SetChatStatus(result.Message, CadOrange);
                            SetSettingsStatus(result.Message, CadOrange);
                        }
                    });
                }
                catch (Exception ex)
                {
                    var message = "Agent Lite auto-start failed: " + SecretRedactor.Redact(ex.Message);
                    PostToUi(() =>
                    {
                        SetChatStatus(message, CadOrange);
                        SetSettingsStatus(message, CadOrange);
                    });
                }
            });
        }

        private void PostToUi(Action action)
        {
            if (action == null || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
#if NETFRAMEWORK
                StopVoiceInputSilently();
                if (_speechEngine != null)
                {
                    _speechEngine.Dispose();
                    _speechEngine = null;
                }
#endif
                if (_toolTip != null)
                {
                    _toolTip.Dispose();
                    _toolTip = null;
                }
            }
            base.Dispose(disposing);
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
                Text = "VoiceCAD 助手",
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
                Location = new Point(94, 12),
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
            _btnAttachFile = new Button
            {
                Text = "",
                Width = 36,
                Height = 40,
                Location = new Point(10, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            _btnAttachFile.Click += (s, e) => OnAttachFile();
            _btnAttachFile.Paint += (s, e) => DrawPaperclipIcon(e.Graphics, _btnAttachFile.ClientRectangle, _btnAttachFile.Enabled ? CadMuted : CadBorder);
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_btnAttachFile, "上传文件");
            _btnVoiceInput = new Button
            {
                Text = "",
                Width = 36,
                Height = 40,
                Location = new Point(50, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            _btnVoiceInput.Click += (s, e) => ToggleVoiceInput();
            _btnVoiceInput.Paint += (s, e) =>
            {
#if NETFRAMEWORK
                var color = _voiceListening ? CadOrange : (_btnVoiceInput.Enabled ? CadCyan : CadBorder);
#else
                var color = _btnVoiceInput.Enabled ? CadMuted : CadBorder;
#endif
                DrawMicrophoneIcon(e.Graphics, _btnVoiceInput.ClientRectangle, color);
            };
            _toolTip.SetToolTip(_btnVoiceInput, "语音输入");
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
                _btnAttachFile.Left = 10;
                _btnVoiceInput.Left = _btnAttachFile.Right + 4;
                _btnUseAgent.Left = inputPanel.Width - _btnUseAgent.Width - 10;
                _txtNaturalLanguage.Left = _btnVoiceInput.Right + 8;
                _txtNaturalLanguage.Width = Math.Max(96, _btnUseAgent.Left - _txtNaturalLanguage.Left - 10);
            };
            inputPanel.Controls.Add(_btnAttachFile);
            inputPanel.Controls.Add(_btnVoiceInput);
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
            StyleGhostButton(_btnAttachFile);
            StyleGhostButton(_btnVoiceInput);
            AddAssistantCard("VoiceCAD 核心引擎", "可以打字，也可以点麦克风说出你要画什么。");
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
            panel.Controls.Add(MetricLabel("今日费用", "$0.00", out _lblChatCost), 0, 0);
            panel.Controls.Add(MetricLabel("会话请求", "0", out _lblChatRequests), 1, 0);
            panel.Controls.Add(MetricLabel("状态", "接口已连接", out _lblChatConnection), 2, 0);
            return panel;
        }

        private Control MetricLabel(string title, string value, out Label valueLabel)
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
            valueLabel = new Label
            {
                Text = value,
                Left = 0,
                Top = 17,
                Width = 110,
                Height = 20,
                ForeColor = CadCyan,
                Font = UiFontBold,
            };
            panel.Controls.Add(valueLabel);
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
            var width = GetChatCardWidth();
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
            var width = GetChatCardWidth();
            foreach (Control card in _chatList.Controls)
            {
                card.Width = width;
                if (card.Controls.Count > 0)
                {
                    var label = card.Controls[0] as Label;
                    if (label != null)
                    {
                        label.Width = width - 20;
                        var isConfirmCard = card.Tag is string tag && tag.StartsWith("confirm", StringComparison.Ordinal);
                        if (!isConfirmCard)
                        {
                            var preferred = label.GetPreferredSize(new Size(width - 20, 0));
                            label.Height = Math.Max(34, preferred.Height + 4);
                            card.Height = label.Height + 16;
                        }
                    }
                }
            }
        }

        private int GetChatCardWidth()
        {
            if (_chatList == null) return 180;
            return Math.Max(180, _chatList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 24);
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
            _numTimeout = new NumericUpDown { Minimum = 5, Maximum = 1800, Value = 300, Width = 90 };
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
            if (_lblChatRequests != null)
            {
                _lblChatRequests.Text = _chatRequests.ToString();
            }
            if (_lblChatCost != null)
            {
                _lblChatCost.Text = "$0.00";
            }

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
                var color = selected ? CadCyan : CadMuted;
                var textSize = TextRenderer.MeasureText(
                    e.Graphics,
                    caption,
                    UiFontBold,
                    new Size(Math.Max(20, rect.Width - 24), rect.Height),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                var iconSize = 14;
                var textWidth = Math.Min(textSize.Width, Math.Max(20, rect.Width - iconSize - 14));
                var groupWidth = iconSize + 5 + textWidth;
                var iconX = rect.X + Math.Max(6, (rect.Width - groupWidth) / 2);
                var iconRect = new Rectangle(iconX, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);
                var textRect = new Rectangle(iconRect.Right + 5, rect.Y, rect.Right - iconRect.Right - 7, rect.Height);
                DrawTabIcon(e.Graphics, e.Index, iconRect, color);
                TextRenderer.DrawText(
                    e.Graphics,
                    caption,
                    UiFontBold,
                    textRect,
                    color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static void DrawTabIcon(Graphics g, int index, Rectangle rect, Color color)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.7F))
            using (var brush = new SolidBrush(color))
            {
                if (index == 0)
                {
                    var bubble = new Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 3, rect.Height - 5);
                    g.DrawRectangle(pen, bubble);
                    g.DrawLine(pen, bubble.Left + 3, bubble.Bottom, bubble.Left + 6, bubble.Bottom + 3);
                    g.DrawLine(pen, bubble.Left + 6, bubble.Bottom + 3, bubble.Left + 8, bubble.Bottom);
                    g.DrawLine(pen, bubble.Left + 4, bubble.Top + 4, bubble.Right - 4, bubble.Top + 4);
                }
                else if (index == 1)
                {
                    var x1 = rect.X + 3;
                    var x2 = rect.X + rect.Width / 2;
                    var x3 = rect.Right - 3;
                    g.DrawLine(pen, x1, rect.Top + 2, x1, rect.Bottom - 2);
                    g.DrawLine(pen, x2, rect.Top + 2, x2, rect.Bottom - 2);
                    g.DrawLine(pen, x3, rect.Top + 2, x3, rect.Bottom - 2);
                    g.FillEllipse(brush, x1 - 2, rect.Top + 8, 4, 4);
                    g.FillEllipse(brush, x2 - 2, rect.Top + 3, 4, 4);
                    g.FillEllipse(brush, x3 - 2, rect.Top + 10, 4, 4);
                }
                else
                {
                    g.DrawLine(pen, rect.Left + 1, rect.Bottom - 2, rect.Right - 1, rect.Bottom - 2);
                    g.DrawLine(pen, rect.Left + 1, rect.Top + 2, rect.Left + 1, rect.Bottom - 2);
                    g.FillRectangle(brush, rect.Left + 4, rect.Bottom - 6, 2, 4);
                    g.FillRectangle(brush, rect.Left + 8, rect.Bottom - 9, 2, 7);
                    g.FillRectangle(brush, rect.Left + 12, rect.Bottom - 12, 2, 10);
                }
            }
            g.SmoothingMode = oldMode;
        }

        private static void DrawPaperclipIcon(Graphics g, Rectangle rect, Color color)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.8F))
            {
                var x = rect.Left + rect.Width / 2 - 5;
                var y = rect.Top + 8;
                g.DrawArc(pen, x + 1, y, 12, 18, 180, 270);
                g.DrawArc(pen, x + 4, y + 4, 7, 11, 180, 270);
                g.DrawLine(pen, x + 4, y + 15, x + 10, y + 6);
            }
            g.SmoothingMode = oldMode;
        }

        private static void DrawMicrophoneIcon(Graphics g, Rectangle rect, Color color)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.8F))
            using (var brush = new SolidBrush(Color.FromArgb(48, color)))
            {
                var cx = rect.Left + rect.Width / 2;
                var top = rect.Top + 8;
                var mic = new Rectangle(cx - 5, top, 10, 17);
                g.FillPie(brush, mic, 180, 360);
                g.DrawArc(pen, mic, 180, 360);
                g.DrawLine(pen, mic.Left, top + 5, mic.Left, top + 12);
                g.DrawLine(pen, mic.Right, top + 5, mic.Right, top + 12);
                g.DrawArc(pen, cx - 10, top + 8, 20, 16, 20, 140);
                g.DrawLine(pen, cx, top + 24, cx, top + 29);
                g.DrawLine(pen, cx - 6, top + 29, cx + 6, top + 29);
            }
            g.SmoothingMode = oldMode;
        }

        private void ResizeTabItems()
        {
            if (_tabs == null || _tabs.TabCount == 0 || _tabs.Width <= 0) return;
            var width = Math.Max(72, (_tabs.Width - 6) / _tabs.TabCount);
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

        private void ToggleVoiceInput()
        {
#if NETFRAMEWORK
            try
            {
                if (_voiceListening)
                {
                    StopVoiceInput("语音输入已停止。");
                }
                else
                {
                    StartVoiceInput();
                }
            }
            catch (System.Exception ex)
            {
                StopVoiceInputSilently();
                var msg = SecretRedactor.Redact(ex.Message);
                SetChatStatus("语音输入不可用：" + msg, CadOrange);
            }
#else
            SetChatStatus("当前构建暂不支持本地语音输入。", CadOrange);
#endif
        }

#if NETFRAMEWORK
        private void StartVoiceInput()
        {
            if (_speechEngine == null)
            {
                _speechEngine = CreateSpeechEngine();
            }

            _speechEngine.RecognizeAsync(RecognizeMode.Multiple);
            _voiceListening = true;
            if (_btnVoiceInput != null) _btnVoiceInput.Invalidate();
            SetChatStatus("正在听写，说出要执行的 CAD 操作。", CadCyan);
        }

        private SpeechRecognitionEngine CreateSpeechEngine()
        {
            RecognizerInfo selected = null;
            var current = CultureInfo.CurrentUICulture;
            foreach (var info in SpeechRecognitionEngine.InstalledRecognizers())
            {
                if (selected == null)
                {
                    selected = info;
                }
                if (info.Culture.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase) ||
                    info.Culture.TwoLetterISOLanguageName.Equals(current.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = info;
                    break;
                }
                if (info.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                {
                    selected = info;
                }
            }

            if (selected == null)
            {
                throw new InvalidOperationException("Windows 未安装可用的语音识别器。");
            }

            var engine = new SpeechRecognitionEngine(selected);
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += OnSpeechRecognized;
            engine.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
            engine.RecognizeCompleted += OnSpeechRecognizeCompleted;
            return engine;
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result == null || string.IsNullOrWhiteSpace(e.Result.Text) || e.Result.Confidence < 0.22F)
            {
                return;
            }
            if (IsDisposed) return;
            BeginInvoke((Action)(() => AppendRecognizedText(e.Result.Text, e.Result.Confidence)));
        }

        private void OnSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() => SetChatStatus("没有听清，请再说一遍。", CadOrange)));
        }

        private void OnSpeechRecognizeCompleted(object sender, RecognizeCompletedEventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() =>
            {
                _voiceListening = false;
                if (_btnVoiceInput != null) _btnVoiceInput.Invalidate();
                if (e.Error != null)
                {
                    SetChatStatus("语音输入中断：" + SecretRedactor.Redact(e.Error.Message), CadOrange);
                }
            }));
        }

        private void AppendRecognizedText(string text, float confidence)
        {
            if (_txtNaturalLanguage.Text == "询问 CAD 助手...")
            {
                _txtNaturalLanguage.Text = "";
            }
            if (!string.IsNullOrWhiteSpace(_txtNaturalLanguage.Text))
            {
                _txtNaturalLanguage.AppendText(" ");
            }
            _txtNaturalLanguage.AppendText(text);
            SetChatStatus("识别到语音：" + text + " (" + confidence.ToString("0.00") + ")", CadCyan);
        }

        private void StopVoiceInput(string message)
        {
            StopVoiceInputSilently();
            SetChatStatus(message, CadMuted);
        }

        private void StopVoiceInputSilently()
        {
            try
            {
                if (_speechEngine != null && _voiceListening)
                {
                    _speechEngine.RecognizeAsyncCancel();
                }
            }
            catch
            {
                // Best-effort shutdown; speech recognizer errors should not affect CAD.
            }
            _voiceListening = false;
            if (_btnVoiceInput != null) _btnVoiceInput.Invalidate();
        }
#endif

        private void OnAttachFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择附件";
                dialog.Multiselect = true;
                dialog.Filter = "上下文文件|*.dwg;*.dxf;*.lsp;*.scr;*.txt;*.md;*.json;*.csv;*.xml;*.pdf;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|CAD 文件|*.dwg;*.dxf|图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|PDF|*.pdf|文本|*.txt;*.md;*.json;*.csv;*.xml;*.lsp;*.scr|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var skipped = 0;
                foreach (var fileName in dialog.FileNames)
                {
                    if (File.Exists(fileName) && !_attachedFiles.Contains(fileName))
                    {
                        if (_attachedFiles.Count < MaxAttachmentCount)
                        {
                            _attachedFiles.Add(fileName);
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                }

                if (_txtNaturalLanguage.Text == "询问 CAD 助手...")
                {
                    _txtNaturalLanguage.Text = "";
                }
                var status = "已添加附件：" + string.Join(", ", _attachedFiles.ConvertAll(Path.GetFileName));
                if (skipped > 0) status += "；已忽略超出 " + MaxAttachmentCount + " 个的附件。";
                SetChatStatus(status, skipped > 0 ? CadOrange : CadCyan);
            }
        }

        private string BuildPromptWithAttachments(string text)
        {
            if (_attachedFiles.Count == 0) return text;

            var sb = new StringBuilder();
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine("请根据附件内容生成 CAD 操作。");
            }
            else
            {
                sb.AppendLine(text.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("附件上下文（结构化内容会随请求发送）：");
            foreach (var path in _attachedFiles)
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                sb.Append("- ").Append(info.Name)
                    .Append(" | kind=").Append(GetAttachmentKind(info.Extension))
                    .Append(" | bytes=").Append(info.Length)
                    .AppendLine();
            }
            return sb.ToString();
        }

        private JArray BuildAttachmentPayloads(List<string> warnings)
        {
            var payloads = new JArray();
            var index = 1;
            long inlineImageBytesUsed = 0;
            foreach (var path in _attachedFiles)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;

                    var kind = GetAttachmentKind(info.Extension);
                    var attachment = new JObject
                    {
                        ["id"] = "att-" + index.ToString("00"),
                        ["name"] = info.Name,
                        ["kind"] = kind,
                        ["mime_type"] = GetMimeType(info.Extension),
                        ["size_bytes"] = info.Length,
                        ["sha256"] = ComputeSha256(info.FullName),
                    };

                    if (IsTextAttachment(info.Extension))
                    {
                        attachment["text_excerpt"] = ReadAttachmentExcerpt(info.FullName);
                        if (info.Length > 1024 * 1024)
                        {
                            attachment["note"] = "文本附件超过 1MB，仅发送前 " + MaxTextAttachmentChars + " 字符作为上下文。";
                            warnings.Add(info.Name + " 超过 1MB，已只发送文本片段。");
                        }
                    }
                    else if (IsImageAttachment(info.Extension))
                    {
                        if (info.Length <= MaxInlineImageBytes &&
                            inlineImageBytesUsed + info.Length <= MaxInlineImageBytesTotal)
                        {
                            attachment["data_base64"] = Convert.ToBase64String(File.ReadAllBytes(info.FullName));
                            attachment["note"] = "图片将发送给支持视觉输入的模型；文本模型只能看到附件元数据。";
                            inlineImageBytesUsed += info.Length;
                        }
                        else
                        {
                            attachment["note"] = "图片超过单文件 4MB 或本次图片总内联预算，未内联发送；请压缩或后续接入文件上传/视觉预处理。";
                            warnings.Add(info.Name + " 未内联发送，本次只发送图片元数据。");
                        }
                    }
                    else if (IsPdfAttachment(info.Extension))
                    {
                        int pageCount;
                        bool truncated;
                        string pdfError;
                        var pdfText = ReadPdfTextExcerpt(info.FullName, out pageCount, out truncated, out pdfError);
                        attachment["page_count"] = pageCount;
                        if (!string.IsNullOrWhiteSpace(pdfText))
                        {
                            attachment["text_excerpt"] = pdfText;
                            attachment["note"] = truncated
                                ? "PDF 已抽取可复制文字；内容超过上限，仅发送前 " + MaxTextAttachmentChars + " 字符。"
                                : "PDF 已抽取可复制文字并作为上下文发送。";
                            if (truncated)
                            {
                                warnings.Add(info.Name + " PDF 正文较长，已只发送前 " + MaxTextAttachmentChars + " 字符。");
                            }
                        }
                        else
                        {
                            attachment["note"] = string.IsNullOrWhiteSpace(pdfError)
                                ? "PDF 未抽取到可复制文字，可能是扫描件；需要 OCR 或模型文件输入后才能读取正文。"
                                : "PDF 文本抽取失败：" + pdfError;
                            warnings.Add(info.Name + " 未抽取到 PDF 正文，本次只发送元数据。");
                        }
                    }
                    else
                    {
                        attachment["note"] = "二进制或未知格式附件，本次只发送元数据。";
                    }

                    payloads.Add(attachment);
                    index++;
                }
                catch (System.Exception ex)
                {
                    warnings.Add(Path.GetFileName(path) + " 附件处理失败：" + SecretRedactor.Redact(ex.Message));
                }
            }
            return payloads;
        }

        private string BuildDisplayTextWithAttachments(string text)
        {
            if (_attachedFiles.Count == 0) return text;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text.Trim());
            }
            sb.AppendLine("附件：");
            foreach (var path in _attachedFiles)
            {
                sb.Append("- ").Append(Path.GetFileName(path)).AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static bool IsTextAttachment(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".txt":
                case ".md":
                case ".json":
                case ".csv":
                case ".xml":
                case ".dxf":
                case ".lsp":
                case ".scr":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsImageAttachment(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".webp":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPdfAttachment(string extension)
        {
            return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetAttachmentKind(string extension)
        {
            if (IsTextAttachment(extension)) return "text";
            if (IsImageAttachment(extension)) return "image";
            if (IsPdfAttachment(extension)) return "pdf";
            if (string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase)) return "cad";
            return "binary";
        }

        private static string GetMimeType(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".txt":
                case ".md":
                case ".lsp":
                case ".scr":
                    return "text/plain";
                case ".json":
                    return "application/json";
                case ".csv":
                    return "text/csv";
                case ".xml":
                    return "application/xml";
                case ".dxf":
                    return "application/dxf";
                case ".dwg":
                    return "application/acad";
                case ".pdf":
                    return "application/pdf";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".webp":
                    return "image/webp";
                case ".bmp":
                    return "image/bmp";
                case ".tif":
                case ".tiff":
                    return "image/tiff";
                default:
                    return "application/octet-stream";
            }
        }

        private static string ReadAttachmentExcerpt(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var buffer = new char[MaxTextAttachmentChars + 1];
                    var read = reader.Read(buffer, 0, buffer.Length);
                    var content = new string(buffer, 0, Math.Min(read, MaxTextAttachmentChars));
                    return read <= MaxTextAttachmentChars
                        ? content
                        : content + "\r\n...[已截断]";
                }
            }
            catch (System.Exception ex)
            {
                return "[无法读取附件内容：" + SecretRedactor.Redact(ex.Message) + "]";
            }
        }

        private static string ReadPdfTextExcerpt(string path, out int pageCount, out bool truncated, out string error)
        {
            pageCount = 0;
            truncated = false;
            error = "";
            try
            {
                var sb = new StringBuilder();
                using (var document = PdfDocument.Open(path))
                {
                    pageCount = document.NumberOfPages;
                    var pageIndex = 0;
                    foreach (var page in document.GetPages())
                    {
                        pageIndex++;
                        var pageText = page.Text;
                        if (string.IsNullOrWhiteSpace(pageText)) continue;

                        var header = "[Page " + pageIndex + "]\r\n";
                        if (sb.Length + header.Length >= MaxTextAttachmentChars)
                        {
                            truncated = true;
                            break;
                        }
                        sb.Append(header);

                        var remaining = MaxTextAttachmentChars - sb.Length;
                        if (pageText.Length > remaining)
                        {
                            sb.Append(pageText.Substring(0, remaining));
                            truncated = true;
                            break;
                        }
                        sb.AppendLine(pageText);
                    }
                }
                return sb.ToString().Trim();
            }
            catch (System.Exception ex)
            {
                error = SecretRedactor.Redact(ex.Message);
                return "";
            }
        }

        private static string ComputeSha256(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    var hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private async Task OnUseAgentAsync()
        {
            var text = _txtNaturalLanguage.Text;
            if (text == "询问 CAD 助手...") text = "";
            if (string.IsNullOrWhiteSpace(text) && _attachedFiles.Count == 0)
            {
                SetChatStatus("请输入要绘制或修改的内容。", CadOrange);
                return;
            }

            try
            {
                var attachmentWarnings = new List<string>();
                var attachmentPayloads = BuildAttachmentPayloads(attachmentWarnings);
                var prompt = BuildPromptWithAttachments(text);
                var displayText = BuildDisplayTextWithAttachments(text);

#if NETFRAMEWORK
                StopVoiceInputSilently();
#endif
                _btnUseAgent.Enabled = false;
                if (_btnAttachFile != null) _btnAttachFile.Enabled = false;
                if (_btnVoiceInput != null) _btnVoiceInput.Enabled = false;
                SetChatStatus("正在读取当前 DWG 内存状态...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                var cadState = DrawingSnapshotCollector.CaptureActive();
                _attachedFiles.Clear();

                _txtNaturalLanguage.Text = "";
                AddUserCard(displayText);
                _chatRequests++;
                RefreshUsage();
                if (attachmentWarnings.Count > 0)
                {
                    AddAssistantCard("附件处理", string.Join("\r\n", attachmentWarnings));
                }
                AddAssistantCard("CAD 助手", BuildDwgContextReply(cadState));
                SetChatStatus("正在理解意图...", CadCyan);
                System.Windows.Forms.Application.DoEvents();

                var settings = AgentConfigStore.LoadActive();
                NormalizeRuntimeSettings(settings);
                var startResult = await AgentLiteProcessManager.EnsureStartedAsync(settings).ConfigureAwait(true);
                if (!startResult.Success)
                {
                    _usageRequests++;
                    _usageFailed++;
                    RefreshUsage();
                    AddAssistantCard("CAD 助手", "我还不能处理这条请求，因为本地 Agent Lite 没有启动成功。\r\n\r\n" +
                        "没有修改当前图纸。请重新打开 VCAD 面板，或检查 `%APPDATA%\\VCAD\\logs` 后再点发送。\r\n\r\n" +
                        "技术原因：" + startResult.Message);
                    SetChatStatus(startResult.Message, CadOrange);
                    return;
                }

                var client = new AgentLiteClient(settings);
                AddAssistantCard("Agent 进度", "正在调用模型生成结构化 CAD 草案。复杂图形会先被拆成基础线段、矩形和文字；当前图纸还不会被修改。");
                var dsl = await WaitForModelDraftAsync(client.ParseAsync(prompt, attachmentPayloads, cadState));

                if (dsl == null)
                {
                    _usageRequests++;
                    _usageFailed++;
                    AddAssistantCard("CAD 助手", "我没有拿到可执行的 CAD 绘图步骤，所以没有修改当前图纸。\r\n\r\n" +
                        "你可以补充尺寸、位置、图层、对象风格，或者换一个支持稳定 JSON 输出的模型后重试。");
                    SetChatStatus("模型未返回 DSL。", CadOrange);
                    RefreshUsage();
                    return;
                }

                SetChatStatus("正在生成 Intent / Task Plan / CAD-IR...", CadCyan);
                AddAssistantCard("Agent 进度", "模型草案已返回。现在开始生成 Intent、Task Plan、CAD-IR，并执行安全检查。");
                _pendingCandidate = CadAgentPipeline.Interpret(prompt, dsl, cadState);
                AddAssistantCard("CAD 助手", BuildNaturalPreviewReply(_pendingCandidate));
                AddPipelineCards(_pendingCandidate);
                SetChatStatus(_pendingCandidate.Safety.IsAllowed
                    ? "Preview 已生成，等待确认后执行。"
                    : "没有生成可安全执行的计划。", _pendingCandidate.Safety.IsAllowed ? CadOrange : CadOrange);
            }
            catch (System.Exception ex)
            {
                _usageRequests++;
                _usageFailed++;
                RefreshUsage();
                var msg = SecretRedactor.Redact(ex.Message);
                AddAssistantCard("CAD 助手", BuildFriendlyFailureReply(msg));
                SetChatStatus("失败：" + msg, CadOrange);
            }
            finally
            {
                _btnUseAgent.Enabled = true;
                if (_btnAttachFile != null) _btnAttachFile.Enabled = true;
                if (_btnVoiceInput != null) _btnVoiceInput.Enabled = true;
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

        private async Task<string> WaitForModelDraftAsync(Task<string> parseTask)
        {
            var checkpoints = new[]
            {
                new { DelayMs = 15000, Message = "模型仍在生成 CAD 草案。复杂对象会被拆成当前执行器支持的基础图元；图纸仍未修改。" },
                new { DelayMs = 30000, Message = "还在等待模型返回结构化结果。你可以继续等待；如果经常超过 1 分钟，建议换更快模型或提高超时时间。" },
                new { DelayMs = 45000, Message = "任务还在进行中。Agent 会在模型返回后继续生成 Intent、Task Plan、CAD-IR 和安全预览。" },
                new { DelayMs = 90000, Message = "这是一个长任务。当前仍只是在规划阶段，没有对 AutoCAD 图纸做任何修改。" },
            };

            foreach (var checkpoint in checkpoints)
            {
                var completed = await Task.WhenAny(parseTask, Task.Delay(checkpoint.DelayMs)).ConfigureAwait(true);
                if (completed == parseTask)
                {
                    return await parseTask.ConfigureAwait(true);
                }
                AddAssistantCard("Agent 进度", checkpoint.Message);
                SetChatStatus("模型仍在生成 CAD 草案...", CadOrange);
                System.Windows.Forms.Application.DoEvents();
            }

            return await parseTask.ConfigureAwait(true);
        }

        private static string FirstError(VcadResult result)
        {
            if (result.Errors != null && result.Errors.Count > 0)
            {
                return result.Errors[0].Message;
            }
            return "请检查模型输出或图形环境。";
        }

        private static string ShortStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "就绪";
            var s = message.ToLowerInvariant();
            if (s.Contains("失败") || s.Contains("failed") || s.Contains("error")) return "失败";
            if (s.Contains("取消") || s.Contains("cancel")) return "已取消";
            if (s.Contains("完成") || s.Contains("success") || s.Contains("done")) return "已完成";
            if (s.Contains("preview") || s.Contains("预览") || s.Contains("确认")) return "待确认";
            if (s.Contains("连接") || s.Contains("connected") || s.Contains("online")) return "接口已连接";
            if (s.Contains("正在") || s.Contains("start") || s.Contains("处理")) return "处理中";
            return message.Length > 8 ? message.Substring(0, 8) : message;
        }

        private static string BuildDwgContextReply(JObject cadState)
        {
            if (cadState == null)
            {
                return "我会先读取当前图纸上下文，再把你的要求转换成可预览的 CAD 操作。";
            }

            var summary = cadState["summary"] as JObject;
            var warnings = cadState["warnings"] as JArray;
            var entityCount = summary?["entity_count"]?.Value<int>() ?? 0;
            var layerCount = summary?["layer_count"]?.Value<int>() ?? 0;
            var blockRefs = summary?["block_reference_count"]?.Value<int>() ?? 0;
            var exploded = summary?["exploded_entity_count"]?.Value<int>() ?? 0;
            var truncated = summary?["truncated"]?.Value<bool>() ?? false;

            var text = "我已读取当前 DWG 上下文：图层 " + layerCount +
                       " 个、图元 " + entityCount + " 个";
            if (blockRefs > 0 || exploded > 0)
            {
                text += "，其中块引用 " + blockRefs + " 个，块内展开图元 " + exploded + " 个";
            }
            text += "。\r\n\r\n接下来我会把你的自然语言请求转换成受控 CAD 操作，并先给出预览，确认前不会修改图纸。";
            if (truncated)
            {
                text += "\r\n\r\n注意：当前图纸较大，读取上下文时已截断。";
            }
            if (warnings != null && warnings.Count > 0)
            {
                text += "\r\n\r\n读取提示：" + string.Join("; ", warnings.Values<string>());
            }
            return text;
        }

        private static string BuildNaturalPreviewReply(CadPipelineCandidate candidate)
        {
            if (candidate == null)
            {
                return "我没有生成可预览的 CAD 操作。";
            }

            var impact = candidate.Preview?["impact"];
            var operation = candidate.Preview?["operation"];
            var action = operation?["action"]?.Value<string>() ?? candidate.TaskType;
            var count = impact?["matched_count"]?.Value<int>() ?? 0;
            var layer = impact?["layer"]?.Value<string>() ?? "0";
            var entityType = impact?["entity_type"]?.Value<string>() ?? "对象";

            if (candidate.Safety == null || !candidate.Safety.IsAllowed)
            {
                var blocks = candidate.Safety == null || candidate.Safety.Blocks.Count == 0
                    ? "没有生成满足安全检查的 CAD 命令。"
                    : string.Join("；", candidate.Safety.Blocks);
                return "我理解了你的请求，但这次没有生成可安全执行的 CAD 操作，所以没有修改当前图纸。\r\n\r\n原因：" +
                       blocks + "\r\n\r\n你可以把需求拆得更具体一些，例如说明尺寸、位置、图层、由哪些线段或文字组成。";
            }

            var reply = "我理解你要" + DescribeCadAction(action) + "。我已经生成了执行预览：预计影响 " +
                        count + " 个 " + entityType + "，目标图层是 " + layer + "。";
            if (candidate.RequiresSecondConfirmation)
            {
                reply += "\r\n\r\n这个操作风险较高，需要二次确认；确认前不会修改图纸。";
            }
            else if (candidate.RequiresConfirmation)
            {
                reply += "\r\n\r\n请先确认预览，确认后我再执行；确认前不会修改图纸。";
            }
            else
            {
                reply += "\r\n\r\n这是低风险操作，我会直接执行并返回结果。";
            }
            return reply;
        }

        private static string BuildFriendlyFailureReply(string technicalMessage)
        {
            var msg = technicalMessage ?? "";
            var lower = msg.ToLowerInvariant();
            if (lower.Contains("e_schema_invalid") || lower.Contains("missing or empty") || lower.Contains("commands"))
            {
                return "我理解了你的请求，但模型没有返回有效的 CAD 命令列表，所以没有修改当前图纸。\r\n\r\n" +
                       "这通常是因为请求太开放，或当前执行器还不能把复杂对象自动拆成线段、文字等基础图元。你可以补充尺寸、位置和构成方式后重试。\r\n\r\n" +
                       "技术原因：" + msg;
            }
            if (lower.Contains("assistant/ui reply") || lower.Contains("conversational replies"))
            {
                return "模型返回的是对话说明文字，而不是应该写入图纸的标注内容，所以我已阻止执行。\r\n\r\n" +
                       "对话回复只会显示在右侧面板里，不会再被当作 CAD 文字画到图纸上。\r\n\r\n技术原因：" + msg;
            }
            if (lower.Contains("task was canceled") || lower.Contains("已取消一个任务") || lower.Contains("timeout"))
            {
                return "这次模型请求超时或被取消了，所以没有修改当前图纸。\r\n\r\n可以重试一次，或在配置页把超时时间调大。\r\n\r\n技术原因：" + msg;
            }
            if (lower.Contains("403") || lower.Contains("model_not_found") || lower.Contains("does not have access"))
            {
                return "当前 API Key 或项目没有所选模型的调用权限，所以没有生成 CAD 计划。\r\n\r\n请在配置页换成该账号可用的模型，保存后再测试连接。\r\n\r\n技术原因：" + msg;
            }
            return "我这次没有完成请求，也没有修改当前图纸。\r\n\r\n技术原因：" + msg;
        }

        private static string DescribeCadAction(string action)
        {
            if (string.Equals(action, "copy_objects", StringComparison.OrdinalIgnoreCase)) return "绘制这些对象";
            if (string.Equals(action, "query_objects", StringComparison.OrdinalIgnoreCase)) return "查询图纸对象";
            if (string.Equals(action, "count_objects", StringComparison.OrdinalIgnoreCase)) return "统计图纸对象";
            if (string.Equals(action, "highlight_objects", StringComparison.OrdinalIgnoreCase)) return "高亮图纸对象";
            if (string.Equals(action, "set_property", StringComparison.OrdinalIgnoreCase)) return "修改对象属性";
            if (string.Equals(action, "move_objects", StringComparison.OrdinalIgnoreCase)) return "移动对象";
            if (string.Equals(action, "delete_objects", StringComparison.OrdinalIgnoreCase)) return "删除对象";
            return "执行 CAD 操作";
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
            if (_lblChatConnection != null)
            {
                _lblChatConnection.Text = ShortStatus(message);
                _lblChatConnection.ForeColor = color;
            }
        }

        private void AddPipelineCards(CadPipelineCandidate candidate)
        {
            AddAssistantCard("理解结果", CadAgentPipeline.FormatIntent(candidate));
            AddAssistantCard("执行计划", CadAgentPipeline.FormatPlan(candidate));
            AddAssistantCard("执行预览", CadAgentPipeline.FormatPreview(candidate));
            if (candidate.Safety.IsAllowed)
            {
                if (candidate.RequiresConfirmation)
                {
                    AddConfirmCard(candidate);
                }
                else
                {
                    AddLowRiskExecuteCard(candidate);
                }
            }
            else
            {
                AddAssistantCard("安全检查", "已阻止执行。\r\n" + string.Join("\r\n", candidate.Safety.Blocks));
            }
        }

        private void AddLowRiskExecuteCard(CadPipelineCandidate candidate)
        {
            if (_chatList == null) return;
            AddAssistantCard("CAD 助手", "这是低风险任务，不需要确认。我会直接执行并返回结果。");
            ExecuteConfirmedCandidate(candidate);
        }

        private void AddConfirmCard(CadPipelineCandidate candidate)
        {
            AddConfirmCard(candidate, false);
        }

        private void AddConfirmCard(CadPipelineCandidate candidate, bool secondStep)
        {
            if (_chatList == null) return;
            var width = GetChatCardWidth();
            var card = new Panel
            {
                Width = width,
                Height = secondStep ? 132 : 116,
                BackColor = CadPanel,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(8),
                Tag = secondStep ? "confirm-second" : "confirm",
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(CadOrange))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            var label = new Label
            {
                Text = secondStep
                    ? "Second Confirm Card\r\n高风险 CAD-IR 需要二次确认。确认 token 绑定 task_id、ir_hash 和过期时间。"
                    : (candidate.RequiresSecondConfirmation
                        ? "Confirm Card\r\n此任务被标记为 high risk。第一次确认只签发 confirm_token，不会改图。"
                        : "Confirm Card\r\n确认后签发 confirm_token，再由 AdapterCommand(mode=execute) 执行。"),
                Left = 10,
                Top = 8,
                Width = width - 20,
                Height = secondStep ? 64 : 48,
                ForeColor = CadText,
                Font = UiFontBold,
            };
            var preferred = label.GetPreferredSize(new Size(width - 20, 0));
            label.Height = Math.Max(secondStep ? 64 : 48, preferred.Height + 4);
            var buttonTop = label.Top + label.Height + 10;
            card.Height = buttonTop + 38;
            var cancelWidth = 72;
            var confirmWidth = Math.Max(96, Math.Min(secondStep ? 124 : 110, width - cancelWidth - 30));
            var confirm = new Button
            {
                Text = secondStep ? "二次确认执行" : (candidate.RequiresSecondConfirmation ? "第一次确认" : "确认执行"),
                Left = 10,
                Top = buttonTop,
                Width = confirmWidth,
                Height = 30,
            };
            var cancel = new Button
            {
                Text = "取消",
                Left = confirm.Left + confirm.Width + 10,
                Top = buttonTop,
                Width = cancelWidth,
                Height = 30,
            };
            StylePrimaryButton(confirm);
            StyleGhostButton(cancel);
            confirm.Click += (s, e) =>
            {
                confirm.Enabled = false;
                cancel.Enabled = false;
                if (candidate.RequiresSecondConfirmation && !secondStep)
                {
                    CadAgentPipeline.Confirm(candidate);
                    AddConfirmCard(candidate, true);
                    SetChatStatus("已完成第一次确认，等待二次确认。", CadOrange);
                }
                else
                {
                    if (secondStep)
                    {
                        CadAgentPipeline.SecondConfirm(candidate);
                    }
                    else
                    {
                        CadAgentPipeline.Confirm(candidate);
                    }
                    ExecuteConfirmedCandidate(candidate);
                }
            };
            cancel.Click += (s, e) =>
            {
                CadAgentPipeline.Cancel(candidate);
                confirm.Enabled = false;
                cancel.Enabled = false;
                AddAssistantCard("CAD 助手", "已取消这次任务，没有修改当前图纸。");
                SetChatStatus("任务已取消。", CadMuted);
            };
            card.Controls.Add(label);
            card.Controls.Add(confirm);
            card.Controls.Add(cancel);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private void ExecuteConfirmedCandidate(CadPipelineCandidate candidate)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                candidate.Confirmed = true;
                SetChatStatus("Adapter 正在转换 CAD-IR...", CadCyan);
                System.Windows.Forms.Application.DoEvents();

                var idempotencyKey = CadAgentPipeline.GetDefaultIdempotencyKey(candidate);
                var adapterCommand = CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", idempotencyKey);
                var dsl = adapterCommand.Value<string>("command");
                SetChatStatus("Executor 正在执行 Adapter 输出...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                var result = DslExecutor.Execute(dsl);
                sw.Stop();

                _usageRequests++;
                _usageTotalMs += sw.ElapsedMilliseconds;
                if (result.Success) _usageSuccess++; else _usageFailed++;
                RefreshUsage();

                var cadResult = CadAgentPipeline.RecordExecutionResult(candidate, result, sw.ElapsedMilliseconds, idempotencyKey);
                var message = result.Success
                    ? "已完成绘图，并写入当前图纸。\r\n\r\n命令：" + result.Summary.Succeeded +
                      "\r\n对象：" + CountEntities(result) +
                      "\r\n用时：" + sw.ElapsedMilliseconds + " ms"
                    : "执行失败，没有完成这次修改。\r\n\r\n失败命令：" + result.Summary.Failed +
                      "\r\n原因：" + FirstError(result);
                AddAssistantCard("CAD 助手", message);
                SetChatStatus(result.Success ? "完成，用时 " + sw.ElapsedMilliseconds + " ms。" : "执行失败。", result.Success ? CadGreen : CadOrange);
            }
            catch (System.Exception ex)
            {
                sw.Stop();
                _usageRequests++;
                _usageFailed++;
                _usageTotalMs += sw.ElapsedMilliseconds;
                RefreshUsage();
                var msg = SecretRedactor.Redact(ex.Message);
                AddAssistantCard("CAD 助手", BuildFriendlyFailureReply(msg));
                SetChatStatus("执行失败：" + msg, CadOrange);
            }
        }

        // --- Settings tab actions ---

        private static readonly Dictionary<string, (string BaseUrl, string Model)> ProviderDefaults =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai",    ("https://api.openai.com", "gpt-5") },
                { "deepseek",  ("https://api.deepseek.com", "deepseek-v4-flash") },
                { "anthropic", ("https://api.anthropic.com", "claude-sonnet-4-6") },
                { "gemini",    ("https://generativelanguage.googleapis.com", "gemini-3.5-flash") },
                { "ollama",    ("http://localhost:11434", "llama3.2") },
                { "custom",    ("", "") },
            };

        private static readonly Dictionary<string, string[]> ProviderModels =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", new[] { "gpt-5.5", "gpt-5.4", "gpt-5.4-mini", "gpt-5", "gpt-4.1", "gpt-4o" } },
                { "deepseek", new[] { "deepseek-v4-flash", "deepseek-v4-pro" } },
                { "anthropic", new[] { "claude-fable-5", "claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5" } },
                { "gemini", new[] { "gemini-3.5-flash", "gemini-3.1-pro-preview", "gemini-3-flash-preview", "gemini-3.1-flash-lite", "gemini-2.5-flash" } },
                { "ollama", new[] { "llama3.2", "qwen2.5", "deepseek-r1" } },
                { "custom", new string[0] },
            };

        private static readonly Dictionary<string, string[]> LegacyDefaultModels =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", new[] { "gpt-4o-mini" } },
                { "anthropic", new[] { "claude-3-5-haiku-latest", "claude-3-5-sonnet-latest" } },
                { "gemini", new[] { "gemini-1.5-flash", "gemini-1.5-pro" } },
                { "deepseek", new[] { "deepseek-chat", "deepseek-reasoner" } },
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
            foreach (var kv in LegacyDefaultModels)
            {
                foreach (var model in kv.Value)
                {
                    if (string.Equals(model, s, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static string NormalizeModelForProvider(string provider, string configuredModel)
        {
            if (!ProviderDefaults.TryGetValue(provider, out var def)) return configuredModel ?? "";
            if (string.IsNullOrWhiteSpace(configuredModel)) return def.Model;
            if (LegacyDefaultModels.TryGetValue(provider, out var legacyModels))
            {
                foreach (var model in legacyModels)
                {
                    if (string.Equals(model, configuredModel, StringComparison.OrdinalIgnoreCase))
                    {
                        return def.Model;
                    }
                }
            }
            return configuredModel;
        }

        private static void NormalizeRuntimeSettings(AgentSettings settings)
        {
            if (settings == null) return;
            var provider = string.IsNullOrWhiteSpace(settings.Provider) ? "openai" : settings.Provider;
            settings.Provider = provider;
            if (!ProviderDefaults.TryGetValue(provider, out var def)) return;
            if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
            {
                settings.ApiBaseUrl = def.BaseUrl;
            }
            settings.Model = NormalizeModelForProvider(provider, settings.Model);
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
                _cmbModel.Text = hasDefault
                    ? NormalizeModelForProvider(provider, s.Model)
                    : (s.Model ?? "");
                _txtApiKey.Text = s.ApiKeyPlain ?? "";
                _numPort.Value = s.AgentPort == 0 ? 8765 : s.AgentPort;
                _chkStrictJson.Checked = s.StrictJson;
                _numTimeout.Value = s.TimeoutSeconds <= 120 ? 300 : s.TimeoutSeconds;
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
                NormalizeRuntimeSettings(settings);
                var startResult = await AgentLiteProcessManager.EnsureStartedAsync(settings).ConfigureAwait(true);
                if (!startResult.Success)
                {
                    SetSettingsStatus(startResult.Message, CadOrange);
                    return;
                }
                if (startResult.Started)
                {
                    SetSettingsStatus(startResult.Message, CadGreen);
                    System.Windows.Forms.Application.DoEvents();
                }
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
