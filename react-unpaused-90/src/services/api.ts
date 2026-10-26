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
  "http://localhost:5165",
  "http://127.0.0.1:5165",
  "https://localhost:7190",
  "", // Relative path fallback
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

export async function fetchMatchById(
  matchId = "PL2027-001",
  minute?: number
): Promise<MatchData> {
  const query = minute !== undefined ? `?minute=${minute}` : "";
  const res = await fetchWithFallback(`/api/v1/matches/${matchId}${query}`);
  if (!res.ok) {
    throw new Error(`Failed to fetch match ${matchId}: ${res.status}`);
  }
  return res.json();
}

export async function resetMatchSimulation(
  matchId = "PL2027-001",
  startMinute?: number
): Promise<MatchData> {
  const query = startMinute !== undefined ? `?startMinute=${startMinute}` : "";
  const res = await fetchWithFallback(
    `/api/v1/matches/${matchId}/simulation/reset${query}`,
    {
      method: "POST",
    }
  );
  if (!res.ok) {
    throw new Error(`Failed to reset match simulation ${matchId}: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchTimeline(
  matchId = "PL2027-001",
  upToMinute?: number
): Promise<TimelineEvent[]> {
  const query = upToMinute !== undefined ? `?upToMinute=${upToMinute}` : "";
  const res = await fetchWithFallback(
    `/api/v1/matches/${matchId}/timeline${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch timeline for ${matchId}: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchInsights(
  matchId = "PL2027-001",
  upToMinute?: number
): Promise<GeneratedInsight[]> {
  const query = upToMinute !== undefined ? `?upToMinute=${upToMinute}` : "";
  const res = await fetchWithFallback(
    `/api/v1/matches/${matchId}/insights${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch insights for ${matchId}: ${res.status}`);
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
  matchId = "PL2027-001",
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
  const res = await fetchWithFallback(
    `/api/v1/matches/${matchId}/possession${query}`
  );
  if (!res.ok) {
    throw new Error(`Failed to fetch possession for ${matchId}: ${res.status}`);
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
  baselineHome = 54
): { home: number; away: number } {
  let homePos = baselineHome;

  // Progression baseline
  if (minute < 15) {
    homePos = 58.0 + (minute / 15.0) * 4.0;
  } else if (minute < 35) {
    homePos = 62.0 - ((minute - 15.0) / 20.0) * 12.0;
  } else if (minute < 55) {
    homePos = 50.0 + ((minute - 35.0) / 20.0) * 5.0;
  } else {
    homePos = 55.0 + ((minute - 55.0) / 35.0) * 5.0;
  }

  // Red card man-advantage adjustments
  const homeReds = activeEvents.filter(
    (e) =>
      e.eventType.toLowerCase() === "redcard" &&
      (e.team === "ARS" || e.team.toLowerCase().includes("arsenal"))
  ).length;

  const awayReds = activeEvents.filter(
    (e) =>
      e.eventType.toLowerCase() === "redcard" &&
      (e.team === "LIV" || e.team.toLowerCase().includes("liverpool"))
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
      seq.team === "ARS" || seq.team.toLowerCase().includes("arsenal");
    const dur = Math.min(2.0, Math.max(0.5, (seq.durationSeconds ?? 60) / 60));
    eventDelta += (isHome ? 1.5 : -1.5) * dur;
  }

  const finalHome = Math.min(75, Math.max(25, Math.round(homePos + eventDelta)));
  return {
    home: finalHome,
    away: 100 - finalHome,
  };
}
