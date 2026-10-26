import React from "react";
import stadiumImg from "../assets/stadium.jpe";
import Goal from "../Animations/Goal";
import YellowCard from "../Animations/YellowCard";
import RedCard from "../Animations/RedCard";
import Substitution from "../Animations/Substitution";
import type {
  EventAnimationPayload,
  MatchData,
  TimelineEvent,
} from "../types/match";
import { ArsenalCrest, LiverpoolCrest, BallIcon } from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

export interface ResponsiveScoreContainerProps {
  match: MatchData | null;
  currentMinute: number;
  stoppedMinute?: number;
  timeline?: TimelineEvent[];
  activeAnimation?: EventAnimationPayload | null;
  onTriggerAnimation?: (payload: EventAnimationPayload) => void;
  onSelectTimelineEvent?: (ev: TimelineEvent) => void;
  className?: string;
}

/**
 * Newcastle United crest SVG (supporting Newcastle vs Chelsea as shown in match designs).
 */
export const NewcastleCrest: React.FC<{ className?: string }> = ({
  className = "w-full h-full",
}) => (
  <svg
    viewBox="0 0 100 100"
    className={className}
    aria-label="Newcastle United Crest"
  >
    <defs>
      <linearGradient id="newCastleGold" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stopColor="#F59E0B" />
        <stop offset="100%" stopColor="#D97706" />
      </linearGradient>
    </defs>
    {/* Shield Base */}
    <path
      d="M20 22 H80 V55 C80 75 50 88 50 88 C50 88 20 75 20 55 Z"
      fill="#1E293B"
      stroke="#F59E0B"
      strokeWidth="3.5"
    />
    {/* Black and White Stripes */}
    <path d="M28 24 H36 V62 C33 59 30 56 28 53 Z" fill="#FFFFFF" />
    <path d="M44 24 H56 V78 C54 77 52 76 50 75 C48 76 46 77 44 78 Z" fill="#FFFFFF" />
    <path d="M64 24 H72 V53 C70 56 67 59 64 62 Z" fill="#FFFFFF" />
    {/* Blue Chief with Club Ribbon */}
    <rect x="22" y="24" width="56" height="12" fill="#0284C7" />
    <text
      x="50"
      y="33"
      textAnchor="middle"
      fill="#FFFFFF"
      fontFamily="sans-serif"
      fontWeight="900"
      fontSize="8.5"
      letterSpacing="0.8"
    >
      NEWCASTLE
    </text>
    {/* Castle / Flag Motif on top */}
    <path
      d="M44 14 H56 V22 H44 Z M48 8 H52 V14 H48 Z"
      fill="#F59E0B"
    />
  </svg>
);

/**
 * Chelsea FC crest SVG (supporting Newcastle vs Chelsea as shown in match designs).
 */
export const ChelseaCrest: React.FC<{ className?: string }> = ({
  className = "w-full h-full",
}) => (
  <svg
    viewBox="0 0 100 100"
    className={className}
    aria-label="Chelsea FC Crest"
  >
    {/* Outer Blue Circle */}
    <circle
      cx="50"
      cy="50"
      r="44"
      fill="#1D4ED8"
      stroke="#F59E0B"
      strokeWidth="3.5"
    />
    {/* Inner White Ring */}
    <circle
      cx="50"
      cy="50"
      r="33"
      fill="#FFFFFF"
      stroke="#1D4ED8"
      strokeWidth="2"
    />
    {/* Blue Lion Rampant holding staff */}
    <path
      d="M46 32 C48 30 54 30 56 34 C58 38 54 42 56 46 C60 44 64 48 62 52 C58 56 56 62 58 68 L50 68 C48 64 46 60 48 56 C44 58 40 54 42 48 C44 44 42 38 46 32 Z"
      fill="#1D4ED8"
    />
    {/* Staff */}
    <line x1="57" y1="28" x2="57" y2="70" stroke="#F59E0B" strokeWidth="2.5" />
    {/* Red Footballs / Roses */}
    <circle cx="28" cy="50" r="3" fill="#DC2626" />
    <circle cx="72" cy="50" r="3" fill="#DC2626" />
    {/* Ring Text */}
    <text
      x="50"
      y="22"
      textAnchor="middle"
      fill="#F59E0B"
      fontFamily="sans-serif"
      fontWeight="900"
      fontSize="7.5"
      letterSpacing="1"
    >
      CHELSEA
    </text>
    <text
      x="50"
      y="84"
      textAnchor="middle"
      fill="#F59E0B"
      fontFamily="sans-serif"
      fontWeight="900"
      fontSize="6.5"
      letterSpacing="1"
    >
      FOOTBALL CLUB
    </text>
  </svg>
);

/**
 * Resolves the appropriate crest SVG based on teamId or team name.
 */
export function renderTeamCrest(
  teamId?: string,
  teamName?: string,
  className = "w-14 h-14 sm:w-16 sm:h-16"
) {
  const code = (teamId ?? "").toUpperCase();
  const name = (teamName ?? "").toLowerCase();

  if (code === "ARS" || name.includes("arsenal")) {
    return <ArsenalCrest className={className} />;
  }
  if (code === "LIV" || name.includes("liverpool")) {
    return <LiverpoolCrest className={className} />;
  }
  if (code === "NEW" || name.includes("newcastle")) {
    return <NewcastleCrest className={className} />;
  }
  if (code === "CHE" || name.includes("chelsea")) {
    return <ChelseaCrest className={className} />;
  }

  // Dynamic fallback crest with team initials & gold border
  const initials = (name || code || "FC")
    .split(/\s+/)
    .map((w) => w[0])
    .join("")
    .slice(0, 3)
    .toUpperCase();

  return (
    <div
      className={`${className} rounded-full bg-gradient-to-br from-indigo-700 via-purple-800 to-slate-900 border-2 border-amber-400 flex items-center justify-center text-white font-display font-black text-sm shadow-md`}
    >
      {initials}
    </div>
  );
}

/**
 * ResponsiveScoreContainer:
 * Modern mobile-first score container card matching Premier League broadcast stadium aesthetics.
 * Seamless flush layout for small screens (< 768px):
 * - Neutral stadium background with broadcast contrast scrim
 * - Official Premier League & WEEK 10 branding matching desktop
 * - 3-column layout: Home Crest & Name | Large Score & Live Pill | Away Crest & Name
 * - Glowing magenta live minute pill
 * - Compact live possession bar & goal scorers summary
 * - Responsive adaptive event animations overlay (Goal, Cards, Subs)
 */
export const ResponsiveScoreContainer: React.FC<
  ResponsiveScoreContainerProps
> = ({
  match,
  currentMinute,
  stoppedMinute,
  timeline = [],
  activeAnimation,
  className = "",
}) => {
  const { t } = useLanguage();

  const homeTeam = match?.homeTeam ?? {
    teamId: "ARS",
    name: "Arsenal",
    score: 1,
    formation: "4-3-3",
  };
  const awayTeam = match?.awayTeam ?? {
    teamId: "LIV",
    name: "Liverpool",
    score: 2,
    formation: "4-2-3-1",
  };

  const competition = match?.competition ?? "Premier League";
  const round = "WEEK 10"; // Premier League Matchweek indicator
  const isFullTime = currentMinute >= 90 || match?.status === "FullTime";

  const homeScore = homeTeam.score ?? 0;
  const awayScore = awayTeam.score ?? 0;

  const possessionHome = match?.statistics?.possession?.home ?? 54;
  const possessionAway = match?.statistics?.possession?.away ?? 46;

  // Format time pill text (e.g. "83'" or "90' FT" or "HT 45'")
  const timePillText = isFullTime
    ? "90' FT"
    : match?.status === "Half Time" || currentMinute === 45
    ? "45' HT"
    : `${currentMinute}'`;

  // Goal scorers for mobile ticker
  const homeGoals = timeline.filter(
    (e) =>
      (e.team === homeTeam.teamId || e.team === homeTeam.name) &&
      e.eventType === "Goal"
  );
  const awayGoals = timeline.filter(
    (e) =>
      (e.team === awayTeam.teamId || e.team === awayTeam.name) &&
      e.eventType === "Goal"
  );

  const homeScorers = homeGoals
    .map((g) => `${g.player?.split(" ").pop() ?? g.player} ${g.minute}'`)
    .join(", ");
  const awayScorers = awayGoals
    .map((g) => `${g.player?.split(" ").pop() ?? g.player} ${g.minute}'`)
    .join(", ");

  const isShaking =
    activeAnimation?.kind === "goal" || activeAnimation?.kind === "red";

  return (
    <div
      id="responsive-score-card"
      aria-label={`${competition}: ${homeTeam.name} ${homeScore} - ${awayScore} ${awayTeam.name}`}
      className={`relative overflow-hidden bg-slate-950 px-4 py-5 sm:px-6 sm:py-6 text-white border-b border-white/10 min-h-[250px] flex flex-col justify-between ${
        isShaking ? "anim-shake" : ""
      } ${className}`}
    >
      {/* Neutral Stadium Photograph Layer */}
      <img
        src={stadiumImg}
        alt="Neutral Premier League Stadium Panorama"
        className="absolute inset-0 w-full h-full object-cover object-center scale-105 transition-transform duration-1000"
      />
      {/* Multi-stop Broadcast Contrast Scrim so team names & crests pop */}
      <div className="absolute inset-0 bg-gradient-to-r from-slate-950/85 via-slate-900/55 to-slate-950/85 pointer-events-none"></div>
      <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-transparent to-slate-950/40 pointer-events-none"></div>

      {/* Top Header: Competition & Matchweek */}
      <div className="relative z-10 flex flex-col items-center justify-center text-center">
        <h2 className="text-xs sm:text-sm font-semibold tracking-wide text-purple-100/90 font-display">
          {competition}
        </h2>
        <span className="text-[11px] font-semibold tracking-widest text-purple-300/80 mt-0.5 uppercase">
          {round}
        </span>
      </div>

      {/* Main Row: [ Home Crest & Info ] [ Score & Live Minute Pill ] [ Away Crest & Info ] */}
      <div className="relative z-10 grid grid-cols-7 items-center my-auto pt-2 pb-1">
        {/* Home Club (3 columns) */}
        <div className="col-span-3 flex flex-col items-center justify-center text-center">
          <div className="relative w-14 h-14 sm:w-16 sm:h-16 flex items-center justify-center drop-shadow-[0_8px_16px_rgba(0,0,0,0.65)] hover:scale-105 transition-transform duration-200">
            {renderTeamCrest(
              homeTeam.teamId,
              homeTeam.name,
              "w-14 h-14 sm:w-16 sm:h-16"
            )}
          </div>
          <span
            id="mobile-home-name"
            className="mt-2 text-sm sm:text-base font-bold text-white tracking-wide truncate max-w-[105px] sm:max-w-[135px]"
            title={homeTeam.name}
          >
            {homeTeam.name}
          </span>
          <span className="text-[10.5px] font-medium uppercase tracking-wider text-purple-300/60 mt-0.5">
            {t.scoreContainer?.timelineFilters?.homeOnly ? "Home" : "Home"}
          </span>
        </div>

        {/* Center: Large Score & Minute Pill (1 column) */}
        <div className="col-span-1 flex flex-col items-center justify-center text-center">
          <div
            id="mobile-score-display"
            className="flex items-center justify-center gap-1.5 sm:gap-2 font-display font-black text-3xl sm:text-4xl text-white tracking-wider leading-none select-none drop-shadow-md"
          >
            <span id="mobile-home-score">{homeScore}</span>
            <span className="text-purple-300/50 font-light text-2xl sm:text-3xl">
              :
            </span>
            <span id="mobile-away-score">{awayScore}</span>
          </div>

          {/* Minute Pill Badge with Magenta Glow */}
          <div
            id="mobile-minute-pill"
            className="mt-2.5 px-3.5 py-1 rounded-full bg-[#3d0043]/95 border border-fuchsia-500/70 shadow-[0_0_14px_rgba(217,70,239,0.35)] flex items-center justify-center gap-1.5 shrink-0"
          >
            {!isFullTime && (
              <span
                aria-hidden="true"
                className="w-1.5 h-1.5 rounded-full bg-pink-400 animate-pulse shrink-0"
              />
            )}
            <span className="font-mono text-xs font-black text-fuchsia-200 tracking-tight">
              {timePillText}
            </span>
          </div>
        </div>

        {/* Away Club (3 columns) */}
        <div className="col-span-3 flex flex-col items-center justify-center text-center">
          <div className="relative w-14 h-14 sm:w-16 sm:h-16 flex items-center justify-center drop-shadow-[0_8px_16px_rgba(0,0,0,0.65)] hover:scale-105 transition-transform duration-200">
            {renderTeamCrest(
              awayTeam.teamId,
              awayTeam.name,
              "w-14 h-14 sm:w-16 sm:h-16"
            )}
          </div>
          <span
            id="mobile-away-name"
            className="mt-2 text-sm sm:text-base font-bold text-white tracking-wide truncate max-w-[105px] sm:max-w-[135px]"
            title={awayTeam.name}
          >
            {awayTeam.name}
          </span>
          <span className="text-[10.5px] font-medium uppercase tracking-wider text-purple-300/60 mt-0.5">
            {t.scoreContainer?.timelineFilters?.awayOnly ? "Away" : "Away"}
          </span>
        </div>
      </div>

      {/* Optional Compact Match Ticker: Goal Scorers & Possession */}
      {(homeScorers || awayScorers || stoppedMinute !== undefined) && (
        <div className="relative z-10 mt-3 pt-2.5 border-t border-white/15 text-[11px] font-mono text-slate-200 flex flex-col gap-1.5">
          {/* Scorers Summary if any goals scored */}
          {(homeScorers || awayScorers) && (
            <div className="flex items-center justify-between gap-2 px-1 text-[10.5px]">
              <span className="truncate max-w-[48%] flex items-center gap-1 text-emerald-300">
                {homeScorers && <BallIcon className="w-3 h-3 shrink-0" />}
                <span className="truncate">{homeScorers}</span>
              </span>
              <span className="truncate max-w-[48%] flex items-center justify-end gap-1 text-emerald-300">
                <span className="truncate">{awayScorers}</span>
                {awayScorers && <BallIcon className="w-3 h-3 shrink-0" />}
              </span>
            </div>
          )}

          {/* Slim Possession Bar */}
          <div className="flex items-center justify-between gap-2 pt-0.5 text-[10px] text-slate-300">
            <span className="font-bold">{possessionHome}%</span>
            <div className="flex-1 h-1.5 bg-slate-900/90 rounded-full overflow-hidden flex mx-1">
              <div
                className="bg-red-500 h-full transition-all duration-500"
                style={{ width: `${possessionHome}%` }}
              />
              <div
                className="bg-cyan-400 h-full transition-all duration-500"
                style={{ width: `${possessionAway}%` }}
              />
            </div>
            <span className="font-bold">{possessionAway}%</span>
          </div>
        </div>
      )}

      {/* Broadcast Event Animation Overlay Stage */}
      {activeAnimation && (
        <div
          id="mobile-event-fx-overlay"
          className={`absolute inset-0 z-30 flex items-center justify-center p-2 sm:p-4 bg-slate-950/90 backdrop-blur-md transition-opacity overflow-hidden ${
            activeAnimation.kind === "red" ? "anim-red-pulse" : ""
          }`}
        >
          <div className="w-full flex items-center justify-center transform scale-[0.80] sm:scale-95 md:scale-100 origin-center max-w-full">
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
        </div>
      )}
    </div>
  );
};

export default ResponsiveScoreContainer;
