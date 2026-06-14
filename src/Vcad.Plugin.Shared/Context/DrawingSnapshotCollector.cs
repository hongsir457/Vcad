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
        private const int DefaultMaxEntities = 3000;
        private const int DefaultMaxBlockDepth = 5;
        private const int DefaultMaxExplodedEntitiesPerBlock = 500;
        private const int BriefMaxEntities = 120;

        public static JObject CaptureActive()
        {
            return CaptureActive(FullOptions());
        }

        public static JObject CaptureActiveBrief()
        {
            return CaptureActive(BriefOptions());
        }

        private static JObject CaptureActive(CaptureOptions options)
        {
            var warnings = new JArray();
            var layers = new JArray();
            var linetypes = new JArray();
            var textStyles = new JArray();
            var dimStyles = new JArray();
            var blocks = new JArray();
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
                ["database"] = new JObject(),
                ["limits"] = new JObject
                {
                    ["profile"] = options.Profile,
                    ["max_entities"] = options.MaxEntities,
                    ["max_block_depth"] = options.MaxBlockDepth,
                    ["max_exploded_entities_per_block"] = options.MaxExplodedEntitiesPerBlock,
                    ["include_entity_properties"] = options.IncludeEntityProperties,
                    ["include_entity_geometry"] = options.IncludeEntityGeometry,
                    ["include_entity_bounds"] = options.IncludeEntityBounds,
                    ["explode_blocks"] = options.ExplodeBlocks,
                },
                ["summary"] = summary,
                ["layers"] = layers,
                ["linetypes"] = linetypes,
                ["text_styles"] = textStyles,
                ["dim_styles"] = dimStyles,
                ["blocks"] = blocks,
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
            snapshot["database"] = new JObject
            {
                ["insunits"] = TryGetStringProperty(doc.Database, "Insunits"),
                ["measurement"] = TryGetStringProperty(doc.Database, "Measurement"),
                ["tilemode"] = TryGetStringProperty(doc.Database, "TileMode"),
                ["current_layer"] = TryGetSymbolNameSafe(doc.Database, doc.Database.Clayer),
                ["current_linetype"] = TryGetSymbolNameSafe(doc.Database, doc.Database.Celtype),
                ["text_style"] = TryGetSymbolNameSafe(doc.Database, doc.Database.Textstyle),
                ["dim_style"] = TryGetSymbolNameSafe(doc.Database, doc.Database.Dimstyle),
                ["ltscale"] = TryReadDoubleProperty(doc.Database, "Ltscale"),
                ["dimscale"] = TryReadDoubleProperty(doc.Database, "Dimscale"),
            };

            try
            {
                using (var docLock = doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    CaptureLayers(doc.Database, tr, layers, warnings);
                    CaptureLinetypes(doc.Database, tr, linetypes, warnings);
                    CaptureTextStyles(doc.Database, tr, textStyles, warnings);
                    CaptureDimStyles(doc.Database, tr, dimStyles, warnings);
                    CaptureBlocks(doc.Database, tr, blocks, warnings);
                    summary["layer_count"] = layers.Count;

                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    CaptureSpace(bt, tr, BlockTableRecord.ModelSpace, "model", entities, summary, warnings, options);
                    CaptureSpace(bt, tr, BlockTableRecord.PaperSpace, "paper", entities, summary, warnings, options);

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Drawing snapshot failed: " + ex.Message);
            }

            summary["entity_count"] = entities.Count;
            summary["linetype_count"] = linetypes.Count;
            summary["text_style_count"] = textStyles.Count;
            summary["dim_style_count"] = dimStyles.Count;
            summary["block_definition_count"] = blocks.Count;
            snapshot["geometry_index"] = BuildGeometryIndex(entities, layers, blocks);
            return snapshot;
        }

        private sealed class CaptureOptions
        {
            public string Profile { get; set; }
            public int MaxEntities { get; set; }
            public int MaxBlockDepth { get; set; }
            public int MaxExplodedEntitiesPerBlock { get; set; }
            public bool IncludeEntityProperties { get; set; }
            public bool IncludeEntityGeometry { get; set; }
            public bool IncludeEntityBounds { get; set; }
            public bool ExplodeBlocks { get; set; }
        }

        private static CaptureOptions FullOptions()
        {
            return new CaptureOptions
            {
                Profile = "full",
                MaxEntities = DefaultMaxEntities,
                MaxBlockDepth = DefaultMaxBlockDepth,
                MaxExplodedEntitiesPerBlock = DefaultMaxExplodedEntitiesPerBlock,
                IncludeEntityProperties = true,
                IncludeEntityGeometry = true,
                IncludeEntityBounds = true,
                ExplodeBlocks = true,
            };
        }

        private static CaptureOptions BriefOptions()
        {
            return new CaptureOptions
            {
                Profile = "brief",
                MaxEntities = BriefMaxEntities,
                MaxBlockDepth = 0,
                MaxExplodedEntitiesPerBlock = 0,
                IncludeEntityProperties = false,
                IncludeEntityGeometry = false,
                IncludeEntityBounds = false,
                ExplodeBlocks = false,
            };
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
                        ["linetype"] = TryGetSymbolNameSafe(db, ltr.LinetypeObjectId),
                        ["lineweight"] = TryGetStringProperty(ltr, "LineWeight"),
                        ["transparency"] = TryGetStringProperty(ltr, "Transparency"),
                        ["is_plottable"] = TryGetBoolProperty(ltr, "IsPlottable"),
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

        private static void CaptureLinetypes(Database db, Transaction tr, JArray linetypes, JArray warnings)
        {
            try
            {
                var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(table))
                {
                    var record = tr.GetObject(id, OpenMode.ForRead) as LinetypeTableRecord;
                    if (record == null) continue;
                    linetypes.Add(new JObject
                    {
                        ["name"] = record.Name,
                        ["handle"] = record.Handle.ToString(),
                        ["description"] = TryGetStringProperty(record, "Comments"),
                    });
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Linetype scan failed: " + ex.Message);
            }
        }

        private static void CaptureTextStyles(Database db, Transaction tr, JArray styles, JArray warnings)
        {
            try
            {
                var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(table))
                {
                    var record = tr.GetObject(id, OpenMode.ForRead) as TextStyleTableRecord;
                    if (record == null) continue;
                    styles.Add(new JObject
                    {
                        ["name"] = record.Name,
                        ["handle"] = record.Handle.ToString(),
                        ["font"] = TryGetStringProperty(record, "FileName"),
                        ["big_font"] = TryGetStringProperty(record, "BigFontFileName"),
                        ["text_size"] = TryReadDoubleProperty(record, "TextSize"),
                        ["x_scale"] = TryReadDoubleProperty(record, "XScale"),
                        ["obliquing_angle"] = TryReadDoubleProperty(record, "ObliquingAngle"),
                    });
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Text style scan failed: " + ex.Message);
            }
        }

        private static void CaptureDimStyles(Database db, Transaction tr, JArray styles, JArray warnings)
        {
            try
            {
                var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(table))
                {
                    var record = tr.GetObject(id, OpenMode.ForRead) as DimStyleTableRecord;
                    if (record == null) continue;
                    styles.Add(new JObject
                    {
                        ["name"] = record.Name,
                        ["handle"] = record.Handle.ToString(),
                        ["dimscale"] = TryReadDoubleProperty(record, "Dimscale"),
                        ["dimtxt"] = TryReadDoubleProperty(record, "Dimtxt"),
                        ["dimasz"] = TryReadDoubleProperty(record, "Dimasz"),
                        ["dimclrd"] = TryGetStringProperty(record, "Dimclrd"),
                        ["dimclre"] = TryGetStringProperty(record, "Dimclre"),
                        ["dimclrt"] = TryGetStringProperty(record, "Dimclrt"),
                    });
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Dimension style scan failed: " + ex.Message);
            }
        }

        private static void CaptureBlocks(Database db, Transaction tr, JArray blocks, JArray warnings)
        {
            try
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(bt))
                {
                    var btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                    if (btr == null) continue;
                    var count = 0;
                    foreach (ObjectId ignored in EnumerateObjectIds(btr)) count++;
                    blocks.Add(new JObject
                    {
                        ["name"] = btr.Name,
                        ["handle"] = btr.Handle.ToString(),
                        ["origin"] = ToPointToken(btr.Origin),
                        ["entity_count"] = count,
                        ["is_layout"] = TryGetBoolProperty(btr, "IsLayout"),
                        ["is_anonymous"] = TryGetBoolProperty(btr, "IsAnonymous"),
                        ["is_dynamic_block"] = TryGetBoolProperty(btr, "IsDynamicBlock"),
                        ["is_from_external_reference"] = TryGetBoolProperty(btr, "IsFromExternalReference"),
                        ["path_name"] = TryGetStringProperty(btr, "PathName"),
                    });
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Block definition scan failed: " + ex.Message);
            }
        }

        private static void CaptureSpace(BlockTable bt, Transaction tr, string blockName, string space, JArray entities, JObject summary, JArray warnings, CaptureOptions options)
        {
            try
            {
                if (!bt.Has(blockName)) return;
                var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                foreach (ObjectId id in EnumerateObjectIds(btr))
                {
                    if (IsTruncated(entities, summary, options)) return;
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;
                    CaptureEntity(entity, space, new JArray(), 0, false, entities, summary, warnings, options);
                    summary["top_level_entity_count"] = summary.Value<int>("top_level_entity_count") + 1;
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Space scan failed for " + space + ": " + ex.Message);
            }
        }

        private static void CaptureEntity(Entity entity, string space, JArray blockPath, int blockDepth, bool exploded, JArray entities, JObject summary, JArray warnings, CaptureOptions options)
        {
            if (IsTruncated(entities, summary, options)) return;

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
                ["selectors"] = BuildSelectorRefs(entity, blockPath),
            };
            if (options.IncludeEntityProperties)
            {
                item["properties"] = ReadEntityProperties(entity);
            }
            if (options.IncludeEntityBounds)
            {
                item["bounds"] = TryReadBounds(entity);
            }
            if (options.IncludeEntityGeometry)
            {
                item["geometry"] = ReadGeometry(entity);
            }

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

            if (options.ExplodeBlocks && IsBlockReference(entity) && blockDepth < options.MaxBlockDepth)
            {
                CaptureExplodedBlock(entity, space, blockPath, blockDepth, entities, summary, warnings, options);
            }
        }

        private static void CaptureExplodedBlock(Entity blockRef, string space, JArray blockPath, int blockDepth, JArray entities, JObject summary, JArray warnings, CaptureOptions options)
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
                    if (count >= options.MaxExplodedEntitiesPerBlock)
                    {
                        warnings.Add("Block explode truncated at " + options.MaxExplodedEntitiesPerBlock + " entities for " + blockRef.Handle);
                        break;
                    }
                    if (IsTruncated(entities, summary, options)) break;
                    var childEntity = child as Entity;
                    if (childEntity != null)
                    {
                        CaptureEntity(childEntity, space, childPath, blockDepth + 1, true, entities, summary, warnings, options);
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

        private static bool IsTruncated(JArray entities, JObject summary, CaptureOptions options)
        {
            if (entities.Count < options.MaxEntities) return false;
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

        private static JArray BuildSelectorRefs(Entity entity, JArray blockPath)
        {
            var refs = new JArray
            {
                "handle:" + entity.Handle,
                "type:" + entity.GetType().Name,
            };
            if (!string.IsNullOrWhiteSpace(entity.Layer))
            {
                refs.Add("layer:" + entity.Layer);
            }

            if (blockPath != null)
            {
                foreach (var part in blockPath.Values<string>())
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        refs.Add("block:" + part);
                    }
                }
            }

            return refs;
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
            AddDoubleIfPresent(geometry, entity, "Diameter", "diameter");
            AddDoubleIfPresent(geometry, entity, "Height", "height");
            AddDoubleIfPresent(geometry, entity, "TextHeight", "height");
            AddDoubleIfPresent(geometry, entity, "Length", "length");
            AddDoubleIfPresent(geometry, entity, "Area", "area");
            AddDoubleIfPresent(geometry, entity, "Rotation", "rotation");
            AddDoubleIfPresent(geometry, entity, "StartAngle", "start_angle");
            AddDoubleIfPresent(geometry, entity, "EndAngle", "end_angle");
            AddPolylineVerticesIfPresent(geometry, entity);
            return geometry;
        }

        private static JObject ReadEntityProperties(Entity entity)
        {
            var obj = new JObject
            {
                ["color"] = DescribeColor(entity.Color),
                ["linetype"] = TryGetStringProperty(entity, "Linetype"),
                ["lineweight"] = TryGetStringProperty(entity, "LineWeight"),
                ["linetype_scale"] = TryReadDoubleProperty(entity, "LinetypeScale"),
                ["transparency"] = TryGetStringProperty(entity, "Transparency"),
                ["visible"] = TryGetBoolProperty(entity, "Visible"),
                ["material"] = TryGetStringProperty(entity, "Material"),
            };

            AddStringIfPresent(obj, entity, "TextStyleName", "text_style");
            AddStringIfPresent(obj, entity, "DimensionStyleName", "dimension_style");
            return obj;
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
            AddStringIfPresent(obj, color, "ColorName", "name");
            return obj;
        }

        private static string TryGetSymbolNameSafe(Database db, ObjectId id)
        {
            try
            {
                if (id.IsNull) return null;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var record = tr.GetObject(id, OpenMode.ForRead) as SymbolTableRecord;
                    var name = record == null ? null : record.Name;
                    tr.Commit();
                    return name;
                }
            }
            catch
            {
                return null;
            }
        }

        private static JObject BuildGeometryIndex(JArray entities, JArray layers, JArray blocks)
        {
            var byLayer = new JObject();
            var byType = new JObject();
            var textEntities = new JArray();
            var closedEntities = new JArray();
            var blockRefs = new JArray();
            var acc = new GeometryBounds();

            foreach (var token in entities)
            {
                var entity = token as JObject;
                if (entity == null) continue;
                Increment(byLayer, entity.Value<string>("layer"));
                Increment(byType, entity.Value<string>("type"));
                acc.Add(entity["bounds"] as JObject);

                var text = entity["geometry"]?["text"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textEntities.Add(new JObject
                    {
                        ["handle"] = entity.Value<string>("handle"),
                        ["layer"] = entity.Value<string>("layer"),
                        ["type"] = entity.Value<string>("type"),
                        ["text"] = text.Length > 200 ? text.Substring(0, 200) : text,
                        ["bounds"] = entity["bounds"],
                    });
                }

                if (entity["geometry"]?["closed"]?.Value<bool>() == true)
                {
                    closedEntities.Add(new JObject
                    {
                        ["handle"] = entity.Value<string>("handle"),
                        ["layer"] = entity.Value<string>("layer"),
                        ["type"] = entity.Value<string>("type"),
                        ["vertex_count"] = entity["geometry"]?["vertex_count"],
                        ["bounds"] = entity["bounds"],
                    });
                }

                if (entity["block_reference"] != null)
                {
                    blockRefs.Add(new JObject
                    {
                        ["handle"] = entity.Value<string>("handle"),
                        ["layer"] = entity.Value<string>("layer"),
                        ["name"] = entity["block_reference"]?["name"],
                        ["position"] = entity["block_reference"]?["position"],
                    });
                }
            }

            return new JObject
            {
                ["drawing_bounds"] = acc.ToJson(),
                ["by_layer"] = byLayer,
                ["by_type"] = byType,
                ["text_entities"] = textEntities,
                ["closed_entities"] = closedEntities,
                ["block_references"] = blockRefs,
                ["layer_count"] = layers.Count,
                ["block_definition_count"] = blocks.Count,
            };
        }

        private static void Increment(JObject obj, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "(none)";
            obj[key] = (obj.Value<int?>(key) ?? 0) + 1;
        }

        private sealed class GeometryBounds
        {
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            public bool HasValue { get; private set; }

            public void Add(JObject bounds)
            {
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
        }
    }
}
