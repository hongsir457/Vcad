using System;
using System.IO;
using System.Reflection;
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
                var asm = typeof(VcadExtensionApplication).Assembly;
                var name = asm.GetName();
                string built = "?";
                try
                {
                    var path = new Uri(asm.CodeBase).LocalPath;
                    built = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
                }
                catch { /* best effort */ }

                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage(
                    "\nVCAD plugin loaded — " + name.Name + " v" + name.Version +
                    " (built " + built + "). Type VCAD to open the sidebar.\n");
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
