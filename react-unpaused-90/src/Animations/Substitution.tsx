import React from "react";
import { SubIcon } from "../components/Icons";

interface SubstitutionAnimationProps {
  min: number;
  club: string;
  player: string;
  sub: string;
}

export const Substitution: React.FC<SubstitutionAnimationProps> = ({
  min,
  club,
  player,
  sub,
}) => {
  return (
    <div className="relative flex flex-col items-center text-center select-none">
      <div className="w-16 h-16 rounded-full bg-sky-500 text-white flex items-center justify-center shadow-lg anim-goal-ball">
        <SubIcon className="w-9 h-9" />
      </div>
      <div className="mt-3 px-6 py-2 rounded-xl bg-slate-900 border-2 border-sky-400 shadow-2xl anim-banner">
        <div className="text-xs font-mono font-bold text-sky-300 uppercase">
          {club} • {min}&apos; MINUTE
        </div>
        <div className="text-2xl sm:text-3xl font-display font-black uppercase text-white">
          {player}
        </div>
      </div>
      <div className="mt-2 px-4 py-1 rounded-full bg-white text-slate-900 font-mono text-xs font-extrabold shadow">
        {sub}
      </div>
    </div>
  );
};

export default Substitution;
