using API_NAA.EndPoints;
using API_NAA.Extensions;
using API_NAA.Interfaces;
using API_NAA.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IDbServices, DbServices>();
builder.Services.AddTransient<IEndpoint, NaaEndPoints>();
builder.Services.AddHealthChecks();
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

app.UseHealthChecks("/healthz");
app.MapEndpoints();

app.Run();
