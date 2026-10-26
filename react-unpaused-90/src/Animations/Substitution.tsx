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
    <div className="relative flex flex-col items-center justify-center text-center select-none py-2 sm:py-4 px-2 sm:px-4 max-w-xl mx-auto w-full">
      <div className="w-14 h-14 sm:w-18 sm:h-18 rounded-full bg-sky-500 text-white flex items-center justify-center shadow-[0_0_35px_rgba(14,165,233,0.75)] anim-goal-ball">
        <SubIcon className="w-7 h-7 sm:w-9 sm:h-9" />
      </div>
      <div className="mt-2.5 px-4 sm:px-7 py-1.5 sm:py-2.5 rounded-xl bg-slate-900 border-2 border-sky-400 shadow-2xl anim-banner max-w-[92vw]">
        <div className="text-[10px] sm:text-xs font-mono font-bold text-sky-300 uppercase truncate">
          {club} • {min}&apos; MINUTE
        </div>
        <div className="text-xl sm:text-3xl md:text-4xl font-display font-black uppercase text-white leading-none mt-0.5 truncate">
          {player}
        </div>
      </div>
      <div className="mt-2 px-4 sm:px-5 py-1 sm:py-1.5 rounded-full bg-white text-slate-900 font-mono text-xs sm:text-sm font-extrabold shadow-lg max-w-[92vw] truncate">
        {sub}
      </div>
    </div>
  );
};

export default Substitution;
