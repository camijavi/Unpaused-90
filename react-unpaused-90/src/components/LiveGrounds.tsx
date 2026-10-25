import React from "react";
import type { MatchData } from "../types/match";
import { BallIcon } from "./Icons";

interface LiveGroundsProps {
  match: MatchData | null;
  initialStartMinute?: number | null;
  onResetSimulation?: () => void;
}

export const LiveGrounds: React.FC<LiveGroundsProps> = ({
  match,
  initialStartMinute,
  onResetSimulation,
}) => {
  const homeName = match?.homeTeam.name ?? "Arsenal";
  const awayName = match?.awayTeam.name ?? "Liverpool";
  const homeScore = match?.homeTeam.score ?? 0;
  const awayScore = match?.awayTeam.score ?? 0;
  const minute = match?.currentMinute ?? 1;
  const isFullTime = minute >= 90 || match?.status === "FullTime";

  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-white p-3 rounded-2xl border border-slate-200 shadow-sm">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[11px] font-display font-extrabold uppercase tracking-widest text-pl-purple px-2">
          LIVE GROUNDS:
        </span>

        <button
          id="match-btn-ars-liv"
          type="button"
          className="px-3 py-1.5 rounded-lg text-xs font-bold border transition-all flex items-center gap-2 bg-red-50 border-red-500 text-red-700 shadow-sm"
        >
          <BallIcon className="w-4 h-4 text-red-600 shrink-0" />
          <span>
            {homeName} {homeScore} – {awayScore} {awayName} (
            {isFullTime ? "FT 90'" : `${minute}'`})
          </span>
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-2.5 px-2 sm:px-0">
        {initialStartMinute !== null && initialStartMinute !== undefined && (
          <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-2.5 py-1 rounded-lg border border-slate-200">
            Random Start:{" "}
            <strong className="text-pl-purple">{initialStartMinute}&apos;</strong>{" "}
            • Clock: <strong className="text-slate-700">1 min / 30s (2× Speed)</strong>
          </span>
        )}
        {onResetSimulation && (
          <button
            type="button"
            onClick={onResetSimulation}
            className="px-3 py-1.5 rounded-lg text-xs font-mono font-bold uppercase tracking-wider bg-pl-purple text-white hover:brightness-110 transition-all shadow-sm cursor-pointer"
          >
            Simulate Random Minute (1&apos;–90&apos;) ↻
          </button>
        )}
      </div>
    </div>
  );
};

export default LiveGrounds;
