using DockerApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(context.Configuration).CreateLogger();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}

// Map the endpoint
app.MapRecreateEndpoint();
app.MapRestartEndpoint();


// Start the server.
app.Run();