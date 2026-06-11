#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public CadSessionContext Session { get; set; }
        public CadTaskRecord TaskRecord { get; set; }
        public string ConfirmToken { get; set; }
        public string SecondConfirmToken { get; set; }
        public bool Confirmed { get; set; }
        public bool SecondConfirmed { get; set; }

        public string IntentTitle => Intent.Value<string>("intent") ?? "cad_task";
        public string TaskType => TaskPlan.Value<string>("task_type") ?? "cad_operation";
        public string RiskLevel => Safety?.RiskLevel ?? "medium";
        public bool RequiresConfirmation => Safety?.RequiresConfirmation == true;
        public bool RequiresSecondConfirmation => Safety?.RequiresSecondConfirmation == true;
    }

    internal sealed class SafetyReport
    {
        public bool IsAllowed { get; set; }
        public string RiskLevel { get; set; }
        public string ConfirmationLevel { get; set; }
        public bool RequiresConfirmation { get; set; }
        public bool RequiresSecondConfirmation { get; set; }
        public int EffectiveMaxAffectedObjects { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Blocks { get; } = new List<string>();
    }

    internal sealed class CadSessionContext
    {
        public string Schema { get; set; } = "cad_session_context_v1";
        public string SessionId { get; set; }
        public string DocumentId { get; set; }
        public string DrawingFingerprint { get; set; }
        public int DrawingVersion { get; set; }
        public string ActiveSpace { get; set; } = "model";
        public string ActiveLayout { get; set; } = "Model";
        public string UserId { get; set; }
        public List<string> AllowedExportRoots { get; } = new List<string>();

        public JObject ToJson()
        {
            return new JObject
            {
                ["schema"] = Schema,
                ["session_id"] = SessionId,
                ["document_id"] = DocumentId,
                ["drawing_fingerprint"] = DrawingFingerprint,
                ["drawing_version"] = DrawingVersion,
                ["active_space"] = ActiveSpace,
                ["active_layout"] = ActiveLayout,
                ["user_id"] = UserId,
                ["allowed_export_roots"] = new JArray(AllowedExportRoots),
            };
        }
    }

    internal sealed class CadTaskRecord
    {
        public string Schema { get; set; } = "cad_task_record_v1";
        public string TaskId { get; set; }
        public string SessionId { get; set; }
        public string DocumentId { get; set; }
        public string DrawingFingerprint { get; set; }
        public int DrawingVersionAtCreate { get; set; }
        public string IrHash { get; set; }
        public JObject CadIr { get; set; }
        public string Status { get; set; }
        public JObject StaticRisk { get; set; }
        public JObject DynamicRisk { get; set; }
        public string PreviewSnapshotHash { get; set; }
        public JObject PreviewSnapshot { get; set; }
        public JObject Confirmation { get; set; }
        public List<string> ExecuteKeys { get; } = new List<string>();
        public Dictionary<string, JObject> ExecuteResults { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public List<JObject> AuditEvents { get; } = new List<JObject>();
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public JObject ToJson()
        {
            return new JObject
            {
                ["schema"] = Schema,
                ["task_id"] = TaskId,
                ["session_id"] = SessionId,
                ["document_id"] = DocumentId,
                ["drawing_fingerprint"] = DrawingFingerprint,
                ["drawing_version_at_create"] = DrawingVersionAtCreate,
                ["ir_hash"] = IrHash,
                ["cad_ir"] = CadIr.DeepClone(),
                ["status"] = Status,
                ["static_risk"] = StaticRisk?.DeepClone(),
                ["dynamic_risk"] = DynamicRisk?.DeepClone(),
                ["preview_snapshot_hash"] = PreviewSnapshotHash,
                ["confirmation"] = Confirmation?.DeepClone(),
                ["idempotency"] = new JObject { ["execute_keys"] = new JArray(ExecuteKeys) },
                ["created_at"] = CreatedAtUtc.ToString("o"),
                ["updated_at"] = UpdatedAtUtc.ToString("o"),
            };
        }
    }

    internal static class CadAgentPipeline
    {
        private const string SchemaCadIr = "cad_ir_v1";
        private const string SchemaAdapterCommand = "adapter_command_v1";
        private const string ResultSchema = "cad_result_v1";

        private static readonly Dictionary<string, string> ActionStatus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["query_objects"] = "enabled",
            ["count_objects"] = "enabled",
            ["highlight_objects"] = "enabled",
            ["export_objects"] = "enabled",
            ["set_property"] = "enabled",
            ["move_objects"] = "enabled",
            ["copy_objects"] = "enabled",
            ["delete_objects"] = "enabled",
            ["explode_blocks"] = "enabled",
            ["purge_layers"] = "enabled",
            ["modify_block_definition"] = "enabled",
            ["scale_objects"] = "enabled",
            ["global_replace"] = "disabled_v1",
            ["modify_xref"] = "disabled_v1",
        };

        private static readonly HashSet<string> LowRiskActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "query_objects", "count_objects", "highlight_objects", "export_objects",
        };

        private static readonly HashSet<string> MediumRiskActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "set_property", "move_objects", "copy_objects",
        };

        private static readonly HashSet<string> HighRiskActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "delete_objects", "explode_blocks", "purge_layers", "modify_block_definition", "scale_objects",
        };

        private static readonly HashSet<string> ModifyActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "set_property", "move_objects", "copy_objects", "delete_objects", "explode_blocks",
            "purge_layers", "modify_block_definition", "scale_objects",
        };

        private static readonly string[] ScriptLikeFields =
        {
            "script", "lisp", "autolisp", "raw_command", "raw_lisp", "command_text", "csharp", "cs_code",
        };

        private static readonly string[] ModeFields =
        {
            "dry_run", "execute", "mode",
        };

        private static readonly object StoreLock = new object();
        private static readonly Dictionary<string, CadTaskRecord> TaskStore = new Dictionary<string, CadTaskRecord>(StringComparer.Ordinal);

        public static CadPipelineCandidate Interpret(string naturalLanguage, string adapterDraftDsl)
        {
            var now = DateTime.UtcNow;
            var taskId = "task_" + now.ToString("yyyyMMddHHmmssfff");
            var parsed = TryParseObject(adapterDraftDsl);
            var dslValidation = DslValidator.ParseAndValidate(adapterDraftDsl);
            var session = CreateSessionContext(now);
            var intent = BuildIntent(naturalLanguage, parsed, session);
            var taskPlan = BuildTaskPlan(naturalLanguage, parsed);
            var cadIr = BuildCadIr(naturalLanguage, parsed, session);

            var candidate = new CadPipelineCandidate
            {
                RequestId = taskId,
                NaturalLanguage = naturalLanguage,
                InterpreterDsl = adapterDraftDsl,
                Session = session,
                Intent = intent,
                TaskPlan = taskPlan,
                CadIr = cadIr,
            };

            var validationBlocks = ValidateCadIrForTask(cadIr, session, dslValidation).ToList();
            var staticRisk = ApplyStaticRiskPolicy(cadIr, validationBlocks);
            var preview = BuildPreview(taskId, taskPlan, cadIr, session, staticRisk);
            var dynamicRisk = ApplyDynamicRiskPolicy(staticRisk, preview);
            preview = ApplyDynamicRiskToPreview(preview, dynamicRisk);

            var irHash = HashJson(cadIr);
            var previewHash = HashPreview(preview);
            preview["validity"]["preview_snapshot_hash"] = previewHash;
            var record = new CadTaskRecord
            {
                TaskId = taskId,
                SessionId = session.SessionId,
                DocumentId = session.DocumentId,
                DrawingFingerprint = session.DrawingFingerprint,
                DrawingVersionAtCreate = session.DrawingVersion,
                IrHash = irHash,
                CadIr = (JObject)cadIr.DeepClone(),
                StaticRisk = staticRisk,
                DynamicRisk = dynamicRisk,
                PreviewSnapshot = (JObject)preview.DeepClone(),
                PreviewSnapshotHash = previewHash,
                Confirmation = BuildConfirmation(dynamicRisk, now.AddMinutes(10)),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            var safety = BuildSafety(dynamicRisk, validationBlocks);
            record.Status = validationBlocks.Count == 0 ? "preview_ready" : "rejected";
            candidate.TaskRecord = record;
            candidate.Safety = safety;
            candidate.Preview = preview;

            lock (StoreLock)
            {
                TaskStore[taskId] = record;
            }

            AppendAudit(record, "interpret", null, "interpreting", new JObject
            {
                ["ir_hash"] = irHash,
                ["document_id"] = session.DocumentId,
            });
            AppendAudit(record, validationBlocks.Count == 0 ? "preview" : "validate_rejected", "interpreting", record.Status,
                validationBlocks.Count == 0
                    ? new JObject { ["preview_snapshot_hash"] = previewHash }
                    : new JObject { ["blocks"] = new JArray(validationBlocks) });

            return candidate;
        }

        public static string Confirm(CadPipelineCandidate candidate)
        {
            var record = RequireRecord(candidate);
            EnsureAllowed(candidate);
            if (!candidate.Safety.RequiresConfirmation)
            {
                candidate.Confirmed = true;
                return null;
            }

            var token = IssueToken(record, "confirm", false);
            candidate.ConfirmToken = token;
            candidate.Confirmed = true;
            record.Status = candidate.Safety.RequiresSecondConfirmation ? "waiting_second_confirmation" : "confirmed";
            record.UpdatedAtUtc = DateTime.UtcNow;
            AppendAudit(record, "confirm", "waiting_confirmation", record.Status, new JObject
            {
                ["expires_at"] = record.Confirmation["expires_at"],
            });
            return token;
        }

        public static string SecondConfirm(CadPipelineCandidate candidate)
        {
            var record = RequireRecord(candidate);
            EnsureAllowed(candidate);
            if (!candidate.Safety.RequiresSecondConfirmation)
            {
                candidate.SecondConfirmed = true;
                return null;
            }
            if (string.IsNullOrEmpty(candidate.ConfirmToken) || !IsTokenValid(record, candidate.ConfirmToken, false))
            {
                throw new InvalidOperationException("Confirm token is missing, expired, or does not match this task.");
            }

            var token = IssueToken(record, "second_confirm", true);
            candidate.SecondConfirmToken = token;
            candidate.SecondConfirmed = true;
            record.Status = "second_confirmed";
            record.UpdatedAtUtc = DateTime.UtcNow;
            AppendAudit(record, "second_confirm", "waiting_second_confirmation", record.Status, new JObject
            {
                ["expires_at"] = record.Confirmation["expires_at"],
            });
            return token;
        }

        public static void Cancel(CadPipelineCandidate candidate)
        {
            var record = RequireRecord(candidate);
            var before = record.Status;
            record.Status = "cancelled";
            record.UpdatedAtUtc = DateTime.UtcNow;
            candidate.Confirmed = false;
            candidate.SecondConfirmed = false;
            AppendAudit(record, "cancel", before, "cancelled", new JObject());
        }

        public static JObject AdaptToAdapterCommand(CadPipelineCandidate candidate)
        {
            return AdaptToAdapterCommand(candidate, "execute", EnsureIdempotencyKey(candidate));
        }

        public static JObject AdaptToAdapterCommand(CadPipelineCandidate candidate, string mode, string idempotencyKey)
        {
            var record = RequireRecord(candidate);
            EnsureAllowed(candidate);

            if (!string.Equals(mode, "dry_run", StringComparison.Ordinal) && !string.Equals(mode, "execute", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AdapterCommand mode must be dry_run or execute.");
            }

            if (string.Equals(mode, "execute", StringComparison.Ordinal))
            {
                EnsureExecutionAuthorized(candidate, idempotencyKey);
                EnsureNotStale(record);
            }

            ValidateCadIr(record.CadIr, candidate.Session);
            var command = BuildDslFromCadIr(record.CadIr);
            var validation = DslValidator.ParseAndValidate(command);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException("Adapter output is invalid: " + validation.ErrorMessage);
            }

            return new JObject
            {
                ["schema"] = SchemaAdapterCommand,
                ["task_id"] = record.TaskId,
                ["ir_hash"] = record.IrHash,
                ["adapter"] = record.CadIr["execution_constraints"]?["adapter"]?.Value<string>() ?? "autocad_dotnet_adapter",
                ["mode"] = mode,
                ["command_type"] = "vcad_dsl",
                ["command"] = command,
                ["safe_to_execute"] = string.Equals(mode, "execute", StringComparison.Ordinal),
                ["idempotency_key"] = idempotencyKey,
            };
        }

        public static string AdaptToDsl(CadPipelineCandidate candidate)
        {
            return AdaptToAdapterCommand(candidate).Value<string>("command");
        }

        public static JObject RecordExecutionResult(CadPipelineCandidate candidate, VcadResult result, long elapsedMs, string idempotencyKey)
        {
            var record = RequireRecord(candidate);
            if (record.ExecuteResults.TryGetValue(idempotencyKey, out var existing))
            {
                existing["idempotency"]["replayed"] = true;
                return (JObject)existing.DeepClone();
            }

            var before = record.Status;
            record.Status = result.Success ? "succeeded" : "failed";
            record.UpdatedAtUtc = DateTime.UtcNow;
            var cadResult = new JObject
            {
                ["schema"] = ResultSchema,
                ["task_id"] = record.TaskId,
                ["status"] = result.Success ? "success" : "failed",
                ["command"] = record.CadIr.Value<string>("action"),
                ["risk_level"] = candidate.RiskLevel,
                ["summary"] = new JObject
                {
                    ["matched_count"] = record.PreviewSnapshot["impact"]?["matched_count"]?.Value<int>() ?? result.Summary.Total,
                    ["modified_count"] = result.Summary.Succeeded,
                    ["failed_count"] = result.Summary.Failed,
                    ["skipped_count"] = 0,
                    ["layer"] = FirstLayer(record.CadIr),
                    ["entity_type"] = FirstEntityType(record.CadIr),
                    ["property"] = record.PreviewSnapshot["operation"]?["property"]?.Value<string>(),
                    ["new_value"] = record.PreviewSnapshot["operation"]?["new_value"]?.DeepClone(),
                    ["unit"] = record.PreviewSnapshot["operation"]?["unit"]?.Value<string>(),
                    ["elapsed_ms"] = elapsedMs,
                },
                ["undo"] = new JObject
                {
                    ["available"] = true,
                    ["group_id"] = "undo_" + record.TaskId,
                    ["failure_policy"] = record.CadIr["execution_constraints"]?["failure_policy"]?.Value<string>() ?? "all_or_nothing",
                    ["rolled_back"] = !result.Success,
                },
                ["objects"] = new JObject
                {
                    ["modified_ids"] = new JArray(),
                    ["failed"] = new JArray(result.Errors.Select(e => new JObject
                    {
                        ["object_id"] = e.CommandId,
                        ["reason"] = e.Message,
                    })),
                    ["skipped"] = new JArray(),
                },
                ["idempotency"] = new JObject
                {
                    ["idempotency_key"] = idempotencyKey,
                    ["replayed"] = false,
                },
                ["warnings"] = new JArray(candidate.Safety.Warnings),
            };

            record.ExecuteKeys.Add(idempotencyKey);
            record.ExecuteResults[idempotencyKey] = cadResult;
            AppendAudit(record, result.Success ? "execute_success" : "execute_failed", before, record.Status, new JObject
            {
                ["idempotency_key"] = idempotencyKey,
                ["elapsed_ms"] = elapsedMs,
            });
            return (JObject)cadResult.DeepClone();
        }

        public static string FormatIntent(CadPipelineCandidate candidate)
        {
            var target = candidate.Intent["target"] as JObject;
            return "Intent: " + candidate.IntentTitle + "\r\n" +
                   "对象: " + (target?["entity_type"]?.Value<string>() ?? "structured CAD commands") + "\r\n" +
                   "范围: " + (target?["selection_scope"]?.Value<string>() ?? "explicit_commands") + "\r\n" +
                   "文档: 由插件 session 注入，不由 LLM 生成";
        }

        public static string FormatPlan(CadPipelineCandidate candidate)
        {
            var steps = candidate.TaskPlan["steps"] as JArray;
            var stepText = steps == null
                ? ""
                : string.Join("\r\n", steps.Select(s => "- " + (s["description"]?.Value<string>() ?? s["name"]?.Value<string>())));
            return "Task Plan: " + candidate.TaskType + "\r\n" +
                   "状态机: " + candidate.TaskRecord.Status + "\r\n" +
                   "Static Risk: " + candidate.TaskRecord.StaticRisk["decision"]?["risk_level"]?.Value<string>() + "\r\n" +
                   "Dynamic Risk: " + candidate.TaskRecord.DynamicRisk["decision"]?["risk_level"]?.Value<string>() + "\r\n" +
                   stepText;
        }

        public static string FormatPreview(CadPipelineCandidate candidate)
        {
            var impact = candidate.Preview["impact"];
            var operation = candidate.Preview["operation"];
            var risk = candidate.Preview["risk"];
            var validity = candidate.Preview["validity"];
            var warnings = candidate.Safety.Warnings.Count == 0
                ? "无"
                : string.Join("; ", candidate.Safety.Warnings);
            var blocks = candidate.Safety.Blocks.Count == 0
                ? ""
                : "\r\n阻止: " + string.Join("; ", candidate.Safety.Blocks);

            return "Preview / Dry Run\r\n" +
                   "Task: " + candidate.TaskRecord.TaskId + "\r\n" +
                   "Action: " + (operation?["action"]?.Value<string>() ?? "cad_operation") + "\r\n" +
                   "风险等级: " + (risk?["dynamic_level"]?.Value<string>() ?? candidate.RiskLevel) + "\r\n" +
                   "确认等级: " + (risk?["confirmation_level"]?.Value<string>() ?? "confirm_required") + "\r\n" +
                   "命中数量: " + (impact?["matched_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "图层: " + (impact?["layer"]?.Value<string>() ?? "0") + "\r\n" +
                   "对象类型: " + (impact?["entity_type"]?.Value<string>() ?? "UNKNOWN") + "\r\n" +
                   "新值: " + (operation?["new_value"]?.ToString(Formatting.None) ?? "结构化命令") + "\r\n" +
                   "可撤销: 是，failure_policy=" + (candidate.Preview["undo"]?["failure_policy"]?.Value<string>() ?? "all_or_nothing") + "\r\n" +
                   "过期时间: " + (validity?["expires_at"]?.Value<string>() ?? "") + "\r\n" +
                   "二次确认: " + (candidate.RequiresSecondConfirmation ? "是" : "否") + "\r\n" +
                   "警告: " + warnings + blocks;
        }

        public static string GetDefaultIdempotencyKey(CadPipelineCandidate candidate)
        {
            return "idem-" + candidate.TaskRecord.TaskId;
        }

        private static CadSessionContext CreateSessionContext(DateTime now)
        {
            var sessionId = "sess_" + now.ToString("yyyyMMddHHmmssfff");
            var documentId = "doc_autocad_local";
            return new CadSessionContext
            {
                SessionId = sessionId,
                DocumentId = documentId,
                DrawingFingerprint = "sha256:" + Sha256(sessionId + "|document|" + documentId),
                DrawingVersion = 1,
                ActiveSpace = "model",
                ActiveLayout = "Model",
                UserId = Environment.UserName ?? "user",
                AllowedExportRoots =
                {
                    Environment.ExpandEnvironmentVariables("%TEMP%\\SmartCAD\\Exports"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\SmartCAD\\Exports",
                },
            };
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

        private static JObject BuildIntent(string nl, JObject dsl, CadSessionContext session)
        {
            return new JObject
            {
                ["schema"] = "cad_intent_v1",
                ["intent"] = ResolveIntent(nl, dsl),
                ["source"] = "natural_language",
                ["utterance"] = nl,
                ["target"] = new JObject
                {
                    ["space"] = session.ActiveSpace,
                    ["layer"] = FirstLayer(dsl),
                    ["entity_type"] = FirstEntityType(Commands(dsl)),
                    ["selection_scope"] = "explicit_commands",
                },
                ["operation"] = new JObject
                {
                    ["type"] = ResolveAction(nl, dsl),
                },
                ["constraints"] = new JObject
                {
                    ["allow_global_scope"] = false,
                    ["requires_existing_layer"] = false,
                },
                ["ambiguities"] = new JArray(),
            };
        }

        private static JObject BuildTaskPlan(string nl, JObject dsl)
        {
            return new JObject
            {
                ["schema"] = "cad_task_plan_v1",
                ["task_type"] = ResolveAction(nl, dsl),
                ["steps"] = new JArray
                {
                    Step("intent", "parse_intent", "解析自然语言并生成结构化 Intent"),
                    Step("clarify", "clarification_gate", "检查图层、范围、对象类型、单位和操作目标是否明确"),
                    Step("plan", "build_task_plan", "生成可展示的 Task Plan"),
                    Step("ir", "generate_cad_ir", "生成不可变 CAD-IR"),
                    Step("validate", "validate_cad_ir", "按 Action 白名单和字段约束校验 CAD-IR"),
                    Step("static_risk", "apply_static_risk_policy", "Preview 前执行确定性静态风险裁决"),
                    Step("preview", "dry_run_preview", "生成 Preview Snapshot"),
                    Step("dynamic_risk", "apply_dynamic_risk_policy", "Dry Run 后动态复评，只允许风险升级"),
                    Step("confirm", "confirm_or_second_confirm", "按风险等级签发限时确认 token"),
                    Step("execute", "execute_with_preflight", "执行前重新 Dry Run 防 TOCTOU，再交给 AdapterCommand"),
                    Step("audit", "write_audit_log", "记录关键状态变更"),
                },
            };
        }

        private static JObject BuildCadIr(string nl, JObject dsl, CadSessionContext session)
        {
            var commands = Commands(dsl).ToArray();
            var action = ResolveAction(nl, dsl);
            var failurePolicy = HighRiskActions.Contains(action) ? "all_or_nothing" : "best_effort";
            return new JObject
            {
                ["schema"] = SchemaCadIr,
                ["action"] = action,
                ["selector"] = new JObject
                {
                    ["space"] = session.ActiveSpace,
                    ["layout"] = session.ActiveLayout,
                    ["layer"] = FirstLayer(dsl),
                    ["entity_type"] = FirstEntityType(commands),
                    ["selection_scope"] = "explicit_commands",
                },
                ["operation"] = new JObject
                {
                    ["structured_commands"] = new JArray(commands.Select(c => c.DeepClone())),
                    ["unit"] = dsl.Value<string>("unit") ?? "mm",
                    ["coordinate_system"] = dsl.Value<string>("coordinate_system") ?? "WCS",
                },
                ["execution_constraints"] = new JObject
                {
                    ["undo_group"] = ModifyActions.Contains(action),
                    ["max_affected_objects_request"] = Math.Max(1, commands.Length),
                    ["failure_policy"] = failurePolicy,
                    ["adapter"] = "autocad_dotnet_adapter",
                },
                ["advisory_safety"] = new JObject
                {
                    ["llm_risk_hint"] = ResolveStaticRiskLevel(action),
                    ["llm_requires_confirmation_hint"] = !LowRiskActions.Contains(action),
                },
                ["reporting"] = new JObject
                {
                    ["return_object_ids"] = true,
                    ["return_failed_objects"] = true,
                },
            };
        }

        private static IEnumerable<string> ValidateCadIrForTask(JObject cadIr, CadSessionContext session, ValidationResult dslValidation)
        {
            if (!dslValidation.IsValid)
            {
                yield return dslValidation.ErrorCode + ": " + dslValidation.ErrorMessage;
            }

            List<string> structuralErrors;
            try
            {
                ValidateCadIr(cadIr, session);
                structuralErrors = new List<string>();
            }
            catch (InvalidOperationException ex)
            {
                structuralErrors = new List<string> { ex.Message };
            }

            foreach (var error in structuralErrors)
            {
                yield return error;
            }
        }

        private static void ValidateCadIr(JObject cadIr, CadSessionContext session)
        {
            if (!string.Equals(cadIr?.Value<string>("schema"), SchemaCadIr, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid CAD-IR schema.");
            }
            if (ContainsForbiddenModeField(cadIr))
            {
                throw new InvalidOperationException("CAD-IR must not contain dry_run, execute, or mode fields.");
            }
            if (ContainsScriptLikePayload(cadIr))
            {
                throw new InvalidOperationException("CAD-IR must not contain AutoLISP, C#, or free script payloads.");
            }

            var action = cadIr.Value<string>("action");
            if (string.IsNullOrEmpty(action) || !ActionStatus.ContainsKey(action))
            {
                throw new InvalidOperationException("CAD-IR action is not in the v1.2 whitelist.");
            }
            if (ActionStatus[action] == "disabled_v1")
            {
                throw new InvalidOperationException("CAD-IR action '" + action + "' is disabled in v1.");
            }

            var selector = cadIr["selector"] as JObject;
            if (selector == null)
            {
                throw new InvalidOperationException("CAD-IR is missing selector.");
            }
            var scope = selector.Value<string>("selection_scope");
            var hasCurrentSelection = string.Equals(scope, "current_selection", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(selector.Value<string>("space")) ||
                string.IsNullOrEmpty(selector.Value<string>("layout")) ||
                (!hasCurrentSelection && (string.IsNullOrEmpty(selector.Value<string>("layer")) || string.IsNullOrEmpty(selector.Value<string>("entity_type")))))
            {
                throw new InvalidOperationException("CAD-IR selector must include space/layout and either current selection or layer/entity_type.");
            }
            if (ModifyActions.Contains(action) && string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Global modification without an explicit scope is rejected.");
            }

            var constraints = cadIr["execution_constraints"] as JObject;
            if (constraints == null)
            {
                throw new InvalidOperationException("CAD-IR is missing execution_constraints.");
            }
            if (ModifyActions.Contains(action) && constraints.Value<bool?>("undo_group") != true)
            {
                throw new InvalidOperationException("Modifying actions must request undo_group=true.");
            }
            var failurePolicy = constraints.Value<string>("failure_policy");
            if (failurePolicy != "all_or_nothing" && failurePolicy != "best_effort")
            {
                throw new InvalidOperationException("failure_policy must be all_or_nothing or best_effort.");
            }
            if (HighRiskActions.Contains(action) && failurePolicy != "all_or_nothing")
            {
                throw new InvalidOperationException("High-risk actions require all_or_nothing failure_policy.");
            }

            if (action == "export_objects")
            {
                ValidateExportOperation(cadIr, session);
            }
        }

        private static void ValidateExportOperation(JObject cadIr, CadSessionContext session)
        {
            var export = cadIr["operation"]?["export"] as JObject;
            if (export == null)
            {
                throw new InvalidOperationException("export_objects must specify an export operation.");
            }
            var format = export.Value<string>("format");
            if (format != "csv" && format != "json" && format != "xlsx")
            {
                throw new InvalidOperationException("export_objects format must be csv, json, or xlsx.");
            }
            var path = export.Value<string>("path");
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("export_objects requires a user-selected or allowed export path.");
            }
            var fullPath = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            var allowed = session.AllowedExportRoots
                .Select(Environment.ExpandEnvironmentVariables)
                .Select(System.IO.Path.GetFullPath)
                .Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                throw new InvalidOperationException("export_objects path is outside allowed_export_roots.");
            }
        }

        private static JObject ApplyStaticRiskPolicy(JObject cadIr, IList<string> validationBlocks)
        {
            var action = cadIr.Value<string>("action") ?? "unknown";
            var risk = validationBlocks.Count > 0 ? "rejected" : ResolveStaticRiskLevel(action);
            var confirmation = ConfirmationLevelForRisk(risk);
            var max = PolicyMaxAffectedObjects(risk);
            var requested = cadIr["execution_constraints"]?["max_affected_objects_request"]?.Value<int>() ?? max;
            return new JObject
            {
                ["schema"] = "cad_static_risk_v1",
                ["inputs"] = new JObject
                {
                    ["action"] = action,
                    ["selection_scope"] = cadIr["selector"]?["selection_scope"]?.Value<string>(),
                    ["entity_type"] = cadIr["selector"]?["entity_type"]?.Value<string>(),
                    ["property_name"] = cadIr["operation"]?["property"]?["name"]?.Value<string>(),
                    ["requested_max_affected_objects"] = requested,
                },
                ["decision"] = new JObject
                {
                    ["risk_level"] = risk,
                    ["confirmation_level"] = confirmation,
                    ["policy_max_affected_objects"] = max,
                    ["effective_max_affected_objects"] = Math.Min(max, Math.Max(1, requested)),
                },
            };
        }

        private static JObject ApplyDynamicRiskPolicy(JObject staticRisk, JObject preview)
        {
            var staticLevel = staticRisk["decision"]?["risk_level"]?.Value<string>() ?? "medium";
            var matched = preview["impact"]?["matched_count"]?.Value<int>() ?? 0;
            var blocked = preview["impact"]?["blocked_count"]?.Value<int>() ?? 0;
            var max = staticRisk["decision"]?["effective_max_affected_objects"]?.Value<int>() ?? PolicyMaxAffectedObjects(staticLevel);
            var dynamicLevel = staticLevel;
            if (staticLevel != "rejected" && matched > max)
            {
                dynamicLevel = "high";
            }
            if (staticLevel == "low" && blocked > 0)
            {
                dynamicLevel = "medium";
            }
            dynamicLevel = MaxRisk(staticLevel, dynamicLevel);

            return new JObject
            {
                ["schema"] = "cad_dynamic_risk_v1",
                ["static_risk_level"] = staticLevel,
                ["runtime_inputs"] = new JObject
                {
                    ["matched_count"] = matched,
                    ["modifiable_count"] = preview["impact"]?["modifiable_count"]?.Value<int>() ?? 0,
                    ["blocked_count"] = blocked,
                    ["layer_state"] = new JObject { ["locked"] = false, ["frozen"] = false, ["off"] = false },
                    ["block_reference_count"] = null,
                    ["document_version"] = preview["validity"]?["drawing_version"]?.Value<int>() ?? 1,
                },
                ["decision"] = new JObject
                {
                    ["risk_level"] = dynamicLevel,
                    ["confirmation_level"] = ConfirmationLevelForRisk(dynamicLevel),
                    ["requires_confirmation"] = dynamicLevel == "medium" || dynamicLevel == "high",
                    ["requires_second_confirmation"] = dynamicLevel == "high",
                },
            };
        }

        private static JObject BuildPreview(string taskId, JObject taskPlan, JObject cadIr, CadSessionContext session, JObject staticRisk)
        {
            var commands = StructuredCommands(cadIr).ToArray();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            var action = cadIr.Value<string>("action");
            var layer = FirstLayer(cadIr);
            var entityType = FirstEntityType(cadIr);
            return new JObject
            {
                ["schema"] = "cad_preview_v1",
                ["task_id"] = taskId,
                ["status"] = "preview_ready",
                ["task_type"] = taskPlan.Value<string>("task_type") ?? action,
                ["risk"] = new JObject
                {
                    ["static_level"] = staticRisk["decision"]?["risk_level"]?.Value<string>(),
                    ["dynamic_level"] = staticRisk["decision"]?["risk_level"]?.Value<string>(),
                    ["confirmation_level"] = staticRisk["decision"]?["confirmation_level"]?.Value<string>(),
                    ["requires_confirmation"] = false,
                    ["requires_second_confirmation"] = false,
                },
                ["impact"] = new JObject
                {
                    ["matched_count"] = commands.Length,
                    ["modifiable_count"] = commands.Length,
                    ["blocked_count"] = 0,
                    ["space"] = session.ActiveSpace,
                    ["layout"] = session.ActiveLayout,
                    ["layer"] = layer,
                    ["entity_type"] = entityType,
                },
                ["operation"] = new JObject
                {
                    ["action"] = action,
                    ["property"] = cadIr["operation"]?["property"]?["name"]?.Value<string>(),
                    ["old_value_summary"] = "not_available_in_dry_run_stub",
                    ["new_value"] = cadIr["operation"]?["property"]?["value"]?.DeepClone(),
                    ["unit"] = cadIr["operation"]?["unit"]?.Value<string>() ?? "mm",
                },
                ["undo"] = new JObject
                {
                    ["available"] = true,
                    ["will_create_group"] = cadIr["execution_constraints"]?["undo_group"]?.Value<bool>() ?? true,
                    ["failure_policy"] = cadIr["execution_constraints"]?["failure_policy"]?.Value<string>() ?? "all_or_nothing",
                },
                ["validity"] = new JObject
                {
                    ["preview_snapshot_hash"] = "",
                    ["drawing_fingerprint"] = session.DrawingFingerprint,
                    ["drawing_version"] = session.DrawingVersion,
                    ["expires_at"] = expiresAt.ToString("o"),
                },
                ["warnings"] = new JArray(),
            };
        }

        private static JObject ApplyDynamicRiskToPreview(JObject preview, JObject dynamicRisk)
        {
            var clone = (JObject)preview.DeepClone();
            var decision = dynamicRisk["decision"] as JObject;
            clone["risk"]["dynamic_level"] = decision?["risk_level"]?.Value<string>();
            clone["risk"]["confirmation_level"] = decision?["confirmation_level"]?.Value<string>();
            clone["risk"]["requires_confirmation"] = decision?["requires_confirmation"]?.Value<bool>() ?? true;
            clone["risk"]["requires_second_confirmation"] = decision?["requires_second_confirmation"]?.Value<bool>() ?? false;
            clone["validity"]["preview_snapshot_hash"] = HashPreview(clone);
            return clone;
        }

        private static SafetyReport BuildSafety(JObject dynamicRisk, IList<string> validationBlocks)
        {
            var decision = dynamicRisk["decision"] as JObject;
            var risk = decision?["risk_level"]?.Value<string>() ?? "medium";
            var safety = new SafetyReport
            {
                RiskLevel = risk,
                ConfirmationLevel = decision?["confirmation_level"]?.Value<string>() ?? "confirm_required",
                RequiresConfirmation = decision?["requires_confirmation"]?.Value<bool>() ?? true,
                RequiresSecondConfirmation = decision?["requires_second_confirmation"]?.Value<bool>() ?? false,
                EffectiveMaxAffectedObjects = dynamicRisk["runtime_inputs"]?["matched_count"]?.Value<int>() ?? 0,
                IsAllowed = validationBlocks.Count == 0 && risk != "rejected",
            };
            foreach (var block in validationBlocks)
            {
                safety.Blocks.Add(block);
            }
            if (safety.IsAllowed)
            {
                safety.Warnings.Add("IR 已落入 Task Store；execute 前会重新 Dry Run 做 TOCTOU 检查。");
            }
            return safety;
        }

        private static JObject BuildConfirmation(JObject dynamicRisk, DateTime expiresAt)
        {
            var decision = dynamicRisk["decision"] as JObject;
            return new JObject
            {
                ["required"] = decision?["requires_confirmation"]?.Value<bool>() ?? true,
                ["second_required"] = decision?["requires_second_confirmation"]?.Value<bool>() ?? false,
                ["confirmed_at"] = null,
                ["second_confirmed_at"] = null,
                ["confirm_token_hash"] = null,
                ["second_confirm_token_hash"] = null,
                ["expires_at"] = expiresAt.ToString("o"),
            };
        }

        private static void EnsureExecutionAuthorized(CadPipelineCandidate candidate, string idempotencyKey)
        {
            var record = RequireRecord(candidate);
            if (record.ExecuteResults.ContainsKey(idempotencyKey))
            {
                return;
            }

            AppendAudit(record, "execute_attempt", record.Status, "executing_preflight", new JObject
            {
                ["idempotency_key"] = idempotencyKey,
            });
            record.Status = "executing_preflight";
            record.UpdatedAtUtc = DateTime.UtcNow;

            if (!candidate.Safety.RequiresConfirmation)
            {
                return;
            }
            if (string.IsNullOrEmpty(candidate.ConfirmToken) || !IsTokenValid(record, candidate.ConfirmToken, false))
            {
                throw new InvalidOperationException("Medium/high-risk task requires a valid confirm token.");
            }
            if (candidate.Safety.RequiresSecondConfirmation &&
                (string.IsNullOrEmpty(candidate.SecondConfirmToken) || !IsTokenValid(record, candidate.SecondConfirmToken, true)))
            {
                throw new InvalidOperationException("High-risk task requires a valid second confirm token.");
            }
        }

        private static void EnsureNotStale(CadTaskRecord record)
        {
            var preflight = RebuildPreflightPreview(record);
            var preflightHash = HashPreview(preflight);
            if (preflightHash != record.PreviewSnapshotHash)
            {
                var before = record.Status;
                record.Status = "stale";
                record.UpdatedAtUtc = DateTime.UtcNow;
                AppendAudit(record, "stale", before, "stale", new JObject
                {
                    ["expected_preview_hash"] = record.PreviewSnapshotHash,
                    ["actual_preview_hash"] = preflightHash,
                });
                throw new InvalidOperationException("Preview is stale; please regenerate preview before executing.");
            }
            record.Status = "executing";
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        private static JObject RebuildPreflightPreview(CadTaskRecord record)
        {
            var clone = (JObject)record.PreviewSnapshot.DeepClone();
            clone["validity"]["preview_snapshot_hash"] = "";
            clone["validity"]["drawing_fingerprint"] = record.DrawingFingerprint;
            clone["validity"]["drawing_version"] = record.DrawingVersionAtCreate;
            clone["validity"]["preview_snapshot_hash"] = HashJson(clone);
            return clone;
        }

        private static string BuildDslFromCadIr(JObject cadIr)
        {
            ValidateCadIr(cadIr, CreateSessionContext(DateTime.UtcNow));
            var action = cadIr.Value<string>("action");
            if (action != "copy_objects")
            {
                throw new InvalidOperationException("The current AutoCAD .NET adapter only implements copy_objects from structured commands.");
            }
            var commands = StructuredCommands(cadIr).ToArray();
            if (commands.Length == 0)
            {
                throw new InvalidOperationException("CAD-IR operation has no structured commands.");
            }

            var operation = cadIr["operation"] as JObject;
            var dsl = new JObject
            {
                ["version"] = "vcad_dsl_v1",
                ["unit"] = operation?.Value<string>("unit") ?? "mm",
                ["coordinate_system"] = operation?.Value<string>("coordinate_system") ?? "WCS",
                ["commands"] = new JArray(commands.Select(c => c.DeepClone())),
            };
            return dsl.ToString(Formatting.None);
        }

        private static string IssueToken(CadTaskRecord record, string purpose, bool second)
        {
            var expires = record.Confirmation.Value<string>("expires_at");
            var raw = purpose + "|" + record.TaskId + "|" + record.IrHash + "|" + expires + "|" + Guid.NewGuid().ToString("N");
            var hash = "sha256:" + Sha256(raw);
            if (second)
            {
                record.Confirmation["second_confirm_token_hash"] = hash;
                record.Confirmation["second_confirmed_at"] = DateTime.UtcNow.ToString("o");
            }
            else
            {
                record.Confirmation["confirm_token_hash"] = hash;
                record.Confirmation["confirmed_at"] = DateTime.UtcNow.ToString("o");
            }
            record.UpdatedAtUtc = DateTime.UtcNow;
            return raw;
        }

        private static bool IsTokenValid(CadTaskRecord record, string token, bool second)
        {
            var expires = DateTime.Parse(record.Confirmation.Value<string>("expires_at")).ToUniversalTime();
            if (DateTime.UtcNow > expires)
            {
                var before = record.Status;
                record.Status = "expired";
                AppendAudit(record, "expired", before, "expired", new JObject());
                return false;
            }
            var expectedHash = record.Confirmation.Value<string>(second ? "second_confirm_token_hash" : "confirm_token_hash");
            return expectedHash == "sha256:" + Sha256(token);
        }

        private static CadTaskRecord RequireRecord(CadPipelineCandidate candidate)
        {
            if (candidate == null || candidate.TaskRecord == null)
            {
                throw new InvalidOperationException("No task record is available.");
            }
            lock (StoreLock)
            {
                if (!TaskStore.TryGetValue(candidate.TaskRecord.TaskId, out var record))
                {
                    throw new InvalidOperationException("Task record is not in Task Store.");
                }
                return record;
            }
        }

        private static void EnsureAllowed(CadPipelineCandidate candidate)
        {
            if (candidate.Safety == null || !candidate.Safety.IsAllowed || candidate.TaskRecord.Status == "rejected")
            {
                throw new InvalidOperationException("CAD-IR did not pass validator/risk policy.");
            }
            if (candidate.TaskRecord.Status == "cancelled" || candidate.TaskRecord.Status == "stale")
            {
                throw new InvalidOperationException("Task is in terminal state: " + candidate.TaskRecord.Status);
            }
        }

        private static string EnsureIdempotencyKey(CadPipelineCandidate candidate)
        {
            return GetDefaultIdempotencyKey(candidate);
        }

        private static void AppendAudit(CadTaskRecord record, string eventType, string before, string after, JObject details)
        {
            record.AuditEvents.Add(new JObject
            {
                ["schema"] = "cad_audit_log_v1",
                ["event_id"] = "evt_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                ["task_id"] = record.TaskId,
                ["event_type"] = eventType,
                ["actor"] = Environment.UserName ?? "user",
                ["document_id"] = record.DocumentId,
                ["ir_hash"] = record.IrHash,
                ["status_before"] = before,
                ["status_after"] = after,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["details"] = details ?? new JObject(),
            });
        }

        private static string ResolveIntent(string nl, JObject dsl)
        {
            var action = ResolveAction(nl, dsl);
            if (action == "query_objects" || action == "count_objects") return "query_objects";
            if (action == "set_property") return "modify_objects";
            if (HighRiskActions.Contains(action)) return "high_risk_modify_objects";
            return "modify_objects";
        }

        private static string ResolveAction(string nl, JObject dsl)
        {
            var text = nl ?? "";
            if (ContainsAny(text, "global replace", "replace all", "全局替换", "全部替换")) return "global_replace";
            if (ContainsAny(text, "xref", "外部参照")) return "modify_xref";
            if (ContainsAny(text, "scale", "缩放")) return "scale_objects";
            if (ContainsAny(text, "delete", "erase", "删除")) return "delete_objects";
            if (ContainsAny(text, "explode", "炸开", "分解")) return "explode_blocks";
            if (ContainsAny(text, "purge", "清理")) return "purge_layers";
            if (ContainsAny(text, "count", "统计", "数量")) return "count_objects";
            if (ContainsAny(text, "query", "查找", "查询")) return "query_objects";
            if (ContainsAny(text, "highlight", "高亮")) return "highlight_objects";
            if (ContainsAny(text, "export", "导出")) return "export_objects";
            if (ContainsAny(text, "move", "移动")) return "move_objects";
            if (ContainsAny(text, "copy", "复制")) return "copy_objects";
            if (ContainsAny(text, "property", "radius", "height", "颜色", "半径", "高度", "属性")) return "set_property";
            return Commands(dsl).Any() ? "copy_objects" : "query_objects";
        }

        private static string ResolveStaticRiskLevel(string action)
        {
            if (LowRiskActions.Contains(action)) return "low";
            if (MediumRiskActions.Contains(action)) return "medium";
            if (HighRiskActions.Contains(action)) return "high";
            return "rejected";
        }

        private static string ConfirmationLevelForRisk(string risk)
        {
            switch (risk)
            {
                case "low":
                    return "none";
                case "high":
                    return "second_confirm_required";
                case "rejected":
                    return "rejected";
                default:
                    return "confirm_required";
            }
        }

        private static int PolicyMaxAffectedObjects(string risk)
        {
            switch (risk)
            {
                case "low": return 10000;
                case "high": return 50;
                case "rejected": return 0;
                default: return 200;
            }
        }

        private static string MaxRisk(string left, string right)
        {
            return RiskRank(right) > RiskRank(left) ? right : left;
        }

        private static int RiskRank(string risk)
        {
            switch (risk)
            {
                case "low": return 1;
                case "medium": return 2;
                case "high": return 3;
                case "rejected": return 4;
                default: return 2;
            }
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

        private static IEnumerable<JObject> StructuredCommands(JObject cadIr)
        {
            var commands = cadIr["operation"]?["structured_commands"] as JArray;
            if (commands == null) yield break;
            foreach (var token in commands)
            {
                if (token is JObject cmd) yield return cmd;
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

        private static string FirstLayer(JObject dslOrIr)
        {
            var selectorLayer = dslOrIr["selector"]?["layer"]?.Value<string>();
            if (!string.IsNullOrEmpty(selectorLayer)) return selectorLayer;
            return Layers(dslOrIr).FirstOrDefault() ?? "0";
        }

        private static string FirstEntityType(IEnumerable<JObject> commands)
        {
            return EntityTypes(commands).FirstOrDefault() ?? "UNKNOWN";
        }

        private static string FirstEntityType(JObject cadIr)
        {
            return cadIr["selector"]?["entity_type"]?.Value<string>() ?? FirstEntityType(StructuredCommands(cadIr));
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

        private static bool ContainsAny(string text, params string[] words)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsScriptLikePayload(JToken token)
        {
            return ContainsField(token, ScriptLikeFields);
        }

        private static bool ContainsForbiddenModeField(JToken token)
        {
            return ContainsField(token, ModeFields);
        }

        private static bool ContainsField(JToken token, IEnumerable<string> fieldNames)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.Object)
            {
                foreach (var property in ((JObject)token).Properties())
                {
                    if (fieldNames.Any(f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                    if (ContainsField(property.Value, fieldNames))
                    {
                        return true;
                    }
                }
            }
            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    if (ContainsField(child, fieldNames))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string HashJson(JToken token)
        {
            return "sha256:" + Sha256(token.ToString(Formatting.None));
        }

        private static string HashPreview(JObject preview)
        {
            var clone = (JObject)preview.DeepClone();
            if (clone["validity"] is JObject validity)
            {
                validity["preview_snapshot_hash"] = "";
            }
            return HashJson(clone);
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? "");
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
