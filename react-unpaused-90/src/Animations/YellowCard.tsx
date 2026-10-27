import React from "react";
import { YellowCardIcon } from "../components/Icons";

interface YellowCardAnimationProps {
  min: number;
  club: string;
  player: string;
  sub: string;
}

export const YellowCard: React.FC<YellowCardAnimationProps> = ({
  min,
  club,
  player,
  sub,
}) => {
  return (
    <div className="relative flex flex-col items-center justify-center text-center select-none py-2 sm:py-4 px-2 sm:px-4 max-w-xl mx-auto w-full">
      {/* Animated 3D Referee Yellow Card */}
      <div className="w-14 h-20 sm:w-18 sm:h-26 rounded-lg sm:rounded-xl bg-gradient-to-br from-amber-300 via-yellow-400 to-amber-500 border-2 border-white shadow-[0_18px_45px_rgba(251,191,36,0.75)] flex flex-col items-center justify-between p-2 anim-card-3d">
        <span className="text-[9px] sm:text-[10px] font-mono font-extrabold text-amber-950 uppercase">
          PL OFFICIAL
        </span>
        <span className="text-xl sm:text-2xl font-display font-black text-amber-950">
          {min}&apos;
        </span>
        <span className="w-6 sm:w-8 h-1 rounded bg-amber-950/40"></span>
      </div>

      <div className="mt-2.5 px-4 sm:px-7 py-1.5 sm:py-2.5 rounded-xl bg-amber-400 border-2 border-white shadow-2xl anim-banner max-w-[92vw]">
        <div className="text-[10px] sm:text-[11px] font-mono font-extrabold uppercase tracking-widest text-amber-950 truncate">
          {club} • DISCIPLINARY ACTION
        </div>
        <div className="text-xl sm:text-3xl md:text-4xl font-display font-black uppercase tracking-wider text-slate-950 flex items-center justify-center gap-2 leading-none mt-0.5">
          <YellowCardIcon className="w-4 h-5 sm:w-5 sm:h-6 shrink-0" />
          <span className="truncate">YELLOW CARD</span>
        </div>
      </div>

      <div className="mt-2 px-4 sm:px-5 py-1 sm:py-1.5 rounded-full bg-white text-slate-900 font-mono text-xs sm:text-sm font-extrabold shadow-lg max-w-[92vw] truncate">
        {player} — {sub}
      </div>
    </div>
  );
};

export default YellowCard;
