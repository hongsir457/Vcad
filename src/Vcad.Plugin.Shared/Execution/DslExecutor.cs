using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Vcad.Core;
using Vcad.Core.Results;
using Vcad.Core.Validation;
using Vcad.Plugin.Mapping;

namespace Vcad.Plugin.Execution
{
    internal static class DslExecutor
    {
        public static VcadResult Execute(string json)
        {
            var requestId = "req-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

            var validation = DslValidator.ParseAndValidate(json);
            if (!validation.IsValid)
            {
                return VcadResult.NewFailure(requestId, validation.ErrorCode, validation.ErrorMessage);
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return VcadResult.NewFailure(requestId, ErrorCodes.AutoCadTransaction,
                    "No active AutoCAD document.");
            }

            var result = new VcadResult
            {
                RequestId = requestId,
                Version = DslVersion.ResultCurrent,
            };

            var mapping = new IdMap();

            using (var docLock = doc.LockDocument())
            {
                // One Transaction.Commit() == one AutoCAD undo record,
                // so Ctrl+Z undoes the whole batch with a single press.
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ctx = new ExecutorContext(doc, tr, mapping);

                    foreach (var cmd in validation.Request.Commands)
                    {
                        var type = cmd.Value<string>("type");
                        var id = cmd.Value<string>("id");
                        var cmdResult = new CommandResult
                        {
                            CommandId = id,
                            Type = type,
                            Success = false,
                        };

                        try
                        {
                            var entities = ExecutorRegistry.Dispatch(type, cmd, ctx);
                            cmdResult.Entities.AddRange(entities);
                            cmdResult.Success = true;
                            result.Summary.Succeeded++;
                        }
                        catch (DslExecutionException dex)
                        {
                            cmdResult.Error = new ErrorInfo(dex.Code, dex.Message, id);
                            result.Summary.Failed++;
                            result.Errors.Add(cmdResult.Error);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception aex)
                        {
                            cmdResult.Error = new ErrorInfo(ErrorCodes.AutoCadTransaction, aex.Message, id);
                            result.Summary.Failed++;
                            result.Errors.Add(cmdResult.Error);
                        }
                        catch (Exception ex)
                        {
                            cmdResult.Error = new ErrorInfo(ErrorCodes.AutoCadTransaction, ex.Message, id);
                            result.Summary.Failed++;
                            result.Errors.Add(cmdResult.Error);
                        }

                        result.Summary.Total++;
                        result.Results.Add(cmdResult);
                    }

                    if (result.Summary.Failed == 0)
                    {
                        tr.Commit();
                    }
                    else
                    {
                        tr.Abort();
                    }
                }
            }

            result.Success = result.Summary.Failed == 0;
            try
            {
                MappingPersistence.Append(mapping, requestId);
            }
            catch
            {
                // Best-effort logging; never fail the whole request.
            }
            return result;
        }
    }
}
