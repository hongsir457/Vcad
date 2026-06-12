using System.Net;
using Vcad.AgentLite;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(opts =>
{
    var port = AgentEnv.Port;
    opts.Listen(IPAddress.Loopback, port);
});

builder.Services.AddSingleton<AgentTurnService>();

var app = builder.Build();

const int MaxRequestBodyBytes = 8 * 1024 * 1024;
const int MaxTextChars = 32000;
const int MaxAttachmentCount = 12;
const int MaxInlineAttachmentBase64Chars = 6 * 1024 * 1024;

app.Use(async (ctx, next) =>
{
    // Token check
    var expected = AgentEnv.Token;
    if (!string.IsNullOrEmpty(expected))
    {
        if (!ctx.Request.Headers.TryGetValue("X-VCAD-Agent-Token", out var got) || got != expected)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Missing or invalid X-VCAD-Agent-Token.");
            return;
        }
    }

    // Keep request bodies bounded; large source files should be summarized or
    // passed as typed attachments, not dumped raw into the prompt.
    if (ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > MaxRequestBodyBytes)
    {
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await ctx.Response.WriteAsync("Request body exceeds 8 MB.");
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "vcad-agent-lite",
    version = "0.1.0",
    supported_agent_protocols = new[] { "vcad_agent_turn_v1" },
}));

app.MapGet("/tools", () => Results.Ok(new
{
    service = "vcad-agent-lite",
    tools = AgentTools.List(),
}));

app.MapPost("/tool", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Empty request body." });
    }

    System.Text.Json.Nodes.JsonObject? req;
    try
    {
        req = System.Text.Json.Nodes.JsonNode.Parse(body)?.AsObject();
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.BadRequest(new { error = "Invalid JSON: " + ex.Message });
    }

    var name = req?["name"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { error = "'name' is required." });
    }
    var args = req?["args"] as System.Text.Json.Nodes.JsonObject;
    var result = await AgentToolRunner.RunAsync(name, args);
    return Results.Json(result);
});

app.MapPost("/agent/turn", async (HttpContext ctx, AgentTurnService agent) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Empty request body." });
    }

    AgentTurnRequest? req;
    try
    {
        req = System.Text.Json.JsonSerializer.Deserialize<AgentTurnRequest>(body);
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.BadRequest(new { error = "Invalid JSON: " + ex.Message });
    }

    if (req == null || (string.IsNullOrWhiteSpace(req.message) &&
        (req.tool_results == null || req.tool_results.Count == 0)))
    {
        return Results.BadRequest(new { error = "'message' or 'tool_results' is required." });
    }
    if ((req.message ?? "").Length > MaxTextChars)
    {
        return Results.BadRequest(new { error = "'message' exceeds 32000 character limit." });
    }

    req.attachments ??= new List<AgentAttachment>();
    if (req.attachments.Count > MaxAttachmentCount)
    {
        return Results.BadRequest(new { error = "'attachments' exceeds 12 item limit." });
    }
    foreach (var attachment in req.attachments)
    {
        if (!string.IsNullOrEmpty(attachment.data_base64) &&
            attachment.data_base64.Length > MaxInlineAttachmentBase64Chars)
        {
            return Results.BadRequest(new { error = "An inline attachment exceeds 6 MB base64 limit." });
        }
    }

    try
    {
        var result = await agent.RunAsync(req);
        return Results.Json(new
        {
            success = true,
            response = result,
        });
    }
    catch (ProviderRequestException ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            provider = ex.Provider,
            upstream_status = ex.StatusCode,
            upstream_body = ex.ResponseBody,
        }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = SecretRedactor.Redact(ex.Message),
        }, statusCode: 500);
    }
});

app.Run();

public partial class Program { }

