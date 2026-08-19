using Inventory.Api.Application;
using Inventory.Api.Errors;
using Inventory.Api.Infrastructure;
using Inventory.Api.Mcp;
using Inventory.Api.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

var internalToken = builder.Configuration[$"{InternalAuthOptions.SectionName}:Token"];
var allowUnauthenticatedInternalRoutes = builder.Configuration.GetValue<bool>(
    $"{InternalAuthOptions.SectionName}:AllowUnauthenticated");
if (string.IsNullOrWhiteSpace(internalToken) && !allowUnauthenticatedInternalRoutes)
{
    throw new InvalidOperationException(
        "InternalAuth:Token is required unless InternalAuth:AllowUnauthenticated is explicitly enabled for local development or tests.");
}

var connectionString = builder.Configuration.GetConnectionString("InventoryDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:InventoryDatabase must be configured.");

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName)));

builder.Services.AddScoped<IProductCatalog, ProductCatalog>();
// Mesma instancia do escopo, exposta pelo contrato sem escrita. As tools MCP
// resolvem por aqui e nao alcancam CreateAsync nem por engano.
builder.Services.AddScoped<IReadOnlyProductCatalog>(sp => sp.GetRequiredService<IProductCatalog>());
builder.Services.AddScoped<IStockReconciliation, StockReconciliation>();
builder.Services.AddScoped<IStockDebitService, StockDebitService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<InternalAuthOptions>(
    builder.Configuration.GetSection(InternalAuthOptions.SectionName));

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://httpstatuses.com/400",
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["code"] = ErrorCodes.ValidationError;
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem);
        };
    });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>("inventory-database");

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "notaflow-inventory",
            Version = "1.0.0",
            Description = "Read-only product catalog and availability tools for NotaFlow."
        };
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<InventoryMcpTools>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseMiddleware<InternalServiceAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health");
app.MapMcp("/mcp");

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await database.Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
