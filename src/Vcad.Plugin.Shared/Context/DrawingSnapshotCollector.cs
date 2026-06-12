#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Vcad.Plugin.Context
{
    internal static class DrawingSnapshotCollector
    {
        private const int MaxEntities = 3000;
        private const int MaxBlockDepth = 5;
        private const int MaxExplodedEntitiesPerBlock = 500;

        public static JObject CaptureActive()
        {
            var warnings = new JArray();
            var layers = new JArray();
            var entities = new JArray();
            var summary = new JObject
            {
                ["entity_count"] = 0,
                ["top_level_entity_count"] = 0,
                ["exploded_entity_count"] = 0,
                ["block_reference_count"] = 0,
                ["layer_count"] = 0,
                ["truncated"] = false,
            };

            var snapshot = new JObject
            {
                ["schema"] = "cad_drawing_snapshot_v1",
                ["captured_at"] = DateTime.UtcNow.ToString("o"),
                ["document"] = new JObject(),
                ["limits"] = new JObject
                {
                    ["max_entities"] = MaxEntities,
                    ["max_block_depth"] = MaxBlockDepth,
                    ["max_exploded_entities_per_block"] = MaxExplodedEntitiesPerBlock,
                },
                ["summary"] = summary,
                ["layers"] = layers,
                ["entities"] = entities,
                ["warnings"] = warnings,
            };

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                warnings.Add("No active AutoCAD document.");
                return snapshot;
            }

            snapshot["document"] = new JObject
            {
                ["name"] = TryGetStringProperty(doc, "Name"),
                ["database_filename"] = TryGetStringProperty(doc.Database, "Filename"),
            };

            try
            {
                using (var docLock = doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    CaptureLayers(doc.Database, tr, layers, warnings);
                    summary["layer_count"] = layers.Count;

                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    CaptureSpace(bt, tr, BlockTableRecord.ModelSpace, "model", entities, summary, warnings);
                    CaptureSpace(bt, tr, BlockTableRecord.PaperSpace, "paper", entities, summary, warnings);

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Drawing snapshot failed: " + ex.Message);
            }

            summary["entity_count"] = entities.Count;
            return snapshot;
        }

        public static string FormatSummary(JObject snapshot)
        {
            if (snapshot == null) return "DWG 状态快照不可用。";
            var summary = snapshot["summary"] as JObject;
            var warnings = snapshot["warnings"] as JArray;
            var warningText = warnings == null || warnings.Count == 0
                ? "无"
                : string.Join("; ", warnings.Values<string>());

            return "DWG Memory Snapshot\r\n" +
                   "图元: " + (summary?["entity_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "顶层图元: " + (summary?["top_level_entity_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "块内展开图元: " + (summary?["exploded_entity_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "块引用: " + (summary?["block_reference_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "图层: " + (summary?["layer_count"]?.Value<int>() ?? 0) + "\r\n" +
                   "截断: " + ((summary?["truncated"]?.Value<bool>() ?? false) ? "是" : "否") + "\r\n" +
                   "警告: " + warningText;
        }

        private static void CaptureLayers(Database db, Transaction tr, JArray layers, JArray warnings)
        {
            try
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId layerId in EnumerateObjectIds(lt))
                {
                    var ltr = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                    if (ltr == null) continue;
                    layers.Add(new JObject
                    {
                        ["name"] = ltr.Name,
                        ["handle"] = ltr.Handle.ToString(),
                        ["color"] = DescribeColor(ltr.Color),
                        ["is_off"] = TryGetBoolProperty(ltr, "IsOff"),
                        ["is_frozen"] = TryGetBoolProperty(ltr, "IsFrozen"),
                        ["is_locked"] = TryGetBoolProperty(ltr, "IsLocked"),
                    });
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Layer scan failed: " + ex.Message);
            }
        }

        private static void CaptureSpace(BlockTable bt, Transaction tr, string blockName, string space, JArray entities, JObject summary, JArray warnings)
        {
            try
            {
                if (!bt.Has(blockName)) return;
                var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(btr))
                {
                    if (IsTruncated(entities, summary)) return;
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;
                    CaptureEntity(entity, space, new JArray(), 0, false, entities, summary, warnings);
                    summary["top_level_entity_count"] = summary.Value<int>("top_level_entity_count") + 1;
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Space scan failed for " + space + ": " + ex.Message);
            }
        }

        private static void CaptureEntity(Entity entity, string space, JArray blockPath, int blockDepth, bool exploded, JArray entities, JObject summary, JArray warnings)
        {
            if (IsTruncated(entities, summary)) return;

            var type = entity.GetType().Name;
            var item = new JObject
            {
                ["handle"] = entity.Handle.ToString(),
                ["object_id"] = entity.ObjectId.ToString(),
                ["type"] = type,
                ["layer"] = entity.Layer,
                ["source"] = new JObject
                {
                    ["space"] = space,
                    ["from_block_explode"] = exploded,
                    ["block_depth"] = blockDepth,
                    ["block_path"] = blockPath.DeepClone(),
                },
                ["bounds"] = TryReadBounds(entity),
                ["geometry"] = ReadGeometry(entity),
            };

            if (IsBlockReference(entity))
            {
                summary["block_reference_count"] = summary.Value<int>("block_reference_count") + 1;
                item["block_reference"] = ReadBlockReference(entity);
            }

            entities.Add(item);
            if (exploded)
            {
                summary["exploded_entity_count"] = summary.Value<int>("exploded_entity_count") + 1;
            }

            if (IsBlockReference(entity) && blockDepth < MaxBlockDepth)
            {
                CaptureExplodedBlock(entity, space, blockPath, blockDepth, entities, summary, warnings);
            }
        }

        private static void CaptureExplodedBlock(Entity blockRef, string space, JArray blockPath, int blockDepth, JArray entities, JObject summary, JArray warnings)
        {
            var exploded = new DBObjectCollection();
            try
            {
                var explode = blockRef.GetType().GetMethod("Explode", new[] { typeof(DBObjectCollection) });
                if (explode == null)
                {
                    warnings.Add("BlockReference does not expose Explode(): " + blockRef.Handle);
                    return;
                }
                explode.Invoke(blockRef, new object[] { exploded });

                var childPath = (JArray)blockPath.DeepClone();
                childPath.Add(TryGetStringProperty(blockRef, "Name") ?? TryGetStringProperty(blockRef, "BlockName") ?? blockRef.Handle.ToString());

                var count = 0;
                foreach (DBObject child in exploded)
                {
                    if (count >= MaxExplodedEntitiesPerBlock)
                    {
                        warnings.Add("Block explode truncated at " + MaxExplodedEntitiesPerBlock + " entities for " + blockRef.Handle);
                        break;
                    }
                    if (IsTruncated(entities, summary)) break;
                    var childEntity = child as Entity;
                    if (childEntity != null)
                    {
                        CaptureEntity(childEntity, space, childPath, blockDepth + 1, true, entities, summary, warnings);
                    }
                    count++;
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Block explode failed for " + blockRef.Handle + ": " + ex.Message);
            }
            finally
            {
                foreach (DBObject child in exploded)
                {
                    try { child.Dispose(); } catch { }
                }
            }
        }

        private static bool IsTruncated(JArray entities, JObject summary)
        {
            if (entities.Count < MaxEntities) return false;
            summary["truncated"] = true;
            return true;
        }

        private static IEnumerable<ObjectId> EnumerateObjectIds(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable == null) yield break;
            foreach (var item in enumerable)
            {
                if (item is ObjectId id) yield return id;
            }
        }

        private static bool IsBlockReference(Entity entity)
        {
            return string.Equals(entity.GetType().Name, "BlockReference", StringComparison.Ordinal);
        }

        private static JObject ReadBlockReference(Entity entity)
        {
            return new JObject
            {
                ["name"] = TryGetStringProperty(entity, "Name") ?? TryGetStringProperty(entity, "BlockName"),
                ["position"] = TryReadPointProperty(entity, "Position"),
                ["rotation"] = TryReadDoubleProperty(entity, "Rotation"),
                ["scale"] = TryReadPointProperty(entity, "ScaleFactors"),
            };
        }

        private static JObject ReadGeometry(Entity entity)
        {
            var type = entity.GetType().Name;
            var geometry = new JObject { ["entity_type"] = type };
            AddPointIfPresent(geometry, entity, "StartPoint", "start");
            AddPointIfPresent(geometry, entity, "EndPoint", "end");
            AddPointIfPresent(geometry, entity, "Center", "center");
            AddPointIfPresent(geometry, entity, "Position", "position");
            AddPointIfPresent(geometry, entity, "Location", "location");
            AddStringIfPresent(geometry, entity, "TextString", "text");
            AddStringIfPresent(geometry, entity, "Contents", "text");
            AddDoubleIfPresent(geometry, entity, "Radius", "radius");
            AddDoubleIfPresent(geometry, entity, "Height", "height");
            AddDoubleIfPresent(geometry, entity, "TextHeight", "height");
            AddDoubleIfPresent(geometry, entity, "Rotation", "rotation");
            AddDoubleIfPresent(geometry, entity, "StartAngle", "start_angle");
            AddDoubleIfPresent(geometry, entity, "EndAngle", "end_angle");
            AddPolylineVerticesIfPresent(geometry, entity);
            return geometry;
        }

        private static void AddPolylineVerticesIfPresent(JObject geometry, object entity)
        {
            var numberProp = entity.GetType().GetProperty("NumberOfVertices");
            var getPoint = entity.GetType().GetMethod("GetPoint2dAt", new[] { typeof(int) });
            if (numberProp == null || getPoint == null) return;

            try
            {
                var count = Convert.ToInt32(numberProp.GetValue(entity, null));
                var vertices = new JArray();
                for (var i = 0; i < Math.Min(count, 200); i++)
                {
                    vertices.Add(ToPointToken(getPoint.Invoke(entity, new object[] { i })));
                }
                geometry["vertex_count"] = count;
                geometry["vertices"] = vertices;
                var closed = TryGetBoolProperty(entity, "Closed");
                if (closed.HasValue) geometry["closed"] = closed.Value;
            }
            catch
            {
                // Geometry extraction is best-effort; the entity record remains useful.
            }
        }

        private static void AddPointIfPresent(JObject obj, object source, string property, string outputName)
        {
            var value = TryReadPointProperty(source, property);
            if (value != null) obj[outputName] = value;
        }

        private static void AddStringIfPresent(JObject obj, object source, string property, string outputName)
        {
            var value = TryGetStringProperty(source, property);
            if (!string.IsNullOrEmpty(value)) obj[outputName] = value;
        }

        private static void AddDoubleIfPresent(JObject obj, object source, string property, string outputName)
        {
            var value = TryReadDoubleProperty(source, property);
            if (value.HasValue) obj[outputName] = value.Value;
        }

        private static JObject TryReadBounds(Entity entity)
        {
            try
            {
                var extents = entity.GetType().GetProperty("GeometricExtents")?.GetValue(entity, null);
                if (extents == null) return null;
                return new JObject
                {
                    ["min"] = ToPointToken(extents.GetType().GetProperty("MinPoint")?.GetValue(extents, null)),
                    ["max"] = ToPointToken(extents.GetType().GetProperty("MaxPoint")?.GetValue(extents, null)),
                };
            }
            catch
            {
                return null;
            }
        }

        private static JToken TryReadPointProperty(object source, string property)
        {
            try
            {
                var value = source.GetType().GetProperty(property)?.GetValue(source, null);
                return value == null ? null : ToPointToken(value);
            }
            catch
            {
                return null;
            }
        }

        private static JArray ToPointToken(object point)
        {
            if (point == null) return null;
            var type = point.GetType();
            return new JArray(
                Convert.ToDouble(type.GetProperty("X")?.GetValue(point, null) ?? 0),
                Convert.ToDouble(type.GetProperty("Y")?.GetValue(point, null) ?? 0),
                Convert.ToDouble(type.GetProperty("Z")?.GetValue(point, null) ?? 0));
        }

        private static string TryGetStringProperty(object source, string property)
        {
            try
            {
                return source.GetType().GetProperty(property)?.GetValue(source, null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryGetBoolProperty(object source, string property)
        {
            try
            {
                var value = source.GetType().GetProperty(property)?.GetValue(source, null);
                return value == null ? (bool?)null : Convert.ToBoolean(value);
            }
            catch
            {
                return null;
            }
        }

        private static double? TryReadDoubleProperty(object source, string property)
        {
            try
            {
                var value = source.GetType().GetProperty(property)?.GetValue(source, null);
                return value == null ? (double?)null : Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private static JObject DescribeColor(object color)
        {
            if (color == null) return null;
            var obj = new JObject();
            AddStringIfPresent(obj, color, "ColorMethod", "method");
            AddDoubleIfPresent(obj, color, "ColorIndex", "index");
            return obj;
        }
    }
}
