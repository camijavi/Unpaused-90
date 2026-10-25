import React from "react";
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
  ArsenalCrest,
  BallIcon,
  LiverpoolCrest,
  RedCardIcon,
  SubIcon,
  YellowCardIcon,
} from "./Icons";

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
  awayName: string
): {
  clubName: string;
  playerLine: string;
  title: string;
  fallbackDesc: string;
} {
  const clubName = ev.team === homeCode ? homeName : awayName;
  if (ev.eventType === "Goal") {
    const assistText = ev.assist ? ` (Assist: ${ev.assist})` : "";
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: `${ev.player ?? clubName} Goal (${ev.minute}')${assistText}`,
      fallbackDesc: `Clinical finish by ${ev.player ?? clubName}${
        ev.assist ? ` following a decisive pass from ${ev.assist}` : ""
      } in the ${ev.minute}th minute.`,
    };
  }
  if (ev.eventType === "YellowCard") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: `${ev.player ?? clubName} Tactical Yellow Card (${ev.minute}')`,
      fallbackDesc: `Official disciplinary caution shown to ${
        ev.player ?? clubName
      } at ${ev.minute}', forcing extra caution in midfield duels.`,
    };
  }
  if (ev.eventType === "RedCard") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
      title: `${ev.player ?? clubName} Red Card Dismissal (${ev.minute}')`,
      fallbackDesc: `${clubName} reduced to 10 men after ${
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
      title: `Tactical Substitution (${ev.minute}'): ${ev.playerIn} replaces ${ev.playerOut}`,
      fallbackDesc: `Fresh legs introduced by ${clubName} at ${ev.minute}' as ${ev.playerIn} comes on for ${ev.playerOut}.`,
    };
  }
  if (ev.eventType === "PressureSequence") {
    return {
      clubName: clubName.toUpperCase(),
      playerLine: `${clubName} High Press (${ev.durationSeconds ?? 90}s Wave)`,
      title: `${clubName} Sustained Pressure Sequence (${ev.minute}')`,
      fallbackDesc: `${clubName} pinned the opposition inside their defensive third for ${
        ev.durationSeconds ?? 90
      } seconds (Importance: ${ev.importanceScore ?? 80}/100).`,
    };
  }
  return {
    clubName: clubName.toUpperCase(),
    playerLine: `${ev.player ?? clubName} (${ev.minute}')`,
    title: `${ev.player ?? clubName} ${ev.eventType} (${ev.minute}')${
      ev.onTarget ? " — On Target" : ""
    }`,
    fallbackDesc: `Key ${ev.eventType.toLowerCase()} by ${
      ev.player ?? clubName
    } at minute ${ev.minute}'.`,
  };
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
  const homeCode = match?.homeTeam.teamId ?? "ARS";
  const awayCode = match?.awayTeam.teamId ?? "LIV";
  const homeName = match?.homeTeam.name ?? "Arsenal";
  const awayName = match?.awayTeam.name ?? "Liverpool";
  const homeFormation = match?.homeTeam.formation ?? "4-3-3";
  const awayFormation = match?.awayTeam.formation ?? "4-2-3-1";
  const homeScore = match?.homeTeam.score ?? 2;
  const awayScore = match?.awayTeam.score ?? 1;
  const competition = match?.competition ?? "Premier League";
  const season = match?.season ?? "2026/27";
  const stadium = match?.stadium ?? "Emirates Stadium";
  const possessionHome = match?.statistics.possession.home ?? 54;
  const possessionAway = match?.statistics.possession.away ?? 46;

  // Derive real goal scorers and disciplinary notes from fetched timeline
  const homeGoals = timeline.filter(
    (e) => e.team === homeCode && e.eventType === "Goal"
  );
  const awayGoals = timeline.filter(
    (e) => e.team === awayCode && e.eventType === "Goal"
  );
  const awayCards = timeline.filter(
    (e) =>
      e.team === awayCode &&
      (e.eventType === "YellowCard" || e.eventType === "RedCard")
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
    Math.max(0, ((currentMinute - stoppedMinute) / 90) * 100)
  );

  // Active event for the inspector drawer
  const activeEv =
    selectedEvent ??
    timeline.find((e) => e.eventType === "Goal" && e.team === awayCode) ??
    timeline[0] ??
    null;

  const formattedActiveEv = activeEv
    ? formatTimelineEventTitle(activeEv, homeCode, homeName, awayName)
    : null;

  const matchingInsight = activeEv
    ? insights.find((i) => i.minute === activeEv.minute)
    : null;

  const displayTitle =
    eventExplanation?.title ||
    formattedActiveEv?.title ||
    "Select a timeline event pin to inspect tactical context";

  const displayDesc = eventExplanation?.loading
    ? "Generating live AI tactical breakdown from Azure OpenAI..."
    : eventExplanation?.explanation ||
      (matchingInsight
        ? `Why It Matters: ${matchingInsight.summary} — ${matchingInsight.reason}`
        : formattedActiveEv?.fallbackDesc ||
          "Click any event pin on the match timeline above to view its tactical explanation.");

  const isShaking =
    activeAnimation?.kind === "goal" || activeAnimation?.kind === "red";

  const handleQuickFx = (kind: AnimationKind) => {
    if (kind === "goal") {
      const goalEv =
        timeline.find((e) => e.eventType === "Goal" && e.team === awayCode) ??
        timeline.find((e) => e.eventType === "Goal");
      if (goalEv) {
        const info = formatTimelineEventTitle(
          goalEv,
          homeCode,
          homeName,
          awayName
        );
        onTriggerAnimation({
          kind: "goal",
          min: goalEv.minute,
          club: info.clubName,
          player: info.playerLine,
          sub: info.title,
        });
        return;
      }
    }
    if (kind === "yellow") {
      const yellowEv = timeline.find((e) => e.eventType === "YellowCard");
      if (yellowEv) {
        const info = formatTimelineEventTitle(
          yellowEv,
          homeCode,
          homeName,
          awayName
        );
        onTriggerAnimation({
          kind: "yellow",
          min: yellowEv.minute,
          club: info.clubName,
          player: info.playerLine,
          sub: info.title,
        });
        return;
      }
    }
    // Red card or fallback
    onTriggerAnimation({
      kind,
      min: 58,
      club: awayName.toUpperCase(),
      player: "Ibrahima Konaté (58')",
      sub: "SENT OFF! • LAST-MAN PROFESSIONAL FOUL",
    });
  };

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
      <div className="px-4 sm:px-6 py-2.5 bg-slate-50 border-b border-slate-200 flex flex-wrap items-center justify-between gap-2 text-[11px] font-mono text-slate-600">
        <div className="flex items-center gap-2">
          <span className="text-pl-purple font-extrabold uppercase">
            {competition} • {season}
          </span>
          <span>•</span>
          <span id="match-venue" className="text-slate-900 font-bold">
            {stadium}
          </span>
        </div>

        {/* Live Event Animation Trigger Bar (SVG Icons Only, No Emojis) */}
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-[10px] font-mono uppercase tracking-wider text-slate-500 mr-1">
            Test Broadcast FX:
          </span>
          <button
            id="fx-btn-goal"
            type="button"
            onClick={() => handleQuickFx("goal")}
            className="px-2.5 py-1 rounded-md bg-emerald-500 hover:bg-emerald-600 text-white font-mono font-extrabold text-[11px] shadow-sm transition-transform active:scale-95 flex items-center gap-1.5 cursor-pointer"
          >
            <BallIcon className="w-3.5 h-3.5" />
            <span>Goal FX</span>
          </button>
          <button
            id="fx-btn-yellow"
            type="button"
            onClick={() => handleQuickFx("yellow")}
            className="px-2.5 py-1 rounded-md bg-amber-400 hover:bg-amber-500 text-slate-950 font-mono font-extrabold text-[11px] shadow-sm transition-transform active:scale-95 flex items-center gap-1.5 cursor-pointer"
          >
            <YellowCardIcon className="w-3 h-3.5" />
            <span>Yellow Card FX</span>
          </button>
          <button
            id="fx-btn-red"
            type="button"
            onClick={() => handleQuickFx("red")}
            className="px-2.5 py-1 rounded-md bg-red-600 hover:bg-red-700 text-white font-mono font-extrabold text-[11px] shadow-sm transition-transform active:scale-95 flex items-center gap-1.5 cursor-pointer"
          >
            <RedCardIcon className="w-3 h-3.5" />
            <span>Red Card FX</span>
          </button>
        </div>
      </div>

      {/* HERO SCORE CONTAINER WITH PANORAMIC NEUTRAL STADIUM BACKGROUND IMAGE */}
      <div
        id="stadium-score-hero"
        className={`relative overflow-hidden min-h-[300px] sm:min-h-[340px] lg:min-h-[380px] flex flex-col justify-center ${
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
                <ArsenalCrest />
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
                    {match?.statistics.shots.home ?? 14} (
                    {match?.statistics.shotsOnTarget.home ?? 6} OT)
                  </span>
                </p>
              </div>
            </div>

            {/* CENTER BROADCAST SCOREBUG (3 cols) */}
            <div className="lg:col-span-3 flex flex-col items-center justify-center">
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
                    {currentMinute}&apos;
                  </span>
                  <span className="text-[10px] font-mono font-bold uppercase tracking-widest text-slate-500 mt-1.5">
                    2ND HALF
                  </span>
                </div>
                <span
                  id="away-score"
                  className="text-5xl sm:text-6xl font-display font-black text-slate-900 leading-none"
                >
                  {awayScore}
                </span>
              </div>
              <span
                id="match-possession"
                className="mt-3 px-3.5 py-1 rounded-full bg-slate-900/85 backdrop-blur-md border border-white/20 text-xs font-mono font-bold text-white shadow"
              >
                POSSESSION: {possessionHome}% – {possessionAway}%
              </span>
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
                  <span>{awayScorersText}</span>
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
                <LiverpoolCrest />
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* LIGHT-MODE MATCH TIMELINE SCRUBBER & EVENT PINS (REAL DATA) */}
      <div className="p-4 sm:p-6 bg-white border-t border-slate-200">
        <div className="flex flex-wrap items-center justify-between gap-2 mb-2.5">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="px-2.5 py-0.5 rounded bg-pl-purple text-white font-mono text-[11px] font-bold uppercase">
              MATCH TIMELINE &amp; LIVE FX
            </span>
            <span className="text-xs text-slate-600 flex items-center gap-1.5 flex-wrap">
              <span>Click any event pin (</span>
              <strong className="text-emerald-700 inline-flex items-center gap-1">
                <BallIcon className="w-3.5 h-3.5" /> Goal
              </strong>
              <span>,</span>
              <strong className="text-amber-600 inline-flex items-center gap-1">
                <YellowCardIcon className="w-3 h-3.5" /> Yellow
              </strong>
              <span>,</span>
              <strong className="text-sky-700 inline-flex items-center gap-1">
                <SubIcon className="w-3.5 h-3.5" /> Tactical
              </strong>
              <span>
                ) to trigger its broadcast animation &amp; tactical breakdown
              </span>
            </span>
          </div>
          <div className="flex items-center gap-4 text-[11px] font-mono">
            <span className="flex items-center gap-1.5 text-slate-500">
              <span className="w-3 h-2 rounded-sm bg-slate-400 inline-block"></span>{" "}
              Watched Live
            </span>
            <span className="flex items-center gap-1.5 text-teal-700 font-bold">
              <span className="w-3 h-2 rounded-sm bg-mint inline-block"></span>{" "}
              Unpaused Window (
              <span id="window-range-label">
                {stoppedMinute}&apos; → {currentMinute}&apos;
              </span>
              )
            </span>
          </div>
        </div>

        {/* Custom Daylight Broadcast Timeline Track */}
        <div className="relative h-16 bg-slate-100 rounded-xl border border-slate-300 flex items-center px-4 select-none overflow-hidden shadow-inner">
          {/* Pitch Halfway & 15-min Grid Lines */}
          <div className="absolute inset-x-4 top-1.5 flex justify-between text-[10px] font-mono text-slate-500 pointer-events-none">
            <span>KO 0&apos;</span>
            <span>15&apos;</span>
            <span>30&apos;</span>
            <span className="text-pl-purple font-bold">HT 45&apos;</span>
            <span className="text-red-600 font-bold">60&apos;</span>
            <span>75&apos;</span>
            <span>FT 90&apos;</span>
          </div>

          {/* Base 90-Minute Track */}
          <div className="relative w-full h-3 bg-slate-300 rounded-sm mt-3 overflow-hidden">
            {/* Watched segment */}
            <div
              id="timeline-watched-bar"
              className="absolute top-0 left-0 h-full bg-slate-500 transition-all duration-300"
              style={{ width: `${watchedPct}%` }}
            ></div>
            {/* Missed / Unpaused Window Highlight */}
            <div
              id="timeline-missed-bar"
              className="absolute top-0 h-full bg-gradient-to-r from-teal-500 via-mint to-sky-500 transition-all duration-300"
              style={{ left: `${watchedPct}%`, width: `${missedPct}%` }}
            ></div>
          </div>

          {/* Interactive Timeline Event Pins from Real Backend Data */}
          <div
            id="timeline-markers-container"
            className="absolute inset-x-4 inset-y-0 pointer-events-none"
          >
            {timeline.map((ev) => {
              const leftPct = Math.min(96, Math.max(4, (ev.minute / 90) * 100));
              const inWindow =
                ev.minute >= stoppedMinute && ev.minute <= currentMinute;
              const kind = mapEventTypeToAnimationKind(ev.eventType);
              const isSelected = activeEv?.eventId === ev.eventId;

              const colorClass =
                kind === "red"
                  ? "bg-red-600 text-white ring-red-300"
                  : kind === "yellow"
                  ? "bg-amber-400 text-slate-950 ring-amber-200"
                  : kind === "goal"
                  ? "bg-emerald-600 text-white ring-emerald-300"
                  : "bg-sky-600 text-white ring-sky-200";

              const info = formatTimelineEventTitle(
                ev,
                homeCode,
                homeName,
                awayName
              );

              return (
                <button
                  key={ev.eventId}
                  type="button"
                  style={{ left: `${leftPct}%` }}
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
                  className={`timeline-pin pointer-events-auto -translate-x-1/2 top-5 absolute px-2 py-0.5 rounded-md text-[11px] font-mono font-extrabold transition-transform hover:scale-125 shadow flex items-center gap-1 cursor-pointer ${
                    inWindow
                      ? `${colorClass} ring-4`
                      : "bg-white text-slate-700 border border-slate-300"
                  } ${isSelected ? "scale-110 ring-pl-purple" : ""}`}
                  title={`${ev.minute}' ${info.title}`}
                >
                  {kind === "goal" && <BallIcon className="w-3 h-3 shrink-0" />}
                  {kind === "yellow" && (
                    <YellowCardIcon className="w-2.5 h-3 shrink-0" />
                  )}
                  {kind === "red" && (
                    <RedCardIcon className="w-2.5 h-3 shrink-0" />
                  )}
                  {kind === "sub" && <SubIcon className="w-3 h-3 shrink-0" />}
                  <span>{ev.minute}&apos;</span>
                </button>
              );
            })}
          </div>
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
                    {activeEv.minute}&apos; {activeEv.eventType.toUpperCase()}
                  </span>
                </>
              ) : (
                <span>TIMELINE EVENT</span>
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
          <button
            id="replay-event-fx-btn"
            type="button"
            onClick={() => {
              if (!activeEv || !formattedActiveEv) return;
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
            ▶ Replay Event Animation
          </button>
        </div>
      </div>
    </section>
  );
};

export default ScoreContainer;
