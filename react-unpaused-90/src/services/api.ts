import type {
  AudioRequest,
  AudioResponse,
  CatchUpRequest,
  CatchUpResponse,
  ExplainEventRequest,
  ExplainEventResponse,
  GeneratedInsight,
  MatchData,
  TimelineEvent,
} from "../types/match";

const API_BASE = import.meta.env.VITE_API_BASE_URL || "http://localhost:5165";

export async function fetchMatchById(matchId = "PL2027-001"): Promise<MatchData> {
  const res = await fetch(`${API_BASE}/api/v1/matches/${matchId}`);
  if (!res.ok) {
    throw new Error(`Failed to fetch match ${matchId}: ${res.status}`);
  }
  return res.json();
}

export async function fetchMatchTimeline(
  matchId = "PL2027-001",
  upToMinute?: number
): Promise<TimelineEvent[]> {
  const query = upToMinute !== undefined ? `?upToMinute=${upToMinute}` : "";
  const res = await fetch(`${API_BASE}/api/v1/matches/${matchId}/timeline${query}`);
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
  const res = await fetch(`${API_BASE}/api/v1/matches/${matchId}/insights${query}`);
  if (!res.ok) {
    throw new Error(`Failed to fetch insights for ${matchId}: ${res.status}`);
  }
  return res.json();
}

export async function generateCatchUp(
  payload: CatchUpRequest
): Promise<CatchUpResponse> {
  const res = await fetch(`${API_BASE}/api/v1/catchup`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...payload, includeAudio: false }),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Catch-up generation failed (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function explainTimelineEvent(
  payload: ExplainEventRequest
): Promise<ExplainEventResponse> {
  const res = await fetch(`${API_BASE}/api/v1/explain`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Event explanation failed (${res.status}): ${errText}`);
  }
  return res.json();
}

export async function synthesizeAudio(
  payload: AudioRequest
): Promise<AudioResponse> {
  const res = await fetch(`${API_BASE}/api/v1/audio`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`Audio synthesis failed (${res.status}): ${errText}`);
  }
  return res.json();
}
