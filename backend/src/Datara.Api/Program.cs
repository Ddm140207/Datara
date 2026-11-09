using System.Text.Json.Serialization;
using Datara.Api.Controllers;
using Datara.Api.Infrastructure;
using Datara.Application.Analysis;
using Datara.Application.Common;
using Datara.Application.Datasets.Abstractions;
using Datara.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = true;
    })
    .AddJsonOptions(options =>
    {
        // Mirror JsonDefaults so the same shape is used for MVC and for the
        // middleware-level problem details writer.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonDefaults.Options.PropertyNamingPolicy;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonDefaults.Options.DefaultIgnoreCondition;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = JsonDefaults.Options.PropertyNameCaseInsensitive;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Datara API",
        Version = "v1",
        Description =
            "Dataset upload, profiling, data quality, ML readiness scoring and "
            + "Datara AI. Datara is single-tenant in this release.",
    });

    var xml = Path.Combine(AppContext.BaseDirectory, "Datara.Api.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<OwnerContext>();
builder.Services.AddDataraInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(
            builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173" })
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Datara API v1");
        options.DocumentTitle = "Datara API";
    });
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors("frontend");

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Apply migrations on startup so a fresh database is usable with one command.
// Guarded by configuration so a managed environment can disable it.
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await app.Services.MigrateAndSeedAsync();
}

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
