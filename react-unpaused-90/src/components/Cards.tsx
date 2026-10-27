import React from "react";
import type {
  CatchUpResponse,
  GeneratedInsight,
  MatchData,
  TimelineEvent,
} from "../types/match";
import {
  BallIcon,
  RedCardIcon,
  SubIcon,
  YellowCardIcon,
} from "./Icons";
import { mapEventTypeToAnimationKind } from "./ScoreContainer";
import { useLanguage } from "../languages/LanguageContext";

interface CardsProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  insights: GeneratedInsight[];
  stoppedMinute: number;
  currentMinute: number;
  catchUp: CatchUpResponse | null;
  isGenerating: boolean;
  statusBadgeText: string;
  onClose: () => void;
}

function formatInsightType(type?: string): string {
  if (!type) return "Tactical Shift";
  return type.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export const Cards: React.FC<CardsProps> = ({
  match,
  timeline,
  insights,
  stoppedMinute,
  currentMinute,
  catchUp,
  isGenerating,
  statusBadgeText,
  onClose,
}) => {
  const { t } = useLanguage();

  // Strictly bind the window to the generated catchUp window to avoid drifting as the live clock ticks
  const windowStart = catchUp?.lastViewedMinute ?? stoppedMinute;
  const windowEnd = catchUp?.currentMinute ?? currentMinute;
  const windowStr = `${windowStart}' – ${windowEnd}'`;

  const homeCode = match?.homeTeam?.teamId ?? "";
  const awayCode = match?.awayTeam?.teamId ?? "";
  const homeName = match?.homeTeam?.name ?? "Home";
  const awayName = match?.awayTeam?.name ?? "Away";

  // Dynamic possession: extract from catchUp narrative if present, or fallback to live match stats
  const posRegexMatch = catchUp?.momentumChanges.match(
    /(\d{1,2})%\s+(?:for|para)\s+.*?\s+(\d{1,2})%/i
  );
  const possessionHome = posRegexMatch
    ? parseInt(posRegexMatch[1], 10)
    : (match?.statistics?.possession?.home ?? 50);
  const possessionAway = posRegexMatch
    ? parseInt(posRegexMatch[2], 10)
    : (match?.statistics?.possession?.away ?? 50);

  // Real events occurring strictly within the user's missed window
  const windowEvents = timeline.filter(
    (ev) => ev.minute >= windowStart && ev.minute <= windowEnd
  );
  const displayWindowEvents = windowEvents.slice(0, 3);
  const windowGoals = windowEvents.filter((e) => e.eventType === "Goal").length;
  const windowReds = windowEvents.filter((e) => e.eventType === "RedCard").length;

  // Real tactical insight strictly matching the window, or the nearest prior event
  const windowInsight =
    insights.find((i) => i.minute >= windowStart && i.minute <= windowEnd) ??
    insights.find((i) => i.minute <= windowEnd) ??
    insights[0];

  // Dynamic score and numerical status strictly at return minute
  const homeGoalsAtReturn = timeline.filter(
    (e) =>
      e.minute <= windowEnd &&
      e.eventType === "Goal" &&
      (e.team === homeCode || e.team === homeName)
  ).length;
  const awayGoalsAtReturn = timeline.filter(
    (e) =>
      e.minute <= windowEnd &&
      e.eventType === "Goal" &&
      (e.team === awayCode || e.team === awayName)
  ).length;

  const homeRedsAtReturn = timeline.filter(
    (e) =>
      e.minute <= windowEnd &&
      e.eventType === "RedCard" &&
      (e.team === homeCode || e.team === homeName)
  ).length;
  const awayRedsAtReturn = timeline.filter(
    (e) =>
      e.minute <= windowEnd &&
      e.eventType === "RedCard" &&
      (e.team === awayCode || e.team === awayName)
  ).length;

  const isNumericalDisparity = homeRedsAtReturn !== awayRedsAtReturn;

  const situationDescription = isNumericalDisparity
    ? `${11 - homeRedsAtReturn}v${11 - awayRedsAtReturn} • ${
        homeRedsAtReturn > awayRedsAtReturn
          ? `${awayName} Man Advantage`
          : `${homeName} Man Advantage`
      }`
    : windowEnd >= 75
    ? "Late-Game High Danger Phase"
    : windowEnd >= 45
    ? "2nd Half Tactical Adjustments"
    : "1st Half Momentum Battle";

  return (
    <section aria-label={t.cards.deckTitle} className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900 flex items-center gap-2">
            <BallIcon className="w-5 h-5 text-pl-purple shrink-0" />
            <span>{t.cards.deckTitle}</span>
          </h2>
          <span
            id="cards-status-badge"
            className="text-xs font-mono font-bold px-2.5 py-0.5 rounded bg-teal-100 text-teal-800 border border-teal-300"
          >
            {statusBadgeText || `${t.cards.deckWindow} ${windowStr}`}
          </span>
          {catchUp?.wordCount ? (
            <span
              id="cards-length-badge"
              className="hidden sm:inline-flex text-[11px] font-mono font-bold px-2 py-0.5 rounded bg-slate-100 text-slate-700 border border-slate-300"
            >
              {catchUp.length === "5m"
                ? "5m Deep"
                : catchUp.length === "full"
                ? "Full Match"
                : "1m Sprint"}{" "}
              • {catchUp.wordCount}w • ~{catchUp.estimatedDurationSeconds}s
            </span>
          ) : null}
        </div>

        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onClose}
            aria-label={t.cards.dismiss}
            title={t.cards.dismiss}
            className="w-8 h-8 rounded-lg bg-slate-100 hover:bg-red-50 text-slate-600 hover:text-red-600 border border-slate-300 hover:border-red-300 flex items-center justify-center font-mono text-sm font-bold transition-colors cursor-pointer"
          >
            ✕
          </button>
        </div>
      </div>

      {/* Responsive Grid: 1 col mobile, 2 cols tablet, 4 cols desktop */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
        {/* Card 1: What Happened (Chronology + Real Event Tape) */}
        <article className="editorial-card rounded-2xl p-4 sm:p-5 flex flex-col h-[390px] sm:h-[410px] relative overflow-hidden shadow-sm">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-teal-500"></div>

          {/* Header */}
          <div className="shrink-0 mb-2">
            <div className="flex items-center justify-between gap-2 mb-2">
              <span className="px-2.5 py-0.5 rounded bg-teal-50 text-teal-800 border border-teal-200 font-mono text-[11px] font-extrabold uppercase tracking-wide">
                {t.cards.card1.tag}
              </span>
              <span
                className="text-xs font-mono font-bold text-slate-500"
                id="card1-window"
              >
                {windowStr}
              </span>
            </div>
            <h3
              className="text-lg sm:text-xl font-display font-black uppercase tracking-wide text-slate-900 line-clamp-1"
              id="card1-title"
            >
              {t.cards.card1.title}
            </h3>
          </div>

          {/* Body: Scrollable with custom scrollbar, takes all remaining space */}
          <div className="flex-1 min-h-0 overflow-y-auto pr-1.5 custom-scrollbar">
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed whitespace-pre-line ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card1-body"
            >
              {catchUp?.whatHappened ?? t.cards.card1.fallback}
            </p>
          </div>

          {/* Visual Event Tape Footer strictly from Real Window Events (Always pinned at bottom) */}
          <div className="mt-3 pt-3 border-t border-slate-200 space-y-1.5 shrink-0">
            {displayWindowEvents.length > 0 ? (
              <div className="flex items-center justify-between gap-1 text-[11px] font-mono flex-wrap">
                {displayWindowEvents.map((ev) => {
                  const kind = mapEventTypeToAnimationKind(ev.eventType);
                  const shortName =
                    (ev.player ?? ev.playerIn ?? ev.team).split(" ").pop() ??
                    ev.team;
                  const textColor =
                    kind === "goal"
                      ? "text-emerald-700"
                      : kind === "yellow"
                      ? "text-amber-700"
                      : kind === "red"
                      ? "text-red-600"
                      : "text-sky-700";

                  return (
                    <span
                      key={ev.eventId}
                      className={`${textColor} font-bold inline-flex items-center gap-1`}
                    >
                      <span>{ev.minute}&apos;</span>
                      {kind === "goal" && <BallIcon className="w-3 h-3" />}
                      {kind === "yellow" && (
                        <YellowCardIcon className="w-2.5 h-3" />
                      )}
                      {kind === "red" && <RedCardIcon className="w-2.5 h-3" />}
                      {kind === "sub" && <SubIcon className="w-3 h-3" />}
                      <span>{shortName}</span>
                    </span>
                  );
                })}
              </div>
            ) : (
              <div className="text-[11px] font-mono text-slate-500 italic">
                Clean, continuous open play in this window
              </div>
            )}
            <div className="flex items-center justify-between text-[11px] text-slate-500 font-mono">
              <span>{t.cards.deckWindow}</span>
              <span
                className="font-mono text-teal-700 font-bold"
                id="card1-stat"
              >
                {windowEvents.length} Events • {windowGoals} Goals
                {windowReds > 0 ? ` • ${windowReds} Red` : ""}
              </span>
            </div>
          </div>
        </article>

        {/* Card 2: Key Tactical Decisions */}
        <article className="editorial-card rounded-2xl p-4 sm:p-5 flex flex-col h-[390px] sm:h-[410px] relative overflow-hidden shadow-sm">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-sky-500"></div>

          {/* Header */}
          <div className="shrink-0 mb-2">
            <div className="flex items-center justify-between gap-2 mb-2">
              <span className="px-2.5 py-0.5 rounded bg-sky-50 text-sky-800 border border-sky-200 font-mono text-[11px] font-extrabold uppercase tracking-wide">
                {t.cards.card2.tag}
              </span>
              <span className="text-xs font-mono font-bold text-sky-700">
                {match?.homeTeam.formation ?? "4-3-3"} vs{" "}
                {match?.awayTeam.formation ?? "4-2-3-1"}
              </span>
            </div>
            <h3
              className="text-lg sm:text-xl font-display font-black uppercase tracking-wide text-slate-900 line-clamp-1"
              id="card2-title"
            >
              {t.cards.card2.title}
            </h3>
          </div>

          {/* Body: Scrollable with custom scrollbar, takes all remaining space */}
          <div className="flex-1 min-h-0 overflow-y-auto pr-1.5 custom-scrollbar">
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed whitespace-pre-line ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card2-body"
            >
              {catchUp?.whyItMatters ??
                (windowInsight
                  ? `${windowInsight.summary} ${windowInsight.reason}`
                  : t.cards.card2.fallback)}
            </p>
          </div>

          {/* Clean Tactical Breakdown Strip with 2-Row Visible Hierarchy (Always pinned at bottom) */}
          <div className="mt-3 pt-3 border-t border-slate-200 shrink-0">
            <div className="bg-sky-50/90 rounded-xl p-2.5 border border-sky-200 flex flex-col gap-1.5 shadow-2xs">
              <div className="flex items-center justify-between gap-2">
                <span className="px-2 py-0.5 rounded bg-sky-600 text-white font-mono text-[10px] font-extrabold uppercase tracking-wide shrink-0">
                  {formatInsightType(windowInsight?.type)}
                </span>
                <span className="text-[10px] font-mono font-bold text-sky-800 truncate">
                  {match?.homeTeam.coach?.split(" ").pop() ?? "Arteta"} vs {match?.awayTeam.coach?.split(" ").pop() ?? "Slot"} Duel
                </span>
              </div>
              <p
                className="text-xs font-semibold text-slate-800 leading-snug line-clamp-2"
                title={windowInsight?.summary ?? ""}
              >
                {windowInsight?.summary ??
                  `${match?.homeTeam.coach ?? "Arteta"} vs. ${
                    match?.awayTeam.coach ?? "Slot"
                  } tactical battle.`}
              </p>
            </div>
          </div>
        </article>

        {/* Card 3: Momentum Changes */}
        <article className="editorial-card rounded-2xl p-4 sm:p-5 flex flex-col h-[390px] sm:h-[410px] relative overflow-hidden shadow-sm">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-rose-500"></div>

          {/* Header */}
          <div className="shrink-0 mb-2">
            <div className="flex items-center justify-between gap-2 mb-2">
              <span className="px-2.5 py-0.5 rounded bg-rose-50 text-rose-800 border border-rose-200 font-mono text-[11px] font-extrabold uppercase tracking-wide">
                {t.cards.card3.tag}
              </span>
              <span
                className="text-xs font-mono font-bold text-rose-700"
                id="card3-owner"
              >
                {homeCode} {possessionHome}% – {possessionAway}% {awayCode}
              </span>
            </div>
            <h3
              className="text-lg sm:text-xl font-display font-black uppercase tracking-wide text-slate-900 line-clamp-1"
              id="card3-title"
            >
              {t.cards.card3.title}
            </h3>
          </div>

          {/* Body: Scrollable with custom scrollbar, takes all remaining space */}
          <div className="flex-1 min-h-0 overflow-y-auto pr-1.5 custom-scrollbar">
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed whitespace-pre-line ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card3-body"
            >
              {catchUp?.momentumChanges ?? t.cards.card3.fallback}
            </p>
          </div>

          {/* Dynamic Momentum Split Bar + Real Match Statistics (Always pinned at bottom) */}
          <div className="mt-3 pt-3 border-t border-slate-200 space-y-2 shrink-0">
            {/* Dynamic Two-Tone Possession Split Bar */}
            <div className="space-y-1">
              <div className="flex justify-between text-[11px] font-mono font-bold text-slate-700">
                <span className="text-rose-700 font-extrabold">
                  {homeCode} {possessionHome}%
                </span>
                <span className="text-slate-500 font-medium text-[10px] uppercase tracking-wider">
                  Ball Control
                </span>
                <span className="text-teal-700 font-extrabold">
                  {possessionAway}% {awayCode}
                </span>
              </div>
              <div className="h-2 w-full bg-slate-200 rounded-full overflow-hidden flex shadow-inner">
                <div
                  className="bg-rose-500 transition-all duration-500"
                  style={{ width: `${possessionHome}%` }}
                />
                <div
                  className="bg-teal-500 transition-all duration-500"
                  style={{ width: `${possessionAway}%` }}
                />
              </div>
            </div>

            {/* Real Live Match Statistics in Balanced 2-Column Grid */}
            <div className="grid grid-cols-2 gap-2 text-[10.5px] font-mono bg-slate-50 px-2.5 py-1.5 rounded-lg border border-slate-200 shadow-2xs">
              <div className="flex flex-col min-w-0">
                <span className="text-slate-500 text-[9px] uppercase font-bold tracking-wider">
                  {t.cards.card3.shots}
                </span>
                <span className="text-slate-900 font-black truncate">
                  {match?.statistics.shots.home ?? 16} ({match?.statistics.shotsOnTarget.home ?? 9}) – {match?.statistics.shots.away ?? 10} ({match?.statistics.shotsOnTarget.away ?? 5})
                </span>
              </div>
              <div className="flex flex-col text-right min-w-0">
                <span className="text-slate-500 text-[9px] uppercase font-bold tracking-wider">
                  {t.cards.card3.passAcc}
                </span>
                <span className="text-slate-900 font-black truncate">
                  {match?.statistics.passAccuracy.home ?? 90}% – {match?.statistics.passAccuracy.away ?? 83}%
                </span>
              </div>
            </div>
          </div>
        </article>

        {/* Card 4: Current Game State */}
        <article className="editorial-card rounded-2xl p-4 sm:p-5 flex flex-col h-[390px] sm:h-[410px] relative overflow-hidden shadow-sm">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-amber-500"></div>

          {/* Header */}
          <div className="shrink-0 mb-2">
            <div className="flex items-center justify-between gap-2 mb-2">
              <span className="px-2.5 py-0.5 rounded bg-amber-50 text-amber-800 border border-amber-200 font-mono text-[11px] font-extrabold uppercase tracking-wide">
                {t.cards.card4.tag}
              </span>
              <span className="text-xs font-mono text-amber-700 font-bold">
                Minute {windowEnd}&apos; Live State
              </span>
            </div>
            <h3
              className="text-lg sm:text-xl font-display font-black uppercase tracking-wide text-slate-900 line-clamp-1"
              id="card4-title"
            >
              {t.cards.card4.title}
            </h3>
          </div>

          {/* Body: Scrollable with custom scrollbar, takes all remaining space */}
          <div className="flex-1 min-h-0 overflow-y-auto pr-1.5 custom-scrollbar">
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed whitespace-pre-line ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card4-body"
            >
              {catchUp?.whatToWatchNext ?? t.cards.card4.fallback}
            </p>
          </div>

          {/* Real Dynamic Situation Banner at Return Minute (Always pinned at bottom with 2-Row Layout) */}
          <div className="mt-3 pt-3 border-t border-slate-200 shrink-0">
            <div className="bg-amber-50/90 rounded-xl p-2.5 border border-amber-300 flex flex-col gap-1.5 shadow-2xs">
              <div className="flex items-center justify-between gap-2">
                <span className="font-mono text-xs font-black px-2 py-0.5 rounded bg-amber-500 text-slate-950 shrink-0 shadow-2xs">
                  {homeCode} {homeGoalsAtReturn} – {awayGoalsAtReturn} {awayCode}
                </span>
                <span className="text-[10px] font-mono font-extrabold px-2 py-0.5 rounded bg-amber-200 text-amber-900 border border-amber-300/80 shrink-0 uppercase">
                  {windowEnd >= 90 ? "Full Time" : `${windowEnd}' Live`}
                </span>
              </div>
              <p
                className="text-xs font-bold text-slate-900 leading-snug line-clamp-2"
                title={situationDescription}
              >
                {situationDescription}
              </p>
            </div>
          </div>
        </article>
      </div>
    </section>
  );
};

export default Cards;
