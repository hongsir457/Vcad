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
                _control.CollapsedChanged += (sender, collapsed) => ResizeForCollapse(collapsed);
                _set.Add("VCAD", _control);
            }

            _set.KeepFocus = true;
            _set.Visible = true;
            DockRightBestEffort();
        }

        private static void ResizeForCollapse(bool collapsed)
        {
            if (_set == null) return;
            _set.MinimumSize = collapsed
                ? new System.Drawing.Size(54, 420)
                : new System.Drawing.Size(360, 420);
            _set.Size = collapsed
                ? new System.Drawing.Size(54, 600)
                : new System.Drawing.Size(420, 600);
            DockRightBestEffort();
        }

        private static void DockRightBestEffort()
        {
            try
            {
                var prop = _set?.GetType().GetProperty("Dock");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(_set, DockSides.Right, null);
                }
            }
            catch
            {
                // AutoCAD versions differ; Snappable + DockEnabled still lets users dock right.
            }
        }
    }
}
