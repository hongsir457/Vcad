// COMPILE-TIME-ONLY STUBS for the Autodesk.AutoCAD.* API surface used by
// the VCAD plugin. The real assemblies at runtime have the same shape.
// Do not ship this DLL.
//
// If the plugin source starts using a new Autodesk type or member, add
// it here and the CI build will go green again. If you mistype an
// Autodesk member name, this build will fail before you ever start
// AutoCAD.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// ====== Autodesk.AutoCAD.Runtime ============================================
namespace Autodesk.AutoCAD.Runtime
{
    public class Exception : System.Exception
    {
        public Exception() { }
        public Exception(string message) : base(message) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ExtensionApplicationAttribute : Attribute
    {
        public ExtensionApplicationAttribute(Type type) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class CommandClassAttribute : Attribute
    {
        public CommandClassAttribute(Type type) { }
    }

    [Flags]
    public enum CommandFlags
    {
        Modal = 0,
        Transparent = 1,
        UsePickSet = 2,
        Redraw = 4,
        NoPerspective = 8,
        Session = 0x10,
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CommandMethodAttribute : Attribute
    {
        public CommandMethodAttribute(string globalName) { }
        public CommandMethodAttribute(string globalName, CommandFlags flags) { }
        public CommandMethodAttribute(string group, string globalName, CommandFlags flags) { }
    }

    public interface IExtensionApplication
    {
        void Initialize();
        void Terminate();
    }
}

// ====== Autodesk.AutoCAD.Geometry ===========================================
namespace Autodesk.AutoCAD.Geometry
{
    public struct Point2d
    {
        public double X, Y;
        public Point2d(double x, double y) { X = x; Y = y; }
    }

    public struct Point3d
    {
        public double X, Y, Z;
        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3d Origin => new Point3d(0, 0, 0);
    }

    public struct Vector3d
    {
        public double X, Y, Z;
        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vector3d ZAxis => new Vector3d(0, 0, 1);
    }

    public struct Matrix3d
    {
        public static Matrix3d Rotation(double angleRad, Vector3d axis, Point3d center) => default(Matrix3d);
        public static Matrix3d Displacement(Vector3d v) => default(Matrix3d);
    }
}

// ====== Autodesk.AutoCAD.Colors =============================================
namespace Autodesk.AutoCAD.Colors
{
    public enum ColorMethod
    {
        ByAci = 0xC3,
        ByBlock = 0xC1,
        ByLayer = 0xC0,
        ByPen = 0xC2,
        Foreground = 0xC4,
        LayerOff = 0xC6,
        LayerFrozen = 0xC5,
        None = 0xC8,
    }

    public sealed class Color
    {
        public static Color FromColorIndex(ColorMethod method, short index) => new Color();
    }
}

// ====== Autodesk.AutoCAD.DatabaseServices ===================================
namespace Autodesk.AutoCAD.DatabaseServices
{
    public struct Handle
    {
        public override string ToString() => "0";
    }

    public struct ObjectId
    {
        public static readonly ObjectId Null = default(ObjectId);
        public Handle Handle => default(Handle);
        public override string ToString() => "0";
    }

    public enum OpenMode { ForRead = 0, ForWrite = 1, ForNotify = 2 }

    public enum LineWeight
    {
        ByLayer = -1, ByBlock = -2, ByLineWeightDefault = -3,
        LineWeight000 = 0, LineWeight005 = 5, LineWeight009 = 9,
        LineWeight013 = 13, LineWeight015 = 15, LineWeight018 = 18,
        LineWeight020 = 20, LineWeight025 = 25, LineWeight030 = 30,
        LineWeight035 = 35, LineWeight040 = 40, LineWeight050 = 50,
        LineWeight053 = 53, LineWeight060 = 60, LineWeight070 = 70,
        LineWeight080 = 80, LineWeight090 = 90, LineWeight100 = 100,
        LineWeight106 = 106, LineWeight120 = 120, LineWeight140 = 140,
        LineWeight158 = 158, LineWeight200 = 200, LineWeight211 = 211,
    }

    public enum TextHorizontalMode
    {
        TextLeft = 0, TextCenter = 1, TextRight = 2,
        TextAlign = 3, TextMid = 4, TextFit = 5,
    }

    public enum TextVerticalMode
    {
        TextBase = 0, TextBottom = 1, TextVerticalMid = 2, TextTop = 3,
    }

    public class DisposableWrapper : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() { IsDisposed = true; }
    }

    public class DBObject : DisposableWrapper
    {
        public Handle Handle => default(Handle);
        public ObjectId ObjectId => default(ObjectId);
        public void UpgradeOpen() { }
        public void DowngradeOpen() { }
    }

    public class Entity : DBObject
    {
        public string Layer { get; set; }
        public Autodesk.AutoCAD.Colors.Color Color { get; set; }
        public void TransformBy(Autodesk.AutoCAD.Geometry.Matrix3d m) { }
    }

    public class SymbolTable : DBObject
    {
        public bool Has(string name) => false;
        public ObjectId this[string name] => default(ObjectId);
        public ObjectId Add(SymbolTableRecord record) => default(ObjectId);
    }

    public class SymbolTableRecord : DBObject
    {
        public string Name { get; set; }
    }

    public class BlockTable : SymbolTable { }

    public class BlockTableRecord : SymbolTableRecord
    {
        public const string ModelSpace = "*MODEL_SPACE";
        public const string PaperSpace = "*PAPER_SPACE";
        public ObjectId AppendEntity(Entity ent) => default(ObjectId);
    }

    public class LayerTable : SymbolTable { }

    public class LayerTableRecord : SymbolTableRecord
    {
        public Autodesk.AutoCAD.Colors.Color Color { get; set; }
        public ObjectId LinetypeObjectId { get; set; }
        public LineWeight LineWeight { get; set; }
        public bool IsPlottable { get; set; }
    }

    public class LinetypeTable : SymbolTable { }
    public class LinetypeTableRecord : SymbolTableRecord { }

    public class TextStyleTable : SymbolTable { }
    public class TextStyleTableRecord : SymbolTableRecord { }

    public class Line : Entity
    {
        public Line() { }
        public Line(Autodesk.AutoCAD.Geometry.Point3d start, Autodesk.AutoCAD.Geometry.Point3d end) { }
    }

    public class Polyline : Entity
    {
        public Polyline() { }
        public Polyline(int capacity) { }
        public bool Closed { get; set; }
        public void AddVertexAt(int index, Autodesk.AutoCAD.Geometry.Point2d p, double bulge, double startWidth, double endWidth) { }
    }

    public class DBText : Entity
    {
        public string TextString { get; set; }
        public Autodesk.AutoCAD.Geometry.Point3d Position { get; set; }
        public Autodesk.AutoCAD.Geometry.Point3d AlignmentPoint { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public ObjectId TextStyleId { get; set; }
        public TextHorizontalMode HorizontalMode { get; set; }
        public TextVerticalMode VerticalMode { get; set; }
    }

    public class Transaction : DisposableWrapper
    {
        public DBObject GetObject(ObjectId id, OpenMode mode) => null;
        public void AddNewlyCreatedDBObject(DBObject obj, bool add) { }
        public void Commit() { }
        public void Abort() { }
    }

    public class TransactionManager
    {
        public Transaction StartTransaction() => new Transaction();
    }

    public class Database
    {
        public ObjectId BlockTableId => default(ObjectId);
        public ObjectId LayerTableId => default(ObjectId);
        public ObjectId LinetypeTableId => default(ObjectId);
        public ObjectId TextStyleTableId => default(ObjectId);
        public TransactionManager TransactionManager { get; } = new TransactionManager();
        public void StartUndoRecord() { }
    }
}

// ====== Autodesk.AutoCAD.EditorInput ========================================
namespace Autodesk.AutoCAD.EditorInput
{
    public class Editor
    {
        public void WriteMessage(string fmt, params object[] args) { }
    }
}

// ====== Autodesk.AutoCAD.ApplicationServices ================================
namespace Autodesk.AutoCAD.ApplicationServices
{
    public class DocumentLock : IDisposable
    {
        public void Dispose() { }
    }

    public class Document
    {
        public DatabaseServices.Database Database { get; } = new DatabaseServices.Database();
        public Autodesk.AutoCAD.EditorInput.Editor Editor { get; } = new Autodesk.AutoCAD.EditorInput.Editor();
        public DocumentLock LockDocument() => new DocumentLock();
        public void SendStringToExecute(string commands, bool activate, bool wrapUpInactiveDoc, bool echoCommand) { }
    }

    public class DocumentCollection
    {
        public Document MdiActiveDocument { get; } = new Document();
    }

    public static class Application
    {
        public static DocumentCollection DocumentManager { get; } = new DocumentCollection();
    }
}

// ====== Autodesk.AutoCAD.Windows ============================================
namespace Autodesk.AutoCAD.Windows
{
    [Flags]
    public enum PaletteSetStyles
    {
        None = 0,
        NameEditable = 0x0001,
        ShowPropertiesMenu = 0x0002,
        ShowAutoHideButton = 0x0004,
        ShowCloseButton = 0x0008,
        SingleColDock = 0x0010,
        Snappable = 0x0020,
        UseDefaultProperties = 0x0040,
    }

    [Flags]
    public enum DockSides
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        Any = Left | Right | Top | Bottom,
    }

    public class PaletteSet
    {
        public PaletteSet(string name) { }
        public PaletteSet(string name, Guid toolPaletteId) { }
        public PaletteSetStyles Style { get; set; }
        public DockSides DockEnabled { get; set; }
        public Size Size { get; set; }
        public Size MinimumSize { get; set; }
        public bool Visible { get; set; }
        public bool KeepFocus { get; set; }
        public int Add(string name, Control control) => 0;
    }
}
