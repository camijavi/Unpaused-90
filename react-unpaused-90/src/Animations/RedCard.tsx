import React from "react";
import { RedCardIcon } from "../components/Icons";

interface RedCardAnimationProps {
  min: number;
  club: string;
  player: string;
  sub: string;
}

export const RedCard: React.FC<RedCardAnimationProps> = ({
  min,
  club,
  player,
  sub,
}) => {
  return (
    <div className="relative flex flex-col items-center justify-center text-center select-none py-4 px-4 max-w-xl mx-auto">
      {/* Animated 3D Referee Red Card with Crimson Spotlight */}
      <div className="w-20 h-28 rounded-xl bg-gradient-to-br from-red-500 via-red-600 to-rose-800 border-2 border-white shadow-[0_0_55px_rgba(239,68,68,0.95)] flex flex-col items-center justify-between p-2.5 anim-card-3d">
        <span className="text-[10px] font-mono font-extrabold text-white uppercase">
          DISMISSAL
        </span>
        <span className="text-2xl font-display font-black text-white">
          {min}&apos;
        </span>
        <span className="w-8 h-1 rounded bg-white/50"></span>
      </div>

      <div className="mt-3 px-8 py-2.5 rounded-xl bg-gradient-to-r from-red-700 via-red-600 to-rose-700 border-2 border-white shadow-2xl anim-banner">
        <div className="text-[11px] font-mono font-extrabold uppercase tracking-widest text-amber-200">
          {club} • DOWN TO 10 MEN
        </div>
        <div className="text-3xl sm:text-4xl font-display font-black uppercase tracking-wider text-white flex items-center justify-center gap-2.5 leading-none mt-0.5">
          <RedCardIcon className="w-6 h-7 shrink-0" />
          <span>RED CARD — SENT OFF!</span>
        </div>
      </div>

      <div className="mt-3 px-5 py-1.5 rounded-full bg-white text-red-700 font-mono text-xs sm:text-sm font-extrabold shadow-lg max-w-full truncate">
        {player} — {sub}
      </div>
    </div>
  );
};

export default RedCard;
