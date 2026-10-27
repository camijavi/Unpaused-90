import type {
  AudioRequest,
  AudioResponse,
  CatchUpRequest,
  CatchUpResponse,
  ExplainEventRequest,
  ExplainEventResponse,
  GeneratedInsight,
  MatchData,
  PersonalizedSummaryRequest,
  PersonalizedSummaryResponse,
  TimelineEvent,
} from "../types/match";

const CANDIDATE_BASES = [
  import.meta.env.VITE_API_BASE_URL,
  "", // Primary relative path for production
  "http://localhost:5165",
  "http://127.0.0.1:5165",
  "https://localhost:7190",
].filter((b): b is string => b !== undefined && b !== null);

async function fetchWithFallback(
  path: string,
  init?: RequestInit
): Promise<Response> {
  let lastError: unknown = null;
  let lastBadGatewayRes: Response | null = null;

  for (const base of CANDIDATE_BASES) {
    try {
      const res = await fetch(`${base}${path}`, init);

      // If a proxy returns 502/503/504 Bad Gateway or Vite returns 404 HTML, try the next base URL
      const contentType = res.headers.get("content-type") || "";
      if (
        res.status === 502 ||
        res.status === 503 ||
        res.status === 504 ||
        (base === "" && res.status === 404 && contentType.includes("text/html"))
      ) {
        lastBadGatewayRes = res;
        continue;
      }

      return res;
    } catch (err) {
      lastError = err;
    }
  }

  if (lastBadGatewayRes) {
    return lastBadGatewayRes;
  }

  throw lastError instanceof Error
    ? lastError
    : new Error(`Network request failed for ${path}`);
}

export async function fetchMatches(): Promise<
  {
    matchId: string;
    competition: string;
    stadium: string;
    homeTeam: string;
    awayTeam: string;
    homeScore: number;
    awayScore: number;
    score: string;
    currentMinute: number;
    status: string;
  }[]
> {
  const res = await fetchWithFallback(`/api/v1/matches`);
  if (!res.ok) {
    throw new Error(`Failed to fetch matches: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchById(
  matchId?: string,
  minute?: number
): Promise<MatchData> {
  const query = minute !== undefined ? `?minute=${minute}` : "";
  const targetId = matchId || "random";
  const res = await fetchWithFallback(`/api/v1/matches/${targetId}${query}`);
  if (!res.ok) {
    throw new Error(`Failed to fetch match ${targetId}: ${res.status}`);
  }
  return res.json();
}

export async function resetMatchSimulation(
  matchId?: string,
  startMinute?: number
): Promise<MatchData> {
  const query = startMinute !== undefined ? `?startMinute=${startMinute}` : "";
  const targetId = matchId || "random";
  const res = await fetchWithFallback(
    `/api/v1/matches/${targetId}/simulation/reset${query}`,
    {
      method: "POST",
    }
  );
  if (!res.ok) {
    throw new Error(`Failed to reset match simulation ${targetId}: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchTimeline(
  matchId?: string,
  upToMinute?: number
): Promise<TimelineEvent[]> {
  const query = upToMinute !== undefined ? `?upToMinute=${upToMinute}` : "";
  const targetId = matchId || "PL2027-001";
  const res = await fetchWithFallback(
    `/api/v1/matches/${targetId}/timeline${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch timeline for ${targetId}: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchInsights(
  matchId?: string,
  upToMinute?: number
): Promise<GeneratedInsight[]> {
  const query = upToMinute !== undefined ? `?upToMinute=${upToMinute}` : "";
  const targetId = matchId || "PL2027-001";
  const res = await fetchWithFallback(
    `/api/v1/matches/${targetId}/insights${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch insights for ${targetId}: ${res.status}`);
  }
  return res.json();
}

export async function generateCatchUp(
  payload: CatchUpRequest
): Promise<CatchUpResponse> {
  const res = await fetchWithFallback(`/api/v1/catchup`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...payload, includeAudio: false }),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Catch-Up error (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function generatePersonalizedSummary(
  payload: PersonalizedSummaryRequest
): Promise<PersonalizedSummaryResponse> {
  const res = await fetchWithFallback(`/api/v1/personalized-summary`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...payload, includeAudio: false }),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Summary error (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function explainTimelineEvent(
  payload: ExplainEventRequest
): Promise<ExplainEventResponse> {
  const res = await fetchWithFallback(`/api/v1/explain`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Explain error (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function synthesizeAudio(
  payload: AudioRequest
): Promise<AudioResponse> {
  const res = await fetchWithFallback(`/api/v1/audio`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Audio error (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function fetchMatchPossession(
  matchId?: string,
  minute?: number
): Promise<{
  matchId: string;
  minute: number;
  possession: { home: number; away: number };
  homeTeam: string;
  awayTeam: string;
  score: string;
  status: string;
}> {
  const query = minute !== undefined ? `?minute=${minute}` : "";
  const targetId = matchId || "PL2027-001";
  const res = await fetchWithFallback(
    `/api/v1/matches/${targetId}/possession${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch possession for ${targetId}: ${res.status}`);
  }
  return res.json();
}

/**
 * Manages and calculates dynamic possession percentages for any simulated match minute,
 * ensuring possession adapts to real match events (pressure sequences, red cards, momentum swings).
 */
export function managePossessionUpdates(
  minute: number,
  activeEvents: TimelineEvent[] = [],
  baselineHome = 50,
  homeTeamIdentifier = "home",
  awayTeamIdentifier = "away"
): { home: number; away: number } {
  let homePos = baselineHome;

  // Red card man-advantage adjustments
  const homeReds = activeEvents.filter(
    (e) =>
      e.eventType.toLowerCase() === "redcard" &&
      (e.team.toLowerCase() === homeTeamIdentifier.toLowerCase() ||
        homeTeamIdentifier.toLowerCase().includes(e.team.toLowerCase()))
  ).length;

  const awayReds = activeEvents.filter(
    (e) =>
      e.eventType.toLowerCase() === "redcard" &&
      (e.team.toLowerCase() === awayTeamIdentifier.toLowerCase() ||
        awayTeamIdentifier.toLowerCase().includes(e.team.toLowerCase()))
  ).length;

  const netAdvantage = awayReds - homeReds;
  let eventDelta = netAdvantage * 2.5;

  // Recent pressure sequences in the last 3 minutes
  const recentPressure = activeEvents.filter(
    (e) =>
      e.minute >= minute - 3 &&
      e.minute <= minute &&
      e.eventType.toLowerCase().includes("pressure")
  );

  for (const seq of recentPressure) {
    const isHome =
      seq.team.toLowerCase() === homeTeamIdentifier.toLowerCase() ||
      homeTeamIdentifier.toLowerCase().includes(seq.team.toLowerCase());
    const dur = Math.min(2.0, Math.max(0.5, (seq.durationSeconds ?? 60) / 60));
    eventDelta += (isHome ? 1.5 : -1.5) * dur;
  }

  const finalHome = Math.min(80, Math.max(20, Math.round(homePos + eventDelta)));
  return {
    home: finalHome,
    away: 100 - finalHome,
  };
}
