namespace Vcad.Core.Validation
{
    public static class ParameterLimits
    {
        public const int MaxCommandsPerRequest = 200;

        public const double CoordinateAbsMax = 1_000_000_000.0;

        public const double DimensionMinExclusive = 0.0;
        public const double DimensionMaxInclusive = 100_000_000.0;

        public const int TextMaxLength = 2048;
        public const int LayerNameMaxLength = 255;

        public const long JsonRequestMaxBytes = 1L * 1024 * 1024;
    }
}
