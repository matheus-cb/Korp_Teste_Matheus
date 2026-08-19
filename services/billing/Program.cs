using System.Threading.RateLimiting;
using Billing.Api.Api;
using Billing.Api.Application;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var internalToken = builder.Configuration[$"{InternalAuthOptions.Section}:Token"];
var allowUnauthenticatedInternalCalls = builder.Configuration.GetValue<bool>(
    $"{InternalAuthOptions.Section}:AllowUnauthenticated");
if (string.IsNullOrWhiteSpace(internalToken) && !allowUnauthenticatedInternalCalls)
{
    throw new InvalidOperationException(
        "InternalAuth:Token is required unless InternalAuth:AllowUnauthenticated is explicitly enabled for local development or tests.");
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<InventoryOptions>(builder.Configuration.GetSection(InventoryOptions.Section));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.Section));
builder.Services.Configure<InternalAuthOptions>(builder.Configuration.GetSection(InternalAuthOptions.Section));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 6 * 1024 * 1024;
    options.ValueLengthLimit = 2_100;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<InternalAuthHandler>();

var connectionString = builder.Configuration.GetConnectionString("Billing")
    ?? throw new InvalidOperationException("ConnectionStrings:Billing is required.");
builder.Services.AddDbContextFactory<BillingDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<BillingDbContext>>().CreateDbContext());
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "billing-database", tags: ["ready"]);

builder.Services.AddHttpClient<IInventoryClient, InventoryClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<InventoryOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
}).AddHttpMessageHandler<InternalAuthHandler>();
builder.Services.AddHttpClient<IInvoiceDraftAiClient, OpenAiResponsesClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<IInventoryToolSessionFactory, McpInventoryToolSessionFactory>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ClosureCoordinator>();
builder.Services.AddScoped<AiDraftService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<InvoiceImportService>();
builder.Services.AddScoped<ProposedActionService>();
builder.Services.AddSingleton<IInvoicePdfGenerator, InvoicePdfGenerator>();
builder.Services.AddHostedService<ClosureReconciliationWorker>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AiEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "RATE_LIMIT_EXCEEDED",
            Detail = "O limite de solicitações do copiloto foi atingido. Tente novamente em instantes.",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "RATE_LIMIT_EXCEEDED";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };
});

var app = builder.Build();
QuestPDF.Settings.License = LicenseType.Community;

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready");
app.UseMiddleware<UserContextMiddleware>();
app.MapAuthEndpoints();
app.MapInvoiceEndpoints();
app.MapImportEndpoints();
app.MapProposedActionEndpoints();
app.MapAiEndpoints();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
    await ApplyMigrationsAsync(app.Services);

await app.RunAsync();

/// <summary>
/// Usuários de demonstração. As credenciais são públicas de propósito: este é
/// um ambiente demonstrativo e o avaliador precisa conseguir entrar. Num
/// sistema real viriam de provisionamento, nunca do código.
/// </summary>
static async Task SeedUsersAsync(BillingDbContext db)
{
    if (await db.Users.AnyAsync())
    {
        return;
    }

    var now = TimeProvider.System.GetUtcNow();
    db.Users.Add(AppUser.Create("operador", "Operador de Faturamento", "notaflow123", now));
    db.Users.Add(AppUser.Create("supervisor", "Supervisor de Estoque", "notaflow123", now));
    await db.SaveChangesAsync();
}

static async Task ApplyMigrationsAsync(IServiceProvider services)
{
    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BillingDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            await SeedUsersAsync(db);
            return;
        }
        catch when (attempt < 10)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempt * 2)));
        }
    }
}

public partial class Program;
