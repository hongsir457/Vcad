namespace Vcad.Core.Dsl
{
    public static class CommandTypes
    {
        public const string CreateLayer = "create_layer";
        public const string DrawLine = "draw_line";
        public const string DrawRectangle = "draw_rectangle";
        public const string DrawText = "draw_text";

        public static readonly string[] Whitelist =
        {
            CreateLayer,
            DrawLine,
            DrawRectangle,
            DrawText,
        };

        public static bool IsAllowed(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            for (int i = 0; i < Whitelist.Length; i++)
            {
                if (Whitelist[i] == type) return true;
            }
            return false;
        }
    }
}
