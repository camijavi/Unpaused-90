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
    <div className="relative flex flex-col items-center justify-center text-center select-none py-4 px-4 max-w-xl mx-auto">
      <div className="w-20 h-20 rounded-full bg-sky-500 text-white flex items-center justify-center shadow-[0_0_35px_rgba(14,165,233,0.75)] anim-goal-ball">
        <SubIcon className="w-10 h-10" />
      </div>
      <div className="mt-3 px-7 py-2.5 rounded-xl bg-slate-900 border-2 border-sky-400 shadow-2xl anim-banner">
        <div className="text-xs font-mono font-bold text-sky-300 uppercase">
          {club} • {min}&apos; MINUTE
        </div>
        <div className="text-3xl sm:text-4xl font-display font-black uppercase text-white leading-none mt-0.5">
          {player}
        </div>
      </div>
      <div className="mt-3 px-5 py-1.5 rounded-full bg-white text-slate-900 font-mono text-xs sm:text-sm font-extrabold shadow-lg max-w-full truncate">
        {sub}
      </div>
    </div>
  );
};

export default Substitution;
