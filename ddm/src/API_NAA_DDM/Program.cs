using API_NAA_DDM.Configs;
using API_NAA_DDM.EndPoints;
using API_NAA_DDM.Extensions;
using API_NAA_DDM.Interfaces;
using API_NAA_DDM.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// User Secrets are preferred. This optional Git-ignored INI-style text file
// keeps local development usable when the user-secrets directory is unavailable.
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

builder.Services.AddControllers();
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
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

var agentOptions = builder.Configuration
    .GetSection(AgentOptions.SectionName)
    .Get<AgentOptions>() ?? new AgentOptions();

if (agentOptions.Provider.Equals("AgentBuilder", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(agentOptions.BaseUrl))
        throw new InvalidOperationException("Agent:BaseUrl is required for AgentBuilder.");

    if (string.IsNullOrWhiteSpace(agentOptions.ApiKey))
        throw new InvalidOperationException("Agent:ApiKey is required for AgentBuilder.");

    var agentBaseUri = new Uri(agentOptions.BaseUrl.TrimEnd('/') + "/");
    if (agentBaseUri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("AgentBuilder must use HTTPS.");

    builder.Services.AddHttpClient<IAgentService, AgentBuilderService>(client =>
    {
        client.BaseAddress = agentBaseUri;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(agentOptions.TimeoutSeconds, 10, 300));
    });
}
else
{
    builder.Services.AddSingleton<IAgentService, LocalAgentService>();
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var naaServiceUri = new Uri(NaaConfig.NaaServiceDomain);
if (naaServiceUri.Scheme != Uri.UriSchemeHttps)
    throw new InvalidOperationException("NAA API must use HTTPS.");

builder.Services.AddHttpClient<INaaHttpServices, NaaHttpServices>(client =>
{
    client.BaseAddress = naaServiceUri;
});
builder.Services.AddHttpClient<ICardHttpServices, CardHttpServices>(client =>
{
    client.BaseAddress = naaServiceUri;
});

builder.Services.AddTransient<INaaEndPoint, NaaEndPoints>();
builder.Services.AddTransient<INaaEndPoint, CardEndPoints>();

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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "NAA DDM API v1");
        options.DocumentTitle = "NAA DDM API 測試";
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
