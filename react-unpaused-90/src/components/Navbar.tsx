import React from "react";

interface NavbarProps {
  currentMinute?: number;
  subSeconds?: number;
  status?: string;
  onResetSimulation?: () => void;
}

export const Navbar: React.FC<NavbarProps> = ({
  currentMinute = 60,
  subSeconds = 0,
  status = "2nd Half",
  onResetSimulation,
}) => {
  const isFullTime = currentMinute >= 90 || status === "FullTime";
  const mm = String(Math.min(90, Math.max(1, currentMinute))).padStart(2, "0");
  const ss = isFullTime ? "00" : String(subSeconds % 60).padStart(2, "0");

  return (
    <header className="sticky top-0 z-40 bg-gradient-to-r from-[#0F1420]/95 via-[#161D2F]/95 to-[#0F1420]/95 backdrop-blur-xl border-b border-white/10 shadow-[0_8px_30px_rgba(15,23,42,0.22)]">
      {/* Thin Premier League Top Accent Strip */}
      <div className="h-1.5 w-full bg-gradient-to-r from-pl-purple via-mint to-pl-crimson"></div>

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between gap-3">
        {/* Left: Sky MNF Slanted Bug (Brief C) Final Logo + Tagline */}
        <div className="flex items-center gap-3.5">
          <div className="flex items-center gap-2.5">
            <div className="flex items-center bg-gradient-to-r from-pl-purple via-[#2A0838] to-slate-900 px-3.5 py-1.5 rounded-md border border-mint-bright/60 -skew-x-12 shadow-[0_0_20px_rgba(0,245,212,0.18)]">
              <span className="text-lg font-display font-black tracking-wider text-white skew-x-12">
                UNPAUSED
              </span>
              <span className="text-lg font-display font-black text-mint-bright ml-1.5 skew-x-12">
                90
              </span>
              <svg
                className="w-3.5 h-3.5 ml-0.5 skew-x-12"
                viewBox="0 0 16 16"
                fill="none"
              >
                <polygon points="2,1 15,6 5,15" fill="#00F5D4" />
              </svg>
            </div>
          </div>

          <div className="hidden sm:flex items-center gap-2.5 pl-3.5 border-l border-white/15">
            <span className="px-2.5 py-0.5 rounded bg-pl-purple/90 border border-mint/30 text-[10px] font-mono font-bold uppercase tracking-widest text-mint-bright">
              INSIDE THE GAME
            </span>
            <span className="text-xs font-medium text-slate-300 italic">
              &ldquo;Never feel like you missed the match.&rdquo;
            </span>
          </div>
        </div>

        {/* Right: Live Simulation Clock Pill + Reset Simulation Button */}
        <div className="flex items-center gap-2.5">
          {onResetSimulation && (
            <button
              type="button"
              onClick={onResetSimulation}
              title="Initialize a new live match simulation starting at a random minute (1'–90')"
              className="hidden md:inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-white/[0.06] hover:bg-white/[0.14] text-mint-bright border border-mint-bright/30 text-[11px] font-mono font-bold uppercase tracking-wider transition-all cursor-pointer"
            >
              <span>New Random Match (1&apos;–90&apos;) ↻</span>
            </button>
          )}

          <div className="flex items-center gap-2.5 bg-white/[0.07] px-3.5 py-1.5 rounded-xl border border-white/15 backdrop-blur-md shadow-inner">
            <span className="relative flex h-2.5 w-2.5">
              {!isFullTime && (
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-500 opacity-75"></span>
              )}
              <span
                className={`relative inline-flex rounded-full h-2.5 w-2.5 ${
                  isFullTime ? "bg-emerald-400" : "bg-red-500"
                }`}
              ></span>
            </span>
            <span
              className="text-xs font-mono font-extrabold tracking-wider text-slate-100"
              id="header-live-clock"
            >
              {isFullTime ? `FULL TIME • 90:00` : `LIVE • ${mm}:${ss}`}
            </span>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Navbar;
