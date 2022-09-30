using DragaliaAPI;
using DragaliaAPI.Models.Database;
using DragaliaAPI.Models.Dragalia.Responses;
using DragaliaAPI.Services;
using MessagePack.Resolvers;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager configuration = builder.Configuration;

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddMvc().AddMvcOptions(option =>
{
    // Must use ContractlessResolver because the DefaultResolver doesn't like serializing the generic BaseResponse<T> 
    // record, even when it is properly annotated with the MessagePackObject decorator.
    option.OutputFormatters.Add(new DragaliaAPI.CustomMessagePackOutputFormatter(ContractlessStandardResolver.Options));
    option.InputFormatters.Add(new DragaliaAPI.CustomMessagePackInputFormatter(ContractlessStandardResolver.Options));
});

builder.Services.AddDbContext<ApiContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlConnection")), ServiceLifetime.Transient, ServiceLifetime.Transient);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
    options.InstanceName = "RedisInstance";
});

builder.Services
    .AddScoped<ISessionService, SessionService>()
    .AddScoped<IDeviceAccountService, DeviceAccountService>()
    .AddScoped<IApiRepository, ApiRepository>();

WebApplication app = builder.Build();

using (IServiceScope serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    ILogger<Program> logger = serviceScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade db = serviceScope.ServiceProvider.GetRequiredService<ApiContext>().Database;

    logger.LogInformation("Migrating database...");

    while (!db.CanConnect())
    {
        logger.LogInformation("Database not ready yet; waiting...");
        Thread.Sleep(1000);
    }

    try
    {
        serviceScope.ServiceProvider.GetRequiredService<ApiContext>().Database.Migrate();
        logger.LogInformation("Database migrated successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

//app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        DateTime expires = DateTime.UtcNow + TimeSpan.FromMinutes(30);

        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Expires"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss") + " GMT";
        context.Response.Headers["Cache-Control"] = "max-age=0, no-cache, no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        
        return Task.CompletedTask;
    });

    await next();
});

app.UseMiddleware<DeChunkerMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }