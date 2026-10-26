import React, { useState, useMemo } from "react";
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
  FilterIcon,
  LiverpoolCrest,
  RedCardIcon,
  SubIcon,
  TargetIcon,
  TimerIcon,
  YellowCardIcon,
} from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

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
  const { t } = useLanguage();
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

  // Multi-tier anti-collision algorithm to guarantee pins never overlap
  const layoutPins = (events: TimelineEvent[]) => {
    const sorted = [...events].sort((a, b) => a.minute - b.minute);
    const lastMin = [-999, -999, -999];
    const COLLISION_MINUTES = 4.2;

    return sorted.map((ev, idx) => {
      let leftPct = Math.min(96, Math.max(4, (ev.minute / 90) * 100));
      const inWindow = ev.minute >= stoppedMinute && ev.minute <= currentMinute;
      const kind = mapEventTypeToAnimationKind(ev.eventType);
      const isSelected = activeEv?.eventId === ev.eventId;

      const dists = [
        Math.abs(ev.minute - lastMin[0]),
        Math.abs(ev.minute - lastMin[1]),
        Math.abs(ev.minute - lastMin[2]),
      ];

      let chosenTier = -1;
      for (let i = 0; i < 3; i++) {
        if (dists[i] >= COLLISION_MINUTES) {
          chosenTier = i;
          break;
        }
      }

      if (chosenTier === -1) {
        chosenTier = dists.indexOf(Math.max(...dists));
        if (dists[chosenTier] < 2.5) {
          const nudge = idx % 2 === 0 ? -1.2 : 1.2;
          leftPct = Math.min(96, Math.max(4, leftPct + nudge));
        }
      }

      lastMin[chosenTier] = ev.minute;

      return {
        ev,
        leftPct,
        tier: chosenTier,
        inWindow,
        kind,
        isSelected,
      };
    });
  };

  const positionedHome = useMemo(
    () => layoutPins(homeEvents),
    [homeEvents, stoppedMinute, currentMinute, activeEv],
  );
  const positionedAway = useMemo(
    () => layoutPins(awayEvents),
    [awayEvents, stoppedMinute, currentMinute, activeEv],
  );

  const formattedActiveEv = activeEv
    ? formatTimelineEventTitle(activeEv, homeCode, homeName, awayName)
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
                    {match?.statistics.shots.home ?? 0} (
                    {match?.statistics.shotsOnTarget.home ?? 0} OT)
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
                <LiverpoolCrest />
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

        {/* Custom Daylight Multi-Tier Broadcast Timeline Track */}
        <div className="relative h-[192px] bg-slate-50/90 rounded-2xl border border-slate-300 select-none shadow-inner overflow-hidden">
          {/* Subtle Team Lane Banners */}
          <div className="absolute left-3 top-2 flex items-center gap-1.5 pointer-events-none select-none z-0 opacity-80">
            <span className="w-2 h-2 rounded-full bg-red-600"></span>
            <span className="text-[10px] font-mono font-bold uppercase tracking-wider text-slate-500">
              {homeName} ({homeCode}) • {t.scoreContainer.timelineFilters.homeLane}
            </span>
          </div>
          <div className="absolute left-3 bottom-2 flex items-center gap-1.5 pointer-events-none select-none z-0 opacity-80">
            <span className="w-2 h-2 rounded-full bg-rose-600"></span>
            <span className="text-[10px] font-mono font-bold uppercase tracking-wider text-slate-500">
              {awayName} ({awayCode}) • {t.scoreContainer.timelineFilters.awayLane}
            </span>
          </div>

          {/* Minute Tick Labels */}
          <div className="absolute inset-x-6 top-[74px] flex justify-between text-[10px] font-mono text-slate-500 pointer-events-none z-0">
            <span>{t.scoreContainer.timelineTicks.kickOff}</span>
            <span>15&apos;</span>
            <span>30&apos;</span>
            <span className="text-pl-purple font-bold">
              {t.scoreContainer.timelineTicks.halfTime}
            </span>
            <span className="text-red-600 font-bold">60&apos;</span>
            <span>75&apos;</span>
            <span>{t.scoreContainer.timelineTicks.fullTime}</span>
          </div>

          {/* Central 90-Minute Track Bar */}
          <div className="absolute inset-x-6 top-[92px] h-3 bg-slate-200 rounded-full border border-slate-300 overflow-hidden shadow-inner">
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

          {/* Live Match Clock Needle Cursor on the Track */}
          <div
            className="absolute top-[88px] -translate-x-1/2 pointer-events-none z-10 flex flex-col items-center"
            style={{ left: `calc(1.5rem + (100% - 3rem) * ${Math.min(100, (currentMinute / 90) * 100) / 100})` }}
          >
            <span className="w-5 h-5 rounded-full bg-red-600/20 animate-ping absolute -top-0.5"></span>
            <span className="w-5 h-5 rounded-full bg-red-600 border-2 border-white shadow-md flex items-center justify-center text-[9px] font-mono font-black text-white">
              {currentMinute}&apos;
            </span>
          </div>

          {/* Stem Lines & Pins Container */}
          <div className="absolute inset-x-6 inset-y-0 pointer-events-none">
            {/* HOME TEAM EVENTS (TOP LANE: Tiers 2, 1, 0) */}
            {positionedHome.map((pos) => {
              const { ev, leftPct, tier, inWindow, kind, isSelected } = pos;
              const info = formatTimelineEventTitle(ev, homeCode, homeName, awayName);
              const homeTiersTop = [50, 26, 2];
              const topPx = homeTiersTop[tier];
              const stemTop = topPx + 24;
              const stemHeight = Math.max(0, 92 - stemTop);

              const isGoal = kind === "goal";
              const isRed = kind === "red";
              const isYellow = kind === "yellow";
              const isPenalty = ev.eventType.toLowerCase().includes("penalty");
              const isShot = ev.eventType.toLowerCase().includes("shot");
              const isPressure = ev.eventType.toLowerCase().includes("pressure");

              let badgeStyle = "bg-white text-slate-700 border border-slate-300 hover:border-slate-500";
              if (inWindow) {
                if (isGoal) badgeStyle = "bg-emerald-600 text-white ring-2 ring-emerald-300 shadow-md";
                else if (isRed) badgeStyle = "bg-red-600 text-white ring-2 ring-red-300 shadow-md";
                else if (isYellow) badgeStyle = "bg-amber-400 text-slate-950 ring-2 ring-amber-200 shadow-md";
                else if (isPenalty) badgeStyle = "bg-purple-700 text-white ring-2 ring-purple-300 shadow-md";
                else if (isShot) badgeStyle = "bg-slate-700 text-white ring-2 ring-slate-300 shadow-md";
                else if (isPressure) badgeStyle = "bg-indigo-600 text-white ring-2 ring-indigo-300 shadow-md";
                else badgeStyle = "bg-sky-600 text-white ring-2 ring-sky-300 shadow-md";
              } else {
                if (isGoal) badgeStyle = "bg-emerald-50 text-emerald-800 border border-emerald-400 hover:bg-emerald-600 hover:text-white";
                else if (isRed) badgeStyle = "bg-red-50 text-red-800 border border-red-400 hover:bg-red-600 hover:text-white";
                else if (isYellow) badgeStyle = "bg-amber-50 text-amber-900 border border-amber-400 hover:bg-amber-400 hover:text-slate-950";
                else if (isPenalty) badgeStyle = "bg-purple-50 text-purple-900 border border-purple-400 hover:bg-purple-700 hover:text-white";
                else if (isShot) badgeStyle = "bg-white text-slate-600 border border-slate-300 hover:bg-slate-700 hover:text-white";
                else if (isPressure) badgeStyle = "bg-indigo-50 text-indigo-800 border border-indigo-300 hover:bg-indigo-600 hover:text-white";
                else badgeStyle = "bg-sky-50 text-sky-800 border border-sky-300 hover:bg-sky-600 hover:text-white";
              }

              return (
                <React.Fragment key={ev.eventId}>
                  {/* Stem line connecting pin to track */}
                  <div
                    style={{ left: `${leftPct}%`, top: `${stemTop}px`, height: `${stemHeight}px` }}
                    className={`absolute w-[1.5px] -translate-x-1/2 pointer-events-none transition-colors ${
                      isSelected ? "bg-pl-purple z-20" : inWindow ? "bg-teal-400 z-10" : "bg-slate-300/80"
                    }`}
                  />
                  {/* Event Pin Button */}
                  <button
                    type="button"
                    style={{ left: `${leftPct}%`, top: `${topPx}px` }}
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
                    className={`timeline-pin pointer-events-auto -translate-x-1/2 absolute h-6 px-1.5 py-0.5 rounded-md text-[10.5px] font-mono font-bold transition-all duration-150 flex items-center gap-1 cursor-pointer z-10 hover:z-30 hover:scale-115 shadow-sm ${badgeStyle} ${
                      isSelected ? "ring-2 ring-pl-purple scale-110 z-30 shadow-lg" : ""
                    }`}
                    title={`${ev.minute}' ${info.title}`}
                  >
                    {isGoal && <BallIcon className="w-3 h-3 shrink-0" />}
                    {isYellow && <YellowCardIcon className="w-2.5 h-3 shrink-0" />}
                    {isRed && <RedCardIcon className="w-2.5 h-3 shrink-0" />}
                    {isPenalty && <BallIcon className="w-3 h-3 text-purple-300 shrink-0" />}
                    {isShot && <TargetIcon className="w-2.5 h-2.5 shrink-0" />}
                    {isPressure && <TimerIcon className="w-2.5 h-2.5 shrink-0" />}
                    {!isGoal && !isYellow && !isRed && !isPenalty && !isShot && !isPressure && (
                      <SubIcon className="w-3 h-3 shrink-0" />
                    )}
                    <span>{ev.minute}&apos;</span>
                  </button>
                </React.Fragment>
              );
            })}

            {/* AWAY TEAM EVENTS (BOTTOM LANE: Tiers 0, 1, 2) */}
            {positionedAway.map((pos) => {
              const { ev, leftPct, tier, inWindow, kind, isSelected } = pos;
              const info = formatTimelineEventTitle(ev, homeCode, homeName, awayName);
              const awayTiersTop = [110, 136, 162];
              const topPx = awayTiersTop[tier];
              const stemTop = 104;
              const stemHeight = Math.max(0, topPx - stemTop);

              const isGoal = kind === "goal";
              const isRed = kind === "red";
              const isYellow = kind === "yellow";
              const isPenalty = ev.eventType.toLowerCase().includes("penalty");
              const isShot = ev.eventType.toLowerCase().includes("shot");
              const isPressure = ev.eventType.toLowerCase().includes("pressure");

              let badgeStyle = "bg-white text-slate-700 border border-slate-300 hover:border-slate-500";
              if (inWindow) {
                if (isGoal) badgeStyle = "bg-emerald-600 text-white ring-2 ring-emerald-300 shadow-md";
                else if (isRed) badgeStyle = "bg-red-600 text-white ring-2 ring-red-300 shadow-md";
                else if (isYellow) badgeStyle = "bg-amber-400 text-slate-950 ring-2 ring-amber-200 shadow-md";
                else if (isPenalty) badgeStyle = "bg-purple-700 text-white ring-2 ring-purple-300 shadow-md";
                else if (isShot) badgeStyle = "bg-slate-700 text-white ring-2 ring-slate-300 shadow-md";
                else if (isPressure) badgeStyle = "bg-indigo-600 text-white ring-2 ring-indigo-300 shadow-md";
                else badgeStyle = "bg-sky-600 text-white ring-2 ring-sky-300 shadow-md";
              } else {
                if (isGoal) badgeStyle = "bg-emerald-50 text-emerald-800 border border-emerald-400 hover:bg-emerald-600 hover:text-white";
                else if (isRed) badgeStyle = "bg-red-50 text-red-800 border border-red-400 hover:bg-red-600 hover:text-white";
                else if (isYellow) badgeStyle = "bg-amber-50 text-amber-900 border border-amber-400 hover:bg-amber-400 hover:text-slate-950";
                else if (isPenalty) badgeStyle = "bg-purple-50 text-purple-900 border border-purple-400 hover:bg-purple-700 hover:text-white";
                else if (isShot) badgeStyle = "bg-white text-slate-600 border border-slate-300 hover:bg-slate-700 hover:text-white";
                else if (isPressure) badgeStyle = "bg-indigo-50 text-indigo-800 border border-indigo-300 hover:bg-indigo-600 hover:text-white";
                else badgeStyle = "bg-sky-50 text-sky-800 border border-sky-300 hover:bg-sky-600 hover:text-white";
              }

              return (
                <React.Fragment key={ev.eventId}>
                  {/* Stem line connecting pin to track */}
                  <div
                    style={{ left: `${leftPct}%`, top: `${stemTop}px`, height: `${stemHeight}px` }}
                    className={`absolute w-[1.5px] -translate-x-1/2 pointer-events-none transition-colors ${
                      isSelected ? "bg-pl-purple z-20" : inWindow ? "bg-teal-400 z-10" : "bg-slate-300/80"
                    }`}
                  />
                  {/* Event Pin Button */}
                  <button
                    type="button"
                    style={{ left: `${leftPct}%`, top: `${topPx}px` }}
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
                    className={`timeline-pin pointer-events-auto -translate-x-1/2 absolute h-6 px-1.5 py-0.5 rounded-md text-[10.5px] font-mono font-bold transition-all duration-150 flex items-center gap-1 cursor-pointer z-10 hover:z-30 hover:scale-115 shadow-sm ${badgeStyle} ${
                      isSelected ? "ring-2 ring-pl-purple scale-110 z-30 shadow-lg" : ""
                    }`}
                    title={`${ev.minute}' ${info.title}`}
                  >
                    {isGoal && <BallIcon className="w-3 h-3 shrink-0" />}
                    {isYellow && <YellowCardIcon className="w-2.5 h-3 shrink-0" />}
                    {isRed && <RedCardIcon className="w-2.5 h-3 shrink-0" />}
                    {isPenalty && <BallIcon className="w-3 h-3 text-purple-300 shrink-0" />}
                    {isShot && <TargetIcon className="w-2.5 h-2.5 shrink-0" />}
                    {isPressure && <TimerIcon className="w-2.5 h-2.5 shrink-0" />}
                    {!isGoal && !isYellow && !isRed && !isPenalty && !isShot && !isPressure && (
                      <SubIcon className="w-3 h-3 shrink-0" />
                    )}
                    <span>{ev.minute}&apos;</span>
                  </button>
                </React.Fragment>
              );
            })}
          </div>

          {/* Empty State when filters yield 0 events */}
          {filteredTimeline.length === 0 && (
            <div className="absolute inset-0 flex items-center justify-center bg-slate-50/90 text-xs font-mono text-slate-500 z-20">
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
                    {activeEv.minute}&apos; {activeEv.eventType.toUpperCase()}
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
