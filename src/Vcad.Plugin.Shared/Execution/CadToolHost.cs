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
                { "cad.read_layers", new ToolDefinition("cad.read_layers", false, ReadLayers) },
                { "cad.read_styles", new ToolDefinition("cad.read_styles", false, ReadStyles) },
                { "cad.read_blocks", new ToolDefinition("cad.read_blocks", false, ReadBlocks) },
                { "cad.query_entities", new ToolDefinition("cad.query_entities", false, QueryEntities) },
                { "cad.describe_entity", new ToolDefinition("cad.describe_entity", false, DescribeEntity) },
                { "cad.describe_selection", new ToolDefinition("cad.describe_selection", false, DescribeSelection) },
                { "cad.find_near", new ToolDefinition("cad.find_near", false, FindNear) },
                { "cad.find_intersections", new ToolDefinition("cad.find_intersections", false, FindIntersections) },
                { "cad.find_connected_contours", new ToolDefinition("cad.find_connected_contours", false, FindConnectedContours) },
                { "cad.find_closed_regions", new ToolDefinition("cad.find_closed_regions", false, FindClosedRegions) },
                { "cad.measure_relation", new ToolDefinition("cad.measure_relation", false, MeasureRelation) },
                { "cad.semantic_scan", new ToolDefinition("cad.semantic_scan", false, SemanticScan) },
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
                { "cad.draw_arc", new ToolDefinition("cad.draw_arc", true, (id, name, args) => RunWrite(id, name, args, DrawArc)) },
                { "cad.draw_rectangle", new ToolDefinition("cad.draw_rectangle", true, (id, name, args) => RunWrite(id, name, args, DrawRectangle)) },
                { "cad.draw_room", new ToolDefinition("cad.draw_room", true, (id, name, args) => RunWrite(id, name, args, DrawRoom)) },
                { "cad.draw_wall", new ToolDefinition("cad.draw_wall", true, (id, name, args) => RunWrite(id, name, args, DrawWall)) },
                { "cad.draw_stair", new ToolDefinition("cad.draw_stair", true, (id, name, args) => RunWrite(id, name, args, DrawStair)) },
                { "cad.draw_text", new ToolDefinition("cad.draw_text", true, (id, name, args) => RunWrite(id, name, args, DrawText)) },
                { "cad.draw_mtext", new ToolDefinition("cad.draw_mtext", true, (id, name, args) => RunWrite(id, name, args, DrawMText)) },
                { "cad.draw_dimension", new ToolDefinition("cad.draw_dimension", true, (id, name, args) => RunWrite(id, name, args, DrawDimension)) },
                { "cad.insert_block", new ToolDefinition("cad.insert_block", true, (id, name, args) => RunWrite(id, name, args, InsertBlock)) },
                { "cad.move_entities", new ToolDefinition("cad.move_entities", true, (id, name, args) => RunWrite(id, name, args, MoveEntities)) },
                { "cad.copy_entities", new ToolDefinition("cad.copy_entities", true, (id, name, args) => RunWrite(id, name, args, CopyEntities)) },
                { "cad.rotate_entities", new ToolDefinition("cad.rotate_entities", true, (id, name, args) => RunWrite(id, name, args, RotateEntities)) },
                { "cad.scale_entities", new ToolDefinition("cad.scale_entities", true, (id, name, args) => RunWrite(id, name, args, ScaleEntities)) },
                { "cad.offset_entities", new ToolDefinition("cad.offset_entities", true, (id, name, args) => RunWrite(id, name, args, OffsetEntities)) },
                { "cad.delete_entities", new ToolDefinition("cad.delete_entities", true, (id, name, args) => RunWrite(id, name, args, DeleteEntities)) },
                { "cad.change_layer", new ToolDefinition("cad.change_layer", true, (id, name, args) => RunWrite(id, name, args, ChangeLayer)) },
                { "cad.set_properties", new ToolDefinition("cad.set_properties", true, (id, name, args) => RunWrite(id, name, args, SetProperties)) },
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

        private static CadToolExecutionResult ReadLayers(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            return Success(callId, name, "DWG layers read.", new JObject
            {
                ["layers"] = snapshot["layers"] ?? new JArray(),
                ["summary"] = snapshot["summary"] ?? new JObject(),
            });
        }

        private static CadToolExecutionResult ReadStyles(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            return Success(callId, name, "DWG styles read.", new JObject
            {
                ["linetypes"] = snapshot["linetypes"] ?? new JArray(),
                ["text_styles"] = snapshot["text_styles"] ?? new JArray(),
                ["dim_styles"] = snapshot["dim_styles"] ?? new JArray(),
                ["database"] = snapshot["database"] ?? new JObject(),
            });
        }

        private static CadToolExecutionResult ReadBlocks(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            return Success(callId, name, "DWG blocks read.", new JObject
            {
                ["blocks"] = snapshot["blocks"] ?? new JArray(),
                ["block_references"] = snapshot["geometry_index"]?["block_references"] ?? new JArray(),
                ["summary"] = snapshot["summary"] ?? new JObject(),
            });
        }

        private static CadToolExecutionResult QueryEntities(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            ApplySnapshotLimit(snapshot, args.Value<int?>("scan_limit"));
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 120, 1000));
            var includeGeometry = args.Value<bool?>("include_geometry") ?? true;
            var includeProperties = args.Value<bool?>("include_properties") ?? true;

            return Success(callId, name, "DWG entities queried.", new JObject
            {
                ["filter"] = FilterSummary(args),
                ["count"] = selected.Count,
                ["returned"] = Math.Min(selected.Count, limit),
                ["by_layer"] = CountBy(selected, "layer"),
                ["by_type"] = CountBy(selected, "type"),
                ["bounds"] = MeasureSelectionBounds(selected),
                ["entities"] = EntityDetailArray(selected, limit, includeGeometry, includeProperties),
            });
        }

        private static CadToolExecutionResult DescribeEntity(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            if (selected.Count == 0)
            {
                return Fail(callId, name, "NOT_FOUND", "No entity matched the selector.");
            }

            var entity = selected[0];
            var center = EntityCenter(entity);
            var nearbyArgs = new JObject
            {
                ["x"] = center.X,
                ["y"] = center.Y,
                ["radius"] = args.Value<double?>("near_radius") ?? 1000,
                ["include_exploded"] = args.Value<bool?>("include_exploded") ?? true,
            };
            var nearby = ApplyAdvancedEntityFilters(snapshot, nearbyArgs)
                .Where(e => !string.Equals(e.Value<string>("handle"), entity.Value<string>("handle"), StringComparison.OrdinalIgnoreCase))
                .Take(30)
                .ToList();

            return Success(callId, name, "DWG entity described.", new JObject
            {
                ["entity"] = entity,
                ["center"] = new JArray(center.X, center.Y, center.Z),
                ["nearby"] = EntitySummaryArray(nearby, 30),
                ["relations"] = DescribeRelations(entity, nearby),
            });
        }

        private static CadToolExecutionResult DescribeSelection(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            return Success(callId, name, "DWG selection described.", new JObject
            {
                ["filter"] = FilterSummary(args),
                ["count"] = selected.Count,
                ["by_layer"] = CountBy(selected, "layer"),
                ["by_type"] = CountBy(selected, "type"),
                ["bounds"] = MeasureSelectionBounds(selected),
                ["text_sample"] = TextSample(selected, 30),
                ["closed_entities"] = EntitySummaryArray(selected.Where(IsClosedEntity), 50),
                ["entities"] = EntitySummaryArray(selected, 120),
            });
        }

        private static CadToolExecutionResult FindNear(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            var origin = ReadNearOrigin(snapshot, args);
            var radius = args.Value<double?>("radius") ?? 1000;
            ValidatePositiveDimension(radius, "radius");
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 80, 500));

            var ranked = selected
                .Select(e => new { Entity = e, Distance = DistanceToEntity(origin, e) })
                .Where(x => x.Distance <= radius)
                .OrderBy(x => x.Distance)
                .Take(limit)
                .Select(x =>
                {
                    var item = EntitySummary(x.Entity);
                    item["distance"] = x.Distance;
                    item["bounds"] = x.Entity["bounds"];
                    return item;
                });

            return Success(callId, name, "Nearby DWG entities found.", new JObject
            {
                ["origin"] = new JArray(origin.X, origin.Y, origin.Z),
                ["radius"] = radius,
                ["entities"] = new JArray(ranked),
            });
        }

        private static CadToolExecutionResult FindIntersections(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            var segments = ExtractSegments(selected).Take(1200).ToList();
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 100, 1000));
            var intersections = new JArray();

            for (var i = 0; i < segments.Count && intersections.Count < limit; i++)
            {
                for (var j = i + 1; j < segments.Count && intersections.Count < limit; j++)
                {
                    if (string.Equals(segments[i].Handle, segments[j].Handle, StringComparison.OrdinalIgnoreCase)) continue;
                    Point2d point;
                    if (!TryIntersectSegments(segments[i].Start, segments[i].End, segments[j].Start, segments[j].End, out point)) continue;
                    intersections.Add(new JObject
                    {
                        ["point"] = new JArray(point.X, point.Y, 0),
                        ["a"] = segments[i].ToJson(),
                        ["b"] = segments[j].ToJson(),
                    });
                }
            }

            return Success(callId, name, "DWG intersections found.", new JObject
            {
                ["segment_count"] = segments.Count,
                ["intersection_count"] = intersections.Count,
                ["intersections"] = intersections,
            });
        }

        private static CadToolExecutionResult FindConnectedContours(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            var tolerance = args.Value<double?>("tolerance") ?? 1.0;
            ValidatePositiveDimension(tolerance, "tolerance");
            var contours = BuildConnectedContours(ExtractSegments(selected), tolerance);

            return Success(callId, name, "Connected DWG contours found.", new JObject
            {
                ["tolerance"] = tolerance,
                ["contour_count"] = contours.Count,
                ["closed_count"] = contours.Count(c => c.Value<bool>("closed")),
                ["contours"] = new JArray(contours.Take(Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 80, 500)))),
            });
        }

        private static CadToolExecutionResult FindClosedRegions(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var selected = ApplyAdvancedEntityFilters(snapshot, args);
            var closedEntities = selected.Where(IsClosedEntity).ToList();
            var tolerance = args.Value<double?>("tolerance") ?? 1.0;
            var contours = BuildConnectedContours(ExtractSegments(selected), tolerance)
                .Where(c => c.Value<bool>("closed"))
                .ToList();

            return Success(callId, name, "Closed DWG regions found.", new JObject
            {
                ["closed_entity_count"] = closedEntities.Count,
                ["closed_contour_count"] = contours.Count,
                ["closed_entities"] = EntitySummaryArray(closedEntities, 100),
                ["connected_regions"] = new JArray(contours.Take(100)),
            });
        }

        private static CadToolExecutionResult MeasureRelation(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var aArgs = args["a"] as JObject ?? new JObject { ["selector"] = args.Value<string>("a_selector") };
            var bArgs = args["b"] as JObject ?? new JObject { ["selector"] = args.Value<string>("b_selector") };
            var a = ApplyAdvancedEntityFilters(snapshot, aArgs);
            var b = ApplyAdvancedEntityFilters(snapshot, bArgs);
            var aBounds = MeasureSelectionBounds(a);
            var bBounds = MeasureSelectionBounds(b);
            var aCenter = BoundsCenter(aBounds);
            var bCenter = BoundsCenter(bBounds);

            return Success(callId, name, "DWG relation measured.", new JObject
            {
                ["a_count"] = a.Count,
                ["b_count"] = b.Count,
                ["a_bounds"] = aBounds,
                ["b_bounds"] = bBounds,
                ["center_distance"] = aCenter.DistanceTo(bCenter),
                ["bounds_intersect"] = BoundsIntersect(aBounds, bBounds),
                ["a_contains_b"] = BoundsContains(aBounds, bBounds),
                ["b_contains_a"] = BoundsContains(bBounds, aBounds),
                ["sample_relations"] = DescribeRelations(a.Take(10), b.Take(10)),
            });
        }

        private static CadToolExecutionResult SemanticScan(string callId, string name, JObject args)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
            var entities = SelectMatchingEntities(snapshot, args);
            var layers = snapshot["layers"] as JArray ?? new JArray();
            var semantic = new JObject
            {
                ["wall_candidates"] = EntitySummaryArray(entities.Where(IsWallCandidate), 120),
                ["room_candidates"] = EntitySummaryArray(entities.Where(IsRoomCandidate), 120),
                ["stair_candidates"] = EntitySummaryArray(entities.Where(IsStairCandidate), 120),
                ["annotation_candidates"] = EntitySummaryArray(entities.Where(IsAnnotationCandidate), 120),
                ["door_window_candidates"] = EntitySummaryArray(entities.Where(IsDoorWindowCandidate), 120),
                ["layers"] = new JObject
                {
                    ["wall"] = new JArray(layers.Where(l => NameHints(l?["name"]?.Value<string>(), "wall", "a-wall", "墙")).Select(l => l?["name"]?.Value<string>())),
                    ["stair"] = new JArray(layers.Where(l => NameHints(l?["name"]?.Value<string>(), "stair", "楼梯", "梯")).Select(l => l?["name"]?.Value<string>())),
                    ["text"] = new JArray(layers.Where(l => NameHints(l?["name"]?.Value<string>(), "text", "anno", "标注", "文字")).Select(l => l?["name"]?.Value<string>())),
                },
            };

            semantic["summary"] = new JObject
            {
                ["wall_candidates"] = ((JArray)semantic["wall_candidates"]).Count,
                ["room_candidates"] = ((JArray)semantic["room_candidates"]).Count,
                ["stair_candidates"] = ((JArray)semantic["stair_candidates"]).Count,
                ["annotation_candidates"] = ((JArray)semantic["annotation_candidates"]).Count,
                ["door_window_candidates"] = ((JArray)semantic["door_window_candidates"]).Count,
            };

            return Success(callId, name, "DWG semantic scan completed.", semantic);
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
                arr.Add(EntitySummary(entity));
            }
            return arr;
        }

        private static JObject EntitySummary(JObject entity)
        {
            return new JObject
            {
                ["handle"] = entity.Value<string>("handle"),
                ["type"] = entity.Value<string>("type"),
                ["layer"] = entity.Value<string>("layer"),
                ["selectors"] = entity["selectors"] ?? new JArray(),
            };
        }

        private static JArray EntityDetailArray(IEnumerable<JObject> entities, int limit, bool includeGeometry, bool includeProperties)
        {
            var arr = new JArray();
            foreach (var entity in entities.Take(limit))
            {
                var item = EntitySummary(entity);
                item["bounds"] = entity["bounds"];
                item["source"] = entity["source"];
                if (includeGeometry) item["geometry"] = entity["geometry"];
                if (includeProperties) item["properties"] = entity["properties"];
                if (entity["block_reference"] != null) item["block_reference"] = entity["block_reference"];
                arr.Add(item);
            }
            return arr;
        }

        private static List<JObject> ApplyAdvancedEntityFilters(JObject snapshot, JObject args)
        {
            var selected = SelectMatchingEntities(snapshot, args);
            var textContains = args.Value<string>("text_contains");
            if (!string.IsNullOrWhiteSpace(textContains))
            {
                selected = selected
                    .Where(e => (e["geometry"]?["text"]?.Value<string>() ?? "").IndexOf(textContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            var minLength = args.Value<double?>("min_length");
            if (minLength.HasValue)
            {
                selected = selected.Where(e => (e["geometry"]?["length"]?.Value<double?>() ?? SegmentLengthSum(e)) >= minLength.Value).ToList();
            }

            var maxLength = args.Value<double?>("max_length");
            if (maxLength.HasValue)
            {
                selected = selected.Where(e => (e["geometry"]?["length"]?.Value<double?>() ?? SegmentLengthSum(e)) <= maxLength.Value).ToList();
            }

            var window = ReadBoundsFilter(args);
            if (window != null)
            {
                selected = selected.Where(e => BoundsIntersect(e["bounds"] as JObject, window)).ToList();
            }

            if (args["near"] != null || args["x"] != null || !string.IsNullOrWhiteSpace(args.Value<string>("near_selector")))
            {
                var origin = ReadNearOrigin(snapshot, args);
                var radius = args.Value<double?>("radius");
                if (radius.HasValue)
                {
                    selected = selected.Where(e => DistanceToEntity(origin, e) <= radius.Value).ToList();
                }
            }

            return selected;
        }

        private static JObject ReadBoundsFilter(JObject args)
        {
            var bounds = args["bounds"] as JObject ?? args["window"] as JObject ?? args["within"] as JObject;
            if (bounds != null && bounds["min"] != null && bounds["max"] != null) return bounds;

            var arr = args["bounds"] as JArray ?? args["window"] as JArray ?? args["within"] as JArray;
            if (arr != null && arr.Count >= 4)
            {
                var x1 = arr[0].Value<double>();
                var y1 = arr[1].Value<double>();
                var x2 = arr[2].Value<double>();
                var y2 = arr[3].Value<double>();
                return new JObject
                {
                    ["min"] = new JArray(Math.Min(x1, x2), Math.Min(y1, y2), 0),
                    ["max"] = new JArray(Math.Max(x1, x2), Math.Max(y1, y2), 0),
                };
            }

            return null;
        }

        private static Point3d ReadNearOrigin(JObject snapshot, JObject args)
        {
            var selector = args.Value<string>("near_selector");
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var selected = SelectMatchingEntities(snapshot, new JObject
                {
                    ["selector"] = selector,
                    ["include_exploded"] = args.Value<bool?>("include_exploded") ?? true,
                });
                var center = BoundsCenter(MeasureSelectionBounds(selected));
                return center;
            }

            var near = args["near"] as JObject;
            if (near != null)
            {
                return ReadPoint(near, "point", "x", "y");
            }

            return ReadPoint(args, "point", "x", "y");
        }

        private static Point3d EntityCenter(JObject entity)
        {
            return BoundsCenter(entity["bounds"] as JObject);
        }

        private static Point3d BoundsCenter(JObject bounds)
        {
            var center = bounds?["center"] as JArray;
            if (center != null && center.Count >= 2)
            {
                return new Point3d(center[0].Value<double>(), center[1].Value<double>(), center.Count > 2 ? center[2].Value<double>() : 0);
            }

            var min = bounds?["min"] as JArray;
            var max = bounds?["max"] as JArray;
            if (min == null || max == null || min.Count < 2 || max.Count < 2)
            {
                throw new InvalidOperationException("Selection has no measurable bounds.");
            }

            return new Point3d(
                (min[0].Value<double>() + max[0].Value<double>()) / 2,
                (min[1].Value<double>() + max[1].Value<double>()) / 2,
                ((min.Count > 2 ? min[2].Value<double>() : 0) + (max.Count > 2 ? max[2].Value<double>() : 0)) / 2);
        }

        private static double DistanceToEntity(Point3d origin, JObject entity)
        {
            try
            {
                return origin.DistanceTo(EntityCenter(entity));
            }
            catch
            {
                return double.MaxValue;
            }
        }

        private static bool BoundsIntersect(JObject a, JObject b)
        {
            if (!TryReadBounds(a, out var amin, out var amax) || !TryReadBounds(b, out var bmin, out var bmax)) return false;
            return amin.X <= bmax.X && amax.X >= bmin.X &&
                   amin.Y <= bmax.Y && amax.Y >= bmin.Y &&
                   amin.Z <= bmax.Z && amax.Z >= bmin.Z;
        }

        private static bool BoundsContains(JObject outer, JObject inner)
        {
            if (!TryReadBounds(outer, out var omin, out var omax) || !TryReadBounds(inner, out var imin, out var imax)) return false;
            return omin.X <= imin.X && omin.Y <= imin.Y && omin.Z <= imin.Z &&
                   omax.X >= imax.X && omax.Y >= imax.Y && omax.Z >= imax.Z;
        }

        private static bool TryReadBounds(JObject bounds, out Point3d min, out Point3d max)
        {
            min = Point3d.Origin;
            max = Point3d.Origin;
            var minArr = bounds?["min"] as JArray;
            var maxArr = bounds?["max"] as JArray;
            if (minArr == null || maxArr == null || minArr.Count < 2 || maxArr.Count < 2) return false;
            min = new Point3d(minArr[0].Value<double>(), minArr[1].Value<double>(), minArr.Count > 2 ? minArr[2].Value<double>() : 0);
            max = new Point3d(maxArr[0].Value<double>(), maxArr[1].Value<double>(), maxArr.Count > 2 ? maxArr[2].Value<double>() : 0);
            return true;
        }

        private static JArray TextSample(IEnumerable<JObject> entities, int limit)
        {
            var arr = new JArray();
            foreach (var entity in entities.Take(limit))
            {
                var text = entity["geometry"]?["text"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(text)) continue;
                arr.Add(new JObject
                {
                    ["handle"] = entity.Value<string>("handle"),
                    ["layer"] = entity.Value<string>("layer"),
                    ["text"] = text.Length > 300 ? text.Substring(0, 300) : text,
                });
            }
            return arr;
        }

        private static bool IsClosedEntity(JObject entity)
        {
            return entity?["geometry"]?["closed"]?.Value<bool>() == true;
        }

        private static double SegmentLengthSum(JObject entity)
        {
            return ExtractSegments(new[] { entity }).Sum(s => s.Start.GetDistanceTo(s.End));
        }

        private sealed class SegmentInfo
        {
            public string Handle { get; set; }
            public string Layer { get; set; }
            public string Type { get; set; }
            public Point2d Start { get; set; }
            public Point2d End { get; set; }

            public JObject ToJson()
            {
                return new JObject
                {
                    ["handle"] = Handle,
                    ["layer"] = Layer,
                    ["type"] = Type,
                    ["start"] = new JArray(Start.X, Start.Y),
                    ["end"] = new JArray(End.X, End.Y),
                };
            }
        }

        private static IEnumerable<SegmentInfo> ExtractSegments(IEnumerable<JObject> entities)
        {
            foreach (var entity in entities)
            {
                var geometry = entity["geometry"] as JObject;
                if (geometry == null) continue;
                var handle = entity.Value<string>("handle");
                var layer = entity.Value<string>("layer");
                var type = entity.Value<string>("type");
                var start = geometry["start"] as JArray;
                var end = geometry["end"] as JArray;
                if (start != null && end != null && start.Count >= 2 && end.Count >= 2)
                {
                    yield return new SegmentInfo
                    {
                        Handle = handle,
                        Layer = layer,
                        Type = type,
                        Start = new Point2d(start[0].Value<double>(), start[1].Value<double>()),
                        End = new Point2d(end[0].Value<double>(), end[1].Value<double>()),
                    };
                }

                var vertices = geometry["vertices"] as JArray;
                if (vertices == null || vertices.Count < 2) continue;
                for (var i = 0; i < vertices.Count - 1; i++)
                {
                    var a = vertices[i] as JArray;
                    var b = vertices[i + 1] as JArray;
                    if (a == null || b == null || a.Count < 2 || b.Count < 2) continue;
                    yield return new SegmentInfo
                    {
                        Handle = handle,
                        Layer = layer,
                        Type = type,
                        Start = new Point2d(a[0].Value<double>(), a[1].Value<double>()),
                        End = new Point2d(b[0].Value<double>(), b[1].Value<double>()),
                    };
                }
                if (geometry.Value<bool?>("closed") == true)
                {
                    var a = vertices[vertices.Count - 1] as JArray;
                    var b = vertices[0] as JArray;
                    if (a != null && b != null && a.Count >= 2 && b.Count >= 2)
                    {
                        yield return new SegmentInfo
                        {
                            Handle = handle,
                            Layer = layer,
                            Type = type,
                            Start = new Point2d(a[0].Value<double>(), a[1].Value<double>()),
                            End = new Point2d(b[0].Value<double>(), b[1].Value<double>()),
                        };
                    }
                }
            }
        }

        private static bool TryIntersectSegments(Point2d a, Point2d b, Point2d c, Point2d d, out Point2d point)
        {
            point = Point2d.Origin;
            var r = b - a;
            var s = d - c;
            var denom = Cross(r, s);
            if (Math.Abs(denom) < 1e-9) return false;
            var u = Cross(c - a, r) / denom;
            var t = Cross(c - a, s) / denom;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;
            point = a + r * t;
            return true;
        }

        private static double Cross(Vector2d a, Vector2d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static List<JObject> BuildConnectedContours(IEnumerable<SegmentInfo> sourceSegments, double tolerance)
        {
            var segments = sourceSegments.Take(3000).ToList();
            var parent = new Dictionary<int, int>();
            for (var i = 0; i < segments.Count; i++) parent[i] = i;

            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = i + 1; j < segments.Count; j++)
                {
                    if (PointsClose(segments[i].Start, segments[j].Start, tolerance) ||
                        PointsClose(segments[i].Start, segments[j].End, tolerance) ||
                        PointsClose(segments[i].End, segments[j].Start, tolerance) ||
                        PointsClose(segments[i].End, segments[j].End, tolerance))
                    {
                        Union(parent, i, j);
                    }
                }
            }

            var groups = segments.Select((s, i) => new { Segment = s, Index = i })
                .GroupBy(x => Find(parent, x.Index))
                .ToList();
            var contours = new List<JObject>();
            foreach (var group in groups)
            {
                var groupSegments = group.Select(x => x.Segment).ToList();
                var degrees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var acc = new BoundsAccumulator();
                foreach (var segment in groupSegments)
                {
                    IncrementDegree(degrees, PointKey(segment.Start, tolerance));
                    IncrementDegree(degrees, PointKey(segment.End, tolerance));
                    handles.Add(segment.Handle);
                    acc.Add(new JObject
                    {
                        ["min"] = new JArray(Math.Min(segment.Start.X, segment.End.X), Math.Min(segment.Start.Y, segment.End.Y), 0),
                        ["max"] = new JArray(Math.Max(segment.Start.X, segment.End.X), Math.Max(segment.Start.Y, segment.End.Y), 0),
                    });
                }

                contours.Add(new JObject
                {
                    ["segment_count"] = groupSegments.Count,
                    ["entity_count"] = handles.Count,
                    ["handles"] = new JArray(handles.Take(100)),
                    ["closed"] = groupSegments.Count >= 3 && degrees.Values.All(v => v % 2 == 0),
                    ["bounds"] = acc.ToJson(),
                });
            }

            return contours.OrderByDescending(c => c.Value<int>("segment_count")).ToList();
        }

        private static bool PointsClose(Point2d a, Point2d b, double tolerance)
        {
            return a.GetDistanceTo(b) <= tolerance;
        }

        private static string PointKey(Point2d p, double tolerance)
        {
            var scale = Math.Max(tolerance, 1e-9);
            return Math.Round(p.X / scale).ToString(CultureInfo.InvariantCulture) + "," +
                   Math.Round(p.Y / scale).ToString(CultureInfo.InvariantCulture);
        }

        private static int Find(Dictionary<int, int> parent, int x)
        {
            if (parent[x] == x) return x;
            parent[x] = Find(parent, parent[x]);
            return parent[x];
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            var pa = Find(parent, a);
            var pb = Find(parent, b);
            if (pa != pb) parent[pb] = pa;
        }

        private static void IncrementDegree(Dictionary<string, int> map, string key)
        {
            map[key] = map.ContainsKey(key) ? map[key] + 1 : 1;
        }

        private static JArray DescribeRelations(JObject entity, IEnumerable<JObject> others)
        {
            return DescribeRelations(new[] { entity }, others);
        }

        private static JArray DescribeRelations(IEnumerable<JObject> aEntities, IEnumerable<JObject> bEntities)
        {
            var arr = new JArray();
            foreach (var a in aEntities)
            {
                foreach (var b in bEntities)
                {
                    if (string.Equals(a.Value<string>("handle"), b.Value<string>("handle"), StringComparison.OrdinalIgnoreCase)) continue;
                    var relation = new JObject
                    {
                        ["a"] = a.Value<string>("handle"),
                        ["b"] = b.Value<string>("handle"),
                        ["bounds_intersect"] = BoundsIntersect(a["bounds"] as JObject, b["bounds"] as JObject),
                        ["center_distance"] = DistanceToEntity(EntityCenter(a), b),
                    };
                    var aSeg = ExtractSegments(new[] { a }).FirstOrDefault();
                    var bSeg = ExtractSegments(new[] { b }).FirstOrDefault();
                    if (aSeg != null && bSeg != null)
                    {
                        var av = aSeg.End - aSeg.Start;
                        var bv = bSeg.End - bSeg.Start;
                        var dot = Math.Abs(av.DotProduct(bv));
                        var cross = Math.Abs(Cross(av, bv));
                        relation["parallel"] = cross <= 1e-6;
                        relation["perpendicular"] = dot <= 1e-6;
                    }
                    arr.Add(relation);
                    if (arr.Count >= 80) return arr;
                }
            }
            return arr;
        }

        private static bool IsWallCandidate(JObject entity)
        {
            var layer = entity.Value<string>("layer");
            if (NameHints(layer, "wall", "a-wall", "墙")) return true;
            return TypeMatches(entity, "Line") && SegmentLengthSum(entity) > 1000;
        }

        private static bool IsRoomCandidate(JObject entity)
        {
            if (!IsClosedEntity(entity)) return false;
            var bounds = entity["bounds"] as JObject;
            if (!TryReadBounds(bounds, out var min, out var max)) return false;
            return (max.X - min.X) > 1000 && (max.Y - min.Y) > 1000;
        }

        private static bool IsStairCandidate(JObject entity)
        {
            return NameHints(entity.Value<string>("layer"), "stair", "楼梯", "梯") ||
                   NameHints(entity["block_reference"]?["name"]?.Value<string>(), "stair", "楼梯", "梯");
        }

        private static bool IsAnnotationCandidate(JObject entity)
        {
            return !string.IsNullOrWhiteSpace(entity["geometry"]?["text"]?.Value<string>()) ||
                   NameHints(entity.Value<string>("layer"), "text", "anno", "note", "标注", "文字");
        }

        private static bool IsDoorWindowCandidate(JObject entity)
        {
            return NameHints(entity.Value<string>("layer"), "door", "window", "门", "窗") ||
                   NameHints(entity["block_reference"]?["name"]?.Value<string>(), "door", "window", "门", "窗");
        }

        private static bool NameHints(string value, params string[] hints)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var hint in hints)
            {
                if (value.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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

        private static JObject DrawArc(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var center = ReadPoint(args, "center", "x", "y");
            var radius = RequiredDouble(args, "radius");
            var startAngle = OptionalDouble(args, "start_angle", 0);
            var endAngle = OptionalDouble(args, "end_angle", 90);
            ValidatePositiveDimension(radius, "radius");
            EnsureLayer(db, tr, layer);

            var arc = new Arc(center, radius, startAngle * Math.PI / 180.0, endAngle * Math.PI / 180.0) { Layer = layer };
            ApplyEntityColor(arc, args);
            AppendToModelSpace(db, tr, arc);
            var data = EntityResult("Arc", arc, layer);
            data["radius"] = radius;
            data["start_angle"] = startAngle;
            data["end_angle"] = endAngle;
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

        private static JObject DrawRoom(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "A-WALL");
            ValidateLayerName(layer);
            var x = OptionalDouble(args, "x", 0);
            var y = OptionalDouble(args, "y", 0);
            var width = RequiredDouble(args, "width");
            var height = RequiredDouble(args, "height");
            var wallThickness = OptionalDouble(args, "wall_thickness", OptionalDouble(args, "thickness", 200));
            ValidatePositiveDimension(width, "width");
            ValidatePositiveDimension(height, "height");
            ValidatePositiveDimension(wallThickness, "wall_thickness");
            EnsureLayer(db, tr, layer);

            var handles = new JArray();
            handles.Add(AppendRectangleEntity(db, tr, args, layer, x, y, width, height).Handle.ToString());
            if (wallThickness > 0 && width > wallThickness * 2 && height > wallThickness * 2)
            {
                handles.Add(AppendRectangleEntity(db, tr, args, layer, x + wallThickness, y + wallThickness, width - wallThickness * 2, height - wallThickness * 2).Handle.ToString());
            }

            return new JObject
            {
                ["entity_type"] = "Room",
                ["layer"] = layer,
                ["handles"] = handles,
                ["width"] = width,
                ["height"] = height,
                ["wall_thickness"] = wallThickness,
                ["bounds"] = new JObject
                {
                    ["min"] = new JArray(x, y, 0),
                    ["max"] = new JArray(x + width, y + height, 0),
                    ["width"] = width,
                    ["height"] = height,
                },
            };
        }

        private static JObject DrawWall(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "A-WALL");
            ValidateLayerName(layer);
            var start = ReadPoint(args, "start", "x1", "y1");
            var end = ReadPoint(args, "end", "x2", "y2");
            var thickness = OptionalDouble(args, "thickness", 200);
            ValidatePositiveDimension(thickness, "thickness");
            EnsureLayer(db, tr, layer);

            var direction = end - start;
            if (direction.Length <= 1e-9) throw new InvalidOperationException("Wall start and end points must be different.");
            var normal = direction.GetNormal().CrossProduct(Vector3d.ZAxis).GetNormal() * (thickness / 2.0);
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(start.X + normal.X, start.Y + normal.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(end.X + normal.X, end.Y + normal.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(end.X - normal.X, end.Y - normal.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(start.X - normal.X, start.Y - normal.Y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            ApplyEntityColor(pl, args);
            AppendToModelSpace(db, tr, pl);
            var data = EntityResult("Wall", pl, layer);
            data["thickness"] = thickness;
            data["length"] = direction.Length;
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

        private static JObject DrawMText(Database db, Transaction tr, JObject args)
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
            var width = OptionalDouble(args, "width", 0);
            var rotationDeg = OptionalDouble(args, "rotation", 0);
            ValidatePositiveDimension(height, "height");
            EnsureLayer(db, tr, layer);

            var mtext = new MText
            {
                Contents = text,
                Location = pos,
                TextHeight = height,
                Rotation = rotationDeg * Math.PI / 180.0,
                Layer = layer,
            };
            if (width > 0) mtext.Width = width;
            ApplyEntityColor(mtext, args);
            AppendToModelSpace(db, tr, mtext);
            var data = EntityResult("MText", mtext, layer);
            data["text"] = text;
            data["width"] = width;
            return data;
        }

        private static JObject DrawDimension(Database db, Transaction tr, JObject args)
        {
            var layer = OptionalString(args, "layer", "A-DIMS");
            ValidateLayerName(layer);
            var p1 = ReadPoint(args, "p1", "x1", "y1");
            var p2 = ReadPoint(args, "p2", "x2", "y2");
            var dimLine = args["dim_line"] != null
                ? ReadPoint(args, "dim_line", "dim_x", "dim_y")
                : new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 + 300, 0);
            EnsureLayer(db, tr, layer);

            var dim = new AlignedDimension(p1, p2, dimLine, OptionalString(args, "text", ""), db.Dimstyle) { Layer = layer };
            ApplyEntityColor(dim, args);
            AppendToModelSpace(db, tr, dim);
            return EntityResult("AlignedDimension", dim, layer);
        }

        private static JObject InsertBlock(Database db, Transaction tr, JObject args)
        {
            var blockName = RequiredString(args, "name");
            var layer = OptionalString(args, "layer", "0");
            ValidateLayerName(layer);
            var position = ReadPoint(args, "position", "x", "y");
            var rotationDeg = OptionalDouble(args, "rotation", 0);
            var scale = OptionalDouble(args, "scale", 1);
            ValidatePositiveDimension(scale, "scale");
            EnsureLayer(db, tr, layer);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName)) throw new InvalidOperationException("Block not found: " + blockName);
            var br = new BlockReference(position, bt[blockName])
            {
                Layer = layer,
                Rotation = rotationDeg * Math.PI / 180.0,
                ScaleFactors = new Scale3d(scale),
            };
            ApplyEntityColor(br, args);
            AppendToModelSpace(db, tr, br);
            var data = EntityResult("BlockReference", br, layer);
            data["block"] = blockName;
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

        private static JObject MoveEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var dx = OptionalDouble(args, "dx", 0);
            var dy = OptionalDouble(args, "dy", 0);
            var dz = OptionalDouble(args, "dz", 0);
            if (args["vector"] is JArray vector && vector.Count >= 2)
            {
                dx = vector[0].Value<double>();
                dy = vector[1].Value<double>();
                dz = vector.Count > 2 ? vector[2].Value<double>() : 0;
            }
            ValidateCoordinate(dx, "dx");
            ValidateCoordinate(dy, "dy");
            ValidateCoordinate(dz, "dz");
            var matrix = Matrix3d.Displacement(new Vector3d(dx, dy, dz));
            foreach (var entity in selected) entity.TransformBy(matrix);
            return SelectionWriteResult("move", selected, new JObject { ["dx"] = dx, ["dy"] = dy, ["dz"] = dz });
        }

        private static JObject CopyEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var dx = OptionalDouble(args, "dx", 0);
            var dy = OptionalDouble(args, "dy", 0);
            var dz = OptionalDouble(args, "dz", 0);
            if (args["vector"] is JArray vector && vector.Count >= 2)
            {
                dx = vector[0].Value<double>();
                dy = vector[1].Value<double>();
                dz = vector.Count > 2 ? vector[2].Value<double>() : 0;
            }
            var matrix = Matrix3d.Displacement(new Vector3d(dx, dy, dz));
            var handles = new JArray();
            foreach (var entity in selected)
            {
                var clone = entity.Clone() as Entity;
                if (clone == null) continue;
                clone.TransformBy(matrix);
                AppendToModelSpace(db, tr, clone);
                handles.Add(clone.Handle.ToString());
            }
            return new JObject
            {
                ["operation"] = "copy",
                ["source_count"] = selected.Count,
                ["object_count"] = handles.Count,
                ["handles"] = handles,
                ["dx"] = dx,
                ["dy"] = dy,
                ["dz"] = dz,
            };
        }

        private static JObject RotateEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var angle = RequiredDouble(args, "angle");
            var basePoint = args["base"] != null ? ReadPoint(args, "base", "x", "y") : ReadPoint(args, "point", "x", "y");
            var matrix = Matrix3d.Rotation(angle * Math.PI / 180.0, Vector3d.ZAxis, basePoint);
            foreach (var entity in selected) entity.TransformBy(matrix);
            return SelectionWriteResult("rotate", selected, new JObject { ["angle"] = angle, ["base"] = new JArray(basePoint.X, basePoint.Y, basePoint.Z) });
        }

        private static JObject ScaleEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var factor = RequiredDouble(args, "factor");
            ValidatePositiveDimension(factor, "factor");
            var basePoint = args["base"] != null ? ReadPoint(args, "base", "x", "y") : ReadPoint(args, "point", "x", "y");
            var matrix = Matrix3d.Scaling(factor, basePoint);
            foreach (var entity in selected) entity.TransformBy(matrix);
            return SelectionWriteResult("scale", selected, new JObject { ["factor"] = factor, ["base"] = new JArray(basePoint.X, basePoint.Y, basePoint.Z) });
        }

        private static JObject OffsetEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var distance = RequiredDouble(args, "distance");
            if (Math.Abs(distance) < 1e-9) throw new InvalidOperationException("'distance' must not be zero.");
            var handles = new JArray();
            foreach (var entity in selected)
            {
                var curve = entity as Curve;
                if (curve == null) continue;
                var offsets = curve.GetOffsetCurves(distance);
                foreach (DBObject obj in offsets)
                {
                    var offsetEntity = obj as Entity;
                    if (offsetEntity == null)
                    {
                        obj.Dispose();
                        continue;
                    }
                    offsetEntity.Layer = entity.Layer;
                    AppendToModelSpace(db, tr, offsetEntity);
                    handles.Add(offsetEntity.Handle.ToString());
                }
            }
            return new JObject
            {
                ["operation"] = "offset",
                ["source_count"] = selected.Count,
                ["object_count"] = handles.Count,
                ["handles"] = handles,
                ["distance"] = distance,
            };
        }

        private static JObject DeleteEntities(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var handles = new JArray(selected.Select(e => e.Handle.ToString()));
            foreach (var entity in selected) entity.Erase();
            return new JObject
            {
                ["operation"] = "delete",
                ["object_count"] = selected.Count,
                ["handles"] = handles,
            };
        }

        private static JObject ChangeLayer(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var layer = RequiredString(args, "target_layer");
            ValidateLayerName(layer);
            EnsureLayer(db, tr, layer);
            foreach (var entity in selected) entity.Layer = layer;
            return SelectionWriteResult("change_layer", selected, new JObject { ["target_layer"] = layer });
        }

        private static JObject SetProperties(Database db, Transaction tr, JObject args)
        {
            var selected = SelectWritableEntities(db, tr, args);
            var layer = args.Value<string>("layer");
            if (!string.IsNullOrWhiteSpace(layer))
            {
                ValidateLayerName(layer);
                EnsureLayer(db, tr, layer);
            }
            foreach (var entity in selected)
            {
                if (!string.IsNullOrWhiteSpace(layer)) entity.Layer = layer;
                ApplyEntityColor(entity, args);
                var linetype = args.Value<string>("linetype");
                if (!string.IsNullOrWhiteSpace(linetype)) entity.Linetype = linetype;
                var lineweight = args.Value<string>("lineweight");
                if (!string.IsNullOrWhiteSpace(lineweight) && Enum.IsDefined(typeof(LineWeight), lineweight))
                {
                    entity.LineWeight = (LineWeight)Enum.Parse(typeof(LineWeight), lineweight);
                }
            }
            return SelectionWriteResult("set_properties", selected, new JObject
            {
                ["layer"] = string.IsNullOrWhiteSpace(layer) ? null : layer,
                ["linetype"] = string.IsNullOrWhiteSpace(args.Value<string>("linetype")) ? null : args.Value<string>("linetype"),
            });
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

        private static List<Entity> SelectWritableEntities(Database db, Transaction tr, JObject args)
        {
            var selected = new List<Entity>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            SelectWritableEntitiesFromSpace(bt, tr, BlockTableRecord.ModelSpace, args, selected);
            SelectWritableEntitiesFromSpace(bt, tr, BlockTableRecord.PaperSpace, args, selected);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("No editable top-level entities matched the selector. Block-internal exploded entities are read-only until their block reference is edited explicitly.");
            }
            return selected;
        }

        private static void SelectWritableEntitiesFromSpace(BlockTable bt, Transaction tr, string blockName, JObject args, List<Entity> selected)
        {
            if (!bt.Has(blockName)) return;
            var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) continue;
                if (!WritableEntityMatches(entity, args)) continue;
                entity.UpgradeOpen();
                selected.Add(entity);
            }
        }

        private static bool WritableEntityMatches(Entity entity, JObject args)
        {
            var layer = args.Value<string>("layer");
            if (!string.IsNullOrWhiteSpace(layer) &&
                !string.Equals(entity.Layer, layer, StringComparison.OrdinalIgnoreCase)) return false;

            var type = args.Value<string>("type");
            if (!string.IsNullOrWhiteSpace(type) &&
                !string.Equals(entity.GetType().Name, type, StringComparison.OrdinalIgnoreCase)) return false;

            var handle = args.Value<string>("handle");
            if (!string.IsNullOrWhiteSpace(handle) &&
                !string.Equals(entity.Handle.ToString(), handle, StringComparison.OrdinalIgnoreCase)) return false;

            var selectors = ReadSelectors(args);
            if (selectors.Count > 0)
            {
                var token = new JObject
                {
                    ["handle"] = entity.Handle.ToString(),
                    ["type"] = entity.GetType().Name,
                    ["layer"] = entity.Layer,
                    ["selectors"] = new JArray("handle:" + entity.Handle, "type:" + entity.GetType().Name, "layer:" + entity.Layer),
                };
                if (!selectors.Any(selector => MatchesSelector(token, selector))) return false;
            }

            var textContains = args.Value<string>("text_contains");
            if (!string.IsNullOrWhiteSpace(textContains))
            {
                var text = TryGetEntityText(entity);
                if (text.IndexOf(textContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }

        private static string TryGetEntityText(Entity entity)
        {
            var text = entity as DBText;
            if (text != null) return text.TextString ?? "";
            var mtext = entity as MText;
            if (mtext != null) return mtext.Contents ?? "";
            return "";
        }

        private static JObject SelectionWriteResult(string operation, IList<Entity> selected, JObject extra)
        {
            var data = extra ?? new JObject();
            data["operation"] = operation;
            data["object_count"] = selected.Count;
            data["handles"] = new JArray(selected.Select(e => e.Handle.ToString()));
            data["layers"] = new JArray(selected.Select(e => e.Layer).Distinct(StringComparer.OrdinalIgnoreCase));
            return data;
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

        private static CadToolExecutionResult Success(string callId, string name, string message, JObject data)
        {
            return new CadToolExecutionResult
            {
                CallId = callId,
                ToolName = name,
                Success = true,
                Message = message,
                Data = data ?? new JObject(),
            };
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
