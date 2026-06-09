using System;
using System.Collections.Generic;
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
        private Button _btnTestConnection;
        private Button _btnSaveSettings;
        private ComboBox _cmbProfile;
        private Button _btnNewProfile;
        private Button _btnDeleteProfile;
        private Label _lblStatus;

        public SidebarControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            BuildLayout();
            LoadProfilesIntoUi();
        }

        private void BuildLayout()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };

            var dslTab = new TabPage("DSL Input");
            BuildDslTab(dslTab);
            _tabs.TabPages.Add(dslTab);

            var settingsTab = new TabPage("Model Settings");
            BuildSettingsTab(settingsTab);
            _tabs.TabPages.Add(settingsTab);

            Controls.Add(_tabs);
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
                Text = "1. Natural language  (optional, calls Agent Lite)",
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
                Text = "Parse via Agent",
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
                Text = "2. VCAD DSL JSON  (paste or auto-filled by Agent)",
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
            _btnRun = new Button { Text = "Run DSL", Width = 110, Height = 28 };
            _btnSample = new Button { Text = "Load Sample", Width = 110, Height = 28 };
            _btnRun.Click += (s, e) => OnRunDsl();
            _btnSample.Click += (s, e) => OnLoadSample();
            btnPanel.Controls.Add(_btnRun);
            btnPanel.Controls.Add(_btnSample);
            layout.Controls.Add(btnPanel, 0, 2);

            var logGroup = new GroupBox
            {
                Text = "3. Result / log",
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

            _lblStatus = new Label { Text = "Ready.", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            layout.Controls.Add(_lblStatus, 0, 4);

            tab.Controls.Add(layout);
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
            panel.Controls.Add(new Label { Text = "Profile", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var profileRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _cmbProfile = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProfile.SelectedIndexChanged += (s, e) => OnProfileChanged();
            _btnNewProfile = new Button { Text = "+ New", Width = 60 };
            _btnNewProfile.Click += (s, e) => OnNewProfile();
            _btnDeleteProfile = new Button { Text = "Delete", Width = 60 };
            _btnDeleteProfile.Click += (s, e) => OnDeleteProfile();
            profileRow.Controls.Add(_cmbProfile);
            profileRow.Controls.Add(_btnNewProfile);
            profileRow.Controls.Add(_btnDeleteProfile);
            panel.Controls.Add(profileRow, 1, row++);

            panel.Controls.Add(new Label { Text = "Provider", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _cmbProvider = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProvider.Items.AddRange(new object[] { "openai", "anthropic", "gemini", "ollama", "custom" });
            _cmbProvider.SelectedIndexChanged += (s, e) => OnProviderChanged();
            panel.Controls.Add(_cmbProvider, 1, row++);

            panel.Controls.Add(new Label { Text = "API Base URL", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtBaseUrl = new TextBox { Width = 320 };
            panel.Controls.Add(_txtBaseUrl, 1, row++);

            panel.Controls.Add(new Label { Text = "Model", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _txtModel = new TextBox { Width = 220 };
            panel.Controls.Add(_txtModel, 1, row++);

            panel.Controls.Add(new Label { Text = "API Key", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var keyRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _txtApiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
            _chkShowKey = new CheckBox { Text = "Show", AutoSize = true };
            _chkShowKey.CheckedChanged += (s, e) => _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
            keyRow.Controls.Add(_txtApiKey);
            keyRow.Controls.Add(_chkShowKey);
            panel.Controls.Add(keyRow, 1, row++);

            panel.Controls.Add(new Label { Text = "Agent Port", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _numPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 8765, Width = 90 };
            panel.Controls.Add(_numPort, 1, row++);

            panel.Controls.Add(new Label { Text = "Strict JSON", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _chkStrictJson = new CheckBox { Checked = true, AutoSize = true };
            panel.Controls.Add(_chkStrictJson, 1, row++);

            panel.Controls.Add(new Label { Text = "Timeout (s)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            _numTimeout = new NumericUpDown { Minimum = 5, Maximum = 600, Value = 30, Width = 90 };
            panel.Controls.Add(_numTimeout, 1, row++);

            var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnTestConnection = new Button { Text = "Test Connection", Width = 140, Height = 28 };
            _btnTestConnection.Click += async (s, e) => await OnTestConnectionAsync();
            _btnSaveSettings = new Button { Text = "Save", Width = 100, Height = 28 };
            _btnSaveSettings.Click += (s, e) => OnSaveSettings();
            actionRow.Controls.Add(_btnTestConnection);
            actionRow.Controls.Add(_btnSaveSettings);
            panel.Controls.Add(actionRow, 1, row++);

            var notice = new Label
            {
                Text = "VCAD never uploads your key. The key is encrypted with Windows DPAPI" + Environment.NewLine +
                       "(CurrentUser) and stored under %APPDATA%\\VCAD\\agent.config.json.",
                ForeColor = Color.DimGray,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 0),
            };
            panel.SetColumnSpan(notice, 2);
            panel.Controls.Add(notice, 0, row++);

            tab.Controls.Add(panel);
        }

        // --- DSL tab actions ---

        private void OnLoadSample()
        {
            _txtDsl.Text = SampleDsl.RectangleAndText;
            AppendLog("Sample loaded.");
        }

        private void OnRunDsl()
        {
            var json = _txtDsl.Text;
            if (string.IsNullOrWhiteSpace(json))
            {
                AppendLog("Paste a VCAD DSL JSON first.");
                return;
            }
            try
            {
                _btnRun.Enabled = false;
                _lblStatus.Text = "Executing...";

                var result = DslExecutor.Execute(json);
                var pretty = JsonConvert.SerializeObject(result, Formatting.Indented);
                AppendLog(pretty);
                _lblStatus.Text = result.Success ? "Done." : "Completed with errors.";
            }
            catch (Exception ex)
            {
                AppendLog("Unexpected error: " + ex.Message);
                _lblStatus.Text = "Error.";
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
                AppendLog("Type something in the natural-language box first.");
                return;
            }

            try
            {
                _btnUseAgent.Enabled = false;
                _lblStatus.Text = "Calling Agent Lite...";
                var settings = AgentConfigStore.LoadActive();
                var client = new AgentLiteClient(settings);
                var dsl = await client.ParseAsync(text);
                if (dsl == null)
                {
                    AppendLog("Agent returned no DSL.");
                    return;
                }
                _txtDsl.Text = dsl;
                AppendLog("Agent returned DSL. Review then click 'Run DSL'.");
                _lblStatus.Text = "Agent returned DSL.";
            }
            catch (Exception ex)
            {
                AppendLog("Agent call failed: " + ex.Message);
                _lblStatus.Text = "Agent error.";
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
            var name = Prompt("Profile name", "");
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
                store.UpsertProfile(s);
                store.ActiveProfileName = name;
                AgentConfigStore.SaveAll(store);
                _lblStatus.Text = "Settings saved.";
                AppendLog("Settings saved to " + AgentConfigStore.ConfigPath);
            }
            catch (Exception ex)
            {
                AppendLog("Failed to save settings: " + ex.Message);
            }
        }

        private async Task OnTestConnectionAsync()
        {
            try
            {
                _btnTestConnection.Enabled = false;
                _lblStatus.Text = "Testing...";
                OnSaveSettings();
                var settings = AgentConfigStore.LoadActive();
                var client = new AgentLiteClient(settings);
                var ok = await client.HealthAsync();
                _lblStatus.Text = ok ? "Connection OK." : "Connection failed.";
                AppendLog(ok ? "Agent /health returned OK." : "Agent /health failed.");
            }
            catch (Exception ex)
            {
                AppendLog("Test failed: " + SecretRedactor.Redact(ex.Message));
                _lblStatus.Text = "Connection failed.";
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
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 150, Top = 50, Width = 70 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 230, Top = 50, Width = 70 };
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
