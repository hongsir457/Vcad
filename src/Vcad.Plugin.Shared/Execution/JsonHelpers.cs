using System;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;
using Vcad.Core.Results;
using Vcad.Core.Validation;

namespace Vcad.Plugin.Execution
{
    internal static class JsonHelpers
    {
        public static string RequiredString(JObject obj, string field)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Missing required string field: " + field);
            }
            var s = token.Value<string>();
            if (string.IsNullOrEmpty(s))
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Field '" + field + "' must be a non-empty string.");
            }
            return s;
        }

        public static string OptionalString(JObject obj, string field, string defaultValue)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            return token.Value<string>();
        }

        public static double RequiredDouble(JObject obj, string field)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Missing required numeric field: " + field);
            }
            try
            {
                return token.Value<double>();
            }
            catch
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Field '" + field + "' must be a number.");
            }
        }

        public static double OptionalDouble(JObject obj, string field, double defaultValue)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return token.Value<double>(); }
            catch
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Field '" + field + "' must be a number.");
            }
        }

        public static int? OptionalInt(JObject obj, string field)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.Value<int>(); }
            catch
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Field '" + field + "' must be an integer.");
            }
        }

        public static Point3d Required2dOr3dPoint(JObject obj, string field)
        {
            var arr = obj[field] as JArray;
            if (arr == null || arr.Count < 2 || arr.Count > 3)
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Field '" + field + "' must be an array of 2 or 3 numbers.");
            }
            double x = SafeDouble(arr[0], field + "[0]");
            double y = SafeDouble(arr[1], field + "[1]");
            double z = arr.Count == 3 ? SafeDouble(arr[2], field + "[2]") : 0.0;
            ValidateCoordinate(x, field + ".x");
            ValidateCoordinate(y, field + ".y");
            return new Point3d(x, y, z);
        }

        public static double SafeDouble(JToken t, string label)
        {
            try { return t.Value<double>(); }
            catch
            {
                throw new DslExecutionException(ErrorCodes.SchemaInvalid,
                    "Value at " + label + " must be a number.");
            }
        }

        public static void ValidateCoordinate(double v, string label)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new DslExecutionException(ErrorCodes.ParamRange,
                    "Coordinate " + label + " is not a finite number.");
            }
            if (Math.Abs(v) > ParameterLimits.CoordinateAbsMax)
            {
                throw new DslExecutionException(ErrorCodes.ParamRange,
                    "Coordinate " + label + " exceeds the allowed absolute limit.");
            }
        }

        public static void ValidatePositiveDimension(double v, string label)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new DslExecutionException(ErrorCodes.ParamRange,
                    label + " is not a finite number.");
            }
            if (v <= ParameterLimits.DimensionMinExclusive || v > ParameterLimits.DimensionMaxInclusive)
            {
                throw new DslExecutionException(ErrorCodes.ParamRange,
                    label + " must be > 0 and <= " + ParameterLimits.DimensionMaxInclusive + ".");
            }
        }

        public static void ValidateLayerName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new DslExecutionException(ErrorCodes.LayerInvalid, "Layer name is required.");
            }
            if (name.Length > ParameterLimits.LayerNameMaxLength)
            {
                throw new DslExecutionException(ErrorCodes.LayerInvalid,
                    "Layer name exceeds " + ParameterLimits.LayerNameMaxLength + " characters.");
            }
            // AutoCAD layer name forbidden chars: < > / \ " : ; ? * | , = `
            string forbidden = "<>/\\\":;?*|,=`";
            foreach (char c in name)
            {
                if (forbidden.IndexOf(c) >= 0)
                {
                    throw new DslExecutionException(ErrorCodes.LayerInvalid,
                        "Layer name contains forbidden character: " + c);
                }
            }
        }

        public static void ValidateText(string text)
        {
            if (text == null) return;
            if (text.Length > ParameterLimits.TextMaxLength)
            {
                throw new DslExecutionException(ErrorCodes.ParamRange,
                    "Text length exceeds " + ParameterLimits.TextMaxLength + ".");
            }
        }
    }
}
