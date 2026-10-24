using NETUnpaused90.Models;
using NETUnpaused90.Services;

namespace NETUnpaused90.Endpoints;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("AI Intelligence");

        // POST /api/v1/catchup — Hero endpoint: explains what the fan missed between lastViewedMinute and currentMinute
        group.MapPost("/catchup", async (
            CatchUpRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.GenerateCatchUpAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' not found." });
        })
        .WithName("GenerateCatchUp");

        // POST /api/v1/personalized-summary — Generates full or halftime recap tailored to mode & favorite team
        group.MapPost("/personalized-summary", async (
            PersonalizedSummaryRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.GeneratePersonalizedSummaryAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' not found." });
        })
        .WithName("GeneratePersonalizedSummary");

        // POST /api/v1/explain — Expandable "Why It Matters" explanation for a specific timeline event
        group.MapPost("/explain", async (
            ExplainEventRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.ExplainEventAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' or Event '{request.EventId}' not found." });
        })
        .WithName("ExplainEvent");

        // GET /test — Interactive browser test page for POST AI & Audio endpoints
        app.MapGet("/test", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <title>Unpaused 90' — AI & Audio Endpoint Tester</title>
  <style>
    body { font-family: system-ui, sans-serif; background: #0f172a; color: #f8fafc; max-width: 850px; margin: 2rem auto; padding: 0 1rem; }
    h1 { color: #38bdf8; }
    .card { background: #1e293b; border-radius: 10px; padding: 1.25rem; margin-bottom: 1.25rem; border: 1px solid #334155; }
    button { background: #0284c7; color: white; border: none; padding: 0.6rem 1.2rem; border-radius: 6px; cursor: pointer; font-weight: 600; margin-right: 0.5rem; margin-top: 0.5rem; }
    button:hover { background: #0369a1; }
    button.audio-btn { background: #10b981; }
    button.audio-btn:hover { background: #059669; }
    pre { background: #090d16; padding: 1rem; border-radius: 8px; overflow-x: auto; color: #a7f3d0; white-space: pre-wrap; }
    select, input, textarea { background: #0f172a; color: white; border: 1px solid #475569; padding: 0.4rem 0.6rem; border-radius: 6px; margin-right: 0.5rem; }
    textarea { width: 96%; height: 70px; margin-top: 0.5rem; font-family: inherit; }
  </style>
</head>
<body>
  <h1>⚽ Unpaused 90' — AI & Audio Endpoint Tester</h1>
  <div class="card">
    <h3>1. Test Hero Endpoint: <code>POST /api/v1/catchup</code></h3>
    <p>Simulates your dad stopping at minute <input id="lastMin" type="number" value="30" style="width:55px"/> and returning at minute <input id="currMin" type="number" value="60" style="width:55px"/></p>
    <label>Mode:
      <select id="mode">
        <option value="casual">Casual Fan</option>
        <option value="analyst">Analyst</option>
        <option value="kid">Kid Mode</option>
      </select>
    </label>
    <label>Favorite Team:
      <select id="team">
        <option value="Liverpool">Liverpool</option>
        <option value="Arsenal">Arsenal</option>
        <option value="Neutral">Neutral</option>
      </select>
    </label>
    <label>Language:
      <select id="lang">
        <option value="en">English</option>
        <option value="es">Spanish</option>
      </select>
    </label>
    <br/>
    <button onclick="testCatchUp()">🚀 Run Catch Me Up (GPT-4o)</button>
    <button onclick="testSummary()">📊 Test POST /api/v1/personalized-summary</button>
    <button onclick="testExplain()">💡 Test POST /api/v1/explain (Salah 54' Goal)</button>
  </div>

  <div class="card">
    <h3>2. Test Audio Endpoint: <code>POST /api/v1/audio</code> (en-US-AdamMultilingualNeural)</h3>
    <p style="margin:0 0 0.4rem 0; font-size:0.9rem; color:#94a3b8;">Automatically fills with the latest AI response above, or type any English/Spanish text:</p>
    <textarea id="ttsText">Welcome back to Unpaused 90! While you were at the gym, Arsenal broke the deadlock in the 31st minute through Bukayo Saka, before Mohamed Salah equalized for Liverpool in the 54th minute!</textarea>
    <br/>
    <button class="audio-btn" onclick="testAudio()">🎧 Generate & Play Audio (AdamMultilingualNeural)</button>
    <div style="margin-top: 0.85rem;">
      <audio id="audioPlayer" controls style="width: 100%; display: none;"></audio>
    </div>
  </div>

  <div class="card">
    <h3>Response Output:</h3>
    <pre id="output">Click any button above to test your .NET Minimal API...</pre>
  </div>

  <script>
    const out = document.getElementById('output');
    const ttsInput = document.getElementById('ttsText');
    const audioPlayer = document.getElementById('audioPlayer');

    async function callApi(url, body) {
      out.textContent = '⏳ Calling ' + url + '...';
      try {
        const res = await fetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });
        const text = await res.text();
        try {
          const json = JSON.parse(text);
          out.textContent = JSON.stringify(json, null, 2);

          // Automatically populate the Audio box with the generated AI text!
          if (json.whatHappened) {
            ttsInput.value = `${json.whatHappened} ${json.whyItMatters} ${json.whatToWatchNext}`;
          } else if (json.story) {
            ttsInput.value = json.story;
          } else if (json.explanation) {
            ttsInput.value = `${json.title}. ${json.explanation}`;
          }
        } catch {
          out.textContent = 'Status ' + res.status + ':\n' + text;
        }
      } catch (err) {
        out.textContent = '❌ Error: ' + err;
      }
    }

    function testCatchUp() {
      callApi('/api/v1/catchup', {
        matchId: 'PL2027-001',
        lastViewedMinute: parseInt(document.getElementById('lastMin').value),
        currentMinute: parseInt(document.getElementById('currMin').value),
        viewMode: document.getElementById('mode').value,
        favoriteTeam: document.getElementById('team').value,
        language: document.getElementById('lang').value
      });
    }

    function testSummary() {
      callApi('/api/v1/personalized-summary', {
        matchId: 'PL2027-001',
        mode: document.getElementById('mode').value,
        favoriteTeam: document.getElementById('team').value,
        length: '1m',
        language: document.getElementById('lang').value
      });
    }

    function testExplain() {
      callApi('/api/v1/explain', {
        matchId: 'PL2027-001',
        eventId: 'EV005',
        viewMode: document.getElementById('mode').value,
        favoriteTeam: document.getElementById('team').value,
        language: document.getElementById('lang').value
      });
    }

    async function testAudio() {
      out.textContent = '⏳ Synthesizing speech with Azure AI Speech (en-US-AdamMultilingualNeural)...';
      try {
        const res = await fetch('/api/v1/audio', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            text: ttsInput.value,
            voice: 'en-US-AdamMultilingualNeural',
            language: document.getElementById('lang').value
          })
        });
        const text = await res.text();
        const json = JSON.parse(text);
        if (json.audioUrl) {
          audioPlayer.src = json.audioUrl;
          audioPlayer.style.display = 'block';
          audioPlayer.play();
          out.textContent = JSON.stringify({
            voice: json.voice,
            language: json.language,
            audioUrl: json.audioUrl.substring(0, 60) + '... [base64 MP3 audio]'
          }, null, 2);
        } else {
          out.textContent = 'Status ' + res.status + ':\n' + text;
        }
      } catch (err) {
        out.textContent = '❌ Error: ' + err;
      }
    }
  </script>
</body>
</html>
""", "text/html"));
    }
}
