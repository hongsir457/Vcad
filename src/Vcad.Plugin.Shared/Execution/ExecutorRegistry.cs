using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vcad.Core.Dsl;
using Vcad.Core.Results;

namespace Vcad.Plugin.Execution
{
    internal static class ExecutorRegistry
    {
        public static IList<EntityRef> Dispatch(string type, JObject cmd, ExecutorContext ctx)
        {
            switch (type)
            {
                case CommandTypes.CreateLayer:
                    return CreateLayerExecutor.Execute(cmd, ctx);
                case CommandTypes.DrawLine:
                    return DrawLineExecutor.Execute(cmd, ctx);
                case CommandTypes.DrawRectangle:
                    return DrawRectangleExecutor.Execute(cmd, ctx);
                case CommandTypes.DrawText:
                    return DrawTextExecutor.Execute(cmd, ctx);
                default:
                    throw new DslExecutionException(ErrorCodes.CommandNotAllowed,
                        "Command type '" + type + "' is not implemented.");
            }
        }
    }
}
