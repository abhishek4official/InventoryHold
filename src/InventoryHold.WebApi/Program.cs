using System.Text.Json.Serialization;
using InventoryHold.Domain.Services;
using InventoryHold.Infrastructure;
using InventoryHold.WebApi.BackgroundServices;
using InventoryHold.WebApi.ErrorHandling;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<HoldOptions>()
    .Bind(builder.Configuration.GetSection(HoldOptions.SectionName))
    .Validate(o => o.DefaultDurationMinutes > 0, "Holds:DefaultDurationMinutes must be positive.")
    .Validate(o => o.SweepIntervalSeconds > 0, "Holds:SweepIntervalSeconds must be positive.")
    .Validate(o => o.SweepBatchSize > 0, "Holds:SweepBatchSize must be positive.")
    .Validate(o => o.ListLimit > 0, "Holds:ListLimit must be positive.")
    .Validate(o => o.ShippingCutoffHourUtc is >= 0 and <= 23, "Holds:ShippingCutoffHourUtc must be 0 to 23.")
    .ValidateOnStart();
builder.Services.AddScoped<IHoldService, HoldService>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HoldExpirySweeper>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
