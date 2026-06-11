#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;
using Vcad.Core.Validation;

namespace Vcad.Plugin.Pipeline
{
    internal sealed class CadPipelineCandidate
    {
        public string RequestId { get; set; }
        public string NaturalLanguage { get; set; }
        public JObject Intent { get; set; }
        public JObject TaskPlan { get; set; }
        public JObject CadIr { get; set; }
        public JObject Preview { get; set; }
        public SafetyReport Safety { get; set; }
        public string InterpreterDsl { get; set; }
        public bool Confirmed { get; set; }
        public bool SecondConfirmed { get; set; }

        public string IntentTitle => Intent.Value<string>("intent") ?? "cad_task";
        public string TaskType => TaskPlan.Value<string>("task_type") ?? "cad_operation";
        public string RiskLevel => Safety?.RiskLevel ?? "medium";
        public bool RequiresSecondConfirmation => Safety?.RequiresSecondConfirmation == true;
    }

    internal sealed class SafetyReport
    {
        public bool IsAllowed { get; set; }
        public string RiskLevel { get; set; }
        public bool RequiresConfirmation { get; set; }
        public bool RequiresSecondConfirmation { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Blocks { get; } = new List<string>();
    }

    internal static class CadAgentPipeline
    {
        private static readonly string[] SecondConfirmRiskWords =
        {
            "scale", "global replace", "replace all", "batch", "all drawings",
            "缩放", "全局替换", "全部替换", "批量", "所有图纸", "全图"
        };

        private static readonly string[] BlockedRiskWords =
        {
            "delete", "erase", "purge", "explode", "block definition", "xref",
            "删除", "清理", "炸开", "分解", "块定义", "外部参照"
        };

        private static readonly string[] ScriptLikeFields =
        {
            "script", "lisp", "autolisp", "raw_command", "raw_lisp", "command_text"
        };

        public static CadPipelineCandidate Interpret(string naturalLanguage, string adapterDraftDsl)
        {
            var requestId = "task-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var validation = DslValidator.ParseAndValidate(adapterDraftDsl);
            var parsed = TryParseObject(adapterDraftDsl);

            var candidate = new CadPipelineCandidate
            {
                RequestId = requestId,
                NaturalLanguage = naturalLanguage,
                InterpreterDsl = adapterDraftDsl,
                Intent = BuildIntent(naturalLanguage, parsed),
                TaskPlan = BuildTaskPlan(naturalLanguage, parsed),
                CadIr = BuildCadIr(naturalLanguage, parsed),
            };

            candidate.Safety = CheckSafety(naturalLanguage, parsed, validation);
            candidate.Preview = BuildPreview(candidate, parsed);
            return candidate;
        }

        public static JObject AdaptToAdapterCommand(CadPipelineCandidate candidate)
        {
            if (candidate == null)
            {
                throw new InvalidOperationException("No CAD-IR candidate to execute.");
            }
            if (!candidate.Confirmed)
            {
                throw new InvalidOperationException("CAD-IR has not been confirmed by the user.");
            }
            if (candidate.Safety == null || !candidate.Safety.IsAllowed)
            {
                throw new InvalidOperationException("CAD-IR did not pass safety checks.");
            }
            if (candidate.Safety.RequiresSecondConfirmation && !candidate.SecondConfirmed)
            {
                throw new InvalidOperationException("High-risk CAD-IR requires second confirmation.");
            }

            ValidateCadIr(candidate.CadIr);
            var command = BuildDslFromCadIr(candidate.CadIr);
            var validation = DslValidator.ParseAndValidate(command);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException("Adapter output is invalid: " + validation.ErrorMessage);
            }

            return new JObject
            {
                ["schema"] = "vcad_adapter_command_v1",
                ["adapter"] = candidate.CadIr["execution"]?["adapter"]?.Value<string>() ?? "autocad_dotnet_adapter",
                ["command_type"] = "vcad_dsl",
                ["source_schema"] = "cad_ir_v1",
                ["request_id"] = candidate.RequestId,
                ["safe_to_execute"] = true,
                ["command"] = command,
            };
        }

        public static string AdaptToDsl(CadPipelineCandidate candidate)
        {
            return AdaptToAdapterCommand(candidate).Value<string>("command");
        }

        private static void ValidateCadIr(JObject cadIr)
        {
            var schema = cadIr?.Value<string>("schema");
            if (!string.Equals(schema, "cad_ir_v1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid CAD-IR schema.");
            }

            var action = cadIr.Value<string>("action");
            if (string.IsNullOrEmpty(action) || action != "create_geometry")
            {
                throw new InvalidOperationException("CAD-IR action is not supported by this adapter.");
            }

            if (!(cadIr["selector"] is JObject))
            {
                throw new InvalidOperationException("CAD-IR is missing selector.");
            }
            if (!(cadIr["execution"] is JObject execution) || execution.Value<bool?>("undo_group") != true)
            {
                throw new InvalidOperationException("CAD-IR execution must request an undo group.");
            }
            if (!(cadIr["safety"] is JObject))
            {
                throw new InvalidOperationException("CAD-IR is missing safety metadata.");
            }
            if (!(cadIr["reporting"] is JObject))
            {
                throw new InvalidOperationException("CAD-IR is missing reporting metadata.");
            }
        }

        public static string FormatIntent(CadPipelineCandidate candidate)
        {
            return "Intent: " + candidate.IntentTitle + "\r\n" +
                   "目标: " + (candidate.Intent["target"]?["summary"]?.Value<string>() ?? "model space") + "\r\n" +
                   "来源: natural language -> structured intent";
        }

        public static string FormatPlan(CadPipelineCandidate candidate)
        {
            var steps = candidate.TaskPlan["steps"] as JArray;
            var stepText = steps == null
                ? ""
                : string.Join("\r\n", steps.Select(s => "- " + (s["description"]?.Value<string>() ?? s["name"]?.Value<string>())));
            return "Task Plan: " + candidate.TaskType + "\r\n" +
                   "风险: " + candidate.RiskLevel + "\r\n" +
                   "需要确认: " + (candidate.Safety.RequiresConfirmation ? "是" : "否") + "\r\n" +
                   "二次确认: " + (candidate.Safety.RequiresSecondConfirmation ? "是" : "否") + "\r\n" +
                   stepText;
        }

        public static string FormatPreview(CadPipelineCandidate candidate)
        {
            var impact = candidate.Preview["impact"];
            var operation = candidate.Preview["operation"];
            var warnings = candidate.Safety.Warnings.Count == 0
                ? "无"
                : string.Join("; ", candidate.Safety.Warnings);
            var blocks = candidate.Safety.Blocks.Count == 0
                ? ""
                : "\r\n阻止: " + string.Join("; ", candidate.Safety.Blocks);

            return "Preview / Dry Run\r\n" +
                   "操作: " + (operation?["action"]?.Value<string>() ?? "cad_operation") + "\r\n" +
                   "命令数: " + (impact?["command_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "图层: " + (impact?["layers"]?.ToString(Formatting.None) ?? "[]") + "\r\n" +
                   "对象类型: " + (impact?["entity_types"]?.ToString(Formatting.None) ?? "[]") + "\r\n" +
                   "风险等级: " + candidate.RiskLevel + "\r\n" +
                   "二次确认: " + (candidate.Safety.RequiresSecondConfirmation ? "是" : "否") + "\r\n" +
                   "可撤销: 是，确认后才会修改图纸\r\n" +
                   "警告: " + warnings + blocks;
        }

        private static JObject TryParseObject(string json)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return new JObject();
            }
        }

        private static JObject BuildIntent(string nl, JObject dsl)
        {
            var commandTypes = CommandTypes(dsl).ToArray();
            var intent = commandTypes.Any(t => t.StartsWith("draw_", StringComparison.Ordinal) || t == "create_layer")
                ? "create_geometry"
                : "cad_operation";

            return new JObject
            {
                ["schema"] = "cad_intent_v1",
                ["intent"] = intent,
                ["source"] = "natural_language",
                ["utterance"] = nl,
                ["target"] = new JObject
                {
                    ["space"] = "model",
                    ["summary"] = SummarizeTarget(dsl),
                },
                ["constraints"] = new JObject
                {
                    ["unit"] = dsl.Value<string>("unit") ?? "mm",
                    ["coordinate_system"] = dsl.Value<string>("coordinate_system") ?? "WCS",
                },
            };
        }

        private static JObject BuildTaskPlan(string nl, JObject dsl)
        {
            var commandCount = Commands(dsl).Count();
            return new JObject
            {
                ["schema"] = "cad_task_plan_v1",
                ["task_type"] = "create_geometry",
                ["risk_level"] = ResolveRiskLevel(nl),
                ["requires_confirmation"] = true,
                ["steps"] = new JArray
                {
                    Step("intent", "parse_intent", "把自然语言转换为结构化 Intent"),
                    Step("plan", "build_task_plan", "生成可展示的 Task Plan"),
                    Step("ir", "generate_cad_ir", "把任务转换为 CAD-IR"),
                    Step("safety", "run_safety_checker", "校验 CAD-IR 和风险策略"),
                    Step("preview", "dry_run_preview", "展示影响范围，等待确认"),
                    Step("adapter", "adapt_for_autocad_dotnet", "确认后由 Adapter 转换为 AutoCAD 执行请求"),
                    Step("report", "report_result", "返回结构化执行结果"),
                },
                ["estimated_commands"] = commandCount,
            };
        }

        private static JObject BuildCadIr(string nl, JObject dsl)
        {
            var commands = Commands(dsl).ToArray();
            return new JObject
            {
                ["schema"] = "cad_ir_v1",
                ["action"] = "create_geometry",
                ["selector"] = new JObject
                {
                    ["space"] = "model",
                    ["selection_scope"] = "explicit_commands",
                    ["layers"] = new JArray(Layers(dsl)),
                    ["entity_types"] = new JArray(EntityTypes(commands)),
                },
                ["operation"] = new JObject
                {
                    ["type"] = "execute_structured_commands",
                    ["unit"] = dsl.Value<string>("unit") ?? "mm",
                    ["coordinate_system"] = dsl.Value<string>("coordinate_system") ?? "WCS",
                    ["commands"] = new JArray(commands.Select(c => c.DeepClone())),
                },
                ["execution"] = new JObject
                {
                    ["adapter"] = "autocad_dotnet_adapter",
                    ["undo_group"] = true,
                    ["max_affected_objects"] = Math.Max(1, commands.Length),
                    ["source_format"] = "vcad_dsl_v1",
                },
                ["safety"] = new JObject
                {
                    ["risk_level"] = ResolveRiskLevel(nl),
                    ["requires_confirmation"] = true,
                    ["requires_second_confirmation"] = RequiresSecondConfirmation(nl),
                },
                ["reporting"] = new JObject
                {
                    ["return_object_ids"] = true,
                    ["return_handles"] = true,
                    ["return_errors"] = true,
                },
            };
        }

        private static SafetyReport CheckSafety(string nl, JObject dsl, ValidationResult validation)
        {
            var safety = new SafetyReport
            {
                RiskLevel = ResolveRiskLevel(nl),
                RequiresConfirmation = true,
                RequiresSecondConfirmation = RequiresSecondConfirmation(nl),
                IsAllowed = validation.IsValid && !ContainsBlockedOperation(nl),
            };

            if (!validation.IsValid)
            {
                safety.Blocks.Add(validation.ErrorCode + ": " + validation.ErrorMessage);
            }
            if (ContainsBlockedOperation(nl))
            {
                safety.Blocks.Add("当前版本禁止删除、炸开、清理、块定义或外部参照类操作进入 Adapter。");
            }
            if (!Commands(dsl).Any())
            {
                safety.Blocks.Add("CAD-IR 没有可适配的命令。");
                safety.IsAllowed = false;
            }
            if (ContainsScriptLikePayload(dsl))
            {
                safety.Blocks.Add("CAD-IR 不允许携带自由脚本、AutoLISP 或原始命令文本。");
                safety.IsAllowed = false;
            }
            if (safety.Blocks.Count == 0)
            {
                safety.Warnings.Add("当前操作会修改图纸；执行前会创建 AutoCAD undo group。");
            }
            return safety;
        }

        private static JObject BuildPreview(CadPipelineCandidate candidate, JObject dsl)
        {
            var commands = Commands(dsl).ToArray();
            return new JObject
            {
                ["schema"] = "cad_preview_v1",
                ["status"] = candidate.Safety.IsAllowed ? "preview_ready" : "blocked",
                ["task_type"] = candidate.TaskType,
                ["risk_level"] = candidate.RiskLevel,
                ["requires_confirmation"] = candidate.Safety.RequiresConfirmation,
                ["requires_second_confirmation"] = candidate.Safety.RequiresSecondConfirmation,
                ["impact"] = new JObject
                {
                    ["command_count"] = commands.Length,
                    ["matched_count"] = commands.Length,
                    ["modifiable_count"] = candidate.Safety.IsAllowed ? commands.Length : 0,
                    ["blocked_count"] = candidate.Safety.IsAllowed ? 0 : commands.Length,
                    ["space"] = "model",
                    ["layers"] = new JArray(Layers(dsl)),
                    ["entity_types"] = new JArray(EntityTypes(commands)),
                },
                ["operation"] = new JObject
                {
                    ["action"] = candidate.CadIr.Value<string>("action"),
                    ["adapter"] = "autocad_dotnet_adapter",
                },
                ["warnings"] = new JArray(candidate.Safety.Warnings),
                ["blocks"] = new JArray(candidate.Safety.Blocks),
                ["undo"] = new JObject
                {
                    ["available"] = true,
                    ["will_create_group"] = true,
                },
            };
        }

        private static JObject Step(string id, string name, string description)
        {
            return new JObject
            {
                ["id"] = id,
                ["name"] = name,
                ["description"] = description,
            };
        }

        private static IEnumerable<JObject> Commands(JObject dsl)
        {
            var commands = dsl["commands"] as JArray;
            if (commands == null) yield break;
            foreach (var token in commands)
            {
                if (token is JObject cmd) yield return cmd;
            }
        }

        private static IEnumerable<string> CommandTypes(JObject dsl)
        {
            foreach (var cmd in Commands(dsl))
            {
                var type = cmd.Value<string>("type");
                if (!string.IsNullOrEmpty(type)) yield return type;
            }
        }

        private static IEnumerable<string> Layers(JObject dsl)
        {
            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in Commands(dsl))
            {
                var layer = cmd.Value<string>("layer") ?? cmd.Value<string>("name");
                if (!string.IsNullOrEmpty(layer)) values.Add(layer);
            }
            return values.Count == 0 ? new[] { "0" } : values.ToArray();
        }

        private static IEnumerable<string> EntityTypes(IEnumerable<JObject> commands)
        {
            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in commands)
            {
                switch (cmd.Value<string>("type"))
                {
                    case "draw_line":
                        values.Add("LINE");
                        break;
                    case "draw_rectangle":
                        values.Add("POLYLINE");
                        break;
                    case "draw_text":
                        values.Add("TEXT");
                        break;
                    case "create_layer":
                        values.Add("LAYER");
                        break;
                }
            }
            return values.Count == 0 ? new[] { "UNKNOWN" } : values.ToArray();
        }

        private static string SummarizeTarget(JObject dsl)
        {
            var commandCount = Commands(dsl).Count();
            var layers = string.Join(", ", Layers(dsl));
            return commandCount + " commands in model space; layers: " + layers;
        }

        private static string BuildDslFromCadIr(JObject cadIr)
        {
            ValidateCadIr(cadIr);
            var operation = cadIr["operation"] as JObject;
            var commands = operation?["commands"] as JArray;
            if (commands == null || commands.Count == 0)
            {
                throw new InvalidOperationException("CAD-IR operation has no structured commands.");
            }

            var dsl = new JObject
            {
                ["version"] = "vcad_dsl_v1",
                ["unit"] = operation.Value<string>("unit") ?? "mm",
                ["coordinate_system"] = operation.Value<string>("coordinate_system") ?? "WCS",
                ["commands"] = new JArray(commands.Select(c => c.DeepClone())),
            };
            return dsl.ToString(Formatting.None);
        }

        private static string ResolveRiskLevel(string text)
        {
            return RequiresSecondConfirmation(text) ? "high" : "medium";
        }

        private static bool RequiresSecondConfirmation(string text)
        {
            return ContainsAny(text, SecondConfirmRiskWords) || ContainsBlockedOperation(text);
        }

        private static bool ContainsBlockedOperation(string text)
        {
            return ContainsAny(text, BlockedRiskWords);
        }

        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsScriptLikePayload(JToken token)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.Object)
            {
                foreach (var property in ((JObject)token).Properties())
                {
                    if (ScriptLikeFields.Any(f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                    if (ContainsScriptLikePayload(property.Value))
                    {
                        return true;
                    }
                }
            }
            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    if (ContainsScriptLikePayload(child))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
