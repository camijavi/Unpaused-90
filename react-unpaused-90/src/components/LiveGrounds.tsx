import React from "react";
import type { MatchData } from "../types/match";
import { BallIcon } from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

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
  const { t } = useLanguage();
  const homeName = match?.homeTeam?.name ?? "";
  const awayName = match?.awayTeam?.name ?? "";
  const homeScore = match?.homeTeam?.score ?? 0;
  const awayScore = match?.awayTeam?.score ?? 0;
  const minute = match?.currentMinute ?? 1;
  const isFullTime = minute >= 90 || match?.status === "FullTime";

  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-white p-3 rounded-2xl border border-slate-200 shadow-sm">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[11px] font-display font-extrabold uppercase tracking-widest text-pl-purple px-2">
          {t.liveGrounds.liveGrounds}
        </span>

        {homeName && awayName && (
          <button
            id="match-btn-live"
            type="button"
            className="px-3 py-1.5 rounded-lg text-xs font-bold border transition-all flex items-center gap-2 bg-red-50 border-red-500 text-red-700 shadow-sm"
          >
            <BallIcon className="w-4 h-4 text-red-600 shrink-0" />
            <span>
              {homeName} {homeScore} – {awayScore} {awayName} (
              {isFullTime ? t.liveGrounds.fullTimeShort : `${minute}'`})
            </span>
          </button>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2.5 px-2 sm:px-0">
        {initialStartMinute !== null && initialStartMinute !== undefined && (
          <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-2.5 py-1 rounded-lg border border-slate-200">
            {t.liveGrounds.randomStart}{" "}
            <strong className="text-pl-purple">{initialStartMinute}&apos;</strong>{" "}
            • {t.liveGrounds.clock}{" "}
            <strong className="text-pl-purple">30s = 1m</strong>
          </span>
        )}

        {onResetSimulation && (
          <button
            type="button"
            onClick={onResetSimulation}
            className="px-2.5 py-1 rounded-lg text-[11px] font-bold text-pl-purple bg-purple-50 hover:bg-purple-100 border border-purple-200 transition-colors"
          >
            {t.liveGrounds.simulateRandom}
          </button>
        )}
      </div>
    </div>
  );
};

export default LiveGrounds;
