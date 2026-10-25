import React from "react";

export const Navbar: React.FC = () => {
  return (
    <header className="sticky top-0 z-40 bg-black border-b border-slate-800 shadow-md">
      {/* Thin Premier League Top Accent Strip */}
      <div className="h-1.5 w-full bg-gradient-to-r from-pl-purple via-mint to-pl-crimson"></div>

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between gap-3">
        {/* Left: Sky MNF Slanted Bug (Brief C) Final Logo + Tagline */}
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2.5">
            <div className="flex items-center bg-gradient-to-r from-pl-purple to-slate-900 px-3 py-1 rounded border border-mint/50 -skew-x-12 shadow-sm">
              <span className="text-base font-display font-black tracking-wider text-white skew-x-12">
                UNPAUSED
              </span>
              <span className="text-base font-display font-black text-mint-bright ml-1.5 skew-x-12">
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

          <div className="hidden sm:flex items-center gap-2 pl-3 border-l border-slate-800">
            <span className="px-2 py-0.5 rounded bg-pl-purple text-[10px] font-mono font-bold uppercase tracking-widest text-mint-bright">
              INSIDE THE GAME
            </span>
            <span className="text-[11px] font-semibold text-slate-300 italic">
              &ldquo;Never feel like you missed the match.&rdquo;
            </span>
          </div>
        </div>

        {/* Right: Constant Live Broadcast Telemetry Pill */}
        <div className="flex items-center gap-2.5 bg-slate-900 px-3 py-1.5 rounded-xl border border-slate-700">
          <span className="relative flex h-2.5 w-2.5">
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-600 opacity-75"></span>
            <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-red-600"></span>
          </span>
          <span
            className="text-xs font-mono font-extrabold tracking-wider text-white"
            id="header-live-clock"
          >
            LIVE • 60:14
          </span>
        </div>
      </div>
    </header>
  );
};

export default Navbar;
