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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();

app.UseAuthorization();

app.MapMatchEndpoints();
app.MapAiEndpoints();
app.MapAudioEndpoints();
app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();
