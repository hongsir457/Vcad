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

    // 256 KB request body cap
    if (ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > 256 * 1024)
    {
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await ctx.Response.WriteAsync("Request body exceeds 256 KB.");
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
    if (req.text.Length > 8000)
    {
        return Results.BadRequest(new { error = "'text' exceeds 8000 character limit." });
    }

    try
    {
        var dsl = await router.ParseAsync(req);
        return Results.Json(new
        {
            request_id = req.request_id ?? ("req-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")),
            success = true,
            dsl = dsl,
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

