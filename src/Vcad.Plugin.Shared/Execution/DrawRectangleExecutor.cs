using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;

namespace Vcad.Plugin.Execution
{
    internal static class DrawRectangleExecutor
    {
        public static IList<EntityRef> Execute(JObject cmd, ExecutorContext ctx)
        {
            string id = JsonHelpers.OptionalString(cmd, "id", null);
            string layer = JsonHelpers.OptionalString(cmd, "layer", "0");
            JsonHelpers.ValidateLayerName(layer);

            var origin = JsonHelpers.Required2dOr3dPoint(cmd, "origin");
            double width = JsonHelpers.RequiredDouble(cmd, "width");
            double height = JsonHelpers.RequiredDouble(cmd, "height");
            double rotationDeg = JsonHelpers.OptionalDouble(cmd, "rotation", 0.0);

            JsonHelpers.ValidatePositiveDimension(width, "width");
            JsonHelpers.ValidatePositiveDimension(height, "height");

            DrawLineExecutor.EnsureLayerExists(ctx, layer);

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(width, 0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(width, height), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(0, height), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;

            if (Math.Abs(rotationDeg) > 1e-9)
            {
                pl.TransformBy(Matrix3d.Rotation(DegreesToRadians(rotationDeg), Vector3d.ZAxis, Point3d.Origin));
            }

            if (origin.X != 0 || origin.Y != 0 || origin.Z != 0)
            {
                pl.TransformBy(Matrix3d.Displacement(new Vector3d(origin.X, origin.Y, origin.Z)));
            }

            var ms = ctx.ModelSpace();
            ms.AppendEntity(pl);
            ctx.Transaction.AddNewlyCreatedDBObject(pl, true);

            var refEntity = new EntityRef
            {
                DslId = id,
                EntityType = "Polyline",
                Handle = pl.Handle.ToString(),
                ObjectId = pl.ObjectId.ToString(),
                Layer = layer,
            };
            if (!string.IsNullOrEmpty(id))
            {
                ctx.Mapping.Add(id, refEntity);
            }
            return new List<EntityRef> { refEntity };
        }

        private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
    }
}
