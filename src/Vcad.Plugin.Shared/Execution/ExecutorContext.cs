using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Vcad.Plugin.Mapping;

namespace Vcad.Plugin.Execution
{
    internal class ExecutorContext
    {
        public Document Document { get; }
        public Database Database => Document.Database;
        public Transaction Transaction { get; }
        public IdMap Mapping { get; }

        public ExecutorContext(Document doc, Transaction tr, IdMap mapping)
        {
            Document = doc;
            Transaction = tr;
            Mapping = mapping;
        }

        public BlockTableRecord ModelSpace()
        {
            var bt = (BlockTable)Transaction.GetObject(Database.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)Transaction.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }
    }

    internal class DslExecutionException : System.Exception
    {
        public string Code { get; }

        public DslExecutionException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
