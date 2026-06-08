using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;

namespace Vcad.Plugin.Execution
{
    internal static class CreateLayerExecutor
    {
        public static IList<EntityRef> Execute(JObject cmd, ExecutorContext ctx)
        {
            string id = JsonHelpers.OptionalString(cmd, "id", null);
            string name = JsonHelpers.RequiredString(cmd, "name");
            JsonHelpers.ValidateLayerName(name);

            int? color = JsonHelpers.OptionalInt(cmd, "color");
            string linetype = JsonHelpers.OptionalString(cmd, "linetype", "Continuous");
            int? lineweight = JsonHelpers.OptionalInt(cmd, "lineweight");

            var db = ctx.Database;
            var tr = ctx.Transaction;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            LayerTableRecord ltr;
            if (lt.Has(name))
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            }
            else
            {
                ltr = new LayerTableRecord { Name = name };
                lt.UpgradeOpen();
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            if (color.HasValue)
            {
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color.Value);
            }

            if (!string.IsNullOrEmpty(linetype))
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetype))
                {
                    ltr.LinetypeObjectId = ltt[linetype];
                }
            }

            if (lineweight.HasValue)
            {
                try
                {
                    ltr.LineWeight = (LineWeight)lineweight.Value;
                }
                catch
                {
                    // ignore invalid lineweight (keep default)
                }
            }

            var refEntity = new EntityRef
            {
                DslId = id,
                EntityType = "Layer",
                Handle = ltr.Handle.ToString(),
                ObjectId = ltr.ObjectId.ToString(),
                Layer = name,
            };
            if (!string.IsNullOrEmpty(id))
            {
                ctx.Mapping.Add(id, refEntity);
            }

            return new List<EntityRef> { refEntity };
        }
    }
}
