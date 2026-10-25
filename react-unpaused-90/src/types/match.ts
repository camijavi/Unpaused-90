export interface Player {
  playerId: string;
  name: string;
  number: number;
  position: string;
  captain: boolean;
}

export interface SubstitutePlayer {
  playerId: string;
  name: string;
  number: number;
  position: string;
}

export interface Team {
  teamId: string;
  name: string;
  coach: string;
  formation: string;
  score: number;
  startingXI: Player[];
  substitutes: SubstitutePlayer[];
}

export interface StatPair {
  home: number;
  away: number;
}

export interface MatchStats {
  possession: StatPair;
  shots: StatPair;
  shotsOnTarget: StatPair;
  passAccuracy: StatPair;
  corners: StatPair;
  fouls: StatPair;
}

export interface TimelineEvent {
  eventId: string;
  minute: number;
  eventType: string; // "Goal" | "YellowCard" | "RedCard" | "Substitution" | "Shot" | "PressureSequence"
  team: string;
  player?: string | null;
  assist?: string | null;
  onTarget?: boolean | null;
  durationSeconds?: number | null;
  importanceScore?: number | null;
  playerIn?: string | null;
  playerOut?: string | null;
}

export interface GeneratedInsight {
  minute: number;
  type: string;
  summary: string;
  reason: string;
}

export interface MatchData {
  matchId: string;
  competition: string;
  season: string;
  stadium: string;
  status: string;
  currentMinute: number;
  homeTeam: Team;
  awayTeam: Team;
  statistics: MatchStats;
  timeline: TimelineEvent[];
  generatedInsights: GeneratedInsight[];
}

export interface CatchUpRequest {
  matchId: string;
  lastViewedMinute: number;
  currentMinute: number;
  viewMode: "casual" | "analyst" | "child";
  favoriteTeam: string;
  language: "en" | "es";
  length: "1m" | "5m" | "full";
  includeAudio?: boolean;
}

export interface CatchUpResponse {
  whatHappened: string;
  whyItMatters: string;
  momentumChanges: string;
  whatToWatchNext: string;
  length?: string;
  wordCount?: number | null;
  estimatedDurationSeconds?: number | null;
  voice?: string | null;
  audioUrl?: string | null;
}

export interface ExplainEventRequest {
  matchId: string;
  eventId: string;
  viewMode: "casual" | "analyst" | "child";
  favoriteTeam?: string;
  language?: "en" | "es";
}

export interface ExplainEventResponse {
  eventId: string;
  minute: number;
  title: string;
  explanation: string;
}

export interface AudioRequest {
  text: string;
  voice?: string | null;
  language?: "en" | "es";
}

export interface AudioResponse {
  audioUrl: string;
  voice: string;
  language: string;
}

export type AnimationKind = "goal" | "yellow" | "red" | "sub";

export interface EventAnimationPayload {
  kind: AnimationKind;
  min: number;
  club: string;
  player: string;
  sub: string;
}
