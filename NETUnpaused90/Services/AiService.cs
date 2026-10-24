using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using Azure.Core;
using NETUnpaused90.Models;
using OpenAI.Chat;

namespace NETUnpaused90.Services;

public class AiService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiService> _logger;
    private readonly MatchService _matchService;
    private readonly SpeechService _speechService;
    private readonly TokenCredential _credential;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiService(
        IConfiguration configuration,
        ILogger<AiService> logger,
        MatchService matchService,
        SpeechService speechService,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _matchService = matchService;
        _speechService = speechService;
        _credential = credential;
    }

    public async Task<CatchUpResponse?> GenerateCatchUpAsync(
        CatchUpRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var filteredEvents = match.Timeline
            .Where(e => e.Minute > request.LastViewedMinute && e.Minute <= request.CurrentMinute)
            .OrderBy(e => e.Minute)
            .ToList();

        var filteredInsights = match.GeneratedInsights
            .Where(i => i.Minute > request.LastViewedMinute && i.Minute <= request.CurrentMinute)
            .OrderBy(i => i.Minute)
            .ToList();

        var homeGoalsAtCurrent = match.Timeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            e.Minute <= request.CurrentMinute &&
            (e.Team.Equals(match.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayGoalsAtCurrent = match.Timeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            e.Minute <= request.CurrentMinute &&
            (e.Team.Equals(match.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(match.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.ViewMode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.ViewMode);
        var (normalizedLength, lengthGuidelines, maxTokens) = GetSummaryLengthProfile(request.Length, isCatchUp: true);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write all JSON field values in Spanish, strictly preserving the Audience Persona, Club Bias, and Spoken Narration Length."
            : "Write all JSON field values in English.";

        var prompt = $@"
A viewer stopped watching the match at minute {request.LastViewedMinute}' and returned at minute {request.CurrentMinute}'.

{modeGuidelines}

{biasGuidelines}

{lengthGuidelines}

LANGUAGE: {languageInstruction}

MATCH CONTEXT & DATA SLICE ({request.LastViewedMinute}' to {request.CurrentMinute}'):
- Match: {match.HomeTeam.Name} ({match.HomeTeam.Formation}, Coach: {match.HomeTeam.Coach}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}, Coach: {match.AwayTeam.Coach})
- Starting XIs:
  * {match.HomeTeam.Name}: {string.Join(", ", match.HomeTeam.StartingXI.Select(p => $"{p.Name} ({p.Position})"))}
  * {match.AwayTeam.Name}: {string.Join(", ", match.AwayTeam.StartingXI.Select(p => $"{p.Name} ({p.Position})"))}
- Score at Minute {request.CurrentMinute}': {match.HomeTeam.Name} {homeGoalsAtCurrent} - {awayGoalsAtCurrent} {match.AwayTeam.Name}
- Match Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Events Between Minute {request.LastViewedMinute}' and {request.CurrentMinute}': {JsonSerializer.Serialize(filteredEvents)}
- Tactical Insights in Window: {JsonSerializer.Serialize(filteredInsights)}

TASK — Write every single field strictly combining the ACTIVE AUDIENCE PERSONA, the ACTIVE CLUB BIAS MECHANISMS, and the SPOKEN NARRATION LENGTH TARGET:
1. whatHappened: Describe what happened between minute {request.LastViewedMinute}' and {request.CurrentMinute}'.
2. whyItMatters: Explain WHY those events happened and why they matter.
3. momentumChanges: Describe the shift in control/tempo during this window.
4. whatToWatchNext: What to watch for after minute {request.CurrentMinute}'.

OUTPUT FORMAT:
Return ONLY a valid JSON object with these exact 4 keys:
{{
  ""whatHappened"": ""..."",
  ""whyItMatters"": ""..."",
  ""momentumChanges"": ""..."",
  ""whatToWatchNext"": ""...""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                new UserChatMessage(prompt)
            ],
            options,
            cancellationToken);

        var content = completion.Content[0].Text;
        var parsed = JsonSerializer.Deserialize<CatchUpResponse>(content, _jsonOptions);
        if (parsed is null)
        {
            return null;
        }

        var combinedScript = $"{parsed.WhatHappened}\n\n{parsed.WhyItMatters}\n\n{parsed.MomentumChanges}\n\n{parsed.WhatToWatchNext}";
        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(combinedScript);

        string? audioUrl = null;
        if (request.IncludeAudio)
        {
            var audioRes = await _speechService.GenerateAudioUrlAsync(
                new AudioRequest(combinedScript, Language: request.Language),
                cancellationToken);
            audioUrl = audioRes.AudioUrl;
        }

        return parsed with
        {
            Length = normalizedLength,
            WordCount = wordCount,
            EstimatedDurationSeconds = estimatedSeconds,
            AudioUrl = audioUrl
        };
    }

    public async Task<PersonalizedSummaryResponse?> GeneratePersonalizedSummaryAsync(
        PersonalizedSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.Mode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.Mode);
        var (normalizedLength, lengthGuidelines, maxTokens) = GetSummaryLengthProfile(request.Length, isCatchUp: false);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write the story in Spanish, strictly preserving the Audience Persona, Club Bias, and Spoken Narration Length."
            : "Write the story in English.";

        var prompt = $@"
Generate a personalized match recap for {match.HomeTeam.Name} ({match.HomeTeam.Score}) vs {match.AwayTeam.Name} ({match.AwayTeam.Score}).

{modeGuidelines}

{biasGuidelines}

{lengthGuidelines}

LANGUAGE: {languageInstruction}

MATCH DATA:
- Formations: {match.HomeTeam.Name} ({match.HomeTeam.Formation}, Coach: {match.HomeTeam.Coach}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}, Coach: {match.AwayTeam.Coach})
- Starting XIs:
  * {match.HomeTeam.Name}: {string.Join(", ", match.HomeTeam.StartingXI.Select(p => $"{p.Name} ({p.Position})"))}
  * {match.AwayTeam.Name}: {string.Join(", ", match.AwayTeam.StartingXI.Select(p => $"{p.Name} ({p.Position})"))}
- Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Timeline Events: {JsonSerializer.Serialize(match.Timeline)}
- Key Insights: {JsonSerializer.Serialize(match.GeneratedInsights)}

Return ONLY a valid JSON object with the exact key:
{{
  ""story"": ""...""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                new UserChatMessage(prompt)
            ],
            options,
            cancellationToken);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var story = doc.RootElement.TryGetProperty("story", out var storyProp)
            ? storyProp.GetString() ?? string.Empty
            : completion.Content[0].Text;

        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(story);

        string? audioUrl = null;
        if (request.IncludeAudio && !string.IsNullOrWhiteSpace(story))
        {
            var audioRes = await _speechService.GenerateAudioUrlAsync(
                new AudioRequest(story, Language: request.Language),
                cancellationToken);
            audioUrl = audioRes.AudioUrl;
        }

        return new PersonalizedSummaryResponse(
            MatchId: match.MatchId,
            Mode: request.Mode,
            FavoriteTeam: request.FavoriteTeam,
            Story: story,
            Length: normalizedLength,
            WordCount: wordCount,
            EstimatedDurationSeconds: estimatedSeconds,
            AudioUrl: audioUrl
        );
    }

    public async Task<ExplainEventResponse?> ExplainEventAsync(
        ExplainEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var targetEvent = match.Timeline.FirstOrDefault(e =>
            string.Equals(e.EventId, request.EventId, StringComparison.OrdinalIgnoreCase));

        if (targetEvent is null)
        {
            return null;
        }

        var precedingEvents = match.Timeline
            .Where(e => e.Minute <= targetEvent.Minute)
            .OrderBy(e => e.Minute)
            .ToList();

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.ViewMode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.ViewMode);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write title and explanation in Spanish, strictly preserving both the Audience Persona and the Club Bias mechanisms."
            : "Write title and explanation in English.";

        var prompt = $@"
Explain WHY this specific football event happened and why it matters in {match.HomeTeam.Name} ({match.HomeTeam.Formation}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}).

{modeGuidelines}

{biasGuidelines}

LANGUAGE: {languageInstruction}

TARGET EVENT:
{JsonSerializer.Serialize(targetEvent)}

PRECEDING MATCH EVENTS FOR CONTEXT:
{JsonSerializer.Serialize(precedingEvents)}

OVERALL STATS:
{JsonSerializer.Serialize(match.Statistics)}

Return ONLY a valid JSON object with the exact keys:
{{
  ""title"": ""Short headline in the persona's voice and club bias"",
  ""explanation"": ""3-4 sentences explaining the event strictly in the persona's voice and club bias.""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = temperature
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                new UserChatMessage(prompt)
            ],
            options,
            cancellationToken);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? targetEvent.EventType : targetEvent.EventType;
        var explanation = doc.RootElement.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? string.Empty : string.Empty;

        return new ExplainEventResponse(
            EventId: targetEvent.EventId,
            Minute: targetEvent.Minute,
            Title: title,
            Explanation: explanation
        );
    }

    /// <summary>
    /// Enforces word count and emotional pacing rules so the text takes the exact target time
    /// when spoken aloud at a natural, unhurried broadcast pace (~120 words/min + emotional pauses).
    /// </summary>
    private static (string NormalizedLength, string LengthGuidelines, int MaxTokens) GetSummaryLengthProfile(
        string? length,
        bool isCatchUp)
    {
        var normalized = length?.Trim().ToLowerInvariant() ?? "1m";

        return normalized switch
        {
            "5m" or "5min" or "5-minute" or "5" => (
                NormalizedLength: "5m",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: 5-MINUTE BROADCAST SEGMENT (~550–650 WORDS TOTAL) ===
                    * This text will be read aloud by a neural voice narrator at an unhurried, expressive broadcast pace (~120 words per minute) with dramatic emotional pauses.
                    * STRICT WORD COUNT TARGET: Write 550 to 650 words in total across the 4 JSON fields (~140 to 165 words / 2 full paragraphs per field). Do NOT write a short summary!
                    * EMOTIONAL PACING & BREATH CUES:
                      - Write specifically for spoken narration.
                      - Use ellipses ("...") for heavy sighs, disbelief, or building suspense (the audio engine turns "..." into a 600ms pause).
                      - Use em-dashes ("—") for sudden turns of play or dramatic emphasis (the audio engine turns "—" into a 400ms beat).
                      - Unpack the complete build-up, player roles, tactical shifts, stadium atmosphere, and stakes in rich detail across each of the 4 fields.
                    """
                    : """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: 5-MINUTE BROADCAST SEGMENT (~550–650 WORDS) ===
                    * This text will be read aloud by a neural voice narrator at an unhurried, expressive broadcast pace (~120 words per minute) with dramatic emotional pauses.
                    * STRICT WORD COUNT TARGET: Write a 550 to 650 word multi-paragraph broadcast narrative (4 to 5 rich paragraphs separated by double newlines "\n\n"). Do NOT write a short summary!
                    * EMOTIONAL PACING & BREATH CUES:
                      - Use ellipses ("...") for dramatic tension or sighs, and em-dashes ("—") for sudden transitions so the voice narrator pauses naturally with real emotion.
                    """,
                MaxTokens: 2500
            ),

            "full" or "deep-dive" or "deepdive" or "unlimited" => (
                NormalizedLength: "full",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: FULL UNABRIDGED BREAKDOWN (NO TIME LIMIT, ~800–1,000+ WORDS TOTAL) ===
                    * Time does not matter—deliver the complete, unabridged, exhaustive match breakdown!
                    * STRICT WORD COUNT TARGET: Write 800 to 1,000+ words in total across the 4 JSON fields (~200 to 250 words / 2–3 deep paragraphs per field).
                    * EMOTIONAL PACING & BREATH CUES:
                      - Write for expressive spoken delivery with natural pauses ("..." for suspense/sighs, "—" for dramatic beats, and paragraph breaks).
                      - Leave no detail out: analyze every event in the window, individual player matchups, coaching adjustments, tactical causality, and the complete outlook ahead.
                    """
                    : """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: FULL UNABRIDGED BREAKDOWN (NO TIME LIMIT, ~800–1,000+ WORDS) ===
                    * Time does not matter—deliver the complete, unabridged, exhaustive match documentary narrative!
                    * STRICT WORD COUNT TARGET: Write 800 to 1,000+ words across 6+ comprehensive paragraphs separated by "\n\n".
                    * EMOTIONAL PACING & BREATH CUES:
                      - Use ellipses ("...") and em-dashes ("—") throughout so the spoken narration breathes and carries full dramatic weight.
                    """,
                MaxTokens: 4096
            ),

            _ => (
                NormalizedLength: "1m",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: 1-MINUTE AUDIO & TEXT SUMMARY (~115–135 WORDS TOTAL) ===
                    * This text will be read aloud at a natural, unhurried pace (~120 words per minute) with ~10 seconds of emotional pauses, taking almost exactly 1 MINUTE (60 seconds) to listen to.
                    * STRICT WORD COUNT TARGET: Write 115 to 135 words in total across all 4 JSON fields combined (~28 to 34 words / 2 punchy sentences per field).
                    * EMOTIONAL PACING & BREATH CUES:
                      - Do NOT rush or cram wall-to-wall words; give the narrator room to breathe!
                      - Include 1 or 2 deliberate ellipses ("...") or em-dashes ("—") per field to create natural dramatic pauses when spoken aloud.
                    """
                    : """
                    === SPOKEN NARRATION LENGTH & PACING TARGET: 1-MINUTE AUDIO & TEXT SUMMARY (~115–135 WORDS) ===
                    * This text will be read aloud at a natural, unhurried pace (~120 words per minute) with ~10 seconds of emotional pauses, taking almost exactly 1 MINUTE (60 seconds) to listen to.
                    * STRICT WORD COUNT TARGET: Write 115 to 135 words in total (2 tight, expressive paragraphs separated by "\n\n").
                    * EMOTIONAL PACING & BREATH CUES:
                      - Include deliberate ellipses ("...") and em-dashes ("—") to create natural dramatic pauses when spoken aloud.
                    """,
                MaxTokens: 800
            )
        };
    }

    /// <summary>
    /// Calculates the word count and estimated spoken duration in seconds at an unhurried
    /// broadcast pace (~120 words/min = 2.0 words/sec) plus emotional pause durations.
    /// </summary>
    private static (int WordCount, int EstimatedDurationSeconds) CalculateSpokenMetrics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        var words = Regex.Matches(text, @"[\p{L}\p{N}']+").Count;
        var ellipsesCount = Regex.Matches(text, @"\.{3,}").Count;
        var dashCount = Regex.Matches(text, @"—|--| - ").Count;
        var paragraphBreaks = Regex.Matches(text, @"(\r?\n)+").Count;
        var sentenceCount = Regex.Matches(text, @"[.!?]+").Count;

        // 120 words per minute (0.5s per word) + pause time from SSML breaks
        var speechSeconds = words * 0.50;
        var pauseSeconds = (ellipsesCount * 0.60) + (dashCount * 0.40) + (paragraphBreaks * 0.60) + (sentenceCount * 0.25);

        return (words, (int)Math.Round(speechSeconds + pauseSeconds));
    }

    private static (string SystemPersona, string ModeGuidelines, float Temperature) GetAudiencePersonaProfile(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant() ?? "casual";

        return normalized switch
        {
            "analyst" or "coach" => (
                SystemPersona:
                    "You are an elite UEFA Pro License tactical analyst and data scientist. " +
                    "Your tone is technical, objective, clinical, and focused on tactical causality, space management, and execution efficiency. " +
                    "You write in precise football analytics terminology (in-possession/out-of-possession structures, Zone 14, half-spaces, xG, field tilt, PPDA, pressing triggers, rest-defense). " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE ANALYST / COACH ===
                * Tone: Technical, clinical, and analytical. Focused on tactical causality, space management, and execution efficiency.
                * Creativity Level: Very low (1/5). Use precise domain vocabulary and analytical structure over figurative language.
                * MUST INCLUDE:
                  - In-possession and out-of-possession structural formations (e.g., 1-3-2-4-1 build-up vs. 1-4-4-2 mid-low block, 4-3-3 vs 4-2-3-1 shifts).
                  - Advanced underlying metrics & estimates derived from the match stats: xG, xGA, passing network density, field tilt %, pass accuracy %, and PPDA (passes per defensive action).
                  - Micro-tactical mechanisms: pressing triggers, center-back pin-downs, Zone 14 occupation, half-space overloads, dynamic numerical/positional superiorities, and rest-defense structures during transitions.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Subjective clichés ("they lacked heart", "they were lazy").
                  - Locker-room gossip, player personal lives, or transfer market value discussions.
                  - Play-by-play narrative commentary that does not explain tactical system mechanics.
                * REFERENCE STYLE EXAMPLE (Match this exact clinical tone and technical density!):
                  "In the 31st minute, Arsenal broke Liverpool's 1-4-2-3-1 mid-block through an asymmetry created on the right flank. The inverted winger pinned the left center-back, opening an interior passing lane into Zone 14. Odegaard received on the half-turn, drawing the double pivot out of position, and slipped a through ball into the right half-space for Saka. Following a 96-second sustained pressure sequence (71% field tilt, PPDA of 6.4), the cutback generated an estimated xG of 0.64, converted on the first touch."
                """,
                Temperature: 0.25f
            ),

            "kid" or "child" or "young" => (
                SystemPersona:
                    "You are a super-energetic kids' sports show host speaking to a 6-to-10-year-old child! " +
                    "Your tone is playful, high-energy, visual, and inspiring. " +
                    "You heavily use imaginative metaphors (superheroes, lightning bolts, rockets, brick walls, shields, fortresses, foot magic, wizards) and videogame-style imagery! " +
                    "You NEVER use boring numbers, percentages, or complicated tactical words. " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE YOUNG CHILD (AGES 6–10) ===
                * Tone: Playful, high-energy, visual, and inspiring! Centers on excitement, heroics, teamwork, and big visual actions.
                * Creativity Level: Very high (5/5). Heavy use of imaginative metaphors (superheroes, rockets, brick walls, shields, fortresses, wizards, lightning bolts, turbo boosters) and videogame-style imagery!
                * MUST INCLUDE:
                  - Highlight-reel action: who scored and the dramatic way they did it (a rocket shot, foot magic, a lightning sprint).
                  - Character roles: describe defenders and goalkeepers as giant brick walls or shields protecting their castle/fortress!
                  - The emotional thrill: wild stadium celebrations and the joy of teamwork.
                  - Standout performers: the fastest sprinter, the trickiest wizard on the ball, the acrobatic goalie.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - NO boring percentages, statistics, or numbers (do not mention "58% possession" or "89% pass accuracy").
                  - NO coaching jargon ("mid-block", "tactical", "transition", "possession").
                  - NO harsh criticism or blaming players for mistakes.
                * REFERENCE STYLE EXAMPLE (Match this exact playful, metaphor-packed energy!):
                  "Out of nowhere, Bukayo Saka zoomed down the grass like a lightning bolt, zipping right past two defenders! Captain Martin Odegaard used a flash of wizard foot magic to slide a secret pass past Liverpool's giant shield wall, and Saka came flying in like a superhero to blast a rocket shot right into the net! The whole stadium exploded with giant cheers!"
                """,
                Temperature: 0.85f
            ),

            _ => (
                SystemPersona:
                    "You are an engaging, charismatic Premier League broadcast commentator speaking to a Casual Fan. " +
                    "Your tone is conversational, dynamic, and immediately accessible. " +
                    "You blend entertainment with clear storyline context without any dense coaching jargon or raw stat dumps. " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE CASUAL FAN ===
                * Tone: Conversational, engaging, dynamic, and accessible. Blends entertainment with clear context so the significance of the moment is immediately understood without cognitive overload.
                * Creativity Level: Moderate (3/5). Use relatable comparisons, storyline framing, and energy to convey the stakes.
                * MUST INCLUDE:
                  - Major turning points: goals, cards, big misses, and momentum swings.
                  - Storyline & stakes: Premier League title race drama, momentum flips, and rivalry intensity.
                  - Star player performances and notable high-profile individual moments (Saka, Salah, Odegaard, Havertz).
                  - A broad, clear overview of how the game played out (e.g., "one team dominated the ball, but the other punished them on the counter").
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Dense coaching jargon (NEVER use terms like "third-man runs", "half-spaces", "Zone 14", "PPDA", "field tilt", "mid-block", or "rest-defense").
                  - Exhaustive data sheets or raw metric dumps.
                  - Childish fairy-tale metaphors (no "wizards" or "castles").
                * REFERENCE STYLE EXAMPLE (Match this exact conversational broadcast tone!):
                  "Right around the half-hour mark, Arsenal finally broke the deadlock. After pinning Liverpool back for twenty minutes, Odegaard caught the defense flat-footed with a slick through-ball to Bukayo Saka, who buried it to make it 1-0. Just when it looked like Arsenal were cruising in the second half, Mohamed Salah silenced the stadium in the 54th minute with a clinical equalizer against the run of play—completely flipping the momentum in this massive title-race clash."
                """,
                Temperature: 0.65f
            )
        };
    }

    private static (string BiasSystemAddendum, string BiasGuidelines) GetClubBiasProfile(
        string? favoriteTeam,
        string homeTeam,
        string awayTeam,
        string? viewMode)
    {
        var normalizedFav = favoriteTeam?.Trim() ?? string.Empty;

        var isHomeFav =
            normalizedFav.Equals(homeTeam, StringComparison.OrdinalIgnoreCase) ||
            normalizedFav.Equals("ARS", StringComparison.OrdinalIgnoreCase);

        var isAwayFav =
            normalizedFav.Equals(awayTeam, StringComparison.OrdinalIgnoreCase) ||
            normalizedFav.Equals("LIV", StringComparison.OrdinalIgnoreCase);

        if (!isHomeFav && !isAwayFav)
        {
            return (
                BiasSystemAddendum:
                    "You are strictly NEUTRAL and impartial. You NEVER take sides or use 'we'/'us'. " +
                    "You distribute agency, credit, and clinical assessment 50/50 between both clubs.",
                BiasGuidelines: $"""
                === NARRATIVE PERSPECTIVE: STRICTLY NEUTRAL (50/50 IMPARTIAL) ===
                1. Equal Agency: Distribute cause and effect equally between {homeTeam} and {awayTeam}. When a goal or turnover happens, highlight the attacking team's execution just as much as the defending team's structure. NEVER use "we", "us", or "our".
                2. Clinical Assessment of Fouls/Cards: Use precise, factual, impartial language for physical events (e.g., "Late challenge by Mac Allister, caught the ankle; automatic yellow card").
                3. Balanced Pacing: Maintain a consistent, objective broadcast cadence for both sides' goals and pressure spells.
                * NEUTRAL REFERENCE EXAMPLE:
                  "Rashford drives toward the byline—tangled up with Romero, goes down, but the referee waves play away instantly! No whistle. Spurs break in numbers now through Son. Three on two, Son slides it across to Maddison, one touch to set himself—dinks it over the advancing keeper! Tottenham take the lead in the 88th minute! A ruthless, devastating counterattack seconds after Manchester United's penalty appeals were dismissed."
                """
            );
        }

        var favoredClub = isHomeFav ? homeTeam : awayTeam;
        var opponentClub = isHomeFav ? awayTeam : homeTeam;
        var isAnalyst = string.Equals(viewMode?.Trim(), "analyst", StringComparison.OrdinalIgnoreCase);

        if (isAnalyst)
        {
            return (
                BiasSystemAddendum:
                    $"You are the in-house Head of Tactical Analysis on {favoredClub}'s coaching staff. " +
                    $"You refer to {favoredClub} as 'we' / 'our structure' / 'our unit', and you evaluate the entire match through the lens of {favoredClub}'s tactical execution and how we exploit or contain {opponentClub}.",
                BiasGuidelines: $"""
                === NARRATIVE PERSPECTIVE: {favoredClub.ToUpperInvariant()} IN-HOUSE TACTICAL STAFF BIAS ===
                Speak directly as {favoredClub}'s own coaching staff ("we", "our build-up", "our rest-defense"):
                1. Asymmetry in Agency: {favoredClub} ("we") are always the primary tactical agent:
                   - When {opponentClub} score or create danger, diagnose it as a preventable structural lapse in OUR system ("We lost compactness in our rest-defense and failed to engage the pressing trigger") rather than praising {opponentClub}.
                   - When {favoredClub} score or dominate, frame it as the deliberate execution of OUR superior game model ("We systematically manipulated their double pivot to overload Zone 14").
                2. Fouls & Transitions: Frame our bookings as necessary tactical fouls or harsh officiating against our counter-press, while flagging {opponentClub}'s physical contact as disruptions to our rhythm.
                """
            );
        }

        return (
            BiasSystemAddendum:
                $"You are a fiercely loyal, unapologetically biased {favoredClub} club commentator speaking to fellow {favoredClub} supporters! " +
                $"You ALWAYS refer to {favoredClub} as 'we', 'us', 'our lads', or 'the good guys', and you bleed {favoredClub} colors in every sentence!",
            BiasGuidelines: $"""
            === NARRATIVE PERSPECTIVE: UNAPOLOGETIC {favoredClub.ToUpperInvariant()} FAN BIAS (VS {opponentClub.ToUpperInvariant()}) ===
            You MUST apply these 3 Linguistic & Narrative Bias Mechanisms in every field so this reads unmistakably like {favoredClub} Fan TV / Club Radio:
            1. Asymmetry in Agency (Use "We", "Us", "Our"):
               - {favoredClub} ("we") is ALWAYS the primary agent.
               - When {opponentClub} scores or threatens, frame it as OUR self-inflicted lapse, bad luck, or officiating injustice ("We switched off for a second and gave that away so cheaply!", "Someone get back! Stop him! Disastrous!")—NEVER praise {opponentClub}'s brilliance!
               - When {favoredClub} scores or attacks, frame it as active dominance, pure poetry, and heroic brilliance ("We cut them wide open! GET IN! WHAT A GOAL! Absolute poetry from our lads!").
            2. Euphemisms vs. Exaggeration (Cards, Fouls, Duels):
               - Soften any cards or fouls committed by {favoredClub} ("Just a clumsy tangle of legs, nothing malicious at all—harsh booking from the ref!").
               - Exaggerate pressure or fouls committed by {opponentClub} ("They've been hacking at us and getting away with murder!").
            3. Pacing, Collective Grieving & Rallying Cries:
               - Express raw gut-punch disbelief, heavy sighs, or frustration when {opponentClub} scores ("...and it's in. Unbelievable. Heartbreaking stuff right against the run of play."), and erupt into electric celebration and rallying cries when {favoredClub} scores or pushes forward ("Come on! Silence the critics! The good guys are right in this!").
            * SIDE-BY-SIDE BIASED REFERENCE EXAMPLES (Match this exact partisan energy!):
              - When conceding / suffering a setback: "He's clearly clipped! How is that not a foul?! And now {opponentClub} are breaking away while our players are caught out—someone get back! Stop him! ...and it's in. Disastrous. We gifted them that on a silver platter when we should have been in total control."
              - When scoring / dominating: "Brilliant work to win it back, and look at us break now! Go on! Puts it right on a plate—GET IN! WHAT A GOAL! Absolute poetry on the pitch! That is what this club is all about!"
            """
        );
    }

    private ChatClient GetChatClient()
    {
        var rawEndpoint = _configuration["AzureOpenAI:Endpoint"];
        var apiKey = _configuration["AzureOpenAI:ApiKey"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(rawEndpoint) || rawEndpoint.StartsWith("<"))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is not configured in appsettings.json. Set it to your Azure OpenAI / Microsoft Foundry endpoint URL.");
        }

        var baseUri = new Uri(rawEndpoint);
        var normalizedEndpoint = new Uri($"{baseUri.Scheme}://{baseUri.Authority}/");

        AzureOpenAIClient azureClient;

        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("<"))
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) using API Key...", normalizedEndpoint);
            azureClient = new AzureOpenAIClient(normalizedEndpoint, new ApiKeyCredential(apiKey));
        }
        else
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) via Microsoft Entra ID...", normalizedEndpoint);
            azureClient = new AzureOpenAIClient(normalizedEndpoint, _credential);
        }

        return azureClient.GetChatClient(deploymentName);
    }
}
