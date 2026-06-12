using System.Net;
using Vcad.AgentLite;
using Vcad.AgentLite.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(opts =>
{
    var port = AgentEnv.Port;
    opts.Listen(IPAddress.Loopback, port);
});

builder.Services.AddSingleton<ProviderRouter>();

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
    supported_dsl_versions = new[] { "vcad_dsl_v1" },
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

app.MapPost("/parse", async (HttpContext ctx, ProviderRouter router) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Empty request body." });
    }

    ParseRequest? req;
    try
    {
        req = System.Text.Json.JsonSerializer.Deserialize<ParseRequest>(body);
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.BadRequest(new { error = "Invalid JSON: " + ex.Message });
    }

    if (req == null || string.IsNullOrWhiteSpace(req.text))
    {
        return Results.BadRequest(new { error = "'text' is required." });
    }
    if (req.text.Length > MaxTextChars)
    {
        return Results.BadRequest(new { error = "'text' exceeds 32000 character limit." });
    }

    req.attachments ??= new List<ParseAttachment>();
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
        var result = await router.ParseAsync(req);
        return Results.Json(new
        {
            request_id = req.request_id ?? ("req-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")),
            success = true,
            dsl = result.Dsl,
            assistant_message = result.AssistantMessage,
            clarification = result.Clarification,
            usage = result.Usage,
            warnings = Array.Empty<string>(),
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            request_id = req.request_id,
            success = false,
            error = SecretRedactor.Redact(ex.Message),
        }, statusCode: 500);
    }
});

app.Run();

public partial class Program { }

