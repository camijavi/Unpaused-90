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
