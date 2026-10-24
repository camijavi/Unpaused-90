using NETUnpaused90.Endpoints;
using NETUnpaused90.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<AiService>();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();

app.UseAuthorization();

// Friendly root route so clicking http://localhost:5165 in the terminal works immediately
app.MapGet("/", () => Results.Ok(new
{
    service = "Unpaused 90' API",
    status = "Running",
    endpoints = new[]
    {
        "GET  /api/v1/matches",
        "GET  /api/v1/matches/PL2027-001",
        "GET  /api/v1/matches/PL2027-001/timeline",
        "GET  /api/v1/matches/PL2027-001/insights",
        "POST /api/v1/catchup",
        "POST /api/v1/personalized-summary",
        "POST /api/v1/explain"
    }
}));

app.MapMatchEndpoints();
app.MapAiEndpoints();
app.MapControllers();

app.Run();
