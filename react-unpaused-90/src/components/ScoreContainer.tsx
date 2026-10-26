import React, { useState, useMemo, useRef, useLayoutEffect } from "react";
import stadiumImg from "../assets/stadium.jpe";
import Goal from "../Animations/Goal";
import YellowCard from "../Animations/YellowCard";
import RedCard from "../Animations/RedCard";
import Substitution from "../Animations/Substitution";
import type {
  AnimationKind,
  EventAnimationPayload,
  GeneratedInsight,
  MatchData,
  TimelineEvent,
} from "../types/match";
import {

  BallIcon,
  FilterIcon,

  RedCardIcon,
  SubIcon,
  TargetIcon,
  TimerIcon,
  YellowCardIcon,
} from "./Icons";
import { useLanguage } from "../languages/LanguageContext";
import ResponsiveScoreContainer, { renderTeamCrest } from "./ResponsiveScoreContainer";

interface ScoreContainerProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  insights: GeneratedInsight[];
  stoppedMinute: number;
  currentMinute: number;
  selectedEvent: TimelineEvent | null;
  eventExplanation: {
    title: string;
    explanation: string;
    loading?: boolean;
  } | null;
  activeAnimation: EventAnimationPayload | null;
  onTriggerAnimation: (payload: EventAnimationPayload) => void;
  onSelectTimelineEvent: (ev: TimelineEvent) => void;
}

export function mapEventTypeToAnimationKind(eventType: string): AnimationKind {
  const normalized = eventType.toLowerCase();
  if (normalized.includes("goal")) return "goal";
  if (normalized.includes("yellow")) return "yellow";
  if (normalized.includes("red")) return "red";
  return "sub";
}

export function formatTimelineEventTitle(
  ev: TimelineEvent,
  homeCode: string,
  homeName: string,
  awayName: string,
  lang: "en" | "es" = "en",
): {
  clubName: string;
  playerLine: string;
  title: string;
  fallbackDesc: string;
} {
  const clubName = ev.team === homeCode ? homeName : awayName;
  const isEs = lang === "es";

  if (ev.eventType === "Goal") {
    const assistText = ev.assist
      ? isEs
        ? ` (Asistencia: ${ev.assist})`
        : ` (Assist: ${ev.assist})`
      : "";
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: isEs
        ? `Gol de ${ev.player ?? clubName} (${ev.minute}')${assistText}`
        : `${ev.player ?? clubName} Goal (${ev.minute}')${assistText}`,
      fallbackDesc: isEs
        ? `Definición clínica de ${ev.player ?? clubName}${
            ev.assist ? ` tras una asistencia decisiva de ${ev.assist}` : ""
          } al minuto ${ev.minute}'.`
        : `Clinical finish by ${ev.player ?? clubName}${
            ev.assist ? ` following a decisive pass from ${ev.assist}` : ""
          } in the ${ev.minute}th minute.`,
    };
  }
  if (ev.eventType === "YellowCard") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: isEs
        ? `Tarjeta Amarilla para ${ev.player ?? clubName} (${ev.minute}')`
        : `${ev.player ?? clubName} Tactical Yellow Card (${ev.minute}')`,
      fallbackDesc: isEs
        ? `Amonestación arbitral para ${
            ev.player ?? clubName
          } al minuto ${ev.minute}', exigiendo mayor cautela en las disputas.`
        : `Official disciplinary caution shown to ${
            ev.player ?? clubName
          } at ${ev.minute}', forcing extra caution in midfield duels.`,
    };
  }
  if (ev.eventType === "RedCard") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: isEs
        ? `Tarjeta Roja para ${ev.player ?? clubName} (${ev.minute}')`
        : `${ev.player ?? clubName} Red Card Dismissal (${ev.minute}')`,
      fallbackDesc: isEs
        ? `${clubName} se queda con 10 hombres tras la expulsión de ${
            ev.player ?? clubName
          } al minuto ${ev.minute}'.`
        : `${clubName} reduced to 10 men after ${
            ev.player ?? clubName
          } is sent off at ${ev.minute}'.`,
    };
  }
  if (ev.eventType === "Substitution") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.playerIn ?? "Sub"} IN • ${ev.playerOut ?? "Sub"} OUT (${
        ev.minute
      }')`,
      title: isEs
        ? `Sustitución Táctica (${ev.minute}'): ${ev.playerIn} entra por ${ev.playerOut}`
        : `Tactical Substitution (${ev.minute}'): ${ev.playerIn} replaces ${ev.playerOut}`,
      fallbackDesc: isEs
        ? `Piernas frescas en ${clubName} al minuto ${ev.minute}': entra ${ev.playerIn} en lugar de ${ev.playerOut}.`
        : `Fresh legs introduced by ${clubName} at ${ev.minute}' as ${ev.playerIn} comes on for ${ev.playerOut}.`,
    };
  }
  if (ev.eventType === "PressureSequence") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: isEs
        ? `Presión Alta de ${clubName} (${ev.durationSeconds ?? 90}s)`
        : `${clubName} High Press (${ev.durationSeconds ?? 90}s Wave)`,
      title: isEs
        ? `Secuencia de Presión Sostenida de ${clubName} (${ev.minute}')`
        : `${clubName} Sustained Pressure Sequence (${ev.minute}')`,
      fallbackDesc: isEs
        ? `${clubName} arrinconó al rival en su tercio defensivo durante ${
            ev.durationSeconds ?? 90
          } segundos.`
        : `${clubName} pinned the opposition inside their defensive third for ${
            ev.durationSeconds ?? 90
          } seconds (Importance: ${ev.importanceScore ?? 80}/100).`,
    };
  }
  return {
    clubName: clubName.toUpperCase(),
    playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
    title: isEs
      ? `${ev.eventType} de ${ev.player ?? clubName} (${ev.minute}')${
          ev.onTarget ? " — A Puerta" : ""
        }`
      : `${ev.player ?? clubName} ${ev.eventType} (${ev.minute}')${
          ev.onTarget ? " — On Target" : ""
        }`,
    fallbackDesc: isEs
      ? `Acción clave (${ev.eventType}) de ${
          ev.player ?? clubName
        } al minuto ${ev.minute}'.`
      : `Key ${ev.eventType.toLowerCase()} by ${
          ev.player ?? clubName
        } at minute ${ev.minute}'.`,
  };
}

// ---------------------------------------------------------------------------
// Timeline layout (pixel-based, collision-free)
// ---------------------------------------------------------------------------
const TL_PIN_H = 24; // pin height (h-6)
const TL_ROW_H = 30; // vertical pitch between stacked pin rows
const TL_PIN_GAP = 8; // min horizontal gap between pins in the same row (incl. ring/scale room)
const TL_MAX_SHIFT = 10; // px a pin may slide right to stay in a lower row
const TL_LANE_PAD = 10; // breathing room between lane edge and outermost row
const TL_BAR_H = 12;
const TL_RULER_H = 18;
const TL_TICKS = [0, 15, 30, 45, 60, 75, 90];

/** Deterministic pin width: icon + "NN'" label in 10.5px mono. Applied as an explicit style width. */
function timelinePinWidth(ev: TimelineEvent): number {
  const chars = String(ev.minute).length + 1;
  return Math.round(30 + chars * 6.6);
}

interface PackedPin {
  ev: TimelineEvent;
  stemX: number; // true minute position on the track
  pinX: number; // pin center (may differ slightly from stemX to avoid edges/neighbours)
  width: number;
  row: number; // 0 = row nearest to the track bar
}

/**
 * Greedy interval packing: each pin goes into the lowest row where it fits without overlapping,
 * optionally sliding a few px right. New rows are added as needed, so pins can never overlap.
 */
function packTimelinePins(
  events: TimelineEvent[],
  trackWidth: number,
): { pins: PackedPin[]; rows: number } {
  const W = trackWidth > 0 ? trackWidth : 900;
  const rowEnds: number[] = [];
  const sorted = [...events].sort((a, b) => a.minute - b.minute);

  const pins = sorted.map((ev) => {
    const width = timelinePinWidth(ev);
    const half = width / 2;
    const stemX = Math.min(1, Math.max(0, ev.minute / 90)) * W;
    const desired = Math.min(W - half, Math.max(half, stemX));

    let row = -1;
    let pinX = desired;
    for (let r = 0; r < rowEnds.length; r++) {
      const minCenter = rowEnds[r] + TL_PIN_GAP + half;
      if (minCenter <= desired) {
        row = r;
        pinX = desired;
        break;
      }
      const canSlide =
        minCenter - desired <= TL_MAX_SHIFT &&
        minCenter + half <= W &&
        Math.abs(minCenter - stemX) <= half - 6; // stem must still land on the pin
      if (canSlide) {
        row = r;
        pinX = minCenter;
        break;
      }
    }
    if (row === -1) {
      row = rowEnds.length;
      rowEnds.push(-Infinity);
      pinX = desired;
    }
    rowEnds[row] = pinX + half;

    return { ev, stemX, pinX, width, row };
  });

  return { pins, rows: Math.max(1, rowEnds.length) };
}

function timelineBadgeStyle(ev: TimelineEvent, inWindow: boolean): string {
  const kind = mapEventTypeToAnimationKind(ev.eventType);
  const type = ev.eventType.toLowerCase();
  const isPenalty = type.includes("penalty");
  const isShot = type.includes("shot");
  const isPressure = type.includes("pressure");

  if (inWindow) {
    if (kind === "goal") return "bg-emerald-600 text-white border border-emerald-700 shadow-md";
    if (kind === "red") return "bg-red-600 text-white border border-red-700 shadow-md";
    if (kind === "yellow") return "bg-amber-400 text-slate-950 border border-amber-500 shadow-md";
    if (isPenalty) return "bg-purple-700 text-white border border-purple-800 shadow-md";
    if (isShot) return "bg-slate-700 text-white border border-slate-800 shadow-md";
    if (isPressure) return "bg-indigo-600 text-white border border-indigo-700 shadow-md";
    return "bg-sky-600 text-white border border-sky-700 shadow-md";
  }
  if (kind === "goal") return "bg-emerald-50 text-emerald-800 border border-emerald-400 hover:bg-emerald-600 hover:text-white";
  if (kind === "red") return "bg-red-50 text-red-800 border border-red-400 hover:bg-red-600 hover:text-white";
  if (kind === "yellow") return "bg-amber-50 text-amber-900 border border-amber-400 hover:bg-amber-400 hover:text-slate-950";
  if (isPenalty) return "bg-purple-50 text-purple-900 border border-purple-400 hover:bg-purple-700 hover:text-white";
  if (isShot) return "bg-white text-slate-600 border border-slate-300 hover:bg-slate-700 hover:text-white";
  if (isPressure) return "bg-indigo-50 text-indigo-800 border border-indigo-300 hover:bg-indigo-600 hover:text-white";
  return "bg-sky-50 text-sky-800 border border-sky-300 hover:bg-sky-600 hover:text-white";
}

function TimelinePinIcon({ ev }: { ev: TimelineEvent }) {
  const kind = mapEventTypeToAnimationKind(ev.eventType);
  const type = ev.eventType.toLowerCase();
  if (type.includes("penalty")) return <BallIcon className="w-3 h-3 text-purple-300 shrink-0" />;
  if (kind === "goal") return <BallIcon className="w-3 h-3 shrink-0" />;
  if (kind === "yellow") return <YellowCardIcon className="w-2.5 h-3 shrink-0" />;
  if (kind === "red") return <RedCardIcon className="w-2.5 h-3 shrink-0" />;
  if (type.includes("shot")) return <TargetIcon className="w-2.5 h-2.5 shrink-0" />;
  if (type.includes("pressure")) return <TimerIcon className="w-2.5 h-2.5 shrink-0" />;
  return <SubIcon className="w-3 h-3 shrink-0" />;
}

export const ScoreContainer: React.FC<ScoreContainerProps> = ({
  match,
  timeline,
  insights,
  stoppedMinute,
  currentMinute,
  selectedEvent,
  eventExplanation,
  activeAnimation,
  onTriggerAnimation,
  onSelectTimelineEvent,
}) => {
  const { t, language } = useLanguage();
  const homeCode = match?.homeTeam.teamId ?? "ARS";
  const awayCode = match?.awayTeam.teamId ?? "LIV";
  const homeName = match?.homeTeam.name ?? "Arsenal";
  const awayName = match?.awayTeam.name ?? "Liverpool";
  const homeFormation = match?.homeTeam.formation ?? "4-3-3";
  const awayFormation = match?.awayTeam.formation ?? "4-2-3-1";
  const homeScore = match?.homeTeam.score ?? 0;
  const awayScore = match?.awayTeam.score ?? 0;
  const competition = match?.competition ?? "Premier League";
  const season = match?.season ?? "2026/27";
  const stadium = match?.stadium ?? "Emirates Stadium";
  const possessionHome = match?.statistics.possession.home ?? 54;
  const possessionAway = match?.statistics.possession.away ?? 46;
  const cornersHome = match?.statistics.corners.home ?? 0;
  const cornersAway = match?.statistics.corners.away ?? 0;
  const foulsHome = match?.statistics.fouls.home ?? 0;
  const foulsAway = match?.statistics.fouls.away ?? 0;

  const isFullTime = currentMinute >= 90 || match?.status === "FullTime";
  const statusLabel = isFullTime
    ? t.scoreContainer.halves.fullTime.toUpperCase()
    : match?.status === "Half Time" || currentMinute === 45
      ? t.scoreContainer.halves.halfTime.toUpperCase()
      : currentMinute < 45
        ? t.scoreContainer.halves.firstHalf.toUpperCase()
        : t.scoreContainer.halves.secondHalf.toUpperCase();

  // Derive real goal scorers and disciplinary notes from fetched timeline
  const homeGoals = timeline.filter(
    (e) => e.team === homeCode && e.eventType === "Goal",
  );
  const awayGoals = timeline.filter(
    (e) => e.team === awayCode && e.eventType === "Goal",
  );
  const awayCards = timeline.filter(
    (e) =>
      e.team === awayCode &&
      (e.eventType === "YellowCard" || e.eventType === "RedCard"),
  );

  const homeScorersText =
    homeGoals.length > 0
      ? homeGoals
          .map((g) => `${g.player?.split(" ").pop() ?? g.player} ${g.minute}'`)
          .join(", ")
      : "No goals";

  const awayScorersText =
    awayGoals.length > 0
      ? awayGoals
          .map((g) => `${g.player?.split(" ").pop() ?? g.player} ${g.minute}'`)
          .join(", ")
      : "No goals";

  const awayCardText =
    awayCards.length > 0
      ? awayCards
          .map((c) => `${c.player?.split(" ").pop() ?? c.player} ${c.minute}'`)
          .join(", ")
      : null;

  // Timeline progress bar calculations
  const watchedPct = Math.min(100, Math.max(0, (stoppedMinute / 90) * 100));
  const missedPct = Math.min(
    100 - watchedPct,
    Math.max(0, ((currentMinute - stoppedMinute) / 90) * 100),
  );

  // Active event for the inspector drawer
  const activeEv = selectedEvent ?? timeline[timeline.length - 1] ?? null;

  // Timeline Filtering States (All, Key Moments, Goals Only, In-Window Only)
  const [filterCategory, setFilterCategory] = useState<
    "all" | "key" | "goals" | "window"
  >("all");
  const [filterTeam, setFilterTeam] = useState<"all" | "home" | "away">("all");

  // Filter events based on active category and team toggles
  const filteredTimeline = useMemo(() => {
    return timeline.filter((ev) => {
      // Category filter
      if (filterCategory === "goals") {
        if (!ev.eventType.toLowerCase().includes("goal")) return false;
      } else if (filterCategory === "key") {
        const norm = ev.eventType.toLowerCase();
        const isKey =
          norm.includes("goal") ||
          norm.includes("card") ||
          norm.includes("penalty") ||
          norm.includes("sub");
        if (!isKey) return false;
      } else if (filterCategory === "window") {
        if (ev.minute < stoppedMinute || ev.minute > currentMinute) return false;
      }

      // Team filter
      if (filterTeam === "home") {
        if (ev.team !== homeCode && ev.team !== homeName) return false;
      } else if (filterTeam === "away") {
        if (ev.team === homeCode || ev.team === homeName) return false;
      }

      return true;
    });
  }, [
    timeline,
    filterCategory,
    filterTeam,
    stoppedMinute,
    currentMinute,
    homeCode,
    homeName,
  ]);

  const homeEvents = useMemo(() => {
    return filteredTimeline.filter(
      (ev) => ev.team === homeCode || ev.team === homeName,
    );
  }, [filteredTimeline, homeCode, homeName]);

  const awayEvents = useMemo(() => {
    return filteredTimeline.filter(
      (ev) => ev.team !== homeCode && ev.team !== homeName,
    );
  }, [filteredTimeline, homeCode, homeName]);

  // Measure the real track width so pin collisions are resolved in pixels, not minutes
  const trackRef = useRef<HTMLDivElement | null>(null);
  const [trackWidth, setTrackWidth] = useState(0);
  useLayoutEffect(() => {
    const el = trackRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      setTrackWidth((prev) => (Math.abs(prev - width) > 0.5 ? width : prev));
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const homeLayout = useMemo(
    () => packTimelinePins(homeEvents, trackWidth),
    [homeEvents, trackWidth],
  );
  const awayLayout = useMemo(
    () => packTimelinePins(awayEvents, trackWidth),
    [awayEvents, trackWidth],
  );

  // Vertical geometry: [home rows] [track bar] [away rows] [minute ruler]
  const homeRows = homeLayout.rows;
  const awayRows = awayLayout.rows;
  const barTop = TL_LANE_PAD + homeRows * TL_ROW_H + 6;
  const awayLaneTop = barTop + TL_BAR_H + 12;
  const rulerTop = awayLaneTop + awayRows * TL_ROW_H + 4;
  const timelineHeight = rulerTop + TL_RULER_H + 6;
  const homePinTop = (row: number) =>
    TL_LANE_PAD + (homeRows - 1 - row) * TL_ROW_H;
  const awayPinTop = (row: number) =>
    awayLaneTop + row * TL_ROW_H + (TL_ROW_H - TL_PIN_H) / 2;
  const nowPct = Math.min(100, Math.max(0, (currentMinute / 90) * 100));

  const formattedActiveEv = activeEv
    ? formatTimelineEventTitle(activeEv, homeCode, homeName, awayName, language)
    : null;

  const matchingInsight = activeEv
    ? insights.find((i) => i.minute === activeEv.minute)
    : null;

  const displayTitle =
    eventExplanation?.title ||
    formattedActiveEv?.title ||
    (timeline.length === 0
      ? `Match in early opening stages (${currentMinute}') — awaiting first major event`
      : "Select a timeline event pin to inspect tactical context");

  const displayDesc = eventExplanation?.loading
    ? "Generating live AI tactical breakdown from Azure OpenAI..."
    : eventExplanation?.explanation ||
      (matchingInsight
        ? `Why It Matters: ${matchingInsight.summary} — ${matchingInsight.reason}`
        : formattedActiveEv?.fallbackDesc ||
          "As the live match clock advances, key events automatically appear on the timeline above.");

  const isShaking =
    activeAnimation?.kind === "goal" || activeAnimation?.kind === "red";

  return (
    <section
      id="scoreboard-section"
      aria-label="Live Scoreboard and Match Timeline"
      className="broadcast-slab rounded-2xl overflow-hidden relative"
    >
      {/* Dynamic Club Split Header Banner */}
      <div
        id="scoreboard-accent-bar"
        className="h-1.5 w-full bg-gradient-to-r from-red-600 via-mint to-rose-700"
      ></div>

      {/* Top Broadcast Metadata Telemetry Bar */}
      <div className="hidden md:flex px-4 sm:px-6 py-2.5 bg-slate-50 border-b border-slate-200 flex-wrap items-center justify-between gap-2 text-[11px] font-mono text-slate-600">
        <div className="flex items-center gap-2">
          <span className="text-pl-purple font-extrabold uppercase">
            {competition} • WEEK 10 • {season}
          </span>
          <span>•</span>
          <span id="match-venue" className="text-slate-900 font-bold">
            {stadium}
          </span>
        </div>

        <div className="flex flex-wrap items-center gap-3 text-[11px] font-mono">
          <span>
            Corners:{" "}
            <strong className="text-slate-900">
              {cornersHome}–{cornersAway}
            </strong>
          </span>
          <span>•</span>
          <span>
            Fouls:{" "}
            <strong className="text-slate-900">
              {foulsHome}–{foulsAway}
            </strong>
          </span>
          <span>•</span>
          <span
            className={`px-2 py-0.5 rounded font-bold uppercase ${
              isFullTime
                ? "bg-emerald-100 text-emerald-800 border border-emerald-300"
                : "bg-red-100 text-red-700 border border-red-300"
            }`}
          >
            {isFullTime
              ? "FINAL SCORE • 90' FT"
              : `LIVE SIMULATION • ${currentMinute}'`}
          </span>
        </div>
      </div>

      {/* MOBILE / SMALL SCREENS HERO (< md): Responsive Score Container */}
      <div className="md:hidden">
        <ResponsiveScoreContainer
          match={match}
          currentMinute={currentMinute}
          stoppedMinute={stoppedMinute}
          timeline={timeline}
          activeAnimation={activeAnimation}
          onTriggerAnimation={onTriggerAnimation}
          onSelectTimelineEvent={onSelectTimelineEvent}
        />
      </div>

      {/* HERO SCORE CONTAINER WITH PANORAMIC NEUTRAL STADIUM BACKGROUND IMAGE */}
      <div
        id="stadium-score-hero"
        className={`hidden md:flex relative overflow-hidden min-h-[300px] sm:min-h-[340px] lg:min-h-[380px] flex-col justify-center ${
          isShaking ? "anim-shake" : ""
        }`}
      >
        {/* Neutral Stadium Photograph Layer */}
        <img
          src={stadiumImg}
          alt="Neutral Premier League Stadium Panorama"
          className="absolute inset-0 w-full h-full object-cover object-center scale-105 transition-transform duration-1000"
        />
        {/* Multi-stop Broadcast Contrast Scrim so team names & crests pop */}
        <div className="absolute inset-0 bg-gradient-to-r from-slate-950/85 via-slate-900/55 to-slate-950/85"></div>
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-transparent to-slate-950/40"></div>

        {/* ANIMATED BROADCAST EVENT OVERLAY STAGE (SEPARATE COMPONENT FILES) */}
        {activeAnimation && (
          <div
            id="event-fx-overlay"
            className={`absolute inset-0 z-30 flex flex-col items-center justify-center p-6 sm:p-8 bg-slate-950/80 backdrop-blur-md transition-opacity ${
              activeAnimation.kind === "red" ? "anim-red-pulse" : ""
            }`}
          >
            {activeAnimation.kind === "goal" && (
              <Goal
                min={activeAnimation.min}
                club={activeAnimation.club}
                player={activeAnimation.player}
                sub={activeAnimation.sub}
              />
            )}
            {activeAnimation.kind === "yellow" && (
              <YellowCard
                min={activeAnimation.min}
                club={activeAnimation.club}
                player={activeAnimation.player}
                sub={activeAnimation.sub}
              />
            )}
            {activeAnimation.kind === "red" && (
              <RedCard
                min={activeAnimation.min}
                club={activeAnimation.club}
                player={activeAnimation.player}
                sub={activeAnimation.sub}
              />
            )}
            {activeAnimation.kind === "sub" && (
              <Substitution
                min={activeAnimation.min}
                club={activeAnimation.club}
                player={activeAnimation.player}
                sub={activeAnimation.sub}
              />
            )}
          </div>
        )}

        {/* Scoreboard Content Over Stadium Image */}
        <div className="relative z-10 py-10 sm:py-12 px-6 sm:px-10 lg:px-14 w-full">
          <div className="grid grid-cols-1 lg:grid-cols-11 gap-8 items-center">
            {/* HOME CLUB (4 cols) */}
            <div className="lg:col-span-4 flex items-center justify-between sm:justify-start gap-5">
              <div
                id="home-crest-svg"
                className="w-20 h-20 sm:w-24 sm:h-24 shrink-0 drop-shadow-[0_12px_24px_rgba(0,0,0,0.7)] transition-transform hover:scale-105"
              >
                {renderTeamCrest(homeCode, homeName, "w-full h-full")}
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <span
                    id="home-crest-code"
                    className="text-xs font-mono font-extrabold px-2.5 py-0.5 rounded bg-red-600 text-white shadow"
                  >
                    {homeCode}
                  </span>
                  <span
                    id="home-formation"
                    className="text-xs font-mono px-2.5 py-0.5 rounded bg-white/20 backdrop-blur-md text-white border border-white/25"
                  >
                    {homeFormation}
                  </span>
                </div>
                <h1
                  id="home-team-name"
                  className="text-3xl sm:text-5xl font-display font-black uppercase tracking-wide text-white mt-1 drop-shadow-md"
                >
                  {homeName}
                </h1>
                <p
                  id="home-scorers"
                  className="text-xs sm:text-sm font-mono text-emerald-300 font-semibold mt-1 drop-shadow flex items-center gap-1.5 flex-wrap"
                >
                  <BallIcon className="w-3.5 h-3.5 text-emerald-300 shrink-0" />
                  <span>
                    {homeScorersText} • Shots:{" "}
                    {match?.statistics.shots.home ?? 0} (
                    {match?.statistics.shotsOnTarget.home ?? 0} OT)
                  </span>
                </p>
              </div>
            </div>

            {/* CENTER BROADCAST SCOREBUG (3 cols) */}
            <div className="lg:col-span-3 flex flex-col items-center justify-center">
              {/* Competition & Matchweek Header (Matching Mobile UI) */}
              <div className="flex flex-col items-center justify-center text-center mb-3">
                <h2 className="text-xs sm:text-sm font-semibold tracking-wide text-purple-100/90 font-display">
                  {competition}
                </h2>
                <span className="text-[11px] font-semibold tracking-widest text-purple-300/80 mt-0.5 uppercase">
                  WEEK 10
                </span>
              </div>
              <div className="flex items-center gap-4 sm:gap-6 bg-white/95 backdrop-blur-xl px-7 sm:px-8 py-4 sm:py-5 rounded-2xl border-2 border-white shadow-[0_24px_60px_rgba(0,0,0,0.55)]">
                <span
                  id="home-score"
                  className="text-5xl sm:text-6xl font-display font-black text-slate-900 leading-none"
                >
                  {homeScore}
                </span>
                <div className="flex flex-col items-center">
                  <span
                    className="px-3.5 py-1 rounded-md bg-pl-purple text-mint-bright font-mono text-base sm:text-lg font-extrabold tracking-tight shadow"
                    id="scoreboard-minute"
                  >
                    {isFullTime ? "90' FT" : `${currentMinute}'`}
                  </span>
                  <span className="text-[10px] font-mono font-bold uppercase tracking-widest text-slate-500 mt-1.5">
                    {statusLabel}
                  </span>
                </div>
                <span
                  id="away-score"
                  className="text-5xl sm:text-6xl font-display font-black text-slate-900 leading-none"
                >
                  {awayScore}
                </span>
              </div>
              <div
                id="match-possession"
                className="mt-3 px-3.5 py-1.5 rounded-full bg-slate-900/85 backdrop-blur-md border border-white/20 text-xs font-mono font-bold text-white shadow flex items-center gap-2.5"
              >
                <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse shrink-0"></span>
                <span>
                  {t.scoreContainer.possession} <strong className="text-white">{possessionHome}%</strong> – <strong className="text-white">{possessionAway}%</strong>
                </span>
                <div className="w-16 h-2 bg-slate-700/80 rounded-full overflow-hidden flex border border-white/10 shrink-0">
                  <div
                    className="bg-red-500 h-full transition-all duration-700"
                    style={{ width: `${possessionHome}%` }}
                    title={`${homeCode}: ${possessionHome}%`}
                  ></div>
                  <div
                    className="bg-cyan-400 h-full transition-all duration-700"
                    style={{ width: `${possessionAway}%` }}
                    title={`${awayCode}: ${possessionAway}%`}
                  ></div>
                </div>
              </div>
            </div>

            {/* AWAY CLUB (4 cols) */}
            <div className="lg:col-span-4 flex items-center justify-between sm:justify-end gap-5 text-left sm:text-right">
              <div className="order-2 sm:order-1">
                <div className="flex items-center sm:justify-end gap-2">
                  <span
                    id="away-formation"
                    className="text-xs font-mono px-2.5 py-0.5 rounded bg-white/20 backdrop-blur-md text-white border border-white/25"
                  >
                    {awayFormation}
                  </span>
                  <span
                    id="away-crest-code"
                    className="text-xs font-mono font-extrabold px-2.5 py-0.5 rounded bg-rose-600 text-white shadow"
                  >
                    {awayCode}
                  </span>
                </div>
                <h2
                  id="away-team-name"
                  className="text-3xl sm:text-5xl font-display font-black uppercase tracking-wide text-white mt-1 drop-shadow-md"
                >
                  {awayName}
                </h2>
                <p
                  id="away-scorers"
                  className="text-xs sm:text-sm font-mono text-emerald-300 font-semibold mt-1 drop-shadow flex items-center sm:justify-end gap-1.5 flex-wrap"
                >
                  <BallIcon className="w-3.5 h-3.5 text-emerald-300 shrink-0" />
                  <span>
                    {awayScorersText} • Shots:{" "}
                    {match?.statistics.shots.away ?? 0} (
                    {match?.statistics.shotsOnTarget.away ?? 0} OT)
                  </span>
                  {awayCardText && (
                    <>
                      <span>•</span>
                      <YellowCardIcon className="w-3 h-3.5 shrink-0 inline-block" />
                      <span className="text-amber-300">{awayCardText}</span>
                    </>
                  )}
                </p>
              </div>
              <div
                id="away-crest-svg"
                className="order-1 sm:order-2 w-20 h-20 sm:w-24 sm:h-24 shrink-0 drop-shadow-[0_12px_24px_rgba(0,0,0,0.7)] transition-transform hover:scale-105"
              >
                {renderTeamCrest(awayCode, awayName, "w-full h-full")}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* LIGHT-MODE MATCH TIMELINE SCRUBBER & EVENT PINS (REAL DATA) */}
      <div className="p-4 sm:p-6 bg-white border-t border-slate-200">
        {/* MATCH TIMELINE HEADER & LIVE FX LEGEND */}
        <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="px-2.5 py-0.5 rounded bg-pl-purple text-white font-mono text-[11px] font-bold uppercase tracking-wider">
              MATCH TIMELINE &amp; LIVE FX
            </span>
            <span className="text-xs text-slate-600 flex items-center gap-2 flex-wrap">
              <span>Click any pin (</span>
              <span className="text-emerald-700 font-bold inline-flex items-center gap-1">
                <BallIcon className="w-3.5 h-3.5" /> Goal
              </span>
              <span>•</span>
              <span className="text-amber-600 font-bold inline-flex items-center gap-1">
                <YellowCardIcon className="w-3 h-3.5" /> Yellow
              </span>
              <span>•</span>
              <span className="text-red-600 font-bold inline-flex items-center gap-1">
                <RedCardIcon className="w-3 h-3.5" /> Red
              </span>
              <span>•</span>
              <span className="text-sky-700 font-bold inline-flex items-center gap-1">
                <SubIcon className="w-3.5 h-3.5" /> Tactical/Sub
              </span>
              <span>) to trigger broadcast animations &amp; AI breakdown</span>
            </span>
          </div>

          <div className="flex items-center gap-2 text-[11px] font-mono">
            <span className="text-slate-500 font-semibold">
              {t.scoreContainer.timelineFilters.showing}{" "}
              <strong className="text-slate-900">{filteredTimeline.length}</strong>{" "}
              {t.scoreContainer.timelineFilters.of}{" "}
              <strong className="text-slate-900">{timeline.length}</strong>{" "}
              {t.scoreContainer.timelineFilters.events}
            </span>
          </div>
        </div>

        {/* TIMELINE INTERACTIVE FILTER CONTROLS */}
        <div className="flex flex-wrap items-center justify-between gap-3 mb-3.5 pb-2.5 border-b border-slate-100">
          {/* Category Filter Tabs */}
          <div className="flex flex-wrap items-center gap-1.5 bg-slate-100 p-1 rounded-xl border border-slate-200 text-xs font-mono">
            <span className="pl-2 pr-1 text-slate-400 flex items-center">
              <FilterIcon className="w-3.5 h-3.5" />
            </span>
            <button
              type="button"
              onClick={() => setFilterCategory("all")}
              className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer ${
                filterCategory === "all"
                  ? "bg-white text-slate-900 shadow-sm border border-slate-200"
                  : "text-slate-600 hover:text-slate-900"
              }`}
            >
              {t.scoreContainer.timelineFilters.all}
            </button>
            <button
              type="button"
              onClick={() => setFilterCategory("key")}
              className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer flex items-center gap-1.5 ${
                filterCategory === "key"
                  ? "bg-white text-slate-900 shadow-sm border border-slate-200"
                  : "text-slate-600 hover:text-slate-900"
              }`}
            >
              <BallIcon className="w-3 h-3 text-emerald-600 shrink-0" />
              <span>{t.scoreContainer.timelineFilters.keyMoments}</span>
            </button>
            <button
              type="button"
              onClick={() => setFilterCategory("goals")}
              className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer flex items-center gap-1.5 ${
                filterCategory === "goals"
                  ? "bg-white text-slate-900 shadow-sm border border-slate-200"
                  : "text-slate-600 hover:text-slate-900"
              }`}
            >
              <BallIcon className="w-3 h-3 text-emerald-600 shrink-0" />
              <span>{t.scoreContainer.timelineFilters.goals}</span>
            </button>
            <button
              type="button"
              onClick={() => setFilterCategory("window")}
              className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer flex items-center gap-1.5 ${
                filterCategory === "window"
                  ? "bg-teal-600 text-white shadow-sm"
                  : "text-teal-700 hover:text-teal-900"
              }`}
            >
              <span className="w-2 h-2 rounded-full bg-mint shrink-0"></span>
              <span>{t.scoreContainer.timelineFilters.inWindow}</span>
            </button>
          </div>

          {/* Team Filter & Window Indicator */}
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center bg-slate-100 p-1 rounded-xl border border-slate-200 text-xs font-mono">
              <button
                type="button"
                onClick={() => setFilterTeam("all")}
                className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer ${
                  filterTeam === "all"
                    ? "bg-white text-slate-900 shadow-sm border border-slate-200"
                    : "text-slate-600 hover:text-slate-900"
                }`}
              >
                {t.scoreContainer.timelineFilters.bothTeams}
              </button>
              <button
                type="button"
                onClick={() => setFilterTeam("home")}
                className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer flex items-center gap-1 ${
                  filterTeam === "home"
                    ? "bg-red-600 text-white shadow-sm"
                    : "text-red-700 hover:text-red-900"
                }`}
              >
                <span className="w-1.5 h-1.5 rounded-full bg-red-500"></span>
                <span>{homeCode}</span>
              </button>
              <button
                type="button"
                onClick={() => setFilterTeam("away")}
                className={`px-2.5 py-1 rounded-lg font-bold transition-all cursor-pointer flex items-center gap-1 ${
                  filterTeam === "away"
                    ? "bg-rose-600 text-white shadow-sm"
                    : "text-rose-700 hover:text-rose-900"
                }`}
              >
                <span className="w-1.5 h-1.5 rounded-full bg-rose-500"></span>
                <span>{awayCode}</span>
              </button>
            </div>

            <span className="text-[11px] font-mono font-bold text-teal-800 bg-teal-50 px-2.5 py-1 rounded-lg border border-teal-200 flex items-center gap-1.5 shrink-0">
              <span className="w-2 h-2 rounded-full bg-teal-500 animate-pulse shrink-0"></span>
              <span>
                {t.scoreContainer.unpausedWindow}:{" "}
                <span id="window-range-label">
                  {stoppedMinute}&apos; → {currentMinute}&apos;
                </span>
              </span>
            </span>
          </div>
        </div>

        {/* Collision-free Broadcast Timeline Track: [home rows | bar | away rows | ruler] */}
        <div
          className="relative flex bg-slate-50/90 rounded-2xl border border-slate-300 select-none shadow-inner overflow-hidden transition-[height] duration-300"
          style={{ height: `${timelineHeight}px` }}
        >
          {/* Lane background bands */}
          <div
            className="absolute inset-x-0 top-0 bg-white/60 pointer-events-none"
            style={{ height: `${barTop - 3}px` }}
          />

          {/* Measured track area (pins, bar, ruler) */}
          <div ref={trackRef} className="relative flex-1 mx-4 sm:mx-6">
            {/* 15-minute gridlines (HT emphasised) */}
            {TL_TICKS.map((m) => (
              <div
                key={`grid-${m}`}
                aria-hidden="true"
                className={`absolute top-0 w-px -translate-x-1/2 pointer-events-none ${
                  m === 45
                    ? "bg-pl-purple/25"
                    : m === 0 || m === 90
                      ? "bg-transparent"
                      : "bg-slate-300/50"
                }`}
                style={{ left: `${(m / 90) * 100}%`, height: `${rulerTop}px` }}
              />
            ))}

            {/* Live "now" line across both lanes */}
            <div
              aria-hidden="true"
              className="absolute top-0 w-px -translate-x-1/2 bg-red-500/40 pointer-events-none"
              style={{ left: `${nowPct}%`, height: `${rulerTop}px` }}
            />

            {/* Central 90-Minute Track Bar */}
            <div
              className="absolute inset-x-0 bg-slate-200 rounded-full border border-slate-300 overflow-hidden shadow-inner"
              style={{ top: `${barTop}px`, height: `${TL_BAR_H}px` }}
            >
              {/* Watched segment */}
              <div
                id="timeline-watched-bar"
                className="absolute top-0 left-0 h-full bg-slate-400 transition-all duration-300"
                style={{ width: `${watchedPct}%` }}
              ></div>
              {/* Missed / Unpaused Window Highlight */}
              <div
                id="timeline-missed-bar"
                className="absolute top-0 h-full bg-gradient-to-r from-teal-500 via-mint to-sky-500 transition-all duration-300 shadow-[0_0_12px_rgba(20,184,166,0.6)]"
                style={{ left: `${watchedPct}%`, width: `${missedPct}%` }}
              ></div>
            </div>

            {/* Stems (drawn beneath pins so they never cover labels) */}
            {homeLayout.pins.map(({ ev, stemX, row }) => {
              const top = homePinTop(row) + TL_PIN_H;
              const inWindow = ev.minute >= stoppedMinute && ev.minute <= currentMinute;
              const isSelected = activeEv?.eventId === ev.eventId;
              return (
                <div
                  key={`stem-${ev.eventId}`}
                  aria-hidden="true"
                  className={`absolute w-[1.5px] -translate-x-1/2 pointer-events-none ${
                    isSelected ? "bg-pl-purple z-[5]" : inWindow ? "bg-teal-400" : "bg-slate-300"
                  }`}
                  style={{ left: `${stemX}px`, top: `${top}px`, height: `${Math.max(0, barTop - top)}px` }}
                />
              );
            })}
            {awayLayout.pins.map(({ ev, stemX, row }) => {
              const top = barTop + TL_BAR_H;
              const inWindow = ev.minute >= stoppedMinute && ev.minute <= currentMinute;
              const isSelected = activeEv?.eventId === ev.eventId;
              return (
                <div
                  key={`stem-${ev.eventId}`}
                  aria-hidden="true"
                  className={`absolute w-[1.5px] -translate-x-1/2 pointer-events-none ${
                    isSelected ? "bg-pl-purple z-[5]" : inWindow ? "bg-teal-400" : "bg-slate-300"
                  }`}
                  style={{ left: `${stemX}px`, top: `${top}px`, height: `${Math.max(0, awayPinTop(row) - top)}px` }}
                />
              );
            })}

            {/* Live Match Clock Needle on the Track */}
            <div
              className="absolute -translate-x-1/2 -translate-y-1/2 pointer-events-none z-20"
              style={{ left: `${nowPct}%`, top: `${barTop + TL_BAR_H / 2}px` }}
            >
              <span className="absolute inset-0 rounded-full bg-red-600/25 animate-ping"></span>
              <span className="relative block h-5 min-w-5 px-1.5 rounded-full bg-red-600 border-2 border-white shadow-md text-[9px] leading-4 text-center font-mono font-black text-white">
                {currentMinute}&apos;
              </span>
            </div>

            {/* Event Pins */}
            {[
              ...homeLayout.pins.map((p) => ({ ...p, top: homePinTop(p.row) })),
              ...awayLayout.pins.map((p) => ({ ...p, top: awayPinTop(p.row) })),
            ].map(({ ev, pinX, width, top }) => {
              const info = formatTimelineEventTitle(ev, homeCode, homeName, awayName, language);
              const kind = mapEventTypeToAnimationKind(ev.eventType);
              const inWindow = ev.minute >= stoppedMinute && ev.minute <= currentMinute;
              const isSelected = activeEv?.eventId === ev.eventId;
              return (
                <button
                  key={ev.eventId}
                  type="button"
                  style={{ left: `${pinX - width / 2}px`, top: `${top}px`, width: `${width}px` }}
                  onClick={() => {
                    onSelectTimelineEvent(ev);
                    onTriggerAnimation({
                      kind,
                      min: ev.minute,
                      club: info.clubName,
                      player: info.playerLine,
                      sub: info.title,
                    });
                  }}
                  aria-label={`${ev.minute}' ${info.title}`}
                  aria-pressed={isSelected}
                  title={`${ev.minute}' ${info.title}`}
                  className={`timeline-pin absolute h-6 rounded-md text-[10.5px] font-mono font-bold flex items-center justify-center gap-1 cursor-pointer transition-[transform,box-shadow,background-color,color] duration-150 hover:z-30 hover:-translate-y-0.5 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-pl-purple focus-visible:z-30 ${timelineBadgeStyle(
                    ev,
                    inWindow,
                  )} ${isSelected ? "ring-2 ring-pl-purple ring-offset-1 z-20 shadow-lg" : "z-10 shadow-sm"}`}
                >
                  <TimelinePinIcon ev={ev} />
                  <span>{ev.minute}&apos;</span>
                </button>
              );
            })}

            {/* Minute Ruler (own row — nothing can overlay it) */}
            <div
              className="absolute inset-x-0 pointer-events-none border-t border-slate-200"
              style={{ top: `${rulerTop}px`, height: `${TL_RULER_H}px` }}
            >
              {TL_TICKS.map((m) => {
                const label =
                  m === 0
                    ? t.scoreContainer.timelineTicks.kickOff
                    : m === 45
                      ? t.scoreContainer.timelineTicks.halfTime
                      : m === 90
                        ? t.scoreContainer.timelineTicks.fullTime
                        : `${m}'`;
                const align =
                  m === 0 ? "translate-x-0" : m === 90 ? "-translate-x-full" : "-translate-x-1/2";
                return (
                  <span
                    key={`tick-${m}`}
                    className={`absolute top-1 whitespace-nowrap text-[10px] font-mono leading-none ${align} ${
                      m === 45 ? "text-pl-purple font-bold" : "text-slate-500"
                    }`}
                    style={{ left: `${(m / 90) * 100}%` }}
                  >
                    {label}
                  </span>
                );
              })}
            </div>
          </div>

          {/* Empty State when filters yield 0 events */}
          {filteredTimeline.length === 0 && (
            <div className="absolute inset-0 flex items-center justify-center bg-slate-50/90 text-xs font-mono text-slate-500 z-40">
              No events found for this filter combination.
            </div>
          )}
        </div>

        {/* Tactical Event Inspector Drawer (Real Data + /api/v1/explain) */}
        <div
          id="timeline-event-popover"
          className="mt-3 p-4 rounded-xl bg-slate-50 border-l-4 border-pl-purple border-y border-r border-slate-200 flex flex-col sm:flex-row sm:items-center justify-between gap-3"
        >
          <div className="flex items-start gap-3.5">
            <span
              id="event-popover-badge"
              className="px-3 py-1 rounded bg-pl-purple text-white font-mono text-xs font-extrabold shrink-0 mt-0.5 inline-flex items-center gap-1.5"
            >
              {activeEv ? (
                <>
                  {mapEventTypeToAnimationKind(activeEv.eventType) ===
                    "goal" && (
                    <BallIcon className="w-3.5 h-3.5 text-mint-bright" />
                  )}
                  {mapEventTypeToAnimationKind(activeEv.eventType) ===
                    "yellow" && <YellowCardIcon className="w-3 h-3.5" />}
                  {mapEventTypeToAnimationKind(activeEv.eventType) ===
                    "red" && <RedCardIcon className="w-3 h-3.5" />}
                  {mapEventTypeToAnimationKind(activeEv.eventType) ===
                    "sub" && <SubIcon className="w-3.5 h-3.5 text-sky-300" />}
                  <span>
                    {activeEv.minute}&apos;{" "}
                    {(() => {
                      const et = activeEv.eventType.toLowerCase();
                      if (et.includes("goal")) return t.scoreContainer.eventTypes.goal;
                      if (et.includes("yellow")) return t.scoreContainer.eventTypes.yellowCard;
                      if (et.includes("red")) return t.scoreContainer.eventTypes.redCard;
                      if (et.includes("sub")) return t.scoreContainer.eventTypes.substitution;
                      if (et.includes("pressure")) return t.scoreContainer.eventTypes.pressureSequence;
                      if (et.includes("shot")) return t.scoreContainer.eventTypes.shot;
                      if (et.includes("penalty")) return t.scoreContainer.eventTypes.penalty;
                      return activeEv.eventType.toUpperCase();
                    })()}
                  </span>
                </>
              ) : (
                <span>{t.scoreContainer.timelineEvent}</span>
              )}
            </span>
            <div>
              <h3
                id="event-popover-title"
                className="text-sm sm:text-base font-display font-bold uppercase tracking-wide text-slate-900"
              >
                {displayTitle}
              </h3>
              <p
                id="event-popover-desc"
                className="text-xs sm:text-sm text-slate-600 mt-0.5"
              >
                {displayDesc}
              </p>
            </div>
          </div>
          {activeEv && formattedActiveEv && (
            <button
              id="replay-event-fx-btn"
              type="button"
              onClick={() => {
                onTriggerAnimation({
                  kind: mapEventTypeToAnimationKind(activeEv.eventType),
                  min: activeEv.minute,
                  club: formattedActiveEv.clubName,
                  player: formattedActiveEv.playerLine,
                  sub: formattedActiveEv.title,
                });
              }}
              className="text-xs font-mono font-bold uppercase tracking-wider px-3 py-1.5 rounded-lg bg-pl-purple/10 hover:bg-pl-purple/20 text-pl-purple border border-pl-purple/30 shrink-0 transition-colors cursor-pointer"
            >
              {t.scoreContainer.replayAnimation}
            </button>
          )}
        </div>
      </div>
    </section>
  );
};

export default ScoreContainer;
