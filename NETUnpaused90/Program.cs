using Azure.Core;
using NETUnpaused90.Endpoints;
using NETUnpaused90.Services;

var builder = WebApplication.CreateBuilder(args);

// Shared Singleton TokenCredential so all Azure services (Blob, OpenAI, Speech) share and cache Entra ID tokens
builder.Services.AddSingleton<TokenCredential, AppTokenCredential>();
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<AiService>();
builder.Services.AddSingleton<SpeechService>();
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
        "GET  /api/v1/matches/PL2027-001/possession",
        "GET  /api/v1/matches/PL2027-001/timeline",
        "GET  /api/v1/matches/PL2027-001/insights",
        "POST /api/v1/catchup",
        "POST /api/v1/personalized-summary",
        "POST /api/v1/explain",
        "POST /api/v1/audio"
    }
}));

app.MapMatchEndpoints();
app.MapAiEndpoints();
app.MapAudioEndpoints();
app.MapControllers();

app.Run();
