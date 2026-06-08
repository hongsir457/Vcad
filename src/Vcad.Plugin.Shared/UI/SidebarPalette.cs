using System;
using Autodesk.AutoCAD.Windows;

namespace Vcad.Plugin.UI
{
    internal static class SidebarPalette
    {
        private static PaletteSet _set;
        private static SidebarControl _control;

        public static void Show()
        {
            if (_set == null)
            {
                _set = new PaletteSet("VCAD")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.Snappable,
                    DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right),
                    Size = new System.Drawing.Size(420, 600),
                    MinimumSize = new System.Drawing.Size(360, 420),
                };

                _control = new SidebarControl();
                _set.Add("VCAD", _control);
            }

            _set.KeepFocus = true;
            _set.Visible = true;
        }
    }
}
