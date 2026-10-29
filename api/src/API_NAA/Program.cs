using API_NAA.EndPoints;
using API_NAA.Extensions;
using API_NAA.Interfaces;
using API_NAA.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var localSettingsPath = ResolveLocalSettingsPath(
    builder.Environment.ContentRootPath,
    AppContext.BaseDirectory,
    Environment.CurrentDirectory);
if (localSettingsPath is not null)
{
    builder.Configuration.AddIniFile(
        localSettingsPath,
        optional: false,
        reloadOnChange: false);
}

builder.Services.AddScoped<IDbServices, DbServices>();
builder.Services.AddScoped<ICardServices, CardServices>();
builder.Services.AddTransient<IEndpoint, NaaEndPoints>();
builder.Services.AddTransient<IEndpoint, CardEndPoints>();
builder.Services.AddHealthChecks();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "NAA Database API v1");
        options.DocumentTitle = "NAA Database API 測試";
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseHealthChecks("/healthz");
app.MapEndpoints();

app.Run();

static string? ResolveLocalSettingsPath(params string[] searchRoots)
{
    var configuredPath = Environment.GetEnvironmentVariable("NAA_LOCAL_SETTINGS_FILE");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        var fullPath = Path.GetFullPath(configuredPath);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException(
                $"NAA_LOCAL_SETTINGS_FILE does not exist: {fullPath}", fullPath);
    }

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var searchRoot in searchRoots)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(searchRoot));
             directory is not null && visited.Add(directory.FullName);
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "NAA.local.txt");
            if (File.Exists(candidate))
                return candidate;
        }
    }

    return null;
}
