using System.Security.Cryptography;
using System.Text;
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
builder.Services.Configure<ClaudeBridgeOptions>(builder.Configuration.GetSection(ClaudeBridgeOptions.Section));
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
// AI:Provider escolhe quem responde. Vazio ou "openai" mantem o comportamento
// anterior; "claude-bridge" usa o Claude Code da VPS atras da ponte local.
// Em qualquer caso a sessao MCP e a validacao de proveniencia ficam no Billing.
if (string.Equals(builder.Configuration["AI:Provider"], "claude-bridge", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ClaudeBridgeClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ClaudeBridgeOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });
    // A mesma instancia atende os dois contratos.
    builder.Services.AddScoped<IInvoiceDraftAiClient>(sp => sp.GetRequiredService<ClaudeBridgeClient>());
    builder.Services.AddScoped<IAssistantClient>(sp => sp.GetRequiredService<ClaudeBridgeClient>());
}
else
{
    builder.Services.AddHttpClient<IInvoiceDraftAiClient, OpenAiResponsesClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });
    // O provedor OpenAI nao implementa a conversa; o assistente responde
    // AI_DISABLED em vez de o endpoint deixar de existir (INV-23).
    builder.Services.AddSingleton<IAssistantClient, UnavailableAssistantClient>();
}

builder.Services.AddSingleton<IInventoryToolSessionFactory, McpInventoryToolSessionFactory>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<CatalogProductService>();
builder.Services.AddScoped<ClosureCoordinator>();
builder.Services.AddScoped<AiDraftService>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<IAssistantLocalTools, AssistantLocalTools>();
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
    // Particionar por SESSAO, nao por IP. Atras de um proxy o IP de conexao e
    // sempre o do proxy, entao todo mundo caia no mesmo balde e cinco chamadas
    // de qualquer pessoa travavam o assistente para todas -- mais facil de
    // acontecer agora que ele conversa, e nao so monta um rascunho.
    //
    // A chave sai do token de sessao, e nao do usuario resolvido, porque o
    // UserContextMiddleware roda depois do limitador. O token vai hasheado: ele
    // e credencial, e chave de particao fica viva em memoria.
    options.AddPolicy(AiEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ParticaoDoChamador(context),
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
app.MapCatalogProductEndpoints();
app.MapImportEndpoints();
app.MapProposedActionEndpoints();
app.MapAiEndpoints();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
    await ApplyMigrationsAsync(app.Services, builder.Configuration.GetValue("Seed:Password", "notaflow123"));

await app.RunAsync();

/// <summary>
/// Usuários de demonstração. A senha padrão é pública de propósito: em ambiente
/// local e no CI o avaliador precisa conseguir entrar. Fora disso ela vem de
/// <c>Seed:Password</c> (variável <c>Seed__Password</c>), porque uma instância
/// alcançável pela internet não pode nascer com credencial conhecida.
/// Num sistema real os usuários viriam de provisionamento, nunca do código.
/// </summary>
static async Task SeedUsersAsync(BillingDbContext db, string seedPassword)
{
    if (await db.Users.AnyAsync())
    {
        return;
    }

    var now = TimeProvider.System.GetUtcNow();
    db.Users.Add(AppUser.Create("operador", "Operador de Faturamento", seedPassword, now));
    db.Users.Add(AppUser.Create("supervisor", "Supervisor de Estoque", seedPassword, now));
    await db.SaveChangesAsync();
}

static async Task ApplyMigrationsAsync(IServiceProvider services, string seedPassword)
{
    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BillingDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            await SeedUsersAsync(db, seedPassword);
            return;
        }
        catch when (attempt < 10)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempt * 2)));
        }
    }
}

/// <summary>
/// Chave de particao do limitador: sessao quando houver, IP quando anonimo.
/// </summary>
static string ParticaoDoChamador(HttpContext context)
{
    var cabecalho = context.Request.Headers.Authorization.ToString();
    const string prefixo = "Bearer ";
    if (cabecalho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
    {
        var token = cabecalho[prefixo.Length..].Trim();
        if (token.Length > 0)
        {
            var resumo = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return string.Concat("s:", Convert.ToHexString(resumo.AsSpan(0, 8)));
        }
    }

    // Sem sessao sobra o IP. Nao e ideal atras do proxy, mas rota autenticada
    // so chega aqui sem token quando a chamada ja vai ser recusada adiante.
    return string.Concat("ip:", context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");
}

public partial class Program;
