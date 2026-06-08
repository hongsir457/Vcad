using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;

namespace Vcad.Plugin.Execution
{
    internal static class DrawLineExecutor
    {
        public static IList<EntityRef> Execute(JObject cmd, ExecutorContext ctx)
        {
            string id = JsonHelpers.OptionalString(cmd, "id", null);
            string layer = JsonHelpers.OptionalString(cmd, "layer", "0");
            JsonHelpers.ValidateLayerName(layer);

            var start = JsonHelpers.Required2dOr3dPoint(cmd, "start");
            var end = JsonHelpers.Required2dOr3dPoint(cmd, "end");

            EnsureLayerExists(ctx, layer);

            var line = new Line(start, end) { Layer = layer };

            var ms = ctx.ModelSpace();
            ms.AppendEntity(line);
            ctx.Transaction.AddNewlyCreatedDBObject(line, true);

            var refEntity = new EntityRef
            {
                DslId = id,
                EntityType = "Line",
                Handle = line.Handle.ToString(),
                ObjectId = line.ObjectId.ToString(),
                Layer = layer,
            };
            if (!string.IsNullOrEmpty(id))
            {
                ctx.Mapping.Add(id, refEntity);
            }

            return new List<EntityRef> { refEntity };
        }

        internal static void EnsureLayerExists(ExecutorContext ctx, string layer)
        {
            var lt = (LayerTable)ctx.Transaction.GetObject(ctx.Database.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layer))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord { Name = layer };
                lt.Add(ltr);
                ctx.Transaction.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
