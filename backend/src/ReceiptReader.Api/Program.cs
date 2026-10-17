using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptReader.Api.Auth;
using ReceiptReader.Api.Endpoints;
using ReceiptReader.Application;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Inventory;
using ReceiptReader.Infrastructure;
using ReceiptReader.Infrastructure.Extraction;
using ReceiptReader.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 200L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 200L * 1024 * 1024);

builder.Services.AddAppAuth(builder.Configuration);
// nginx forwards the scheme the tunnel saw, so the session cookie is marked Secure on https.
// The API port is only reachable from this machine (nginx), so any proxy may set it.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddSingleton<OllamaHealth>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseForwardedHeaders();

// Map application exceptions to HTTP status codes.
app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        BadRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
        UnavailableException => (StatusCodes.Status503ServiceUnavailable, "Unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "Server error"),
    };
    ctx.Response.StatusCode = status;
    await Results.Problem(
        title: title,
        detail: status == 500 && !app.Environment.IsDevelopment() ? "Unexpected error" : ex?.Message,
        statusCode: status).ExecuteAsync(ctx);
}));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous(); // GET /openapi/v1.json
}

app.MapAuthEndpoints();
app.MapReceiptEndpoints();
app.MapCompanyEndpoints();
app.MapInventoryEndpoints();
app.MapLearningEndpoints();

app.MapGet("/api/health", async (AppDbContext db, OllamaHealth ollama, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var models = await ollama.ListModelsAsync(ct);
    return Results.Ok(new { database = dbOk, ollama = models is not null, models });
}).WithTags("System").AllowAnonymous();

await InitializeDatabaseAsync(app);
app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    // Schema changes go through EF migrations (backend/src/ReceiptReader.Infrastructure/Persistence/Migrations):
    //   dotnet ef migrations add <Name> -p src/ReceiptReader.Infrastructure -s src/ReceiptReader.Api -o Persistence/Migrations
    var logger = app.Logger;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BaselineLegacyDatabaseAsync(db, logger);
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<CompanyService>().EnsureChartsAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<InventoryService>().EnsureWarehousesAsync(CancellationToken.None);
            logger.LogInformation("Database ready");
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning("Database not ready ({Message}); retry {Attempt}/10 in 3s", ex.Message, attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

/// <summary>
/// Databases from before migrations were created by EnsureCreated: they have the tables but no migration history,
/// so MigrateAsync would try to create everything again. Record the first (Initial) migration as applied instead.
/// </summary>
static async Task BaselineLegacyDatabaseAsync(AppDbContext db, ILogger logger)
{
    var legacy = await db.Database.SqlQueryRaw<bool>(
            """SELECT (to_regclass('public.receipts') IS NOT NULL AND to_regclass('public."__EFMigrationsHistory"') IS NULL) AS "Value" """)
        .SingleAsync();
    if (!legacy) return;

    var initial = db.Database.GetMigrations().First();
    logger.LogInformation("Existing database without migration history: marking {Migration} as applied", initial);
    await db.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, '10.0.0');
        """, initial);
}

/// <summary>Lists models installed in Ollama, or null if Ollama is unreachable.</summary>
internal sealed class OllamaHealth(IHttpClientFactory factory, IOptions<ExtractionOptions> options)
{
    private sealed record Tags(List<TagModel>? Models);
    private sealed record TagModel(string Name);

    public async Task<List<string>?> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            var http = factory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            var tags = await http.GetFromJsonAsync<Tags>(new Uri(new Uri(options.Value.OllamaBaseUrl), "/api/tags"),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), ct);
            return tags?.Models?.Select(m => m.Name).ToList() ?? [];
        }
        catch
        {
            return null;
        }
    }
}
