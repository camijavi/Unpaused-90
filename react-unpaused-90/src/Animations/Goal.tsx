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
    <div className="relative flex flex-col items-center text-center select-none">
      {/* Expanding Neon Shockwave Rings */}
      <div className="absolute w-36 h-36 rounded-full border-4 border-mint-bright anim-shockwave"></div>
      <div
        className="absolute w-36 h-36 rounded-full border-2 border-amber-300 anim-shockwave"
        style={{ animationDelay: "0.25s" }}
      ></div>

      {/* 3D Popping Football SVG Icon (No Emoji) */}
      <div className="w-20 h-20 rounded-full bg-gradient-to-br from-mint-bright to-emerald-500 flex items-center justify-center text-slate-950 shadow-[0_0_40px_#00F5D4] anim-goal-ball">
        <BallIcon className="w-11 h-11" />
      </div>

      {/* Slanted Broadcast GOAL Banner */}
      <div className="mt-3 px-8 py-2 rounded-xl bg-gradient-to-r from-pl-purple via-emerald-600 to-teal-500 border-2 border-mint-bright shadow-2xl anim-banner">
        <div className="text-xs font-mono font-extrabold uppercase tracking-widest text-mint-bright">
          {club} • {min}&apos; MINUTE
        </div>
        <div className="text-4xl sm:text-5xl font-display font-black italic uppercase tracking-wider text-white drop-shadow">
          GOOOAL!
        </div>
      </div>

      <div className="mt-2 px-4 py-1 rounded-full bg-white/95 text-slate-950 font-mono text-xs font-extrabold shadow">
        {player} — {sub}
      </div>
    </div>
  );
};

export default Goal;
