using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;

namespace Vcad.Plugin.Execution
{
    internal static class DrawTextExecutor
    {
        public static IList<EntityRef> Execute(JObject cmd, ExecutorContext ctx)
        {
            string id = JsonHelpers.OptionalString(cmd, "id", null);
            string layer = JsonHelpers.OptionalString(cmd, "layer", "0");
            JsonHelpers.ValidateLayerName(layer);

            string text = JsonHelpers.RequiredString(cmd, "text");
            JsonHelpers.ValidateText(text);

            var position = JsonHelpers.Required2dOr3dPoint(cmd, "position");
            double height = JsonHelpers.RequiredDouble(cmd, "height");
            JsonHelpers.ValidatePositiveDimension(height, "text.height");
            double rotationDeg = JsonHelpers.OptionalDouble(cmd, "rotation", 0.0);
            string alignment = JsonHelpers.OptionalString(cmd, "alignment", "left");
            string styleName = JsonHelpers.OptionalString(cmd, "text_style", "STANDARD");

            DrawLineExecutor.EnsureLayerExists(ctx, layer);

            var dbText = new DBText
            {
                TextString = text,
                Position = position,
                Height = height,
                Rotation = rotationDeg * Math.PI / 180.0,
                Layer = layer,
            };

            try
            {
                var tst = (TextStyleTable)ctx.Transaction.GetObject(ctx.Database.TextStyleTableId, OpenMode.ForRead);
                if (!string.IsNullOrEmpty(styleName) && tst.Has(styleName))
                {
                    dbText.TextStyleId = tst[styleName];
                }
            }
            catch
            {
                // fall back to default style
            }

            ApplyAlignment(dbText, alignment, position);

            var ms = ctx.ModelSpace();
            ms.AppendEntity(dbText);
            ctx.Transaction.AddNewlyCreatedDBObject(dbText, true);

            var refEntity = new EntityRef
            {
                DslId = id,
                EntityType = "DBText",
                Handle = dbText.Handle.ToString(),
                ObjectId = dbText.ObjectId.ToString(),
                Layer = layer,
            };
            if (!string.IsNullOrEmpty(id))
            {
                ctx.Mapping.Add(id, refEntity);
            }
            return new List<EntityRef> { refEntity };
        }

        private static void ApplyAlignment(DBText t, string alignment, Point3d position)
        {
            if (string.IsNullOrEmpty(alignment)) return;
            switch (alignment.ToLowerInvariant())
            {
                case "left":
                    t.HorizontalMode = TextHorizontalMode.TextLeft;
                    t.VerticalMode = TextVerticalMode.TextBase;
                    break;
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
                case "middle_left":
                    t.HorizontalMode = TextHorizontalMode.TextLeft;
                    t.VerticalMode = TextVerticalMode.TextVerticalMid;
                    break;
                case "middle_right":
                    t.HorizontalMode = TextHorizontalMode.TextRight;
                    t.VerticalMode = TextVerticalMode.TextVerticalMid;
                    t.AlignmentPoint = position;
                    break;
            }
        }
    }
}
