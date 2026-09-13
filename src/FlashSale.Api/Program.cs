using System.Text.Json.Serialization;
using FlashSale.Api.Endpoints.Orders;
using FlashSale.Api.Endpoints.Products;
using FlashSale.Api.Errors;
using FlashSale.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddFlashSale(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapOrderEndpoints();
app.MapProductEndpoints();
app.Run();

public partial class Program;
