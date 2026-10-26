import React from "react";
import { BallIcon } from "../components/Icons";

interface GoalAnimationProps {
  min: number;
  club: string;
  player: string;
  sub: string;
}

export const Goal: React.FC<GoalAnimationProps> = ({
  min,
  club,
  player,
  sub,
}) => {
  return (
    <div className="relative flex flex-col items-center justify-center text-center select-none py-2 sm:py-4 px-2 sm:px-4 max-w-xl mx-auto w-full">
      {/* Icon Stage with Contained Expanding Neon Shockwave Rings */}
      <div className="relative flex items-center justify-center w-16 h-16 sm:w-20 sm:h-20">
        <div className="absolute w-14 h-14 sm:w-18 sm:h-18 rounded-full border-4 border-mint-bright anim-shockwave"></div>
        <div
          className="absolute w-14 h-14 sm:w-18 sm:h-18 rounded-full border-2 border-amber-300 anim-shockwave"
          style={{ animationDelay: "0.25s" }}
        ></div>

        {/* 3D Popping Football SVG Icon (No Emoji) */}
        <div className="relative z-10 w-14 h-14 sm:w-18 sm:h-18 rounded-full bg-gradient-to-br from-mint-bright to-emerald-500 flex items-center justify-center text-slate-950 shadow-[0_0_40px_#00F5D4] anim-goal-ball">
          <BallIcon className="w-8 h-8 sm:w-10 sm:h-10" />
        </div>
      </div>

      {/* Slanted Broadcast GOAL Banner */}
      <div className="mt-2.5 px-5 sm:px-8 py-1.5 sm:py-2.5 rounded-xl bg-gradient-to-r from-pl-purple via-emerald-600 to-teal-500 border-2 border-mint-bright shadow-2xl anim-banner max-w-[92vw]">
        <div className="text-[10px] sm:text-xs font-mono font-extrabold uppercase tracking-widest text-mint-bright truncate">
          {club} • {min}&apos; MINUTE
        </div>
        <div className="text-3xl sm:text-5xl font-display font-black italic uppercase tracking-wider text-white drop-shadow leading-none mt-0.5">
          GOOOAL!
        </div>
      </div>

      <div className="mt-2 px-4 sm:px-5 py-1 sm:py-1.5 rounded-full bg-white/95 text-slate-950 font-mono text-xs sm:text-sm font-extrabold shadow-lg max-w-[92vw] truncate">
        {player} — {sub}
      </div>
    </div>
  );
};

export default Goal;
