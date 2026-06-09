namespace Vcad.Plugin.UI
{
    /// <summary>
    /// All user-visible UI strings live here so we can swap the language
    /// (zh-CN today; en-US is on the roadmap) without changing layout
    /// code. Error codes stay English on purpose — only messages change.
    /// </summary>
    internal static class Strings
    {
        // Tabs
        public const string TabDslInput = "DSL 输入";
        public const string TabModelSettings = "模型设置";

        // DSL Input tab — groups & buttons
        public const string GroupNaturalLanguage = "1. 自然语言 (可选,调用 Agent Lite)";
        public const string GroupDslJson = "2. VCAD DSL JSON (粘贴或由 Agent 自动填入)";
        public const string GroupResultLog = "3. 执行结果 / 日志";
        public const string BtnParseViaAgent = "通过 Agent 解析";
        public const string BtnRunDsl = "运行 DSL";
        public const string BtnLoadSample = "载入示例";

        // Model Settings tab — labels
        public const string LblProfile = "Profile";
        public const string LblProvider = "供应商";
        public const string LblApiBaseUrl = "API Base URL";
        public const string LblModel = "模型";
        public const string LblApiKey = "API Key";
        public const string LblAgentPort = "Agent 端口";
        public const string LblStrictJson = "严格 JSON";
        public const string LblTimeoutSec = "超时 (秒)";
        public const string LblAutoRun = "解析后自动运行 DSL";
        public const string BtnNewProfile = "+ 新建";
        public const string BtnDeleteProfile = "删除";
        public const string BtnShowKey = "显示";
        public const string BtnTestConnection = "测试连接";
        public const string BtnSave = "保存";
        public const string BtnOk = "确定";
        public const string BtnCancel = "取消";
        public const string PromptProfileName = "Profile 名称";

        // Privacy / disclaimer line under Model Settings
        public const string PrivacyNotice =
            "VCAD 不会上传你的 Key。Key 通过 Windows DPAPI (CurrentUser)\r\n" +
            "加密后保存在 %APPDATA%\\VCAD\\agent.config.json。";

        // Status bar
        public const string StatusReady = "就绪。";
        public const string StatusExecuting = "执行中...";
        public const string StatusValidating = "校验 DSL...";
        public const string StatusLocking = "锁定文档,执行事务...";
        public const string StatusDone = "完成。";
        public const string StatusDoneWithMs = "完成。(耗时 {0} ms)";
        public const string StatusWithErrors = "已完成但存在错误。";
        public const string StatusError = "错误。";
        public const string StatusCallingAgent = "正在调用 Agent Lite...";
        public const string StatusAgentReturned = "Agent 已返回 DSL,请审阅。";
        public const string StatusAgentReturnedAutoRun = "Agent 已返回,正在自动执行...";
        public const string StatusAgentError = "Agent 错误。";
        public const string StatusTesting = "测试中...";
        public const string StatusConnectionOk = "连接成功。";
        public const string StatusConnectionFailed = "连接失败。";
        public const string StatusSettingsSaved = "设置已保存。";

        // Log messages
        public const string LogSampleLoaded = "已载入示例。";
        public const string LogPasteDslFirst = "请先粘贴 VCAD DSL JSON。";
        public const string LogUnexpectedError = "意外错误: ";
        public const string LogTypeNlFirst = "请先在自然语言框中输入内容。";
        public const string LogAgentReturnedNoDsl = "Agent 未返回 DSL。";
        public const string LogAgentReturnedReview = "Agent 已返回 DSL,请审阅后点击「" + BtnRunDsl + "」。";
        public const string LogAgentReturnedAutoRun = "Agent 已返回 DSL,正在自动执行...";
        public const string LogAgentCallFailed = "Agent 调用失败: ";
        public const string LogSettingsSavedTo = "设置已保存到 ";
        public const string LogFailedToSave = "保存设置失败: ";
        public const string LogHealthOk = "Agent /health 返回成功。";
        public const string LogHealthFailed = "Agent /health 失败。";
        public const string LogTestFailed = "测试失败: ";
        public const string LogAgentTimingMs = "[Agent] 耗时 {0} ms";
        public const string LogExecTimingMs = "[执行] 耗时 {0} ms";
    }
}
