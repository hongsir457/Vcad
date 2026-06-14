using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
#if NETFRAMEWORK
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
        private Panel _headerPanel;
        private Panel _brandPanel;
        private Label _lblBrandTitle;
        private Label _lblHeaderLlmStatus;
        private Button _btnHeaderMenu;
        private ContextMenuStrip _headerMenu;
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
        private readonly List<ConversationMessageRecord> _conversationMessages = new List<ConversationMessageRecord>();
        private string _currentConversationId = ConversationHistoryStore.NewId();
        private DateTime _currentConversationStartedUtc = DateTime.UtcNow;
        private bool _suppressConversationRecording;
        private ToolTip _toolTip;
#if NETFRAMEWORK
        private SpeechRecognitionEngine _speechEngine;
        private bool _voiceListening;
        private string _speechRecognizerName;
#endif
        private bool _agentRunning;
        private readonly List<string> _pendingSupplements = new List<string>();
        private readonly object _pendingSupplementsLock = new object();

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
        private TextBox _txtUsageRecent;
        private Button _btnClearUsage;
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
        private const int MaxLiveStreamChars = 1800;

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
                SaveCurrentConversation();
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
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _rootLayout.Controls.Add(BuildHeader(), 0, 0);

            _tabs = new HeaderDrivenTabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                ItemSize = new Size(1, 1),
                SizeMode = TabSizeMode.Fixed,
            };

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
            _tabs.SelectedIndex = 0;

            _rootLayout.Controls.Add(_tabs, 0, 1);
            Controls.Add(_rootLayout);

            _collapsedStrip = BuildCollapsedStrip();
            _collapsedStrip.Visible = false;
            Controls.Add(_collapsedStrip);
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                Padding = new Padding(8, 3, 8, 3),
            };
            _headerPanel = header;

            _brandPanel = new Panel
            {
                Left = 8,
                Top = 3,
                Width = 92,
                Height = 24,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Visible = false,
            };
            var mark = new Label
            {
                Text = "VCAD",
                AutoSize = false,
                Width = 54,
                Height = 20,
                Left = 0,
                Top = 2,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = CadCyan,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
            };
            _lblBrandTitle = new Label
            {
                Text = "",
                AutoSize = false,
                ForeColor = CadCyan,
                Font = UiFontBold,
                Left = 58,
                Top = 4,
                Width = 0,
                Height = 20,
            };
            _brandPanel.Controls.Add(mark);
            _brandPanel.Controls.Add(_lblBrandTitle);

            _lblHeaderLlmStatus = new Label
            {
                Text = "● LLM 未测",
                AutoSize = true,
                ForeColor = CadMuted,
                Font = UiFontSmall,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(230, 8),
            };
            _headerMenu = BuildHeaderMenuStrip();
            _btnHeaderMenu = new Button
            {
                Text = "⚙",
                Width = 22,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                ForeColor = CadCyan,
                BackColor = CadBg,
                Font = new Font("Segoe UI Symbol", 10F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnHeaderMenu.FlatAppearance.BorderColor = CadBorder;
            _btnHeaderMenu.FlatAppearance.BorderSize = 1;
            _btnHeaderMenu.Click += (s, e) => _headerMenu.Show(_btnHeaderMenu, new Point(0, _btnHeaderMenu.Height + 2));
            _btnCollapse = new Button
            {
                Text = "‹",
                Width = 22,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                ForeColor = CadCyan,
                BackColor = CadBg,
                Font = UiFontBold,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnCollapse.FlatAppearance.BorderColor = CadBorder;
            _btnCollapse.FlatAppearance.BorderSize = 1;
            _btnCollapse.Click += (s, e) => SetCollapsed(!_collapsed);
            header.Resize += (s, e) => LayoutHeader();
            header.Controls.Add(_brandPanel);
            header.Controls.Add(_lblHeaderLlmStatus);
            header.Controls.Add(_btnHeaderMenu);
            header.Controls.Add(_btnCollapse);
            LayoutHeader();
            return header;
        }

        private void LayoutHeader()
        {
            if (_headerPanel == null || _lblHeaderLlmStatus == null || _btnCollapse == null || _btnHeaderMenu == null) return;
            _lblHeaderLlmStatus.Left = Math.Max(128, _headerPanel.Width - _lblHeaderLlmStatus.Width - 12);
            _lblHeaderLlmStatus.Top = 8;
            _btnHeaderMenu.Left = Math.Max(96, _lblHeaderLlmStatus.Left - _btnHeaderMenu.Width - 8);
            _btnHeaderMenu.Top = 4;
            _btnCollapse.Left = Math.Max(66, _btnHeaderMenu.Left - _btnCollapse.Width - 6);
            _btnCollapse.Top = 4;
            if (_brandPanel != null)
            {
                _brandPanel.Width = Math.Max(74, _btnCollapse.Left - _brandPanel.Left - 8);
            }
            if (_lblBrandTitle != null && _brandPanel != null)
            {
                _lblBrandTitle.Width = Math.Max(48, _brandPanel.Width - _lblBrandTitle.Left);
            }
        }

        private ContextMenuStrip BuildHeaderMenuStrip()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = CadPanel,
                ForeColor = CadText,
                Font = UiFont,
                ShowImageMargin = false,
            };
            menu.Items.Add(HeaderMenuItem("新建对话", -1));
            menu.Items.Add(HeaderMenuItem("清空对话", -2));
            menu.Items.Add(HeaderMenuItem("历史对话", -3));
            menu.Items.Add(HeaderMenuItem("导出调试会话", -4));
            menu.Items.Add(HeaderMenuItem("帮助 / 使用说明", -5));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(HeaderMenuItem("对话", 0));
            menu.Items.Add(HeaderMenuItem("配置", 1));
            menu.Items.Add(HeaderMenuItem("用量", 2));
            return menu;
        }

        private ToolStripMenuItem HeaderMenuItem(string text, int tabIndex)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => SelectMainTab(tabIndex);
            return item;
        }

        private void SelectMainTab(int tabIndex)
        {
            if (tabIndex == -1)
            {
                StartNewConversation(true);
                return;
            }
            if (tabIndex == -2)
            {
                ClearConversation(true);
                return;
            }
            if (tabIndex == -3)
            {
                ShowConversationHistoryDialog();
                return;
            }
            if (tabIndex == -4)
            {
                ExportCurrentConversationForDebug();
                return;
            }
            if (tabIndex == -5)
            {
                ShowHelpDialog();
                return;
            }
            if (_tabs == null || tabIndex < 0 || tabIndex >= _tabs.TabPages.Count) return;
            _tabs.SelectedIndex = tabIndex;
            if (tabIndex == 2) RefreshUsage();
        }

        private void StartNewConversation(bool showNotice)
        {
            SaveCurrentConversation();
            _conversationMessages.Clear();
            _attachedFiles.Clear();
            _currentConversationId = ConversationHistoryStore.NewId();
            _currentConversationStartedUtc = DateTime.UtcNow;
            _chatRequests = 0;
            if (_chatList != null)
            {
                _chatList.Controls.Clear();
                AddStartupCards();
            }
            if (_txtNaturalLanguage != null) _txtNaturalLanguage.Text = "询问 CAD 助手...";
            RefreshUsage();
            SelectMainTab(0);
            if (showNotice) SetChatStatus("已新建对话，上下文从空白开始。", CadGreen);
        }

        private void ClearConversation(bool confirm)
        {
            if (confirm)
            {
                var result = MessageBox.Show(
                    "清空当前对话内容和上下文？历史记录不会删除。",
                    "清空对话",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question);
                if (result != DialogResult.OK) return;
            }
            _conversationMessages.Clear();
            _attachedFiles.Clear();
            _currentConversationId = ConversationHistoryStore.NewId();
            _currentConversationStartedUtc = DateTime.UtcNow;
            _chatRequests = 0;
            if (_chatList != null)
            {
                _chatList.Controls.Clear();
                AddStartupCards();
            }
            if (_txtNaturalLanguage != null) _txtNaturalLanguage.Text = "询问 CAD 助手...";
            RefreshUsage();
            SelectMainTab(0);
            SetChatStatus("当前对话已清空。", CadGreen);
        }

        private void ShowConversationHistoryDialog()
        {
            SaveCurrentConversation();
            var records = ConversationHistoryStore.LoadAll();
            if (records.Count == 0)
            {
                MessageBox.Show("还没有历史对话。", "历史对话", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new Form())
            using (var list = new ListBox())
            using (var open = new Button())
            using (var cancel = new Button())
            {
                form.Text = "历史对话";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 320);
                form.BackColor = CadBg;
                form.ForeColor = CadText;
                form.Font = UiFont;

                list.Left = 10;
                list.Top = 10;
                list.Width = 400;
                list.Height = 250;
                list.BackColor = CadInput;
                list.ForeColor = CadText;
                list.Font = UiFont;
                foreach (var record in records)
                {
                    list.Items.Add(record.UpdatedUtc.ToLocalTime().ToString("MM-dd HH:mm") + "  " +
                        FirstNonEmpty(record.Title, "未命名对话"));
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;

                open.Text = "打开";
                open.Left = 250;
                open.Top = 275;
                open.Width = 72;
                open.Height = 28;
                open.DialogResult = DialogResult.OK;
                cancel.Text = "取消";
                cancel.Left = 338;
                cancel.Top = 275;
                cancel.Width = 72;
                cancel.Height = 28;
                cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(list);
                form.Controls.Add(open);
                form.Controls.Add(cancel);
                form.AcceptButton = open;
                form.CancelButton = cancel;
                StylePrimaryButton(open);
                StyleGhostButton(cancel);

                if (form.ShowDialog() != DialogResult.OK || list.SelectedIndex < 0) return;
                LoadConversation(records[list.SelectedIndex]);
            }
        }

        private void LoadConversation(ConversationHistoryRecord record)
        {
            if (record == null) return;
            _currentConversationId = string.IsNullOrWhiteSpace(record.Id) ? ConversationHistoryStore.NewId() : record.Id;
            _currentConversationStartedUtc = record.CreatedUtc == default(DateTime) ? DateTime.UtcNow : record.CreatedUtc;
            _conversationMessages.Clear();
            _conversationMessages.AddRange(record.Messages ?? new List<ConversationMessageRecord>());
            _chatRequests = _conversationMessages.Count(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
            if (_chatList != null)
            {
                _chatList.Controls.Clear();
                var old = _suppressConversationRecording;
                _suppressConversationRecording = true;
                try
                {
                    foreach (var message in _conversationMessages)
                    {
                        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                        {
                            AddAssistantCard("CAD 助手", message.Text);
                        }
                        else
                        {
                            AddChatCard(null, message.Text, false);
                        }
                    }
                }
                finally
                {
                    _suppressConversationRecording = old;
                }
            }
            RefreshUsage();
            SelectMainTab(0);
            SetChatStatus("已载入历史对话。", CadGreen);
        }

        private void RecordConversationMessage(string role, string text)
        {
            if (_suppressConversationRecording || string.IsNullOrWhiteSpace(text)) return;
            _conversationMessages.Add(new ConversationMessageRecord
            {
                Role = role,
                Text = TrimForPrompt(text.Trim(), 4000),
                TimestampUtc = DateTime.UtcNow,
            });
            while (_conversationMessages.Count > 80)
            {
                _conversationMessages.RemoveAt(0);
            }
            SaveCurrentConversation();
        }

        private void SaveCurrentConversation()
        {
            if (_conversationMessages.Count == 0) return;
            ConversationHistoryStore.Save(new ConversationHistoryRecord
            {
                Id = _currentConversationId,
                CreatedUtc = _currentConversationStartedUtc,
                UpdatedUtc = DateTime.UtcNow,
                Messages = _conversationMessages.ToList(),
            });
        }

        private void ExportCurrentConversationForDebug()
        {
            try
            {
                SaveCurrentConversation();
                var settings = AgentConfigStore.LoadActive();
                NormalizeRuntimeSettings(settings);
                var export = new JObject
                {
                    ["schema"] = "vcad_debug_conversation_v1",
                    ["exported_at_utc"] = DateTime.UtcNow.ToString("o"),
                    ["conversation"] = JObject.FromObject(new ConversationHistoryRecord
                    {
                        Id = _currentConversationId,
                        CreatedUtc = _currentConversationStartedUtc,
                        UpdatedUtc = DateTime.UtcNow,
                        Messages = _conversationMessages.ToList(),
                    }),
                    ["runtime"] = new JObject
                    {
                        ["provider"] = settings.Provider,
                        ["model"] = settings.Model,
                        ["api_base_url"] = settings.ApiBaseUrl,
                        ["agent_port"] = settings.AgentPort,
                        ["execution_mode"] = settings.ExecutionMode,
                        ["memory_enabled"] = settings.MemoryEnabled,
                        ["api_key"] = string.IsNullOrWhiteSpace(settings.ApiKeyPlain) ? "" : "[redacted]",
                    },
                    ["paths"] = new JObject
                    {
                        ["conversation_history"] = ConversationHistoryStore.HistoryPath,
                        ["usage_today"] = UsageLedgerStore.TodayPath,
                    },
                };

                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "导出 VCAD 调试会话";
                    dialog.Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                    dialog.FileName = "vcad-debug-session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, export.ToString(Formatting.Indented), Encoding.UTF8);
                    SetChatStatus("已导出调试会话：" + dialog.FileName, CadGreen);
                }
            }
            catch (Exception ex)
            {
                SetChatStatus("导出调试会话失败：" + SecretRedactor.Redact(ex.Message), CadOrange);
            }
        }

        private void ShowHelpDialog()
        {
            using (var dialog = new Form())
            using (var text = new TextBox())
            using (var close = new Button())
            {
                dialog.Text = "VCAD 使用说明与常见问题";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(760, 640);
                dialog.MinimumSize = new Size(520, 420);
                dialog.BackColor = CadBg;
                dialog.ForeColor = CadText;

                text.Multiline = true;
                text.ReadOnly = true;
                text.ScrollBars = ScrollBars.Both;
                text.WordWrap = false;
                text.BorderStyle = BorderStyle.FixedSingle;
                text.BackColor = CadPanel;
                text.ForeColor = CadText;
                text.Font = new Font("Microsoft YaHei UI", 9F);
                text.Text = BuildHelpText();
                text.Dock = DockStyle.Fill;

                close.Text = "关闭";
                close.Dock = DockStyle.Bottom;
                close.Height = 34;
                close.FlatStyle = FlatStyle.Flat;
                close.ForeColor = CadCyan;
                close.BackColor = CadPanel;
                close.FlatAppearance.BorderColor = CadBorder;
                close.Click += (s, e) => dialog.Close();

                dialog.Controls.Add(text);
                dialog.Controls.Add(close);
                dialog.ShowDialog(this);
            }
        }

        private static string BuildHelpText()
        {
            return
@"VCAD 快速上手
================

VCAD 是 AutoCAD 里的智能体面板。主入口还是右侧对话框，主产物还是当前打开的 DWG。

它不是把一段回复写到图纸上，也不是靠旧 DSL 流程画图。正确链路是：
自然语言 -> AgentLite/大模型 -> CAD Brief/计划/CAD-IR -> cad.* 工具调用 -> 插件在 AutoCAD 进程内用 AutoCAD .NET API 修改当前 DWG -> 校验结果 -> 面板里自然语言回复。

怎么开始
--------
1. 打开 AutoCAD 后执行 VCAD 命令，打开右侧面板。
2. 到“配置”里选择模型厂家、模型型号、API Base URL、API Key。
3. 点“测试连接”。成功后右上角应显示 LLM 在线。
4. 回到“对话”，直接说要做什么。例如：
   - 画一个 6000x4000 的房间矩形，图层 A-WALL。
   - 把 FROG 图层上的轮廓整体复制到右侧 1200。
   - 读取当前图纸，告诉我有哪些图层、块和尺寸范围。
   - 根据上传的 PDF 鉴定报告生成平面草图。

怎么提问更有效
--------------
- 说明目标：画什么、改什么、查什么。
- 说明单位：mm、m、图纸单位。
- 说明图层：例如 A-WALL、FROG、TEXT。
- 说明位置/尺寸：坐标、宽高、半径、偏移距离。
- 如果是改已有对象，尽量给选择条件：图层、颜色、文字内容、块名、附近坐标。
- 不确定时可以直接让它先读取当前 DWG 上下文。

执行模式
--------
- 确认后执行：写图工具会先询问确认，适合不熟悉任务或风险较高的图纸。
- 完全授权自动执行：模型给出工具调用后直接执行，适合连续绘图。建议先在测试图纸里试。

附件
----
- 支持 PDF、图片和文本类文件作为上下文。
- 可复制文字的 PDF 会抽取文字；扫描版 PDF 需要 OCR，当前会提示缺少可读文字。
- 图片可作为草图/外观参考，前提是所选模型支持视觉输入。
- 大文件不会整体塞进 prompt，会抽取/截断为上下文，避免卡死和爆 token。

工具调用能做什么
----------------
CAD 工具：
- cad.read_dwg_snapshot：读取当前 DWG 图层、图元、块引用、块内展开图元。
- cad.preview_plan：执行前预览计划影响。
- cad.count_entities：按图层/类型/handle/selector 计数。
- cad.measure_bounds：测量对象范围。
- cad.measure_distance：测量两点或两组对象距离。
- cad.layer_diff / cad.before_after_diff：对比执行前后变化。
- cad.create_layer、cad.draw_line、cad.draw_polyline、cad.draw_circle、cad.draw_rectangle、cad.draw_text：实际写图。

外部上下文工具：
- web.search / web.fetch_url：查网页资料。
- workspace.read_file / workspace.write_file：读写配置的工作目录文件。

稳定引用对象
------------
Agent 读取图纸后，会用稳定 selector 引用对象：
- layer:FROG
- handle:1A2F
- type:Polyline
- block:Door#3/entity:Line#2

常见问题
--------
1. 只回复、不画图怎么办？
   - 确认 AgentLite 返回了 tool_calls。
   - 如果只有 CAD-IR 没有 tool_calls，这是 AgentLite 编译或模型输出错误；插件不会兜底执行。
   - 如果仍只回复，请在调试会话里导出 JSON，看 assistant_message、cad_ir、tool_calls。

2. 为什么要确认？
   - 当前是“确认后执行”模式。到配置页把执行模式改成“完全授权自动执行”可减少中断。

3. 为什么显示模型连接失败？
   - 检查 API Key、Base URL、模型名是否可用。
   - OpenAI 兼容接口通常是 https://api.openai.com 或供应商自己的 base url。
   - 403/model_not_found 表示当前 key/project 没有该模型权限，不等于 key 没权限。

4. 为什么 PDF 没读出来？
   - 可能是扫描件，没有可复制文字。需要 OCR 或手工补关键尺寸/文字。

5. 为什么看起来在等待很久？
   - 复杂任务会读取 DWG、附件、工具结果再让模型规划。状态栏会显示当前阶段。
   - 如果配置的超时时间太短，调大到 300 秒以上。

6. 回复怎么复制？
   - 对话内容、工程计划、工具结果都是可选择文本；用鼠标选中后 Ctrl+C。

7. 怎么排查问题？
   - 设置 -> 导出调试会话，导出 JSON。
   - 同时查看 %APPDATA%\VCAD\logs。
   - 重新安装前关闭 acad.exe、Vcad.AgentLite.exe，避免 DLL 被占用。

安装/更新测试命令
----------------
在 F:\Vcad 下执行：

$env:AutoCAD2017_Managed = ""D:\autocad2017\AutoCAD 2017""
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-bundle.ps1 -Target acad2017
$dest = ""$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle""
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item ""bundle\Acad2017"" $dest -Recurse -Force

首次打开 AutoCAD 时如果出现“未签名的可执行文件”安全提示，确认路径是 VCAD-Acad2017.bundle 后点击“加载一次”或“始终加载”。
如果删除失败，先关闭 AutoCAD 和 AgentLite。";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
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
                Padding = new Padding(6),
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
                Height = 76,
                Location = new Point(78, 10),
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
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
                Width = 28,
                Height = 28,
                Location = new Point(8, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            _btnAttachFile.Click += (s, e) => OnAttachFile();
            _btnAttachFile.Paint += (s, e) => DrawPaperclipIcon(e.Graphics, _btnAttachFile.ClientRectangle, _btnAttachFile.Enabled ? CadMuted : CadBorder);
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_btnAttachFile, "上传文件");
            _btnVoiceInput = new Button
            {
                Text = "",
                Width = 28,
                Height = 28,
                Location = new Point(42, 10),
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
                Width = 34,
                Height = 34,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnUseAgent.Click += async (s, e) => await OnUseAgentAsync();
            inputPanel.Resize += (s, e) =>
            {
                var top = 8;
                _btnAttachFile.Left = 8;
                _btnAttachFile.Top = top;
                _btnVoiceInput.Left = _btnAttachFile.Right + 4;
                _btnVoiceInput.Top = top;
                _btnUseAgent.Left = inputPanel.Width - _btnUseAgent.Width - 8;
                _btnUseAgent.Top = top;
                _txtNaturalLanguage.Left = _btnVoiceInput.Right + 8;
                _txtNaturalLanguage.Top = top;
                _txtNaturalLanguage.Height = Math.Max(54, inputPanel.Height - 16);
                _txtNaturalLanguage.Width = Math.Max(150, _btnUseAgent.Left - _txtNaturalLanguage.Left - 8);
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
            AddStartupCards();
        }

        private void AddStartupCards()
        {
            SetChatStatus("就绪。", CadGreen);
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
            panel.Controls.Add(MetricLabel("状态", "未测试", out _lblChatConnection), 2, 0);
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
            RecordConversationMessage("user", text);
            AddChatCard(null, text, false);
        }

        private void AddAssistantCard(string title, string text)
        {
            if (string.Equals(title, "CAD 助手", StringComparison.OrdinalIgnoreCase))
            {
                RecordConversationMessage("assistant", text);
                AddPlainAssistantText(text);
                return;
            }
            AddChatCard(title, text, true);
        }

        private TextBox CreateSelectableTextBox(string text, Font font, Color foreColor, Color backColor)
        {
            return new TextBox
            {
                Text = text ?? "",
                ReadOnly = true,
                Multiline = true,
                BorderStyle = BorderStyle.None,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = font,
                ShortcutsEnabled = true,
                ScrollBars = ScrollBars.None,
                Cursor = Cursors.IBeam,
                AutoSize = false,
                TabStop = false,
            };
        }

        private static int MeasureTextHeight(string text, Font font, int width, int minHeight, int maxHeight = 0)
        {
            width = Math.Max(80, width);
            var measured = TextRenderer.MeasureText(
                string.IsNullOrEmpty(text) ? " " : text,
                font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 8;
            var height = Math.Max(minHeight, measured);
            return maxHeight > 0 ? Math.Min(maxHeight, height) : height;
        }

        private void AddPlainAssistantText(string text)
        {
            if (_chatList == null) return;
            var width = GetChatCardWidth();
            var body = CreateSelectableTextBox(text, UiFontBold, CadText, CadBg);
            body.Left = 10;
            body.Top = 4;
            body.Width = width - 20;
            body.Height = MeasureTextHeight(text, UiFontBold, body.Width, 28);

            var card = new Panel
            {
                Width = width,
                Height = body.Height + 8,
                BackColor = CadBg,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0),
                Tag = "plain-assistant",
            };
            card.Controls.Add(body);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private TextBox AddLiveStreamCard()
        {
            if (_chatList == null) return null;
            var width = GetChatCardWidth();
            var body = CreateSelectableTextBox("", UiFontBold, CadText, CadBg);
            body.Width = width - 20;
            body.Left = 10;
            body.Top = 4;
            body.Height = 34;
            var card = new Panel
            {
                Width = width,
                Height = body.Height + 8,
                BackColor = CadBg,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0),
                Tag = "live-stream",
            };
            card.Controls.Add(body);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
            return body;
        }

        private TextBox AddLiveStreamCardOnUi()
        {
            if (InvokeRequired)
            {
                return (TextBox)Invoke(new Func<TextBox>(AddLiveStreamCard));
            }
            return AddLiveStreamCard();
        }

        private void AddCollapsibleAssistantCard(string title, string summary, string details)
        {
            if (_chatList == null) return;
            var width = GetChatCardWidth();
            var card = new Panel
            {
                Width = width,
                BackColor = CadPanel,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(8),
                Tag = "collapsible-collapsed",
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(CadBorder))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            var header = new Label
            {
                Text = title + "  [+]",
                ForeColor = CadCyan,
                Font = UiFontBold,
                Left = 10,
                Top = 8,
                Width = width - 20,
                Height = 20,
                Cursor = Cursors.Hand,
            };
            var body = CreateSelectableTextBox(summary, UiFont, CadText, CadPanel);
            body.Left = 10;
            body.Top = header.Bottom + 4;
            body.Width = width - 20;
            var detail = CreateSelectableTextBox(details, MonoFont, CadMuted, CadPanel);
            detail.Left = 10;
            detail.Top = body.Bottom + 8;
            detail.Width = width - 20;
            detail.Visible = false;
            detail.ScrollBars = ScrollBars.Vertical;
            Action resize = () =>
            {
                body.Width = card.Width - 20;
                body.Height = MeasureTextHeight(body.Text, body.Font, body.Width, 26);
                detail.Top = body.Bottom + 8;
                detail.Width = body.Width;
                detail.Height = detail.Visible ? MeasureTextHeight(detail.Text, detail.Font, detail.Width, 34, 260) : 0;
                card.Height = detail.Visible ? detail.Bottom + 10 : body.Bottom + 10;
            };
            EventHandler toggle = (s, e) =>
            {
                detail.Visible = !detail.Visible;
                card.Tag = detail.Visible ? "collapsible-expanded" : "collapsible-collapsed";
                header.Text = title + (detail.Visible ? "  [-]" : "  [+]");
                resize();
                _chatList.ScrollControlIntoView(card);
            };
            header.Click += toggle;
            body.Click += toggle;
            card.Controls.Add(header);
            card.Controls.Add(body);
            card.Controls.Add(detail);
            resize();
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private void AddChatCard(string title, string text, bool assistant)
        {
            if (_chatList == null) return;
            var width = GetChatCardWidth();
            var display = (string.IsNullOrEmpty(title) ? "" : title + "\r\n") + text;
            var body = CreateSelectableTextBox(display, assistant ? UiFontBold : UiFont, assistant ? CadText : Color.White, assistant ? CadPanel : CadPanelHigh);
            body.Width = width - 20;
            body.Left = 10;
            body.Top = 8;
            body.Height = MeasureTextHeight(display, body.Font, body.Width, 34);

            var card = new Panel
            {
                Width = width,
                Height = body.Height + 16,
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
            card.Controls.Add(body);
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private void AppendLiveStreamText(TextBox body, StringBuilder buffer, string delta)
        {
            if (body == null || buffer == null || string.IsNullOrEmpty(delta)) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AppendLiveStreamText(body, buffer, delta)));
                return;
            }

            buffer.Append(delta);
            var text = buffer.ToString();
            if (text.Length > MaxLiveStreamChars)
            {
                text = "...[前文省略]\r\n" + text.Substring(text.Length - MaxLiveStreamChars);
            }
            body.Text = text;
            body.Width = Math.Max(120, body.Parent.Width - 20);
            body.Height = MeasureTextHeight(text, body.Font, body.Width, 34, 260);
            body.Parent.Height = body.Height + 8;
            _chatList.ScrollControlIntoView(body.Parent);
        }

        private void ResizeChatCards()
        {
            if (_chatList == null) return;
            var width = GetChatCardWidth();
            foreach (Control card in _chatList.Controls)
            {
                card.Width = width;
                var tagText = card.Tag as string;
                if (!string.IsNullOrEmpty(tagText) &&
                    (tagText.StartsWith("collapsible", StringComparison.Ordinal) ||
                     tagText == "clarification"))
                {
                    continue;
                }
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
                    var textBox = card.Controls[0] as TextBox;
                    if (textBox != null)
                    {
                        textBox.Width = width - 20;
                        var min = (card.Tag as string) == "plain-assistant" || (card.Tag as string) == "live-stream" ? 28 : 34;
                        var max = (card.Tag as string) == "live-stream" ? 260 : 0;
                        textBox.Height = MeasureTextHeight(textBox.Text, textBox.Font, textBox.Width, min, max);
                        card.Height = textBox.Height + (((card.Tag as string) == "plain-assistant" || (card.Tag as string) == "live-stream") ? 8 : 16);
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
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
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
            _cmbProfile = new ComboBox { Width = 180, DropDownWidth = 260, DropDownStyle = ComboBoxStyle.DropDownList };
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
            _cmbProvider = new ComboBox { Width = 220, DropDownWidth = 260, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProvider.Items.AddRange(new object[] { "openai", "deepseek", "anthropic", "gemini", "ollama", "custom" });
            SetComboDropDownWidth(_cmbProvider, 260);
            _cmbProvider.SelectedIndexChanged += (s, e) => OnProviderChanged();
            panel.Controls.Add(_cmbProvider, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblApiBaseUrl, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtBaseUrl = new TextBox { Width = 320, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtBaseUrl, 1, row++);

            panel.Controls.Add(new Label { Text = "模型型号", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbModel = new ComboBox { Width = 260, DropDownWidth = 340, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
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
            _cmbExecutionMode = new ComboBox { Width = 220, DropDownWidth = 260, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbExecutionMode.Items.AddRange(new object[] { "确认后执行", "完全授权自动执行" });
            _cmbExecutionMode.SelectedIndex = 0;
            SetComboDropDownWidth(_cmbExecutionMode, 260);
            panel.Controls.Add(_cmbExecutionMode, 1, row++);

            panel.Controls.Add(new Label { Text = "学习记忆", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _chkMemoryEnabled = new CheckBox { Text = "记录并遵循用户偏好规则", Checked = true, AutoSize = true };
            panel.Controls.Add(_chkMemoryEnabled, 1, row++);

            var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnTestConnection = new Button { Text = Strings.BtnTestConnection, Width = 140, Height = 28 };
            _btnTestConnection.Click += async (s, e) => await OnTestConnectionAsync();
            _btnSaveSettings = new Button { Text = Strings.BtnSave, Width = 100, Height = 28 };
            _btnSaveSettings.Click += (s, e) => OnSaveSettings();
            var helpButton = new Button { Text = "帮助", Width = 76, Height = 28 };
            helpButton.Click += (s, e) => ShowHelpDialog();
            actionRow.Controls.Add(_btnTestConnection);
            actionRow.Controls.Add(_btnSaveSettings);
            actionRow.Controls.Add(helpButton);
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
            StyleGhostButton(helpButton);
            notice.ForeColor = CadMuted;
        }

        private void BuildUsageTab(TabPage tab)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(10, 8, 10, 8),
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleRow = new Panel { Dock = DockStyle.Fill, BackColor = CadBg };
            var title = new Label
            {
                Text = "本次会话用量",
                Left = 0,
                Top = 7,
                Width = 180,
                Height = 26,
                ForeColor = CadText,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            };
            _btnClearUsage = new Button
            {
                Text = "清零",
                Width = 58,
                Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnClearUsage.Click += (s, e) => ClearTodayUsage();
            titleRow.Resize += (s, e) => _btnClearUsage.Left = Math.Max(120, titleRow.Width - _btnClearUsage.Width);
            titleRow.Controls.Add(title);
            titleRow.Controls.Add(_btnClearUsage);
            panel.Controls.Add(titleRow, 0, 0);

            panel.Controls.Add(UsageCard("请求统计", out _lblUsageRequests, out _lblUsageSuccess), 0, 1);
            panel.Controls.Add(UsageCard("失败与耗时", out _lblUsageFailed, out _lblUsageAvg), 0, 2);
            panel.Controls.Add(UsageCard("当前模型", out _lblUsageProvider, out _lblUsageModel), 0, 3);
            panel.Controls.Add(UsageCard("Token 明细", out _lblUsageTokens, out _lblUsageCost), 0, 4);

            var recentCard = UsageTextCard("最近模型请求", out _txtUsageRecent);
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
            StyleGhostButton(_btnClearUsage);
            RefreshUsage();
        }

        private Control UsageCard(string title, out Label left, out Label right)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = CadPanel, Padding = new Padding(8) };
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
                Left = 8,
                Top = 6,
                Width = 220,
                Height = 18,
                ForeColor = CadMuted,
                Font = UiFontBold,
            });
            left = new Label
            {
                Left = 8,
                Top = 29,
                Width = 145,
                Height = 24,
                ForeColor = CadCyan,
                Font = new Font("Consolas", 11F, FontStyle.Bold),
            };
            var rightLabel = new Label
            {
                Left = 158,
                Top = 29,
                Width = 190,
                Height = 24,
                ForeColor = CadText,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
            };
            right = rightLabel;
            panel.Resize += (s, e) => rightLabel.Width = Math.Max(80, panel.Width - rightLabel.Left - 8);
            panel.Controls.Add(left);
            panel.Controls.Add(rightLabel);
            return panel;
        }

        private Control UsageTextCard(string title, out TextBox body)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = CadPanel, Padding = new Padding(8) };
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
                Left = 8,
                Top = 6,
                Width = 220,
                Height = 18,
                ForeColor = CadMuted,
                Font = UiFontBold,
            });
            var bodyBox = new TextBox
            {
                Left = 8,
                Top = 30,
                Width = 360,
                Height = 102,
                ForeColor = CadText,
                Font = MonoFont,
                BackColor = CadPanel,
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
            };
            panel.Resize += (s, e) =>
            {
                bodyBox.Width = Math.Max(120, panel.Width - 16);
                bodyBox.Height = Math.Max(70, panel.Height - 38);
            };
            panel.Controls.Add(bodyBox);
            body = bodyBox;
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
            if (_txtUsageRecent != null)
            {
                _txtUsageRecent.Text = FormatRecentUsage(today);
            }
        }

        private void ClearTodayUsage()
        {
            var confirm = MessageBox.Show(
                "清零今日真实模型 token 和费用记录？",
                "清零用量",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;
            UsageLedgerStore.ClearToday();
            RefreshUsage();
            SetChatStatus("今日用量已清零。", CadGreen);
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
            foreach (var r in summary.Recent)
            {
                lines.Add(r.TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + " " +
                          r.Provider + "/" + r.Model + " " +
                          r.InputTokens + "+" + r.OutputTokens + "=" + r.TotalTokens +
                          " " + FormatUsd(r.CostUsd) +
                          " " + r.UsageSource);
            }
            return string.Join("\r\n", lines);
        }

        private static void DrawPaperclipIcon(Graphics g, Rectangle rect, Color color)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var state = g.Save();
            g.TranslateTransform(rect.Left + rect.Width / 2F, rect.Top + rect.Height / 2F);
            g.RotateTransform(-32F);
            using (var pen = new Pen(color, 1.9F))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                var outer = new RectangleF(-5.5F, -10F, 11F, 20F);
                var inner = new RectangleF(-2.5F, -6.5F, 5F, 13F);
                g.DrawArc(pen, outer, 90, 180);
                g.DrawLine(pen, outer.Right, -4.5F, outer.Right, 4.5F);
                g.DrawArc(pen, inner, -90, 180);
                g.DrawLine(pen, inner.Left, 2F, inner.Left, -4F);
            }
            g.Restore(state);
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
            SetChatStatus("正在听写，说出要执行的 CAD 操作。识别器：" + FirstNonEmpty(_speechRecognizerName, "Windows 默认"), CadCyan);
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
            _speechRecognizerName = selected.Name + " / " + selected.Culture.Name;
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
            engine.BabbleTimeout = TimeSpan.FromSeconds(6);
            engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);
            engine.SpeechRecognized += OnSpeechRecognized;
            engine.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
            engine.RecognizeCompleted += OnSpeechRecognizeCompleted;
            return engine;
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result == null || string.IsNullOrWhiteSpace(e.Result.Text))
            {
                return;
            }
            if (IsDisposed) return;
            BeginInvoke((Action)(() => AppendRecognizedText(e.Result.Text, e.Result.Confidence)));
        }

        private void OnSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() => SetChatStatus("没有识别到文字。请检查麦克风权限/输入设备，或在 Windows 安装中文语音识别包。", CadOrange)));
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
            SetChatStatus("识别到语音：" + text + " (" + confidence.ToString("0.00") + ")", confidence < 0.18F ? CadOrange : CadCyan);
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

        private string BuildPromptWithConversationContext(string currentPrompt)
        {
            if (_conversationMessages.Count == 0) return currentPrompt ?? "";
            var recent = _conversationMessages
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Skip(Math.Max(0, _conversationMessages.Count - 12))
                .ToList();
            if (recent.Count == 0) return currentPrompt ?? "";

            var sb = new StringBuilder();
            sb.AppendLine("最近对话上下文（用于延续当前 CAD 任务；不要重复询问已经给出的信息）：");
            foreach (var m in recent)
            {
                var role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "助手" : "用户";
                sb.Append(role).Append(": ").AppendLine(TrimForPrompt(m.Text, 800));
            }
            sb.AppendLine();
            sb.AppendLine("当前用户请求：");
            sb.Append(currentPrompt ?? "");
            return sb.ToString();
        }

        private static string TrimForPrompt(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars) return value ?? "";
            return value.Substring(0, Math.Max(0, maxChars - 1)) + "…";
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
            if (LooksLikeCadWriteRequest(s)) return false;
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
                   "1. CAD 执行工具：create_layer、draw_line、draw_polyline、draw_circle、draw_rectangle、draw_stair、draw_text。它们只能通过 CAD-IR、Safety、Preview/Confirm 后进入 AutoCAD。\r\n\r\n" +
                   "2. CAD 上下文/验证工具：读取当前打开 DWG 的图层、图元、块引用，并在内存里展开块内图元；也能测量对象包围盒，校验图层、对象数量、类型和警告。\r\n\r\n" +
                   "3. 附件上下文工具：PDF 会先抽取可复制文字；图片会内联给支持视觉的模型；文本、DXF、LSP、CSV、JSON 会抽取文本片段。扫描版 PDF 目前只能识别为“需要 OCR”，不会凭空读出内容。\r\n\r\n" +
                   "AgentLite 工具层：web.search、web.fetch_url、workspace.read_file、workspace.write_file 已有受限端点。读工具默认只读；写文件和 CAD 执行都受“确认后执行/完全授权自动执行”模式控制。\r\n\r\n" +
                   "对话回复、状态说明、PDF 读取失败提示只会显示在右侧对话框里，不会再作为 draw_text 写进图纸。";
        }

        private async Task OnUseAgentAsync()
        {
            if (_agentRunning)
            {
                QueueSupplementalRequirement();
                return;
            }

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
                var prompt = BuildPromptWithConversationContext(BuildPromptWithAttachments(text));
                var displayText = BuildDisplayTextWithAttachments(text);
                var settings = AgentConfigStore.LoadActive();
                NormalizeRuntimeSettings(settings);

#if NETFRAMEWORK
                StopVoiceInputSilently();
#endif
                SetAgentRunningState(true);
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
                SetChatStatus("正在理解意图...", CadCyan);
                System.Windows.Forms.Application.DoEvents();

                var startResult = await AgentLiteProcessManager.EnsureStartedAsync(settings).ConfigureAwait(true);
                if (!startResult.Success)
                {
                    RefreshUsage();
                    SetLlmConnectionStatus("● LLM 未连", CadOrange);
                    AddAssistantCard("CAD 助手", "我还不能处理这条请求，因为本地 Agent Lite 没有启动成功。\r\n\r\n" +
                        "没有修改当前图纸。请重新打开 VCAD 面板，或检查 `%APPDATA%\\VCAD\\logs` 后再点发送。\r\n\r\n" +
                        "技术原因：" + startResult.Message);
                    SetChatStatus(startResult.Message, CadOrange);
                    return;
                }

                var client = new AgentLiteClient(settings);
                SetChatStatus("正在调用模型理解意图...", CadCyan);
                var sessionId = _currentConversationId;
                await RunAgentToolLoopAsync(client, settings, sessionId, prompt, text, attachmentPayloads, cadState);
            }
            catch (System.Exception ex)
            {
                RefreshUsage();
                var msg = SecretRedactor.Redact(ex.Message);
                SetLlmConnectionStatus("● LLM 失败", CadOrange);
                AddAssistantCard("CAD 助手", BuildFriendlyFailureReply(msg));
                SetChatStatus("失败：" + msg, CadOrange);
            }
            finally
            {
                SetAgentRunningState(false);
                if (_btnAttachFile != null) _btnAttachFile.Enabled = true;
                if (_btnVoiceInput != null) _btnVoiceInput.Enabled = true;
            }
        }

        private void SetAgentRunningState(bool running)
        {
            _agentRunning = running;
            if (_btnUseAgent == null) return;
            _btnUseAgent.Text = running ? "Ⅱ" : "▶";
            _toolTip?.SetToolTip(_btnUseAgent, running ? "模型处理中：输入补充后点这里加入下一轮规划" : "发送");
            _btnUseAgent.Invalidate();
        }

        private void QueueSupplementalRequirement()
        {
            var text = _txtNaturalLanguage == null ? "" : _txtNaturalLanguage.Text;
            if (text == "询问 CAD 助手...") text = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                SetChatStatus("模型处理中。可以输入补充需求后点暂停键加入下一轮规划。", CadOrange);
                return;
            }

            text = text.Trim();
            lock (_pendingSupplementsLock)
            {
                _pendingSupplements.Add(text);
            }
            _txtNaturalLanguage.Text = "";
            AddUserCard("补充需求：\r\n" + text);
            SetChatStatus("已加入补充需求，不中断当前模型处理。", CadCyan);
        }

        private async Task RunAgentToolLoopAsync(
            AgentLiteClient client,
            AgentSettings settings,
            string sessionId,
            string message,
            string originalUserText,
            JArray attachments,
            JObject cadObservation)
        {
            var toolResults = new JArray();
            var currentMessage = message ?? "";
            var currentAttachments = attachments ?? new JArray();
            var currentObservation = cadObservation;
            var anyToolFailed = false;
            var anyWriteToolSucceeded = false;
            var lastToolFailure = "";

            for (var turn = 1; turn <= 8; turn++)
            {
                SetChatStatus(turn == 1 ? "正在调用模型..." : "正在把工具结果交给模型继续判断...", CadCyan);
                var sw = Stopwatch.StartNew();
                var liveBuffer = new StringBuilder();
                TextBox liveText = null;
                var hadVisibleDelta = false;
                var turnResult = await WaitForAgentTurnAsync(
                    client.AgentTurnStreamingAsync(
                        sessionId,
                        currentMessage,
                        currentAttachments,
                        currentObservation,
                        toolResults,
                        delta =>
                        {
                            if (!string.IsNullOrEmpty(delta))
                            {
                                hadVisibleDelta = true;
                                if (liveText == null)
                                {
                                    liveText = AddLiveStreamCardOnUi();
                                }
                                AppendLiveStreamText(liveText, liveBuffer, delta);
                            }
                            return Task.FromResult(0);
                        }))
                    .ConfigureAwait(true);
                sw.Stop();
                SetLlmConnectionStatus("● LLM 在线", CadGreen);
                RecordModelUsage(turnResult, settings, true, sw.ElapsedMilliseconds);
                if (!LooksLikeCadWriteRequest(originalUserText))
                {
                    AddEngineeringPlanCard(turnResult);
                }
                if ((turnResult.ToolCalls == null || turnResult.ToolCalls.Count == 0) &&
                    HasExecutableCadIr(turnResult.CadIr))
                {
                    AddAssistantCard("CAD 助手",
                        "AgentLite 返回了 CAD-IR，但没有返回 tool_calls。插件不会再兜底执行；请修复 AgentLite 的 CAD-IR 编译或模型输出。");
                    SetChatStatus("Agent 未返回工具调用。", CadOrange);
                    return;
                }

                var supplement = TakePendingSupplementText();
                if (!string.IsNullOrWhiteSpace(supplement))
                {
                    AddAssistantCard("CAD 助手", "已收到补充需求，会基于同一个任务继续，不会丢掉前面的上下文。");
                    currentMessage =
                        "原始任务和当前对话上下文如下，请继续同一个 CAD 任务，不要回到初始问题：\r\n" +
                        message +
                        "\r\n\r\n用户在模型处理过程中补充了需求：\r\n" +
                        supplement;
                    currentAttachments = attachments == null ? new JArray() : (JArray)attachments.DeepClone();
                    currentObservation = DrawingSnapshotCollector.CaptureActive();
                    continue;
                }

                if (hadVisibleDelta && !string.IsNullOrWhiteSpace(turnResult.AssistantMessage))
                {
                    RecordConversationMessage("assistant", turnResult.AssistantMessage);
                }
                else if (!hadVisibleDelta && !string.IsNullOrWhiteSpace(turnResult.AssistantMessage))
                {
                    AddAssistantCard("CAD 助手", turnResult.AssistantMessage);
                }

                if (turnResult.NeedsClarification)
                {
                    if (anyWriteToolSucceeded)
                    {
                        SetChatStatus(anyToolFailed ? "已完成，但有工具失败。" : "已完成。", anyToolFailed ? CadOrange : CadGreen);
                        return;
                    }
                    if (!anyWriteToolSucceeded &&
                        LooksLikeCadWriteRequest(originalUserText) &&
                        IsVagueClarification(turnResult.Clarification, turnResult.AssistantMessage))
                    {
                        currentMessage = BuildForceToolCallPrompt(originalUserText, turnResult);
                        currentAttachments = attachments == null ? new JArray() : (JArray)attachments.DeepClone();
                        currentObservation = DrawingSnapshotCollector.CaptureActive();
                        SetChatStatus("模型只要求补充信息，正在强制转为工具调用...", CadOrange);
                        continue;
                    }
                    if (anyToolFailed && IsGenericInitialClarification(turnResult.Clarification))
                    {
                        AddAssistantCard("CAD 助手",
                            "上一步工具调用失败，我不会跳回初始意图选项。\r\n\r\n失败原因：" +
                            (string.IsNullOrWhiteSpace(lastToolFailure) ? "工具参数或执行环境不正确。" : lastToolFailure) +
                            "\r\n\r\n请直接补充缺少参数，或重新发送更具体的绘图要求。");
                        SetChatStatus("工具失败，等待你补充具体参数。", CadOrange);
                        return;
                    }
                    AddClarificationCard(turnResult.Clarification, originalUserText);
                    SetChatStatus("需要补充信息，等待你选择。", CadOrange);
                    return;
                }

                if (turnResult.ToolCalls == null || turnResult.ToolCalls.Count == 0)
                {
                    if (!anyWriteToolSucceeded && LooksLikeCadWriteRequest(originalUserText) && turn < 8)
                    {
                        currentMessage = BuildForceToolCallPrompt(originalUserText, turnResult);
                        currentAttachments = attachments == null ? new JArray() : (JArray)attachments.DeepClone();
                        currentObservation = DrawingSnapshotCollector.CaptureActive();
                        SetChatStatus("模型只返回了说明，正在要求它返回可执行工具调用...", CadOrange);
                        continue;
                    }
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
                    var toolName = call.Value<string>("name");
                    var result = await ExecuteToolCallAsync(client, settings, call).ConfigureAwait(true);
                    toolResults.Add(result);
                    if (result.Value<bool?>("success") != true)
                    {
                        anyToolFailed = true;
                        lastToolFailure = ExtractToolFailure(result);
                    }
                    else if (CadToolHost.IsWriteTool(toolName))
                    {
                        anyWriteToolSucceeded = true;
                    }
                }

                currentObservation = DrawingSnapshotCollector.CaptureActive();
                if (anyWriteToolSucceeded)
                {
                    AddAssistantCard("CAD 助手", BuildWriteCompletionReply(toolResults, currentObservation, anyToolFailed, lastToolFailure));
                    SetChatStatus(anyToolFailed ? "已完成，但有工具失败。" : "已完成。", anyToolFailed ? CadOrange : CadGreen);
                    return;
                }
            }

            AddAssistantCard("CAD 助手", "Agent 工具循环已达到 8 轮上限。当前不再继续自动执行，避免无限循环。");
            SetChatStatus("Agent 循环达到上限。", CadOrange);
        }

        private string TakePendingSupplementText()
        {
            lock (_pendingSupplementsLock)
            {
                if (_pendingSupplements.Count == 0) return "";
                var text = string.Join("\r\n", _pendingSupplements);
                _pendingSupplements.Clear();
                return text;
            }
        }

        private static bool IsGenericInitialClarification(JObject clarification)
        {
            var options = clarification?["options"] as JArray;
            if (options == null || options.Count < 2) return false;
            var text = string.Join(" ", options.Values<string>()).ToLowerInvariant();
            return text.Contains("6000x4000") ||
                (text.Contains("rectangle") && text.Contains("cancel")) ||
                (text.Contains("snapshot") && text.Contains("cancel")) ||
                (text.Contains("读取") && text.Contains("取消")) ||
                (text.Contains("图纸") && text.Contains("取消"));
        }

        private static string ExtractToolFailure(JObject result)
        {
            if (result == null) return "";
            var error = result.Value<string>("error");
            if (!string.IsNullOrWhiteSpace(error)) return error;
            var nested = result["result"] as JObject;
            if (nested != null)
            {
                error = nested.Value<string>("message") ?? nested.Value<string>("error");
                if (!string.IsNullOrWhiteSpace(error)) return error;
            }
            return result.ToString(Formatting.None);
        }

        private static string BuildWriteCompletionReply(JArray toolResults, JObject observation, bool anyToolFailed, string lastToolFailure)
        {
            var successfulWrites = toolResults == null
                ? new List<JObject>()
                : toolResults
                    .OfType<JObject>()
                    .Where(r => r.Value<bool?>("success") == true && CadToolHost.IsWriteTool(r.Value<string>("name")))
                    .ToList();

            var layers = successfulWrites
                .Select(r => r["result"] as JObject)
                .Where(r => r != null)
                .Select(r => r.Value<string>("layer"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var entityTypes = successfulWrites
                .Select(r => r["result"] as JObject)
                .Where(r => r != null)
                .Select(r => r.Value<string>("entity_type"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var text = new StringBuilder();
            text.Append("已在当前图纸执行完成。");
            if (successfulWrites.Count > 0)
            {
                text.Append("\r\n\r\n写入对象：").Append(successfulWrites.Count).Append(" 个");
            }
            if (entityTypes.Count > 0)
            {
                text.Append("\r\n对象类型：").Append(string.Join("、", entityTypes));
            }
            if (layers.Count > 0)
            {
                text.Append("\r\n图层：").Append(string.Join("、", layers));
            }

            var snapshot = observation?["geometry_index"] as JObject;
            var counts = snapshot?["counts"] as JObject;
            var total = counts?.Value<int?>("entities");
            if (total.HasValue)
            {
                text.Append("\r\n当前图元总数：").Append(total.Value);
            }

            if (anyToolFailed)
            {
                text.Append("\r\n\r\n注意：有工具调用失败。");
                if (!string.IsNullOrWhiteSpace(lastToolFailure))
                {
                    text.Append("\r\n原因：").Append(lastToolFailure);
                }
            }
            else
            {
                text.Append("\r\n\r\n你可以继续说要加墙厚、门窗、尺寸标注，或移动/缩放/复制这个对象。");
            }
            return text.ToString();
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
                SetChatStatus(checkpoint.Message, CadOrange);
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

        private void AddEngineeringPlanCard(AgentTurnResult result)
        {
            if (result == null) return;
            if (result.CadBrief == null && result.TaskPlan == null && result.CadIr == null &&
                result.Safety == null && result.Validation == null && (result.Trace == null || result.Trace.Count == 0))
            {
                return;
            }

            var summary = new StringBuilder();
            var objective = result.CadBrief?.Value<string>("objective");
            var taskType = result.CadBrief?.Value<string>("task_type");
            var nextStep = result.TaskPlan?.Value<string>("next_step");
            var risk = result.Safety?.Value<string>("risk_level");
            var writes = result.Safety?.Value<bool?>("writes_dwg");
            var confirmation = result.Safety?.Value<bool?>("requires_confirmation");
            if (!string.IsNullOrWhiteSpace(taskType)) summary.Append("类型: ").AppendLine(taskType);
            if (!string.IsNullOrWhiteSpace(objective)) summary.Append("目标: ").AppendLine(TrimForPrompt(objective, 120));
            if (!string.IsNullOrWhiteSpace(nextStep)) summary.Append("下一步: ").AppendLine(TrimForPrompt(nextStep, 120));
            if (!string.IsNullOrWhiteSpace(risk) || writes.HasValue || confirmation.HasValue)
            {
                summary.Append("安全: ")
                    .Append(string.IsNullOrWhiteSpace(risk) ? "未标注" : risk)
                    .Append(writes.HasValue ? ", 写DWG=" + (writes.Value ? "是" : "否") : "")
                    .Append(confirmation.HasValue ? ", 确认=" + (confirmation.Value ? "需要" : "不需要") : "")
                    .AppendLine();
            }
            var operations = result.CadIr?["operations"] as JArray;
            if (operations != null && operations.Count > 0)
            {
                summary.Append("CAD-IR: ").Append(operations.Count).AppendLine(" 个操作");
            }
            var checks = result.Validation?["planned_checks"] as JArray;
            if (checks != null && checks.Count > 0)
            {
                summary.Append("验证: ").AppendLine(string.Join(", ", checks.Values<string>()));
            }
            if (summary.Length == 0)
            {
                summary.Append("模型已返回工程化计划。");
            }

            var details = new JObject
            {
                ["cad_brief"] = result.CadBrief,
                ["task_plan"] = result.TaskPlan,
                ["cad_ir"] = result.CadIr,
                ["safety"] = result.Safety,
                ["validation"] = result.Validation,
                ["trace"] = result.Trace,
            }.ToString(Formatting.Indented);
            AddCollapsibleAssistantCard("Agent 工程计划", summary.ToString().Trim(), details);
        }

        private static bool HasExecutableCadIr(JObject cadIr)
        {
            var operations = cadIr?["operations"] as JArray;
            return operations != null && operations.Count > 0;
        }

        private static bool LooksLikeStairRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.ToLowerInvariant();
            return s.Contains("楼梯") || s.Contains("stair");
        }

        private static bool LooksLikeStairDrawRequest(string text)
        {
            if (!LooksLikeStairRequest(text)) return false;
            var s = text.ToLowerInvariant();
            return s.Contains("画") ||
                   s.Contains("绘制") ||
                   s.Contains("生成") ||
                   s.Contains("创建") ||
                   s.Contains("draw") ||
                   s.Contains("create");
        }

        private static bool LooksLikeCadWriteRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.ToLowerInvariant();
            return s.Contains("画") ||
                   s.Contains("绘制") ||
                   s.Contains("生成") ||
                   s.Contains("创建") ||
                   s.Contains("新建") ||
                   s.Contains("复制") ||
                   s.Contains("移动") ||
                   s.Contains("draw") ||
                   s.Contains("create") ||
                   s.Contains("copy") ||
                   s.Contains("move") ||
                   LooksLikeStairRequest(s);
        }

        private static bool IsVagueClarification(JObject clarification, string assistantMessage)
        {
            var text = (assistantMessage ?? "") + " " + (clarification?.Value<string>("question") ?? "") + " " +
                       string.Join(" ", (clarification?["options"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>());
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("补充缺失") ||
                   text.Contains("缺失的信息") ||
                   text.Contains("需要补充") ||
                   text.Contains("请选择") ||
                   text.Contains("missing") ||
                   text.Contains("clarify");
        }

        private static string BuildForceToolCallPrompt(string originalUserText, AgentTurnResult previous)
        {
            var response = new JObject
            {
                ["assistant_message"] = previous?.AssistantMessage,
                ["cad_brief"] = previous?.CadBrief,
                ["task_plan"] = previous?.TaskPlan,
                ["cad_ir"] = previous?.CadIr,
                ["safety"] = previous?.Safety,
                ["validation"] = previous?.Validation,
                ["clarification"] = previous?.Clarification,
            };
            return "同一个 CAD 任务继续。用户已经要求不要反复追问，必须按合理默认值调用工具执行。\r\n" +
                   "原始用户请求：\r\n" + (originalUserText ?? "") + "\r\n\r\n" +
                   "你上一轮只返回了说明/澄清，没有实际 tool_calls。现在只返回可执行 JSON：必须包含 tool_calls。\r\n" +
                   "如果是楼梯，优先使用 cad.draw_stair；否则把 CAD-IR 分解为 cad.create_layer/cad.draw_polyline/cad.draw_line/cad.draw_rectangle/cad.draw_text 等工具。\r\n" +
                   "上一轮结构化结果：\r\n" + response.ToString(Formatting.None);
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
                var buttonWidth = Math.Max(120, width - 20);
                var wrapped = WrapOptionText(answer, Math.Max(10, (buttonWidth - 28) / 8));
                var buttonHeight = Math.Max(34, TextRenderer.MeasureText(
                    wrapped,
                    UiFontBold,
                    new Size(buttonWidth - 12, 0),
                    TextFormatFlags.WordBreak).Height + 14);
                var button = new Button
                {
                    Text = wrapped,
                    Left = 10,
                    Top = top,
                    Width = buttonWidth,
                    Height = buttonHeight,
                    TextAlign = ContentAlignment.MiddleLeft,
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
                top += button.Height + 6;
            }

            var customBox = new TextBox
            {
                Left = 10,
                Top = top + 4,
                Width = Math.Max(120, width - 86),
                Height = 24,
                BackColor = CadInput,
                ForeColor = CadText,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "其他补充...",
            };
            customBox.GotFocus += (s, e) =>
            {
                if (customBox.Text == "其他补充...") customBox.Text = "";
            };
            var customButton = new Button
            {
                Text = "补充",
                Left = customBox.Right + 6,
                Top = customBox.Top,
                Width = 60,
                Height = 24,
            };
            StylePrimaryButton(customButton);
            customButton.Click += async (s, e) =>
            {
                var custom = customBox.Text == "其他补充..." ? "" : customBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(custom))
                {
                    SetChatStatus("请输入补充内容。", CadOrange);
                    return;
                }
                foreach (Control child in card.Controls)
                {
                    if (child is Button b) b.Enabled = false;
                    if (child is TextBox tb) tb.Enabled = false;
                }
                _txtNaturalLanguage.Text = (originalText ?? "").Trim() + "\r\n补充：" + custom;
                AddAssistantCard("CAD 助手", "已收到补充信息：" + custom + "\r\n我会基于这个补充继续规划。");
                await OnUseAgentAsync();
            };
            card.Controls.Add(customBox);
            card.Controls.Add(customButton);

            card.Height = customBox.Bottom + 12;
            _chatList.Controls.Add(card);
            _chatList.ScrollControlIntoView(card);
        }

        private static string WrapOptionText(string text, int maxCharsPerLine)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            maxCharsPerLine = Math.Max(8, maxCharsPerLine);
            var sb = new StringBuilder();
            var count = 0;
            foreach (var ch in text.Trim())
            {
                if (count >= maxCharsPerLine && (char.IsWhiteSpace(ch) || ch == '，' || ch == '、' || ch == ',' || ch == ';' || ch == '；' || ch == ')' || ch == '）'))
                {
                    sb.Append("\r\n");
                    count = 0;
                    if (char.IsWhiteSpace(ch)) continue;
                }
                sb.Append(ch);
                count++;
                if (count >= maxCharsPerLine + 6)
                {
                    sb.Append("\r\n");
                    count = 0;
                }
            }
            return sb.ToString();
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
            AddCollapsibleAssistantCard("工具调用", name, FormatToolCall(name, args));

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
                AddCollapsibleAssistantCard(result.Success ? "工具结果" : "工具失败",
                    SummarizeCadToolResult(result),
                    FormatCadToolResult(result));
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
                AddCollapsibleAssistantCard(success ? "工具结果" : "工具失败",
                    SummarizeRemoteToolResult(name, remote, sw.ElapsedMilliseconds),
                    FormatRemoteToolResult(name, remote, sw.ElapsedMilliseconds));
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
                AddCollapsibleAssistantCard("工具失败", name, msg);
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

        private void SetLlmConnectionStatus(string text, Color color)
        {
            if (_lblHeaderLlmStatus == null) return;
            _lblHeaderLlmStatus.Text = text;
            _lblHeaderLlmStatus.ForeColor = color;
            LayoutHeader();
        }

        private static bool IsAgentLiteWriteTool(string name)
        {
            return string.Equals(name, "workspace.write_file", StringComparison.OrdinalIgnoreCase);
        }

        private static string SummarizeCadToolResult(CadToolExecutionResult result)
        {
            if (result == null) return "无结果";
            var status = result.Success ? "成功" : "失败";
            if (string.Equals(result.ToolName, "cad.validate_dwg_state", StringComparison.OrdinalIgnoreCase))
            {
                var passed = result.Data?.Value<bool?>("passed");
                var checks = result.Data?["checks"] as JArray;
                return status + "，用时 " + result.ElapsedMs + " ms，DWG 验证" +
                    (passed.HasValue ? (passed.Value ? "通过" : "未通过") : "完成") +
                    (checks == null ? "" : "，检查 " + checks.Count + " 项");
            }
            if (string.Equals(result.ToolName, "cad.measure_bounds", StringComparison.OrdinalIgnoreCase))
            {
                var count = result.Data?.Value<int?>("count") ?? 0;
                var bounds = result.Data?["bounds"] as JObject;
                var size = bounds == null
                    ? ""
                    : "，宽 " + FormatNumber(bounds.Value<double?>("width")) +
                      "，高 " + FormatNumber(bounds.Value<double?>("height"));
                return status + "，用时 " + result.ElapsedMs + " ms，测量对象 " + count + " 个" + size;
            }
            if (string.Equals(result.ToolName, "cad.count_entities", StringComparison.OrdinalIgnoreCase))
            {
                var count = result.Data?.Value<int?>("count") ?? 0;
                return status + "，用时 " + result.ElapsedMs + " ms，计数 " + count + " 个对象";
            }
            if (string.Equals(result.ToolName, "cad.measure_distance", StringComparison.OrdinalIgnoreCase))
            {
                return status + "，用时 " + result.ElapsedMs + " ms，距离 " +
                    FormatNumber(result.Data?.Value<double?>("distance"));
            }
            if (string.Equals(result.ToolName, "cad.layer_diff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ToolName, "cad.before_after_diff", StringComparison.OrdinalIgnoreCase))
            {
                var delta = result.Data?.Value<int?>("delta") ?? 0;
                return status + "，用时 " + result.ElapsedMs + " ms，图元变化 " + delta;
            }
            if (string.Equals(result.ToolName, "cad.preview_plan", StringComparison.OrdinalIgnoreCase))
            {
                var ops = result.Data?.Value<int?>("operations_count") ?? 0;
                var count = result.Data?.Value<int?>("selected_entity_count") ?? 0;
                return status + "，用时 " + result.ElapsedMs + " ms，预览 " + ops + " 个操作，涉及 " + count + " 个对象";
            }
            var entity = result.Data?["entity_type"]?.Value<string>();
            var layer = result.Data?["layer"]?.Value<string>();
            var summary = status + "，用时 " + result.ElapsedMs + " ms";
            if (!string.IsNullOrWhiteSpace(entity)) summary += "，对象 " + entity;
            if (!string.IsNullOrWhiteSpace(layer)) summary += "，图层 " + layer;
            if (!string.IsNullOrWhiteSpace(result.Error)) summary += "，原因：" + result.Error;
            return summary;
        }

        private static string SummarizeRemoteToolResult(string name, JObject result, long elapsedMs)
        {
            var success = result?.Value<bool?>("success") ?? false;
            var status = success ? "成功" : "失败";
            var text = status + "，用时 " + elapsedMs + " ms，" + name;
            var message = result?.Value<string>("message") ?? result?.Value<string>("error");
            if (!string.IsNullOrWhiteSpace(message)) text += "：" + message;
            return text;
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
            if (string.Equals(result.ToolName, "cad.validate_dwg_state", StringComparison.OrdinalIgnoreCase))
            {
                var passed = result.Data?.Value<bool?>("passed");
                var checks = result.Data?["checks"] as JArray;
                var textValidation = status + "，用时 " + result.ElapsedMs + " ms。DWG 验证" +
                    (passed.HasValue ? (passed.Value ? "通过。" : "未通过。") : "完成。");
                if (checks != null)
                {
                    foreach (var check in checks.OfType<JObject>().Take(12))
                    {
                        textValidation += "\r\n- " + check.Value<string>("name") + ": " +
                            ((check.Value<bool?>("passed") ?? false) ? "通过" : "失败") +
                            " " + check.Value<string>("detail");
                    }
                }
                return textValidation;
            }
            if (string.Equals(result.ToolName, "cad.measure_bounds", StringComparison.OrdinalIgnoreCase))
            {
                var count = result.Data?.Value<int?>("count") ?? 0;
                var bounds = result.Data?["bounds"] as JObject;
                var textBounds = status + "，用时 " + result.ElapsedMs + " ms。测量对象 " + count + " 个。";
                if (bounds != null)
                {
                    textBounds += "\r\n宽: " + FormatNumber(bounds.Value<double?>("width")) +
                        "，高: " + FormatNumber(bounds.Value<double?>("height")) +
                        "，深: " + FormatNumber(bounds.Value<double?>("depth"));
                }
                return textBounds;
            }
            if (string.Equals(result.ToolName, "cad.count_entities", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ToolName, "cad.preview_plan", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ToolName, "cad.layer_diff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ToolName, "cad.before_after_diff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ToolName, "cad.measure_distance", StringComparison.OrdinalIgnoreCase))
            {
                return status + "，用时 " + result.ElapsedMs + " ms。\r\n" +
                       TruncateForCard(result.Data?.ToString(Formatting.Indented) ?? "", 1200);
            }
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

        private static string FormatNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        }

        // --- Settings tab actions ---

        private static readonly Dictionary<string, (string BaseUrl, string Model)> ProviderDefaults =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai",    ("https://api.openai.com", "gpt-5") },
                { "deepseek",  ("https://api.deepseek.com", "deepseek-v4-flash") },
                { "anthropic", ("https://api.anthropic.com", "claude-fable-5") },
                { "gemini",    ("https://generativelanguage.googleapis.com", "gemini-3.5-flash") },
                { "ollama",    ("http://localhost:11434", "llama3.2") },
                { "custom",    ("", "") },
            };

        private static readonly Dictionary<string, string[]> ProviderModels =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", new[] { "gpt-5", "gpt-5.5", "gpt-5.4", "gpt-5.4-mini", "gpt-4.1", "gpt-4o" } },
                { "deepseek", new[] { "deepseek-v4-flash", "deepseek-v4-pro" } },
                { "anthropic", new[] { "claude-fable-5", "claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5" } },
                { "gemini", new[] { "gemini-3.5-flash", "gemini-3.1-pro-preview", "gemini-3-flash-preview", "gemini-3.1-flash-lite", "gemini-2.5-flash" } },
                { "ollama", new[] { "llama3.2", "qwen2.5", "deepseek-r1" } },
                { "custom", new string[0] },
            };

        private static readonly Dictionary<string, string[]> LegacyDefaultModels =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", new[] { "gpt-5.5", "gpt-4o-mini" } },
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
            SetComboDropDownWidth(_cmbModel, 340);
        }

        private static void SetComboDropDownWidth(ComboBox combo, int minWidth)
        {
            if (combo == null) return;
            var width = Math.Max(minWidth, combo.Width);
            using (var g = combo.CreateGraphics())
            {
                foreach (var item in combo.Items)
                {
                    var text = Convert.ToString(item) ?? "";
                    var measured = (int)Math.Ceiling(g.MeasureString(text, combo.Font).Width) + 36;
                    if (measured > width) width = measured;
                }
            }
            combo.DropDownWidth = width;
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
            SetComboDropDownWidth(_cmbProfile, 260);
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
                    SetLlmConnectionStatus("● LLM 未连", CadOrange);
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
                SetLlmConnectionStatus(result.Success ? "● LLM 在线" : "● LLM 失败", result.Success ? CadGreen : CadOrange);
                SetSettingsStatus(result.Message, result.Success ? CadGreen : CadOrange);
            }
            catch (System.Exception ex)
            {
                SetLlmConnectionStatus("● LLM 失败", CadOrange);
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

    internal class HeaderDrivenTabControl : TabControl
    {
        public override Rectangle DisplayRectangle
        {
            get
            {
                var rect = base.DisplayRectangle;
                return new Rectangle(rect.Left - 4, rect.Top - 4, rect.Width + 8, rect.Height + 8);
            }
        }
    }

}
