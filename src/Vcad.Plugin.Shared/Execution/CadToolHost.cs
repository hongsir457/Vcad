#nullable disable

using System;
using System.Diagnostics;
using System.Globalization;
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

        public static bool IsCadTool(string name)
        {
            return string.Equals(name, "cad.read_dwg_snapshot", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.create_layer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_line", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_polyline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_circle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_rectangle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_text", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWriteTool(string name)
        {
            return string.Equals(name, "cad.create_layer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_line", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_polyline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_circle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_rectangle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cad.draw_text", StringComparison.OrdinalIgnoreCase);
        }

        public static CadToolExecutionResult Execute(string callId, string name, JObject args)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                args = args ?? new JObject();
                CadToolExecutionResult result;
                if (string.Equals(name, "cad.read_dwg_snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    result = ReadSnapshot(callId, name);
                }
                else if (string.Equals(name, "cad.create_layer", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, CreateLayer);
                }
                else if (string.Equals(name, "cad.draw_line", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, DrawLine);
                }
                else if (string.Equals(name, "cad.draw_polyline", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, DrawPolyline);
                }
                else if (string.Equals(name, "cad.draw_circle", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, DrawCircle);
                }
                else if (string.Equals(name, "cad.draw_rectangle", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, DrawRectangle);
                }
                else if (string.Equals(name, "cad.draw_text", StringComparison.OrdinalIgnoreCase))
                {
                    result = RunWrite(callId, name, args, DrawText);
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

        private static CadToolExecutionResult ReadSnapshot(string callId, string name)
        {
            var snapshot = DrawingSnapshotCollector.CaptureActive();
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
