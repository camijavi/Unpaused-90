import React from "react";
import type { MatchData } from "../types/match";
import { BallIcon } from "./Icons";

interface LiveGroundsProps {
  match: MatchData | null;
}

export const LiveGrounds: React.FC<LiveGroundsProps> = ({ match }) => {
  const homeName = match?.homeTeam.name ?? "Arsenal";
  const awayName = match?.awayTeam.name ?? "Liverpool";
  const homeScore = match?.homeTeam.score ?? 2;
  const awayScore = match?.awayTeam.score ?? 1;
  const minute = match?.currentMinute ?? 90;

  return (
    <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3 bg-white p-3 rounded-2xl border border-slate-200 shadow-sm">
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
            {homeName} {homeScore} – {awayScore} {awayName} ({minute}&apos;)
          </span>
        </button>
      </div>
    </div>
  );
};

export default LiveGrounds;
