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
    <div className="relative flex flex-col items-center text-center select-none">
      {/* Animated 3D Referee Yellow Card */}
      <div className="w-20 h-28 rounded-xl bg-gradient-to-br from-amber-300 via-yellow-400 to-amber-500 border-2 border-white shadow-[0_18px_45px_rgba(251,191,36,0.75)] flex flex-col items-center justify-between p-2.5 anim-card-3d">
        <span className="text-[10px] font-mono font-extrabold text-amber-950 uppercase">
          PL OFFICIAL
        </span>
        <span className="text-2xl font-display font-black text-amber-950">
          {min}&apos;
        </span>
        <span className="w-8 h-1 rounded bg-amber-950/40"></span>
      </div>

      <div className="mt-3 px-7 py-2 rounded-xl bg-amber-400 border-2 border-white shadow-2xl anim-banner">
        <div className="text-[11px] font-mono font-extrabold uppercase tracking-widest text-amber-950">
          {club} • DISCIPLINARY ACTION
        </div>
        <div className="text-3xl sm:text-4xl font-display font-black uppercase tracking-wider text-slate-950 flex items-center justify-center gap-2.5">
          <YellowCardIcon className="w-6 h-7 shrink-0" />
          <span>YELLOW CARD BOOKING</span>
        </div>
      </div>

      <div className="mt-2 px-4 py-1 rounded-full bg-white text-slate-900 font-mono text-xs font-extrabold shadow">
        {player} — {sub}
      </div>
    </div>
  );
};

export default YellowCard;
