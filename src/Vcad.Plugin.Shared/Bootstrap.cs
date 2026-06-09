using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Vcad.Plugin;

[assembly: ExtensionApplication(typeof(VcadExtensionApplication))]

namespace Vcad.Plugin
{
    public class VcadExtensionApplication : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\nVCAD plugin loaded. Type VCAD to open the sidebar.\n");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("VCAD Initialize error: " + ex);
            }
        }

        public void Terminate()
        {
        }
    }
}
