using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
using Vcad.Plugin.Config;
using Vcad.Plugin.Context;
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
        private ComboBox _cmbExecutionMode;
        private CheckBox _chkMemoryEnabled;
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
        private Label _lblUsageTokens;
        private Label _lblUsageCost;
        private Label _lblUsageRecent;
        private Label _lblStatus;

        private int _chatRequests;

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
            AddAssistantCard("当前可调用工具",
                "CAD 执行工具：create_layer、draw_line、draw_rectangle、draw_text。\r\n" +
                "上下文工具：读取当前 DWG 图层/图元/块内展开图元；读取 PDF 可复制文字；图片可发给支持视觉的模型。\r\n" +
                "扩展工具：AgentLite 已提供受限 web.search、web.fetch_url、workspace.read_file、workspace.write_file；写文件和执行 CAD 都会受授权模式约束。\r\n" +
                "安全边界：对话回复、状态说明、PDF 读取失败提示只显示在面板里，不会再写入图纸。");
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
                RowCount = 16,
                Padding = new Padding(10),
                AutoSize = false,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            for (int i = 1; i < 16; i++)
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

            panel.Controls.Add(new Label { Text = "执行模式", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbExecutionMode = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbExecutionMode.Items.AddRange(new object[] { "确认后执行", "完全授权自动执行" });
            _cmbExecutionMode.SelectedIndex = 0;
            panel.Controls.Add(_cmbExecutionMode, 1, row++);

            panel.Controls.Add(new Label { Text = "学习记忆", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _chkMemoryEnabled = new CheckBox { Text = "记录并遵循用户偏好规则", Checked = true, AutoSize = true };
            panel.Controls.Add(_chkMemoryEnabled, 1, row++);

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
                RowCount = 8,
                Padding = new Padding(12),
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
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
            panel.Controls.Add(UsageCard("Token 明细", out _lblUsageTokens, out _lblUsageCost), 0, 4);

            var recentCard = UsageTextCard("最近模型请求", out _lblUsageRecent);
            panel.Controls.Add(recentCard, 0, 6);

            var barCard = new Panel { Dock = DockStyle.Fill, BackColor = CadPanel, Padding = new Padding(10) };
            barCard.Paint += (s, e) =>
            {
                var y = barCard.Height / 2;
                using (var bg = new Pen(CadPanelHigh, 3))
                using (var fg = new Pen(CadGreen, 3))
                {
                    e.Graphics.DrawLine(bg, 12, y, barCard.Width - 12, y);
                    var today = UsageLedgerStore.LoadTodaySummary();
                    var ratio = today.Requests == 0 ? 0 : Math.Min(1.0, today.Success / (double)Math.Max(1, today.Requests));
                    e.Graphics.DrawLine(fg, 12, y, 12 + (int)((barCard.Width - 24) * ratio), y);
                }
            };
            panel.Controls.Add(barCard, 0, 5);

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

        private Control UsageTextCard(string title, out Label body)
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
            var bodyLabel = new Label
            {
                Left = 10,
                Top = 34,
                Width = 360,
                Height = 102,
                ForeColor = CadText,
                Font = MonoFont,
                AutoSize = false,
            };
            panel.Resize += (s, e) =>
            {
                bodyLabel.Width = Math.Max(120, panel.Width - 20);
                bodyLabel.Height = Math.Max(70, panel.Height - 42);
            };
            panel.Controls.Add(bodyLabel);
            body = bodyLabel;
            return panel;
        }

        private void RefreshUsage()
        {
            var today = UsageLedgerStore.LoadTodaySummary();
            if (_lblChatRequests != null)
            {
                _lblChatRequests.Text = _chatRequests.ToString();
            }
            if (_lblChatCost != null)
            {
                _lblChatCost.Text = FormatUsd(today.CostUsd);
            }

            if (_lblUsageRequests == null) return;
            _lblUsageRequests.Text = today.Requests.ToString();
            _lblUsageSuccess.Text = "成功 " + today.Success;
            _lblUsageFailed.Text = today.Failed.ToString();
            _lblUsageAvg.Text = today.Requests == 0 ? "平均 0 ms" : "平均 " + (today.TotalMs / Math.Max(1, today.Requests)) + " ms";
            var active = AgentConfigStore.LoadActive();
            _lblUsageProvider.Text = string.IsNullOrEmpty(active.Provider) ? "openai" : active.Provider;
            _lblUsageModel.Text = string.IsNullOrEmpty(active.Model) ? "未设置" : active.Model;
            if (_lblUsageTokens != null)
            {
                _lblUsageTokens.Text = "in " + today.InputTokens + " / out " + today.OutputTokens;
            }
            if (_lblUsageCost != null)
            {
                _lblUsageCost.Text = "total " + today.TotalTokens + "\r\n" + FormatUsd(today.CostUsd);
            }
            if (_lblUsageRecent != null)
            {
                _lblUsageRecent.Text = FormatRecentUsage(today);
            }
        }

        private static string FormatUsd(decimal value)
        {
            if (value <= 0m) return "$0.00";
            return value < 0.01m ? "$" + value.ToString("0.000000") : "$" + value.ToString("0.00");
        }

        private static string FormatRecentUsage(UsageSummary summary)
        {
            if (summary == null || summary.Recent == null || summary.Recent.Count == 0)
            {
                return "暂无真实模型 token 记录";
            }
            var lines = new List<string>();
            foreach (var r in summary.Recent.Take(5))
            {
                lines.Add(r.TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + " " +
                          r.Provider + "/" + r.Model + " " +
                          r.InputTokens + "+" + r.OutputTokens + "=" + r.TotalTokens +
                          " " + FormatUsd(r.CostUsd) +
                          " " + r.UsageSource);
            }
            return string.Join("\r\n", lines);
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
            var memoryContext = "";
            var settings = AgentConfigStore.LoadActive();
            if (settings.MemoryEnabled)
            {
                memoryContext = UserRuleMemoryStore.BuildPromptContext();
            }
            if (_attachedFiles.Count == 0 && string.IsNullOrWhiteSpace(memoryContext)) return text;

            var sb = new StringBuilder();
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine("请根据附件内容生成 CAD 操作。");
            }
            else
            {
                sb.AppendLine(text.Trim());
            }

            if (!string.IsNullOrWhiteSpace(memoryContext))
            {
                sb.AppendLine();
                sb.AppendLine(memoryContext);
            }

            if (_attachedFiles.Count > 0)
            {
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

        private bool TryHandleLocalConversation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (!LooksLikeCapabilityQuestion(s)) return false;

            _txtNaturalLanguage.Text = "";
            AddUserCard(s);
            _chatRequests++;
            RefreshUsage();
            AddAssistantCard("CAD 助手", BuildCapabilityReply(s));
            SetChatStatus("已在对话框回复，未修改图纸。", CadGreen);
            return true;
        }

        private static bool LooksLikeCapabilityQuestion(string text)
        {
            var s = text.ToLowerInvariant();
            var asksTools = s.Contains("工具") || s.Contains("tool") || s.Contains("能调用") || s.Contains("可以调用");
            var asksPdf = s.Contains("pdf") || s.Contains("附件") || s.Contains("文件");
            var asksSafety = s.Contains("不要") && (s.Contains("画字") || s.Contains("图纸") || s.Contains("回复"));
            var asksCapability = s.Contains("能做什么") || s.Contains("支持什么") || s.Contains("能看") || s.Contains("能读");
            return asksTools || asksPdf || asksSafety || asksCapability;
        }

        private static string BuildCapabilityReply(string text)
        {
            return "当前已接入的工具分三类：\r\n\r\n" +
                   "1. CAD 执行工具：create_layer、draw_line、draw_rectangle、draw_text。它们只能通过 CAD-IR、Safety、Preview/Confirm 后进入 AutoCAD。\r\n\r\n" +
                   "2. CAD 上下文工具：读取当前打开 DWG 的图层、图元、块引用，并在内存里展开块内图元给 Agent 理解。\r\n\r\n" +
                   "3. 附件上下文工具：PDF 会先抽取可复制文字；图片会内联给支持视觉的模型；文本、DXF、LSP、CSV、JSON 会抽取文本片段。扫描版 PDF 目前只能识别为“需要 OCR”，不会凭空读出内容。\r\n\r\n" +
                   "AgentLite 工具层：web.search、web.fetch_url、workspace.read_file、workspace.write_file 已有受限端点。读工具默认只读；写文件和 CAD 执行都受“确认后执行/完全授权自动执行”模式控制。\r\n\r\n" +
                   "对话回复、状态说明、PDF 读取失败提示只会显示在右侧对话框里，不会再作为 draw_text 写进图纸。";
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

            if (_attachedFiles.Count == 0 && TryHandleLocalConversation(text))
            {
                return;
            }

            try
            {
                var attachmentWarnings = new List<string>();
                var attachmentPayloads = BuildAttachmentPayloads(attachmentWarnings);
                var prompt = BuildPromptWithAttachments(text);
                var displayText = BuildDisplayTextWithAttachments(text);
                var settings = AgentConfigStore.LoadActive();
                NormalizeRuntimeSettings(settings);

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
                if (settings.MemoryEnabled && UserRuleMemoryStore.TryLearnFromUserText(text, out var learnedRule))
                {
                    AddAssistantCard("记忆", "已记住这条偏好规则：\r\n" + learnedRule);
                }
                if (attachmentWarnings.Count > 0)
                {
                    AddAssistantCard("附件处理", string.Join("\r\n", attachmentWarnings));
                }
                AddAssistantCard("CAD 助手", BuildDwgContextReply(cadState));
                SetChatStatus("正在理解意图...", CadCyan);
                System.Windows.Forms.Application.DoEvents();

                var startResult = await AgentLiteProcessManager.EnsureStartedAsync(settings).ConfigureAwait(true);
                if (!startResult.Success)
                {
                    RefreshUsage();
                    AddAssistantCard("CAD 助手", "我还不能处理这条请求，因为本地 Agent Lite 没有启动成功。\r\n\r\n" +
                        "没有修改当前图纸。请重新打开 VCAD 面板，或检查 `%APPDATA%\\VCAD\\logs` 后再点发送。\r\n\r\n" +
                        "技术原因：" + startResult.Message);
                    SetChatStatus(startResult.Message, CadOrange);
                    return;
                }

                var client = new AgentLiteClient(settings);
                AddAssistantCard("Agent 进度", "正在调用模型理解意图、读取上下文并选择工具；确认前不会修改当前图纸。");
                var sessionId = "session-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                await RunAgentToolLoopAsync(client, settings, sessionId, prompt, attachmentPayloads, cadState);
            }
            catch (System.Exception ex)
            {
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

        private async Task RunAgentToolLoopAsync(
            AgentLiteClient client,
            AgentSettings settings,
            string sessionId,
            string message,
            JArray attachments,
            JObject cadObservation)
        {
            var toolResults = new JArray();
            var currentMessage = message ?? "";
            var currentAttachments = attachments ?? new JArray();
            var currentObservation = cadObservation;
            var anyToolFailed = false;

            for (var turn = 1; turn <= 8; turn++)
            {
                SetChatStatus(turn == 1 ? "正在调用模型..." : "正在把工具结果交给模型继续判断...", CadCyan);
                var sw = Stopwatch.StartNew();
                var turnResult = await WaitForAgentTurnAsync(
                    client.AgentTurnAsync(sessionId, currentMessage, currentAttachments, currentObservation, toolResults))
                    .ConfigureAwait(true);
                sw.Stop();
                RecordModelUsage(turnResult, settings, true, sw.ElapsedMilliseconds);

                AddAgentTraceCards(turnResult.Trace);
                if (!string.IsNullOrWhiteSpace(turnResult.AssistantMessage))
                {
                    AddAssistantCard("CAD 助手", turnResult.AssistantMessage);
                }

                if (turnResult.NeedsClarification)
                {
                    AddClarificationCard(turnResult.Clarification, message);
                    SetChatStatus("需要补充信息，等待你选择。", CadOrange);
                    return;
                }

                if (turnResult.ToolCalls == null || turnResult.ToolCalls.Count == 0)
                {
                    SetChatStatus(anyToolFailed ? "已完成，但有工具失败。" : "已完成。", anyToolFailed ? CadOrange : CadGreen);
                    return;
                }

                toolResults = new JArray();
                currentMessage = "";
                currentAttachments = new JArray();
                foreach (var token in turnResult.ToolCalls)
                {
                    var call = token as JObject;
                    if (call == null) continue;
                    var result = await ExecuteToolCallAsync(client, settings, call).ConfigureAwait(true);
                    toolResults.Add(result);
                    if (result.Value<bool?>("success") != true)
                    {
                        anyToolFailed = true;
                    }
                }

                currentObservation = DrawingSnapshotCollector.CaptureActive();
            }

            AddAssistantCard("CAD 助手", "Agent 工具循环已达到 8 轮上限。当前不再继续自动执行，避免无限循环。");
            SetChatStatus("Agent 循环达到上限。", CadOrange);
        }

        private async Task<AgentTurnResult> WaitForAgentTurnAsync(Task<AgentTurnResult> turnTask)
        {
            var checkpoints = new[]
            {
                new { DelayMs = 15000, Message = "模型仍在理解意图和选择工具；确认前不会修改图纸。" },
                new { DelayMs = 30000, Message = "任务仍在进行。复杂图纸会携带 DWG 快照、附件摘要和工具结果一起推理。" },
                new { DelayMs = 45000, Message = "仍在等待模型返回下一步动作。长任务会分多轮观察和工具调用完成。" },
                new { DelayMs = 90000, Message = "这是一个长任务。当前仍在 Agent 决策阶段，没有自动绕过确认。" },
            };

            foreach (var checkpoint in checkpoints)
            {
                var completed = await Task.WhenAny(turnTask, Task.Delay(checkpoint.DelayMs)).ConfigureAwait(true);
                if (completed == turnTask)
                {
                    return await turnTask.ConfigureAwait(true);
                }
                AddAssistantCard("Agent 进度", checkpoint.Message);
                SetChatStatus("模型仍在处理...", CadOrange);
                System.Windows.Forms.Application.DoEvents();
            }

            return await turnTask.ConfigureAwait(true);
        }

        private void RecordModelUsage(AgentTurnResult result, AgentSettings settings, bool success, long elapsedMs)
        {
            try
            {
                if (result?.Usage == null) return;
                var requestId = string.IsNullOrWhiteSpace(result.SessionId)
                    ? "turn-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
                    : result.SessionId + "-" + DateTime.UtcNow.ToString("HHmmssfff");
                var record = UsageLedgerStore.FromAgentUsage(result.Usage, requestId, success, elapsedMs, settings);
                UsageLedgerStore.Append(record);
                RefreshUsage();
            }
            catch
            {
                // Usage accounting is informational and must not block the CAD flow.
            }
        }

        private void AddAgentTraceCards(JArray trace)
        {
            if (trace == null) return;
            foreach (var item in trace.OfType<JObject>())
            {
                var title = item.Value<string>("title");
                var summary = item.Value<string>("summary");
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(summary)) continue;
                AddAssistantCard(string.IsNullOrWhiteSpace(title) ? "Agent 步骤" : title, summary ?? "");
            }
        }

        private void AddClarificationCard(JObject clarification, string originalText)
        {
            if (_chatList == null || clarification == null)
            {
                AddAssistantCard("CAD 助手", "我需要你补充一个信息后再继续。");
                return;
            }

            var question = clarification.Value<string>("question");
            if (string.IsNullOrWhiteSpace(question)) question = "请补充缺失的信息后继续。";
            var options = clarification["options"] as JArray;
            if (options == null || options.Count == 0)
            {
                AddAssistantCard("CAD 助手", question);
                return;
            }

            var width = GetChatCardWidth();
            var card = new Panel
            {
                Width = width,
                BackColor = CadPanel,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(8),
                Tag = "clarification",
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(CadCyan))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            var label = new Label
            {
                Text = "需要确认意图\r\n" + question,
                Left = 10,
                Top = 8,
                Width = width - 20,
                ForeColor = CadText,
                Font = UiFontBold,
                AutoSize = false,
            };
            var preferred = label.GetPreferredSize(new Size(width - 20, 0));
            label.Height = Math.Max(48, preferred.Height + 4);
            card.Controls.Add(label);

            var top = label.Bottom + 8;
            foreach (var optionToken in options.Take(4))
            {
                var answer = optionToken.Value<string>();
                if (string.IsNullOrWhiteSpace(answer)) continue;
                var button = new Button
                {
                    Text = answer,
                    Left = 10,
                    Top = top,
                    Width = Math.Max(120, width - 20),
                    Height = 30,
                };
                StyleGhostButton(button);
                button.Click += async (s, e) =>
                {
                    foreach (Control child in card.Controls)
                    {
                        if (child is Button b) b.Enabled = false;
                    }
                    _txtNaturalLanguage.Text = (originalText ?? "").Trim() + "\r\n补充：" + answer;
                    AddAssistantCard("CAD 助手", "已收到补充信息：" + answer + "\r\n我会基于这个选择继续规划。");
                    await OnUseAgentAsync();
                };
                card.Controls.Add(button);
                top += 36;
            }

            card.Height = top + 8;
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
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
                return "我会先读取当前图纸上下文，再由 Agent 选择合适工具执行。";
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
            text += "。\r\n\r\n接下来我会把你的自然语言请求交给 Agent 处理。写图工具在确认模式下会先询问你，确认前不会修改图纸。";
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

        private static string BuildFriendlyFailureReply(string technicalMessage)
        {
            var msg = technicalMessage ?? "";
            var lower = msg.ToLowerInvariant();
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

        private async Task<JObject> ExecuteToolCallAsync(AgentLiteClient client, AgentSettings settings, JObject call)
        {
            var callId = call.Value<string>("id");
            if (string.IsNullOrWhiteSpace(callId)) callId = "call-" + DateTime.UtcNow.ToString("HHmmssfff");
            var name = call.Value<string>("name") ?? "";
            var args = call["args"] as JObject ?? new JObject();
            AddAssistantCard("工具调用", FormatToolCall(name, args));

            var writeTool = CadToolHost.IsWriteTool(name) || IsAgentLiteWriteTool(name);
            if (writeTool && !IsTrustedExecutionMode(settings))
            {
                var confirmed = await AddToolConfirmCard(callId, name, args).ConfigureAwait(true);
                if (!confirmed)
                {
                    AddAssistantCard("CAD 助手", "已取消这次工具调用，没有修改当前图纸。");
                    SetChatStatus("工具调用已取消。", CadMuted);
                    return new JObject
                    {
                        ["id"] = callId,
                        ["name"] = name,
                        ["success"] = false,
                        ["error"] = "User cancelled the tool call.",
                    };
                }
            }

            if (CadToolHost.IsCadTool(name))
            {
                SetChatStatus(CadToolHost.IsWriteTool(name) ? "正在执行 AutoCAD 工具..." : "正在读取 DWG 上下文...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                var result = CadToolHost.Execute(callId, name, args);
                AddAssistantCard(result.Success ? "工具结果" : "工具失败", FormatCadToolResult(result));
                SetChatStatus(result.Success ? "工具完成，用时 " + result.ElapsedMs + " ms。" : "工具失败：" + result.Error,
                    result.Success ? CadGreen : CadOrange);
                return result.ToAgentToolResult();
            }

            try
            {
                SetChatStatus("正在执行 AgentLite 工具...", CadCyan);
                System.Windows.Forms.Application.DoEvents();
                var sw = Stopwatch.StartNew();
                var remote = await client.RunToolAsync(name, args).ConfigureAwait(true);
                sw.Stop();
                var success = remote.Value<bool?>("success") ?? false;
                AddAssistantCard(success ? "工具结果" : "工具失败", FormatRemoteToolResult(name, remote, sw.ElapsedMilliseconds));
                SetChatStatus(success ? "工具完成，用时 " + sw.ElapsedMilliseconds + " ms。" : "工具失败。", success ? CadGreen : CadOrange);
                return new JObject
                {
                    ["id"] = callId,
                    ["name"] = name,
                    ["success"] = success,
                    ["result"] = remote,
                    ["error"] = success ? null : (remote.Value<string>("message") ?? remote.Value<string>("error")),
                };
            }
            catch (System.Exception ex)
            {
                var msg = SecretRedactor.Redact(ex.Message);
                AddAssistantCard("工具失败", msg);
                SetChatStatus("工具失败：" + msg, CadOrange);
                return new JObject
                {
                    ["id"] = callId,
                    ["name"] = name,
                    ["success"] = false,
                    ["error"] = msg,
                };
            }
        }

        private Task<bool> AddToolConfirmCard(string callId, string name, JObject args)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (_chatList == null)
            {
                tcs.SetResult(false);
                return tcs.Task;
            }

            var width = GetChatCardWidth();
            var card = new Panel
            {
                Width = width,
                BackColor = CadPanel,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(8),
                Tag = "confirm-tool",
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
                Text = "确认执行工具\r\n" + FormatToolCall(name, args),
                Left = 10,
                Top = 8,
                Width = width - 20,
                ForeColor = CadText,
                Font = UiFontBold,
                AutoSize = false,
            };
            var preferred = label.GetPreferredSize(new Size(width - 20, 0));
            label.Height = Math.Max(52, preferred.Height + 4);
            var buttonTop = label.Bottom + 10;

            var confirm = new Button { Text = "确认执行", Left = 10, Top = buttonTop, Width = 110, Height = 30 };
            var cancel = new Button { Text = "取消", Left = 130, Top = buttonTop, Width = 72, Height = 30 };
            StylePrimaryButton(confirm);
            StyleGhostButton(cancel);
            confirm.Click += (s, e) =>
            {
                confirm.Enabled = false;
                cancel.Enabled = false;
                AddAssistantCard("CAD 助手", "已确认工具调用：" + name);
                tcs.TrySetResult(true);
            };
            cancel.Click += (s, e) =>
            {
                confirm.Enabled = false;
                cancel.Enabled = false;
                tcs.TrySetResult(false);
            };

            card.Height = buttonTop + 40;
            card.Controls.Add(label);
            card.Controls.Add(confirm);
            card.Controls.Add(cancel);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
            return tcs.Task;
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

        private static bool IsAgentLiteWriteTool(string name)
        {
            return string.Equals(name, "workspace.write_file", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatToolCall(string name, JObject args)
        {
            var json = args == null ? "{}" : args.ToString(Formatting.None);
            return name + "\r\n" + TruncateForCard(json, 900);
        }

        private static string FormatCadToolResult(CadToolExecutionResult result)
        {
            if (result == null) return "无结果。";
            if (result.Data != null && result.Data["summary_text"] != null)
            {
                return result.Data.Value<string>("summary_text");
            }
            var status = result.Success ? "成功" : "失败";
            var text = status + "，用时 " + result.ElapsedMs + " ms。";
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                text += "\r\n原因：" + result.Error;
            }
            if (result.Data != null)
            {
                text += "\r\n" + TruncateForCard(result.Data.ToString(Formatting.Indented), 900);
            }
            return text;
        }

        private static string FormatRemoteToolResult(string name, JObject result, long elapsedMs)
        {
            var success = result.Value<bool?>("success") ?? false;
            return (success ? "成功" : "失败") + "，用时 " + elapsedMs + " ms。\r\n" +
                   name + "\r\n" + TruncateForCard(result.ToString(Formatting.Indented), 1200);
        }

        private static string TruncateForCard(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "\r\n...[已截断]";
        }

        // --- Settings tab actions ---

        private static readonly Dictionary<string, (string BaseUrl, string Model)> ProviderDefaults =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai",    ("https://api.openai.com", "gpt-5.5") },
                { "deepseek",  ("https://api.deepseek.com", "deepseek-v4-flash") },
                { "anthropic", ("https://api.anthropic.com", "claude-fable-5") },
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
            if (string.IsNullOrWhiteSpace(settings.ExecutionMode))
            {
                settings.ExecutionMode = settings.AutoRunAfterParse ? "trusted" : "confirm";
            }
            if (!ProviderDefaults.TryGetValue(provider, out var def)) return;
            if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
            {
                settings.ApiBaseUrl = def.BaseUrl;
            }
            settings.Model = NormalizeModelForProvider(provider, settings.Model);
        }

        private string SelectedExecutionMode()
        {
            var selected = _cmbExecutionMode == null ? null : _cmbExecutionMode.SelectedItem as string;
            return string.Equals(selected, "完全授权自动执行", StringComparison.OrdinalIgnoreCase)
                ? "trusted"
                : "confirm";
        }

        private static bool IsTrustedExecutionMode(AgentSettings settings)
        {
            if (settings == null) return false;
            return string.Equals(settings.ExecutionMode, "trusted", StringComparison.OrdinalIgnoreCase) ||
                settings.AutoRunAfterParse;
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
                if (_cmbExecutionMode != null)
                {
                    _cmbExecutionMode.SelectedItem = IsTrustedExecutionMode(s) ? "完全授权自动执行" : "确认后执行";
                }
                if (_chkMemoryEnabled != null)
                {
                    _chkMemoryEnabled.Checked = s.MemoryEnabled;
                }
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
                s.ExecutionMode = SelectedExecutionMode();
                s.AutoRunAfterParse = string.Equals(s.ExecutionMode, "trusted", StringComparison.OrdinalIgnoreCase);
                s.MemoryEnabled = _chkMemoryEnabled == null || _chkMemoryEnabled.Checked;
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

}
