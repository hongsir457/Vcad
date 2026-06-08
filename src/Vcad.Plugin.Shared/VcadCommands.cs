using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Vcad.Plugin;
using Vcad.Plugin.UI;

[assembly: CommandClass(typeof(VcadCommands))]

namespace Vcad.Plugin
{
    public static class VcadCommands
    {
        [CommandMethod("VCAD", CommandFlags.Modal)]
        public static void OpenSidebar()
        {
            try
            {
                SidebarPalette.Show();
            }
            catch (Exception ex)
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\n[VCAD] Failed to open sidebar: " + ex.Message + "\n");
            }
        }
    }
}
