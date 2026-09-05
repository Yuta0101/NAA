using API_NAA_DDM.Configs;
using API_NAA_DDM.EndPoints;
using API_NAA_DDM.Extensions;
using API_NAA_DDM.Interfaces;
using API_NAA_DDM.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
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

builder.Services.AddTransient<INaaEndPoint, NaaEndPoints>();

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

app.UseHealthChecks("/healthz");
app.MapEndpoints();

app.Run();
