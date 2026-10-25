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

interface CardsProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  insights: GeneratedInsight[];
  stoppedMinute: number;
  currentMinute: number;
  catchUp: CatchUpResponse | null;
  isGenerating: boolean;
  statusBadgeText: string;
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
}) => {
  const windowStr = `${stoppedMinute}' – ${currentMinute}'`;
  const homeCode = match?.homeTeam.teamId ?? "ARS";
  const awayCode = match?.awayTeam.teamId ?? "LIV";
  const possessionHome = match?.statistics.possession.home ?? 54;
  const possessionAway = match?.statistics.possession.away ?? 46;

  // Real events occurring within the user's missed window
  const windowEvents = timeline.filter(
    (ev) => ev.minute >= stoppedMinute && ev.minute <= currentMinute
  );
  const displayWindowEvents = (
    windowEvents.length > 0 ? windowEvents : timeline
  ).slice(0, 3);

  const windowGoals = windowEvents.filter((e) => e.eventType === "Goal").length;

  // Real tactical insight from backend
  const windowInsight =
    insights.find(
      (i) => i.minute >= stoppedMinute && i.minute <= currentMinute
    ) ?? insights[0];

  // Real key players from Starting XI
  const homeKeyPlayer =
    match?.homeTeam.startingXI.find((p) => p.number === 7) ??
    match?.homeTeam.startingXI[0];
  const awayKeyPlayer =
    match?.awayTeam.startingXI.find((p) => p.captain) ??
    match?.awayTeam.startingXI[0];

  return (
    <section aria-label="Catch Me Up Intelligence Cards" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900 flex items-center gap-2">
            <BallIcon className="w-5 h-5 text-pl-purple shrink-0" />
            <span>Live Catch-Up Intelligence Deck</span>
          </h2>
          <span
            id="cards-status-badge"
            className="text-xs font-mono font-bold px-2.5 py-0.5 rounded bg-teal-100 text-teal-800 border border-teal-300"
          >
            {statusBadgeText || `Synced to ${windowStr} Window`}
          </span>
        </div>
        <span className="text-xs font-mono text-slate-500 hidden sm:inline">
          4-Pillar Daylight Editorial Layout • Live Azure OpenAI Telemetry
        </span>
      </div>

      {/* Responsive Grid: 1 col mobile, 2 cols tablet, 4 cols desktop */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
        {/* Card 1: What Happened (Chronology + Mini Event Tape) */}
        <article className="editorial-card rounded-2xl p-5 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-teal-500"></div>
          <div>
            <div className="flex items-center justify-between gap-2 mb-3">
              <span className="px-2.5 py-0.5 rounded bg-teal-50 text-teal-800 border border-teal-200 font-mono text-[11px] font-extrabold">
                01 / CHRONOLOGY
              </span>
              <span
                className="text-xs font-mono font-bold text-slate-500"
                id="card1-window"
              >
                {windowStr}
              </span>
            </div>
            <h3
              className="text-xl font-display font-black uppercase tracking-wide text-slate-900 mb-2"
              id="card1-title"
            >
              What Happened
            </h3>
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card1-body"
            >
              {catchUp?.whatHappened ??
                (isGenerating
                  ? `Synthesizing chronology for minutes ${windowStr}...`
                  : `Between ${windowStr}, ${windowEvents.length} key timeline events unfolded across the pitch.`)}
            </p>
          </div>

          {/* Visual Event Tape Footer from Real Timeline Events */}
          <div className="mt-4 pt-3 border-t border-slate-200 space-y-1.5">
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
            <div className="flex items-center justify-between text-[11px] text-slate-500">
              <span>Window Activity</span>
              <span
                className="font-mono text-teal-700 font-bold"
                id="card1-stat"
              >
                {windowEvents.length} Events • {windowGoals} Goals
              </span>
            </div>
          </div>
        </article>

        {/* Card 2: Why It Matters (With Mini SVG Pitch Zone 14 Overload Map) */}
        <article className="editorial-card rounded-2xl p-5 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-sky-500"></div>
          <div>
            <div className="flex items-center justify-between gap-2 mb-3">
              <span className="px-2.5 py-0.5 rounded bg-sky-50 text-sky-800 border border-sky-200 font-mono text-[11px] font-extrabold">
                02 / CAUSE &amp; EFFECT
              </span>
              <span className="text-xs font-mono text-slate-500">
                Tactical Blueprint
              </span>
            </div>
            <h3
              className="text-xl font-display font-black uppercase tracking-wide text-slate-900 mb-2"
              id="card2-title"
            >
              Why It Matters
            </h3>
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card2-body"
            >
              {catchUp?.whyItMatters ??
                (windowInsight
                  ? `${windowInsight.summary} ${windowInsight.reason}`
                  : "Analyzing tactical shifts and structural overloads...")}
            </p>
          </div>

          {/* Bespoke Mini Tactical Pitch SVG */}
          <div className="mt-4 pt-3 border-t border-slate-200">
            <div className="bg-emerald-950 rounded-lg p-2.5 border border-emerald-800 flex items-center justify-between gap-3">
              <svg
                className="w-24 h-14 shrink-0"
                viewBox="0 0 120 70"
                fill="none"
              >
                <rect
                  x="2"
                  y="2"
                  width="116"
                  height="66"
                  rx="4"
                  fill="#064E3B"
                  stroke="#A7F3D0"
                  strokeOpacity="0.5"
                  strokeWidth="1.5"
                />
                <line
                  x1="60"
                  y1="2"
                  x2="60"
                  y2="68"
                  stroke="#A7F3D0"
                  strokeOpacity="0.5"
                  strokeWidth="1"
                />
                <circle
                  cx="60"
                  cy="35"
                  r="12"
                  stroke="#A7F3D0"
                  strokeOpacity="0.5"
                  strokeWidth="1"
                />
                <rect
                  x="78"
                  y="18"
                  width="22"
                  height="34"
                  rx="2"
                  fill="#00F5D4"
                  fillOpacity="0.28"
                  stroke="#00F5D4"
                  strokeWidth="1.2"
                  strokeDasharray="2 2"
                />
                <path
                  d="M55 48 L88 28"
                  stroke="#00F5D4"
                  strokeWidth="2.2"
                  strokeLinecap="round"
                />
                <circle cx="55" cy="48" r="3.5" fill="#EF4444" />
                <circle cx="88" cy="28" r="4" fill="#00F5D4" />
              </svg>
              <div className="text-[11px] leading-tight">
                <span
                  className="font-mono font-bold text-mint-bright block"
                  id="card2-stat"
                >
                  {windowInsight?.type ?? "Zone 14 Half-Space"}
                </span>
                <span className="text-emerald-100 line-clamp-2">
                  {windowInsight?.summary ??
                    `${match?.homeTeam.formation ?? "4-3-3"} vs ${
                      match?.awayTeam.formation ?? "4-2-3-1"
                    } Transition`}
                </span>
              </div>
            </div>
          </div>
        </article>

        {/* Card 3: Momentum Changes (With Tug-of-War Field Tilt Graph) */}
        <article className="editorial-card rounded-2xl p-5 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-rose-500"></div>
          <div>
            <div className="flex items-center justify-between gap-2 mb-3">
              <span className="px-2.5 py-0.5 rounded bg-rose-50 text-rose-800 border border-rose-200 font-mono text-[11px] font-extrabold">
                03 / TEMPO SWING
              </span>
              <span
                className="text-xs font-mono font-bold text-rose-700"
                id="card3-owner"
              >
                {homeCode} {possessionHome}% – {possessionAway}% {awayCode}
              </span>
            </div>
            <h3
              className="text-xl font-display font-black uppercase tracking-wide text-slate-900 mb-2"
              id="card3-title"
            >
              Momentum Changes
            </h3>
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card3-body"
            >
              {catchUp?.momentumChanges ??
                "Evaluating possession waves, high-press sequences, and field tilt swings..."}
            </p>
          </div>

          {/* Dual-Direction Momentum Sparkbars from Real Timeline */}
          <div className="mt-4 pt-3 border-t border-slate-200">
            <div className="flex justify-between text-[10px] font-mono text-slate-500 mb-1.5">
              <span className="text-red-600 font-bold">
                PASS ACC: {match?.statistics.passAccuracy.home ?? 87}%{" "}
                {homeCode}
              </span>
              <span className="text-teal-700 font-bold">
                CORNERS: {match?.statistics.corners.home ?? 7} –{" "}
                {match?.statistics.corners.away ?? 5}
              </span>
            </div>
            <div className="grid grid-cols-6 gap-1.5 h-8 items-end bg-slate-100 p-1.5 rounded-lg border border-slate-200">
              {(timeline.slice(0, 6).length === 6
                ? timeline.slice(0, 6)
                : [
                    { minute: 15, team: homeCode, importanceScore: 70 },
                    { minute: 31, team: homeCode, importanceScore: 100 },
                    { minute: 43, team: awayCode, importanceScore: 65 },
                    { minute: 54, team: awayCode, importanceScore: 95 },
                    { minute: 72, team: homeCode, importanceScore: 88 },
                    { minute: 79, team: homeCode, importanceScore: 100 },
                  ]
              ).map((bar, idx) => {
                const score = bar.importanceScore ?? 75;
                const heightClass =
                  score >= 95
                    ? "h-full"
                    : score >= 80
                    ? "h-5/6"
                    : score >= 70
                    ? "h-4/6"
                    : "h-3/6";
                const colorClass =
                  bar.team === homeCode ? "bg-red-500" : "bg-teal-500";
                return (
                  <div
                    key={idx}
                    className={`${colorClass} ${heightClass} rounded-sm`}
                    title={`${bar.minute}': ${bar.team}`}
                  ></div>
                );
              })}
            </div>
          </div>
        </article>

        {/* Card 4: What To Watch Next (Key Player 1v1 Radar) */}
        <article className="editorial-card rounded-2xl p-5 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-0 left-0 w-full h-1.5 bg-amber-500"></div>
          <div>
            <div className="flex items-center justify-between gap-2 mb-3">
              <span className="px-2.5 py-0.5 rounded bg-amber-50 text-amber-800 border border-amber-200 font-mono text-[11px] font-extrabold">
                04 / NEXT 15 MINS
              </span>
              <span className="text-xs font-mono text-amber-700 font-bold">
                Live Scout Radar
              </span>
            </div>
            <h3
              className="text-xl font-display font-black uppercase tracking-wide text-slate-900 mb-2"
              id="card4-title"
            >
              What To Watch Next
            </h3>
            <p
              className={`text-xs sm:text-sm text-slate-600 leading-relaxed ${
                isGenerating ? "animate-pulse" : ""
              }`}
              id="card4-body"
            >
              {catchUp?.whatToWatchNext ??
                "Identifying key tactical matchups and decisive zones to watch next..."}
            </p>
          </div>

          {/* 1v1 Duel Badge Footer from Real Match Roster */}
          <div className="mt-4 pt-3 border-t border-slate-200">
            <div className="bg-amber-50/80 rounded-lg p-2.5 border border-amber-300 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="w-6 h-6 rounded-full bg-red-600 text-white font-mono text-[10px] font-bold flex items-center justify-center">
                  #{homeKeyPlayer?.number ?? 7}
                </span>
                <span
                  className="text-xs font-bold text-slate-900"
                  id="card4-stat"
                >
                  {homeKeyPlayer?.name.split(" ").pop() ?? "Saka"} vs.{" "}
                  {awayKeyPlayer?.name.split(" ").slice(-2).join(" ") ??
                    "van Dijk"}
                </span>
              </div>
              <span className="text-[10px] font-mono font-extrabold px-2 py-0.5 rounded bg-amber-200 text-amber-900">
                1v1 ISOLATION
              </span>
            </div>
          </div>
        </article>
      </div>
    </section>
  );
};

export default Cards;
