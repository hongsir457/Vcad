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
        private TabControl _tabs;

        // DSL Tab controls
        private TextBox _txtDsl;
        private Button _btnRun;
        private Button _btnSample;
        private Button _btnUseAgent;
        private TextBox _txtNaturalLanguage;
        private TextBox _txtLog;

        // Settings Tab controls
        private ComboBox _cmbProvider;
        private TextBox _txtBaseUrl;
        private TextBox _txtModel;
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
        private Label _lblStatus;

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
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CadBg,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildHeader(), 0, 0);

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

            var dslTab = new TabPage(Strings.TabDslInput);
            dslTab.BackColor = CadBg;
            BuildDslTab(dslTab);
            _tabs.TabPages.Add(dslTab);

            var settingsTab = new TabPage(Strings.TabModelSettings);
            settingsTab.BackColor = CadBg;
            BuildSettingsTab(settingsTab);
            _tabs.TabPages.Add(settingsTab);

            root.Controls.Add(_tabs, 0, 1);
            Controls.Add(root);
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
            header.Resize += (s, e) => online.Left = Math.Max(160, header.Width - online.Width - 12);
            header.Controls.Add(mark);
            header.Controls.Add(title);
            header.Controls.Add(online);
            return header;
        }

        private void BuildDslTab(TabPage tab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            // Top: natural-language box with a clear title and a Parse button.
            var nlGroup = new GroupBox
            {
                Text = Strings.GroupNaturalLanguage,
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 4),
            };
            var nlInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
            };
            nlInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nlInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            nlInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _txtNaturalLanguage = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Microsoft YaHei", 9F),
            };
            _btnUseAgent = new Button
            {
                Text = Strings.BtnParseViaAgent,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0),
            };
            _btnUseAgent.Click += async (s, e) => await OnUseAgentAsync();
            nlInner.Controls.Add(_txtNaturalLanguage, 0, 0);
            nlInner.Controls.Add(_btnUseAgent, 1, 0);
            nlGroup.Controls.Add(nlInner);
            layout.Controls.Add(nlGroup, 0, 0);

            // Middle: DSL JSON box.
            var dslGroup = new GroupBox
            {
                Text = Strings.GroupDslJson,
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 4),
            };
            _txtDsl = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                AcceptsReturn = true,
                AcceptsTab = true,
                Font = new Font("Consolas", 10F),
            };
            dslGroup.Controls.Add(_txtDsl);
            layout.Controls.Add(dslGroup, 0, 1);

            var btnPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Padding = new Padding(0, 4, 0, 4),
            };
            _btnRun = new Button { Text = Strings.BtnRunDsl, Width = 110, Height = 28 };
            _btnSample = new Button { Text = Strings.BtnLoadSample, Width = 110, Height = 28 };
            _btnRun.Click += (s, e) => OnRunDsl();
            _btnSample.Click += (s, e) => OnLoadSample();
            btnPanel.Controls.Add(_btnRun);
            btnPanel.Controls.Add(_btnSample);
            layout.Controls.Add(btnPanel, 0, 2);

            var logGroup = new GroupBox
            {
                Text = Strings.GroupResultLog,
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 4),
            };
            _txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(0xF8, 0xFA, 0xFC),
            };
            logGroup.Controls.Add(_txtLog);
            layout.Controls.Add(logGroup, 0, 3);

            _lblStatus = new Label { Text = Strings.StatusReady, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            layout.Controls.Add(_lblStatus, 0, 4);

            tab.Controls.Add(layout);
            ApplyIndustrialStyle(tab);
            StylePrimaryButton(_btnUseAgent);
            StylePrimaryButton(_btnRun);
            StyleGhostButton(_btnSample);
            _txtLog.BackColor = Color.FromArgb(0x0E, 0x0E, 0x0E);
            _txtDsl.BackColor = Color.FromArgb(0x0E, 0x0E, 0x0E);
            _lblStatus.ForeColor = CadGreen;
        }

        private void BuildSettingsTab(TabPage tab)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 12,
                Padding = new Padding(10),
                AutoSize = false,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 12; i++)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            }

            int row = 0;
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

            panel.Controls.Add(new Label { Text = Strings.LblProvider, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbProvider = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProvider.Items.AddRange(new object[] { "openai", "deepseek", "anthropic", "gemini", "ollama", "custom" });
            _cmbProvider.SelectedIndexChanged += (s, e) => OnProviderChanged();
            panel.Controls.Add(_cmbProvider, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblApiBaseUrl, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtBaseUrl = new TextBox { Width = 320 };
            panel.Controls.Add(_txtBaseUrl, 1, row++);

            panel.Controls.Add(new Label { Text = Strings.LblModel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtModel = new TextBox { Width = 220 };
            panel.Controls.Add(_txtModel, 1, row++);

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

        // --- DSL tab actions ---

        private void OnLoadSample()
        {
            _txtDsl.Text = SampleDsl.RectangleAndText;
            AppendLog(Strings.LogSampleLoaded);
        }

        private void OnRunDsl()
        {
            var json = _txtDsl.Text;
            if (string.IsNullOrWhiteSpace(json))
            {
                AppendLog(Strings.LogPasteDslFirst);
                return;
            }
            try
            {
                _btnRun.Enabled = false;
                _lblStatus.Text = Strings.StatusValidating;
                System.Windows.Forms.Application.DoEvents();

                var sw = Stopwatch.StartNew();
                _lblStatus.Text = Strings.StatusLocking;
                System.Windows.Forms.Application.DoEvents();

                var result = DslExecutor.Execute(json);
                sw.Stop();

                var pretty = JsonConvert.SerializeObject(result, Formatting.Indented);
                AppendLog(pretty);
                AppendLog(string.Format(Strings.LogExecTimingMs, sw.ElapsedMilliseconds));
                _lblStatus.Text = result.Success
                    ? string.Format(Strings.StatusDoneWithMs, sw.ElapsedMilliseconds)
                    : Strings.StatusWithErrors;
            }
            catch (System.Exception ex)
            {
                AppendLog(Strings.LogUnexpectedError + ex.Message);
                _lblStatus.Text = Strings.StatusError;
            }
            finally
            {
                _btnRun.Enabled = true;
            }
        }

        private async Task OnUseAgentAsync()
        {
            var text = _txtNaturalLanguage.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendLog(Strings.LogTypeNlFirst);
                return;
            }

            bool autoRun = false;
            try
            {
                _btnUseAgent.Enabled = false;
                _lblStatus.Text = Strings.StatusCallingAgent;
                System.Windows.Forms.Application.DoEvents();

                var sw = Stopwatch.StartNew();
                var settings = AgentConfigStore.LoadActive();
                autoRun = settings.AutoRunAfterParse;
                var client = new AgentLiteClient(settings);
                var dsl = await client.ParseAsync(text);
                sw.Stop();
                AppendLog(string.Format(Strings.LogAgentTimingMs, sw.ElapsedMilliseconds));

                if (dsl == null)
                {
                    AppendLog(Strings.LogAgentReturnedNoDsl);
                    _lblStatus.Text = Strings.StatusAgentError;
                    return;
                }
                _txtDsl.Text = dsl;

                if (autoRun)
                {
                    AppendLog(Strings.LogAgentReturnedAutoRun);
                    _lblStatus.Text = Strings.StatusAgentReturnedAutoRun;
                    System.Windows.Forms.Application.DoEvents();
                    OnRunDsl();
                }
                else
                {
                    AppendLog(Strings.LogAgentReturnedReview);
                    _lblStatus.Text = Strings.StatusAgentReturned;
                }
            }
            catch (System.Exception ex)
            {
                AppendLog(Strings.LogAgentCallFailed + SecretRedactor.Redact(ex.Message));
                _lblStatus.Text = Strings.StatusAgentError;
            }
            finally
            {
                _btnUseAgent.Enabled = true;
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
            if (string.IsNullOrWhiteSpace(_txtModel.Text) || IsAnyKnownDefaultModel(_txtModel.Text))
            {
                _txtModel.Text = def.Model;
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

                _txtBaseUrl.Text = !string.IsNullOrEmpty(s.ApiBaseUrl)
                    ? s.ApiBaseUrl
                    : (hasDefault ? def.BaseUrl : "");
                _txtModel.Text = !string.IsNullOrEmpty(s.Model)
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
                s.Model = _txtModel.Text.Trim();
                s.ApiKeyPlain = _txtApiKey.Text;
                s.AgentPort = (int)_numPort.Value;
                s.StrictJson = _chkStrictJson.Checked;
                s.TimeoutSeconds = (int)_numTimeout.Value;
                s.AutoRunAfterParse = _chkAutoRun.Checked;
                store.UpsertProfile(s);
                store.ActiveProfileName = name;
                AgentConfigStore.SaveAll(store);
                _lblStatus.Text = Strings.StatusSettingsSaved;
                AppendLog(Strings.LogSettingsSavedTo + AgentConfigStore.ConfigPath);
            }
            catch (System.Exception ex)
            {
                AppendLog(Strings.LogFailedToSave + ex.Message);
            }
        }

        private async Task OnTestConnectionAsync()
        {
            try
            {
                _btnTestConnection.Enabled = false;
                _lblStatus.Text = Strings.StatusTesting;
                System.Windows.Forms.Application.DoEvents();
                OnSaveSettings();
                var settings = AgentConfigStore.LoadActive();
                var client = new AgentLiteClient(settings);
                var ok = await client.HealthAsync();
                _lblStatus.Text = ok ? Strings.StatusConnectionOk : Strings.StatusConnectionFailed;
                AppendLog(ok ? Strings.LogHealthOk : Strings.LogHealthFailed);
            }
            catch (System.Exception ex)
            {
                AppendLog(Strings.LogTestFailed + SecretRedactor.Redact(ex.Message));
                _lblStatus.Text = Strings.StatusConnectionFailed;
            }
            finally
            {
                _btnTestConnection.Enabled = true;
            }
        }

        private void AppendLog(string text)
        {
            if (_txtLog == null) return;
            _txtLog.AppendText(text);
            _txtLog.AppendText(Environment.NewLine);
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
