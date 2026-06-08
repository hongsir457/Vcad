using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Core.Dsl;
using Vcad.Core.Results;

namespace Vcad.Core.Validation
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public DslRequest Request { get; set; }
    }

    public static class DslValidator
    {
        public static ValidationResult ParseAndValidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Fail(ErrorCodes.SchemaInvalid, "Empty DSL input.");
            }

            if (System.Text.Encoding.UTF8.GetByteCount(json) > ParameterLimits.JsonRequestMaxBytes)
            {
                return Fail(ErrorCodes.SchemaInvalid, "DSL request exceeds 1 MB limit.");
            }

            DslRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<DslRequest>(json);
            }
            catch (JsonException ex)
            {
                return Fail(ErrorCodes.SchemaInvalid, "Invalid JSON: " + ex.Message);
            }

            if (request == null)
            {
                return Fail(ErrorCodes.SchemaInvalid, "DSL request is null.");
            }

            if (string.IsNullOrEmpty(request.Version))
            {
                return Fail(ErrorCodes.SchemaInvalid, "Missing 'version'.");
            }

            if (!IsSupportedVersion(request.Version))
            {
                return Fail(ErrorCodes.UnsupportedVersion,
                    "DSL version '" + request.Version + "' is not supported by this plugin.");
            }

            if (request.Commands == null || request.Commands.Count == 0)
            {
                return Fail(ErrorCodes.SchemaInvalid, "Missing or empty 'commands'.");
            }

            if (request.Commands.Count > ParameterLimits.MaxCommandsPerRequest)
            {
                return Fail(ErrorCodes.ParamRange,
                    "Too many commands (" + request.Commands.Count + "); max is " +
                    ParameterLimits.MaxCommandsPerRequest + ".");
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < request.Commands.Count; i++)
            {
                JObject cmd = request.Commands[i];
                if (cmd == null)
                {
                    return Fail(ErrorCodes.SchemaInvalid, "Command #" + i + " is null.");
                }

                string type = cmd.Value<string>("type");
                if (string.IsNullOrEmpty(type))
                {
                    return Fail(ErrorCodes.SchemaInvalid, "Command #" + i + " missing 'type'.");
                }

                if (!CommandTypes.IsAllowed(type))
                {
                    return Fail(ErrorCodes.CommandNotAllowed,
                        "Command type '" + type + "' is not in the whitelist.");
                }

                string id = cmd.Value<string>("id");
                if (!string.IsNullOrEmpty(id))
                {
                    if (!seenIds.Add(id))
                    {
                        return Fail(ErrorCodes.SchemaInvalid,
                            "Duplicate command id '" + id + "' in this request.");
                    }
                }
            }

            return new ValidationResult
            {
                IsValid = true,
                Request = request,
            };
        }

        public static bool IsSupportedVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            for (int i = 0; i < DslVersion.Supported.Length; i++)
            {
                if (DslVersion.Supported[i] == version) return true;
            }
            return false;
        }

        private static ValidationResult Fail(string code, string message)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorCode = code,
                ErrorMessage = message,
            };
        }
    }
}
