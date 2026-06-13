#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Plugin.Context;

namespace Vcad.Plugin.Execution
{
    internal sealed class CadToolExecutionResult
    {
        public string CallId { get; set; }
        public string ToolName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public long ElapsedMs { get; set; }
        public JObject Data { get; set; }

        public JObject ToAgentToolResult()
        {
            return new JObject
            {
                ["id"] = CallId ?? "",
                ["name"] = ToolName ?? "",
                ["success"] = Success,
                ["result"] = Data ?? new JObject { ["message"] = Message ?? "" },
                ["error"] = Error,
            };
        }
    }

    internal static class CadToolHost
    {
        private const double MaxCoordinate = 1000000000.0;
        private const double MaxDimension = 1000000000.0;
        private const int MaxTextChars = 2000;

        private sealed class ToolDefinition
        {
            public ToolDefinition(string name, bool writesDwg, Func<string, string, JObject, CadToolExecutionResult> execute)
            {
                Name = name;
                WritesDwg = writesDwg;
                Execute = execute;
            }

            public string Name { get; private set; }
            public bool WritesDwg { get; private set; }
            public Func<string, string, JObject, CadToolExecutionResult> Execute { get; private set; }
        }

        private static readonly Dictionary<string, ToolDefinition> Tools =
            new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { "cad.read_dwg_snapshot", new ToolDefinition("cad.read_dwg_snapshot", false, ReadSnapshot) },
                { "cad.preview_plan", new ToolDefinition("cad.preview_plan", false, PreviewPlan) },
                { "cad.count_entities", new ToolDefinition("cad.count_entities", false, CountEntities) },
                { "cad.measure_bounds", new ToolDefinition("cad.measure_bounds", false, MeasureBounds) },
                { "cad.measure_distance", new ToolDefinition("cad.measure_distance", false, MeasureDistance) },
                { "cad.layer_diff", new ToolDefinition("cad.layer_diff", false, LayerDiff) },
                { "cad.before_after_diff", new ToolDefinition("cad.before_after_diff", false, BeforeAfterDiff) },
                { "cad.validate_dwg_state", new ToolDefinition("cad.validate_dwg_state", false, ValidateDwgState) },
                { "cad.create_layer", new ToolDefinition("cad.create_layer", true, (id, name, args) => RunWrite(id, name, args, CreateLayer)) },
                { "cad.draw_line", new ToolDefinition("cad.draw_line", true, (id, name, args) => RunWrite(id, name, args, DrawLine)) },
                { "cad.draw_polyline", new ToolDefinition("cad.draw_polyline", true, (id, name, args) => RunWrite(id, name, args, DrawPolyline)) },
                { "cad.draw_circle", new ToolDefinition("cad.draw_circle", true, (id, name, args) => RunWrite(id, name, args, DrawCircle)) },
                { "cad.draw_rectangle", new ToolDefinition("cad.draw_rectangle", true, (id, name, args) => RunWrite(id, name, args, DrawRectangle)) },
                { "cad.draw_stair", new ToolDefinition("cad.draw_stair", true, (id, name, args) => RunWrite(id, name, args, DrawStair)) },
                { "cad.draw_text", new ToolDefinition("cad.draw_text", true, (id, name, args) => RunWrite(id, name, args, DrawText)) },
            };

        public static bool IsCadTool(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Tools.ContainsKey(name);
        }

        public static bool IsWriteTool(string name)
        {
            ToolDefinition tool;
            return !string.IsNullOrWhiteSpace(name) && Tools.TryGetValue(name, out tool) && tool.WritesDwg;
        }

        public static CadToolExecutionResult Execute(string callId, string name, JObject args)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                args = args ?? new JObject();
                CadToolExecutionResult result;
                ToolDefinition tool;
                if (Tools.TryGetValue(name ?? "", out tool))
                {
                    result = tool.Execute(callId, name, args);
                }
                else
                {
                    result = Fail(callId, name, "UNKNOWN_CAD_TOOL", "Unknown CAD tool: " + name);
                }

                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
                if (result.Data == null) result.Data = new JObject();
                result.Data["elapsed_ms"] = result.ElapsedMs;
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new CadToolExecutionResult
                {
                    CallId = callId,
                    ToolName = name,
                    Success = false,
                    Error = ex.Message,
                    Message = "CAD tool failed.",
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Data = new JObject
                    {
                        ["error_code"] = "CAD_TOOL_FAILED",
                        ["elapsed_ms"] = sw.ElapsedMilliseconds,
                    },
                };
            }
        }

        public static string FormatCall(JObject call)
        {
            if (call == null) return "";
            return (call.Value<string>("name") ?? "") + " " +
                   (call["args"] == null ? "{}" : call["args"].ToString(Formatting.None));
        }

        private static CadToolExecutionResult ReadSnapshot(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG snapshot captured.",
                Data = new JObject
                {
                    ["snapshot"] = snapshot,
                    ["summary_text"] = DrawingSnapshotCollector.FormatSummary(snapshot),
                },
            };
        }

        private static CadToolExecutionResult PreviewPlan(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            var entities = snapshot["entities"] as JArray ?? new JArray();
            var selected = SelectMatchingEntities(snapshot, args);
            var operations = args["operations"] as JArray ?? new JArray();
            var expectedEffect = args["expected_effect"] as JObject ?? new JObject();
            var selectors = SelectorSummary(args);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "CAD plan preview prepared.",
                Data = new JObject
                {
                    ["writes_dwg"] = args.Value<bool?>("writes_dwg") ?? true,
                    ["operations_count"] = operations.Count,
                    ["selectors"] = selectors,
                    ["selected_entity_count"] = selected.Count,
                    ["current_entity_count"] = entities.Count,
                    ["current_summary"] = snapshot["summary"] ?? new JObject(),
                    ["expected_effect"] = expectedEffect,
                    ["selected_entities"] = EntitySummaryArray(selected, 30),
                },
            };
        }

        private static CadToolExecutionResult CountEntities(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            var selected = SelectMatchingEntities(snapshot, args);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG entities counted.",
                Data = new JObject
                {
                    ["filter"] = FilterSummary(args),
                    ["count"] = selected.Count,
                    ["by_layer"] = CountBy(selected, "layer"),
                    ["by_type"] = CountBy(selected, "type"),
                    ["entities"] = EntitySummaryArray(selected, 80),
                },
            };
        }

        private static CadToolExecutionResult MeasureBounds(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            var selected = SelectMatchingEntities(snapshot, args);
            var aggregate = MeasureSelectionBounds(selected);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG bounds measured.",
                Data = new JObject
                {
                    ["filter"] = FilterSummary(args),
                    ["count"] = selected.Count,
                    ["bounds"] = aggregate,
                    ["entities"] = EntitySummaryArray(selected, 80),
                },
            };
        }

        private static CadToolExecutionResult MeasureDistance(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            var from = ReadDistancePoint(snapshot, args, "from", "from_selector", "x1", "y1");
            var to = ReadDistancePoint(snapshot, args, "to", "to_selector", "x2", "y2");
            var distance = from.DistanceTo(to);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG distance measured.",
                Data = new JObject
                {
                    ["from"] = new JArray(from.X, from.Y, from.Z),
                    ["to"] = new JArray(to.X, to.Y, to.Z),
                    ["distance"] = distance,
                },
            };
        }

        private static CadToolExecutionResult LayerDiff(string callId, string name, JObject args)
        {
            var before = args["before_snapshot"] as JObject ?? args["before"] as JObject;
            if (before == null)
            {
                return Fail(callId, name, "SCHEMA_INVALID", "'before_snapshot' is required.");
            }

            var after = args["after_snapshot"] as JObject ?? DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(after, args.Value<int?>("limit"));
            var beforeSelected = SelectMatchingEntities(before, args);
            var afterSelected = SelectMatchingEntities(after, args);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG layer diff computed.",
                Data = new JObject
                {
                    ["filter"] = FilterSummary(args),
                    ["before_count"] = beforeSelected.Count,
                    ["after_count"] = afterSelected.Count,
                    ["delta"] = afterSelected.Count - beforeSelected.Count,
                    ["layer_diff"] = CountDiff(CountMap(beforeSelected, "layer"), CountMap(afterSelected, "layer")),
                    ["before_summary"] = before["summary"] ?? new JObject(),
                    ["after_summary"] = after["summary"] ?? new JObject(),
                },
            };
        }

        private static CadToolExecutionResult BeforeAfterDiff(string callId, string name, JObject args)
        {
            var before = args["before_snapshot"] as JObject ?? args["before"] as JObject;
            if (before == null)
            {
                return Fail(callId, name, "SCHEMA_INVALID", "'before_snapshot' is required.");
            }

            var after = args["after_snapshot"] as JObject ?? DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(after, args.Value<int?>("limit"));
            var beforeSelected = SelectMatchingEntities(before, args);
            var afterSelected = SelectMatchingEntities(after, args);

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = "DWG before/after diff computed.",
                Data = new JObject
                {
                    ["filter"] = FilterSummary(args),
                    ["before_count"] = beforeSelected.Count,
                    ["after_count"] = afterSelected.Count,
                    ["delta"] = afterSelected.Count - beforeSelected.Count,
                    ["layer_diff"] = CountDiff(CountMap(beforeSelected, "layer"), CountMap(afterSelected, "layer")),
                    ["type_diff"] = CountDiff(CountMap(beforeSelected, "type"), CountMap(afterSelected, "type")),
                    ["before_bounds"] = MeasureSelectionBounds(beforeSelected),
                    ["after_bounds"] = MeasureSelectionBounds(afterSelected),
                    ["before_summary"] = before["summary"] ?? new JObject(),
                    ["after_summary"] = after["summary"] ?? new JObject(),
                },
            };
        }

        private static CadToolExecutionResult ValidateDwgState(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("limit"));
            var checks = new JArray();
            var passed = true;
            var layers = snapshot["layers"] as JArray ?? new JArray();
            var entities = SelectMatchingEntities(snapshot, args);
            var warnings = snapshot["warnings"] as JArray ?? new JArray();
            var expectedLayers = args["expected_layers"] as JArray;
            if (expectedLayers != null)
            {
                foreach (var layerToken in expectedLayers)
                {
                    var layerName = layerToken.Value<string>();
                    var ok = layers.Any(l => string.Equals(l?["name"]?.Value<string>(), layerName, StringComparison.OrdinalIgnoreCase));
                    checks.Add(CheckResult("layer_exists:" + layerName, ok, ok ? "" : "Missing layer " + layerName));
                    passed &= ok;
                }
            }

            var minEntities = args.Value<int?>("expected_min_entities");
            if (minEntities.HasValue)
            {
                var ok = entities.Count >= minEntities.Value;
                checks.Add(CheckResult("min_entities", ok, "actual=" + entities.Count + ", expected_min=" + minEntities.Value));
                passed &= ok;
            }

            var expectedTypes = args["expected_types"] as JArray;
            if (expectedTypes != null)
            {
                foreach (var typeToken in expectedTypes)
                {
                    var expectedType = typeToken.Value<string>();
                    var count = entities.Count(e => TypeMatches(e, expectedType));
                    var ok = count > 0;
                    checks.Add(CheckResult("type_exists:" + expectedType, ok, "count=" + count));
                    passed &= ok;
                }
            }

            var expectedLayerCounts = args["expected_layer_entity_counts"] as JObject;
            if (expectedLayerCounts != null)
            {
                foreach (var prop in expectedLayerCounts.Properties())
                {
                    var layerName = prop.Name;
                    var expectedMin = prop.Value.Value<int>();
                    var count = entities.Count(e => string.Equals(e?["layer"]?.Value<string>(), layerName, StringComparison.OrdinalIgnoreCase));
                    var ok = count >= expectedMin;
                    checks.Add(CheckResult("layer_min_count:" + layerName, ok, "actual=" + count + ", expected_min=" + expectedMin));
                    passed &= ok;
                }
            }

            var maxWarnings = args.Value<int?>("max_warnings");
            if (maxWarnings.HasValue)
            {
                var ok = warnings.Count <= maxWarnings.Value;
                checks.Add(CheckResult("max_warnings", ok, "actual=" + warnings.Count + ", max=" + maxWarnings.Value));
                passed &= ok;
            }

            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = passed ? "DWG validation passed." : "DWG validation failed.",
                Data = new JObject
                {
                    ["filter"] = FilterSummary(args),
                    ["passed"] = passed,
                    ["checks"] = checks,
                    ["summary"] = snapshot["summary"] ?? new JObject(),
                    ["warnings"] = warnings,
                },
            };
        }

        private sealed class BoundsAccumulator
        {
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            public bool HasValue { get; private set; }

            public void Add(JObject entity)
            {
                var bounds = entity?["bounds"] as JObject;
                var min = bounds?["min"] as JArray;
                var max = bounds?["max"] as JArray;
                if (min == null || max == null || min.Count < 2 || max.Count < 2) return;

                var minX = min[0].Value<double>();
                var minY = min[1].Value<double>();
                var minZ = min.Count > 2 ? min[2].Value<double>() : 0;
                var maxX = max[0].Value<double>();
                var maxY = max[1].Value<double>();
                var maxZ = max.Count > 2 ? max[2].Value<double>() : 0;
                if (!HasValue)
                {
                    _minX = minX;
                    _minY = minY;
                    _minZ = minZ;
                    _maxX = maxX;
                    _maxY = maxY;
                    _maxZ = maxZ;
                    HasValue = true;
                    return;
                }

                _minX = Math.Min(_minX, minX);
                _minY = Math.Min(_minY, minY);
                _minZ = Math.Min(_minZ, minZ);
                _maxX = Math.Max(_maxX, maxX);
                _maxY = Math.Max(_maxY, maxY);
                _maxZ = Math.Max(_maxZ, maxZ);
            }

            public JObject ToJson()
            {
                if (!HasValue) return null;
                return new JObject
                {
                    ["min"] = new JArray(_minX, _minY, _minZ),
                    ["max"] = new JArray(_maxX, _maxY, _maxZ),
                    ["width"] = _maxX - _minX,
                    ["height"] = _maxY - _minY,
                    ["depth"] = _maxZ - _minZ,
                    ["center"] = new JArray((_minX + _maxX) / 2, (_minY + _maxY) / 2, (_minZ + _maxZ) / 2),
                };
            }

            public Point3d Center()
            {
                if (!HasValue) throw new InvalidOperationException("Selection has no measurable bounds.");
                return new Point3d((_minX + _maxX) / 2, (_minY + _maxY) / 2, (_minZ + _maxZ) / 2);
            }
        }

        private static void ApplySnapshotLimit(JObject snapshot, int? limit)
        {
            if (!limit.HasValue || limit.Value <= 0) return;
            var entities = snapshot?["entities"] as JArray;
            if (entities == null || entities.Count <= limit.Value) return;
            var trimmed = new JArray(entities.Take(limit.Value));
            snapshot["entities"] = trimmed;
            var summary = snapshot["summary"] as JObject;
            if (summary != null) summary["truncated"] = true;
            var warnings = snapshot["warnings"] as JArray;
            if (warnings != null) warnings.Add("Snapshot limited to " + limit.Value + " entities for tool context.");
        }

        private static List<JObject> SelectMatchingEntities(JObject snapshot, JObject args)
        {
            var selected = new List<JObject>();
            var entities = snapshot?["entities"] as JArray ?? new JArray();
            var selectors = ReadSelectors(args);
            var includeExploded = args.Value<bool?>("include_exploded") ?? true;
            var layer = args.Value<string>("layer");
            var type = args.Value<string>("type");
            var handle = args.Value<string>("handle");

            foreach (var token in entities)
            {
                var entity = token as JObject;
                if (entity == null) continue;
                if (!includeExploded && entity["source"]?["from_block_explode"]?.Value<bool>() == true) continue;
                if (!string.IsNullOrWhiteSpace(layer) &&
                    !string.Equals(entity.Value<string>("layer"), layer, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(type) && !TypeMatches(entity, type)) continue;
                if (!string.IsNullOrWhiteSpace(handle) &&
                    !string.Equals(entity.Value<string>("handle"), handle, StringComparison.OrdinalIgnoreCase)) continue;
                if (selectors.Count > 0 && !selectors.Any(selector => MatchesSelector(entity, selector))) continue;
                selected.Add(entity);
            }

            return selected;
        }

        private static List<string> ReadSelectors(JObject args)
        {
            var selectors = new List<string>();
            var selector = args.Value<string>("selector");
            if (!string.IsNullOrWhiteSpace(selector)) selectors.Add(selector.Trim());
            var array = args["selectors"] as JArray;
            if (array != null)
            {
                foreach (var token in array)
                {
                    var value = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value)) selectors.Add(value.Trim());
                }
            }
            return selectors;
        }

        private static bool MatchesSelector(JObject entity, string selector)
        {
            if (entity == null || string.IsNullOrWhiteSpace(selector)) return true;
            var parts = selector.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { selector };

            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.Length == 0) continue;
                var colon = part.IndexOf(':');
                if (colon <= 0)
                {
                    if (!BareSelectorMatches(entity, part)) return false;
                    continue;
                }

                var key = part.Substring(0, colon).Trim().ToLowerInvariant();
                var value = StripOrdinal(part.Substring(colon + 1).Trim());
                if (value.Length == 0) return false;
                switch (key)
                {
                    case "layer":
                        if (!string.Equals(entity.Value<string>("layer"), value, StringComparison.OrdinalIgnoreCase)) return false;
                        break;
                    case "handle":
                        if (!string.Equals(entity.Value<string>("handle"), value, StringComparison.OrdinalIgnoreCase)) return false;
                        break;
                    case "type":
                    case "entity":
                        if (!TypeMatches(entity, value)) return false;
                        break;
                    case "block":
                        if (!BlockPathMatches(entity, value)) return false;
                        break;
                    default:
                        if (!BareSelectorMatches(entity, value)) return false;
                        break;
                }
            }

            return true;
        }

        private static bool BareSelectorMatches(JObject entity, string value)
        {
            value = StripOrdinal(value);
            return string.Equals(entity.Value<string>("handle"), value, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entity.Value<string>("layer"), value, StringComparison.OrdinalIgnoreCase) ||
                   TypeMatches(entity, value) ||
                   BlockPathMatches(entity, value);
        }

        private static bool TypeMatches(JObject entity, string type)
        {
            return string.Equals(entity?["type"]?.Value<string>(), type, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entity?["geometry"]?["entity_type"]?.Value<string>(), type, StringComparison.OrdinalIgnoreCase);
        }

        private static bool BlockPathMatches(JObject entity, string blockName)
        {
            var path = entity?["source"]?["block_path"] as JArray;
            if (path != null)
            {
                foreach (var part in path.Values<string>())
                {
                    if (string.Equals(StripOrdinal(part), blockName, StringComparison.OrdinalIgnoreCase)) return true;
                    if ((part ?? "").IndexOf(blockName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            var referenceName = entity?["block_reference"]?["name"]?.Value<string>();
            return !string.IsNullOrWhiteSpace(referenceName) &&
                   (string.Equals(StripOrdinal(referenceName), blockName, StringComparison.OrdinalIgnoreCase) ||
                    referenceName.IndexOf(blockName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string StripOrdinal(string value)
        {
            if (value == null) return "";
            var trimmed = value.Trim();
            var hash = trimmed.IndexOf('#');
            return hash >= 0 ? trimmed.Substring(0, hash).Trim() : trimmed;
        }

        private static JObject FilterSummary(JObject args)
        {
            return new JObject
            {
                ["selector"] = string.IsNullOrWhiteSpace(args.Value<string>("selector")) ? null : args.Value<string>("selector"),
                ["selectors"] = SelectorSummary(args),
                ["layer"] = string.IsNullOrWhiteSpace(args.Value<string>("layer")) ? null : args.Value<string>("layer"),
                ["type"] = string.IsNullOrWhiteSpace(args.Value<string>("type")) ? null : args.Value<string>("type"),
                ["handle"] = string.IsNullOrWhiteSpace(args.Value<string>("handle")) ? null : args.Value<string>("handle"),
                ["include_exploded"] = args.Value<bool?>("include_exploded") ?? true,
            };
        }

        private static JArray SelectorSummary(JObject args)
        {
            var arr = new JArray();
            foreach (var selector in ReadSelectors(args)) arr.Add(selector);
            return arr;
        }

        private static JArray EntitySummaryArray(IEnumerable<JObject> entities, int limit)
        {
            var arr = new JArray();
            foreach (var entity in entities.Take(limit))
            {
                arr.Add(new JObject
                {
                    ["handle"] = entity.Value<string>("handle"),
                    ["type"] = entity.Value<string>("type"),
                    ["layer"] = entity.Value<string>("layer"),
                    ["selectors"] = entity["selectors"] ?? new JArray(),
                });
            }
            return arr;
        }

        private static JObject MeasureSelectionBounds(IEnumerable<JObject> entities)
        {
            var acc = new BoundsAccumulator();
            foreach (var entity in entities) acc.Add(entity);
            return acc.ToJson();
        }

        private static Point3d ReadDistancePoint(JObject snapshot, JObject args, string arrayName, string selectorName, string xName, string yName)
        {
            var selector = args.Value<string>(selectorName);
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var selectorArgs = new JObject
                {
                    ["selector"] = selector,
                    ["include_exploded"] = args.Value<bool?>("include_exploded") ?? true,
                };
                var selected = SelectMatchingEntities(snapshot, selectorArgs);
                var acc = new BoundsAccumulator();
                foreach (var entity in selected) acc.Add(entity);
                return acc.Center();
            }

            return ReadPoint(args, arrayName, xName, yName);
        }

        private static JObject CountBy(IEnumerable<JObject> entities, string field)
        {
            var obj = new JObject();
            foreach (var pair in CountMap(entities, field).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                obj[pair.Key] = pair.Value;
            }
            return obj;
        }

        private static Dictionary<string, int> CountMap(IEnumerable<JObject> entities, string field)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                var key = field == "type"
                    ? (entity.Value<string>("type") ?? entity["geometry"]?["entity_type"]?.Value<string>())
                    : entity.Value<string>(field);
                if (string.IsNullOrWhiteSpace(key)) key = "(none)";
                map[key] = map.ContainsKey(key) ? map[key] + 1 : 1;
            }
            return map;
        }

        private static JArray CountDiff(Dictionary<string, int> before, Dictionary<string, int> after)
        {
            var arr = new JArray();
            var keys = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in after.Keys) keys.Add(key);
            foreach (var key in keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var b = before.ContainsKey(key) ? before[key] : 0;
                var a = after.ContainsKey(key) ? after[key] : 0;
                if (b == a) continue;
                arr.Add(new JObject
                {
                    ["key"] = key,
                    ["before"] = b,
                    ["after"] = a,
                    ["delta"] = a - b,
                });
            }
            return arr;
        }

        private static JObject CheckResult(string name, bool passed, string detail)
        {
            return new JObject
            {
                ["name"] = name,
                ["passed"] = passed,
                ["detail"] = detail ?? "",
            };
        }

        private static CadToolExecutionResult RunWrite(
            string callId,
            string name,
            JObject args,
            Func<Database, Transaction, JObject, JObject> action)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return Fail(callId, name, "NO_ACTIVE_DOCUMENT", "No active AutoCAD document.");
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var data = action(doc.Database, tr, args);
                tr.Commit();
                return new CadToolExecutionResult
                {
                    CallId = callId,
                    ToolName = name,
                    Success = true,
                    Message = "CAD tool executed.",
                    Data = data,
                };
            }
        }

        private static JObject CreateLayer(Database db, Transaction tr, JObject args)
        {
            var name = RequiredString(args, "name");
            ValidateLayerName(name);
            var layer = EnsureLayer(db, tr, name);
            var color = OptionalColor(args, "color");
            if (color.HasValue)
            {
                ValidateColor(color.Value);
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color.Value);
            }

            return new JObject
            {
                ["layer"] = name,
                ["handle"] = layer.Handle.ToString(),
                ["object_id"] = layer.ObjectId.ToString(),
            };
        }

        private static JObject DrawLine(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var start = ReadPoint(args, "start", "x1", "y1");
            var end = ReadPoint(args, "end", "x2", "y2");
            EnsureLayer(db, tr, layer);

            var line = new Line(start, end) { Layer = layer };
            ApplyEntityColor(line, args);
            AppendToModelSpace(db, tr, line);
            return EntityResult("Line", line, layer);
        }

        private static JObject DrawPolyline(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var points = args["points"] as JArray;
            if (points == null || points.Count < 2)
            {
                throw new InvalidOperationException("'points' must contain at least two points.");
            }
            if (points.Count > 1000)
            {
                throw new InvalidOperationException("'points' contains too many vertices.");
            }

            EnsureLayer(db, tr, layer);
            var pl = new Polyline(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var point = ReadPointToken(points[i], "points[" + i + "]");
                pl.AddVertexAt(i, new Point2d(point.X, point.Y), 0, 0, 0);
            }

            pl.Closed = OptionalBool(args, "closed", false);
            var width = OptionalDouble(args, "constant_width", 0);
            if (width > 0)
            {
                ValidatePositiveDimension(width, "constant_width");
                pl.ConstantWidth = width;
            }
            pl.Layer = layer;
            ApplyEntityColor(pl, args);
            AppendToModelSpace(db, tr, pl);

            var data = EntityResult("Polyline", pl, layer);
            data["point_count"] = points.Count;
            data["closed"] = pl.Closed;
            if (width > 0) data["constant_width"] = width;
            return data;
        }

        private static JObject DrawCircle(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var center = ReadPoint(args, "center", "x", "y");
            var radius = RequiredDouble(args, "radius");
            ValidatePositiveDimension(radius, "radius");
            EnsureLayer(db, tr, layer);

            var circle = new Circle(center, Vector3d.ZAxis, radius) { Layer = layer };
            ApplyEntityColor(circle, args);
            AppendToModelSpace(db, tr, circle);

            var data = EntityResult("Circle", circle, layer);
            data["radius"] = radius;
            return data;
        }

        private static JObject DrawRectangle(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var x = OptionalDouble(args, "x", 0);
            var y = OptionalDouble(args, "y", 0);
            var width = RequiredDouble(args, "width");
            var height = RequiredDouble(args, "height");
            var rotationDeg = OptionalDouble(args, "rotation", 0);
            ValidateCoordinate(x, "x");
            ValidateCoordinate(y, "y");
            ValidatePositiveDimension(width, "width");
            ValidatePositiveDimension(height, "height");
            EnsureLayer(db, tr, layer);

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(width, 0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(width, height), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(0, height), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            ApplyEntityColor(pl, args);
            if (Math.Abs(rotationDeg) > 1e-9)
            {
                pl.TransformBy(Matrix3d.Rotation(rotationDeg * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin));
            }
            if (Math.Abs(x) > 1e-9 || Math.Abs(y) > 1e-9)
            {
                pl.TransformBy(Matrix3d.Displacement(new Vector3d(x, y, 0)));
            }

            AppendToModelSpace(db, tr, pl);
            var data = EntityResult("Polyline", pl, layer);
            data["width"] = width;
            data["height"] = height;
            return data;
        }

        private static JObject DrawText(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var text = RequiredString(args, "text");
            ValidateText(text);
            if (LooksLikeAssistantReplyText(text))
            {
                throw new InvalidOperationException("Text looks like an assistant reply. Replies must stay in the panel.");
            }

            var pos = ReadPoint(args, "position", "x", "y");
            var height = OptionalDouble(args, "height", 250);
            var rotationDeg = OptionalDouble(args, "rotation", 0);
            ValidatePositiveDimension(height, "height");
            EnsureLayer(db, tr, layer);

            var dbText = new DBText
            {
                TextString = text,
                Position = pos,
                Height = height,
                Rotation = rotationDeg * Math.PI / 180.0,
                Layer = layer,
            };
            ApplyEntityColor(dbText, args);
            ApplyTextAlignment(dbText, OptionalString(args, "alignment", "left"), pos);
            AppendToModelSpace(db, tr, dbText);
            var data = EntityResult("DBText", dbText, layer);
            data["text"] = text;
            return data;
        }

        private static JObject DrawStair(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "STAIR");
            ValidateLayerName(layer);
            var x = OptionalDouble(args, "x", 0);
            var y = OptionalDouble(args, "y", 0);
            var width = OptionalDouble(args, "width", OptionalDouble(args, "stair_width", 1200));
            var treadDepth = OptionalDouble(args, "tread_depth", 250);
            var riserHeight = OptionalDouble(args, "riser_height", 150);
            var floorHeight = OptionalDouble(args, "floor_height", 3900);
            var platformDepth = OptionalDouble(args, "platform_depth", width);
            var totalRisersArg = args.Value<int?>("total_risers");
            ValidateCoordinate(x, "x");
            ValidateCoordinate(y, "y");
            ValidatePositiveDimension(width, "width");
            ValidatePositiveDimension(treadDepth, "tread_depth");
            ValidatePositiveDimension(riserHeight, "riser_height");
            ValidatePositiveDimension(floorHeight, "floor_height");
            ValidatePositiveDimension(platformDepth, "platform_depth");

            var totalRisers = totalRisersArg.HasValue && totalRisersArg.Value > 0
                ? totalRisersArg.Value
                : (int)Math.Round(floorHeight / riserHeight);
            if (totalRisers < 2) totalRisers = 2;
            var risersPerRun = (int)Math.Ceiling(totalRisers / 2.0);
            var runLength = risersPerRun * treadDepth;
            var rightX = x + width + platformDepth;
            var topY = y + runLength;
            var totalWidth = width * 2 + platformDepth;
            var totalHeight = runLength + platformDepth;

            EnsureLayer(db, tr, layer);
            var handles = new JArray();
            handles.Add(AppendRectangleEntity(db, tr, args, layer, x, y, width, runLength).Handle.ToString());
            handles.Add(AppendRectangleEntity(db, tr, args, layer, rightX, y, width, runLength).Handle.ToString());
            handles.Add(AppendRectangleEntity(db, tr, args, layer, x, topY, totalWidth, platformDepth).Handle.ToString());

            for (var i = 1; i < risersPerRun; i++)
            {
                var ty = y + i * treadDepth;
                handles.Add(AppendLineEntity(db, tr, args, layer, new Point3d(x, ty, 0), new Point3d(x + width, ty, 0)).Handle.ToString());
                handles.Add(AppendLineEntity(db, tr, args, layer, new Point3d(rightX, ty, 0), new Point3d(rightX + width, ty, 0)).Handle.ToString());
            }

            return new JObject
            {
                ["entity_type"] = "StairU",
                ["layer"] = layer,
                ["handles"] = handles,
                ["object_count"] = handles.Count,
                ["layout"] = "u_double_run",
                ["width"] = width,
                ["tread_depth"] = treadDepth,
                ["riser_height"] = riserHeight,
                ["floor_height"] = floorHeight,
                ["total_risers"] = totalRisers,
                ["risers_per_run"] = risersPerRun,
                ["platform_depth"] = platformDepth,
                ["bounds"] = new JObject
                {
                    ["min"] = new JArray(x, y, 0),
                    ["max"] = new JArray(x + totalWidth, y + totalHeight, 0),
                    ["width"] = totalWidth,
                    ["height"] = totalHeight,
                },
            };
        }

        private static Polyline AppendRectangleEntity(Database db, Transaction tr, JObject args, string layer, double x, double y, double width, double height)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x + width, y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x + width, y + height), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x, y + height), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            ApplyEntityColor(pl, args);
            AppendToModelSpace(db, tr, pl);
            return pl;
        }

        private static Line AppendLineEntity(Database db, Transaction tr, JObject args, string layer, Point3d start, Point3d end)
        {
            var line = new Line(start, end) { Layer = layer };
            ApplyEntityColor(line, args);
            AppendToModelSpace(db, tr, line);
            return line;
        }

        private static LayerTableRecord EnsureLayer(Database db, Transaction tr, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                return (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            }

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = name };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return ltr;
        }

        private static void AppendToModelSpace(Database db, Transaction tr, Entity entity)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ms.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }

        private static JObject EntityResult(string type, Entity entity, string layer)
        {
            return new JObject
            {
                ["entity_type"] = type,
                ["handle"] = entity.Handle.ToString(),
                ["object_id"] = entity.ObjectId.ToString(),
                ["layer"] = layer,
            };
        }

        private static void ApplyEntityColor(Entity entity, JObject args)
        {
            var color = OptionalColor(args, "color");
            if (!color.HasValue) return;
            ValidateColor(color.Value);
            entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color.Value);
        }

        private static void ApplyTextAlignment(DBText t, string alignment, Point3d position)
        {
            if (string.IsNullOrEmpty(alignment)) return;
            switch (alignment.ToLowerInvariant())
            {
                case "center":
                case "middle_center":
                    t.HorizontalMode = TextHorizontalMode.TextCenter;
                    t.VerticalMode = TextVerticalMode.TextVerticalMid;
                    t.AlignmentPoint = position;
                    break;
                case "right":
                    t.HorizontalMode = TextHorizontalMode.TextRight;
                    t.AlignmentPoint = position;
                    break;
                default:
                    t.HorizontalMode = TextHorizontalMode.TextLeft;
                    t.VerticalMode = TextVerticalMode.TextBase;
                    break;
            }
        }

        private static Point3d ReadPoint(JObject args, string arrayName, string xName, string yName)
        {
            var array = args[arrayName] as JArray;
            if (array != null && array.Count >= 2)
            {
                var x = array[0].Value<double>();
                var y = array[1].Value<double>();
                var z = array.Count > 2 ? array[2].Value<double>() : 0;
                ValidateCoordinate(x, arrayName + ".x");
                ValidateCoordinate(y, arrayName + ".y");
                ValidateCoordinate(z, arrayName + ".z");
                return new Point3d(x, y, z);
            }

            var px = RequiredDouble(args, xName);
            var py = RequiredDouble(args, yName);
            var pz = OptionalDouble(args, "z", 0);
            ValidateCoordinate(px, xName);
            ValidateCoordinate(py, yName);
            ValidateCoordinate(pz, "z");
            return new Point3d(px, py, pz);
        }

        private static Point3d ReadPointToken(JToken token, string name)
        {
            var array = token as JArray;
            if (array != null && array.Count >= 2)
            {
                var x = array[0].Value<double>();
                var y = array[1].Value<double>();
                var z = array.Count > 2 ? array[2].Value<double>() : 0;
                ValidateCoordinate(x, name + ".x");
                ValidateCoordinate(y, name + ".y");
                ValidateCoordinate(z, name + ".z");
                return new Point3d(x, y, z);
            }

            var obj = token as JObject;
            if (obj != null)
            {
                var x = RequiredDouble(obj, "x");
                var y = RequiredDouble(obj, "y");
                var z = OptionalDouble(obj, "z", 0);
                ValidateCoordinate(x, name + ".x");
                ValidateCoordinate(y, name + ".y");
                ValidateCoordinate(z, name + ".z");
                return new Point3d(x, y, z);
            }

            throw new InvalidOperationException("'" + name + "' must be [x,y] or {x,y}.");
        }

        private static string RequiredString(JObject args, string name)
        {
            var value = args.Value<string>(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("'" + name + "' is required.");
            }
            return value.Trim();
        }

        private static string OptionalString(JObject args, string name, string fallback)
        {
            var value = args.Value<string>(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static double RequiredDouble(JObject args, string name)
        {
            var token = args[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw new InvalidOperationException("'" + name + "' is required.");
            }
            return token.Value<double>();
        }

        private static double OptionalDouble(JObject args, string name, double fallback)
        {
            var token = args[name];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<double>();
        }

        private static bool OptionalBool(JObject args, string name, bool fallback)
        {
            var token = args[name];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        private static int? OptionalColor(JObject args, string name)
        {
            var token = args[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return NormalizeColorIndex(token.Value<int>());
            }

            var value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "")
                .ToLowerInvariant();
            if (normalized == "bylayer" || normalized == "byblock" || normalized == "layer")
            {
                return null;
            }
            if (normalized.StartsWith("aci", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(3);
            }

            int color;
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out color))
            {
                return NormalizeColorIndex(color);
            }

            switch (normalized)
            {
                case "red":
                    return 1;
                case "yellow":
                    return 2;
                case "green":
                    return 3;
                case "cyan":
                    return 4;
                case "blue":
                    return 5;
                case "magenta":
                case "purple":
                    return 6;
                case "white":
                case "black":
                    return 7;
                case "gray":
                case "grey":
                    return 8;
                default:
                    throw new InvalidOperationException("'color' must be an AutoCAD ACI number, ByLayer, or a known color name.");
            }
        }

        private static int? NormalizeColorIndex(int color)
        {
            if (color == 0 || color == 256)
            {
                return null;
            }
            return color;
        }

        private static void ValidateLayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Layer name is required.");
            }
            if (name.Length > 255)
            {
                throw new InvalidOperationException("Layer name is too long.");
            }
            if (name.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=' }) >= 0)
            {
                throw new InvalidOperationException("Layer name contains invalid characters.");
            }
        }

        private static void ValidateText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("'text' is required.");
            }
            if (text.Length > MaxTextChars)
            {
                throw new InvalidOperationException("'text' is too long.");
            }
        }

        private static void ValidateColor(int color)
        {
            if (color < 1 || color > 255)
            {
                throw new InvalidOperationException("'color' must be an AutoCAD ACI value from 1 to 255.");
            }
        }

        private static void ValidateCoordinate(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxCoordinate)
            {
                throw new InvalidOperationException("'" + name + "' is outside the supported coordinate range.");
            }
        }

        private static void ValidatePositiveDimension(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > MaxDimension)
            {
                throw new InvalidOperationException("'" + name + "' must be positive and within range.");
            }
        }

        private static bool LooksLikeAssistantReplyText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var compact = text.Trim();
            if (compact.Length < 12) return false;

            var lower = compact.ToLowerInvariant();
            var fragments = new[]
            {
                "voicecad",
                "cad 助手",
                "agent 进度",
                "技术原因",
                "没有修改当前图纸",
                "确认前不会修改图纸",
                "我已读取当前 dwg",
                "模型仍在生成",
                "正在调用模型",
                "请告诉我要画什么",
                "对话回复",
                "状态说明",
                "无法读取",
                "需要 ocr",
            };
            foreach (var fragment in fragments)
            {
                if (lower.Contains(fragment.ToLowerInvariant())) return true;
            }
            return false;
        }

        private static CadToolExecutionResult Fail(string callId, string name, string code, string message)
        {
            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = false,
                Error = message,
                Message = message,
                Data = new JObject { ["error_code"] = code },
            };
        }
    }
}
