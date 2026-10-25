import React from "react";
import { BallIcon, HeadphonesIcon } from "./Icons";

interface HeroConsoleProps {
  stoppedMinute: number;
  currentMinute: number;
  mode: "casual" | "analyst" | "child";
  bias: "neutral" | "arsenal" | "liverpool";
  length: "1m" | "5m" | "full";
  lang: "en" | "es";
  isGenerating: boolean;
  onChangeStoppedMinute: (min: number) => void;
  onChangeMode: (mode: "casual" | "analyst" | "child") => void;
  onChangeBias: (bias: "neutral" | "arsenal" | "liverpool") => void;
  onChangeLength: (length: "1m" | "5m" | "full") => void;
  onChangeLang: (lang: "en" | "es") => void;
  onCatchMeUp: () => void;
}

export const HeroConsole: React.FC<HeroConsoleProps> = ({
  stoppedMinute,
  currentMinute,
  mode,
  bias,
  length,
  lang,
  isGenerating,
  onChangeStoppedMinute,
  onChangeMode,
  onChangeBias,
  onChangeLength,
  onChangeLang,
  onCatchMeUp,
}) => {
  const maxStoppedMinute = Math.max(1, currentMinute - 1);
  const clampedStoppedMinute = Math.min(stoppedMinute, maxStoppedMinute);
  const missedMinutes = Math.max(0, currentMinute - clampedStoppedMinute);

  const modeNames: Record<typeof mode, string> = {
    casual: "Casual Fan",
    analyst: "Analyst / Coach",
    child: "Young Child (6–10)",
  };
  const biasNames: Record<typeof bias, string> = {
    neutral: "Neutral 50/50",
    arsenal: "Arsenal Bias",
    liverpool: "Liverpool Bias",
  };
  const lenNames: Record<typeof length, string> = {
    "1m": "1m Sprint",
    "5m": "5m Catch-Up",
    full: "Full Summary",
  };

  return (
    <section
      aria-label="Unpause Catch-Up Control Bar"
      className="broadcast-slab rounded-2xl p-5 sm:p-6 relative overflow-hidden"
    >
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 mb-5 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="w-2 h-8 bg-pl-purple rounded-sm"></div>
          <div>
            <span className="text-[11px] font-mono font-bold uppercase tracking-widest text-teal-700">
              HERO CONSOLE • PERSONALIZE YOUR CATCH-UP
            </span>
            <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900">
              Time-Warp &amp; Broadcast Persona Mixer
            </h2>
          </div>
        </div>
        <div className="flex items-center gap-2 bg-slate-100 px-3.5 py-2 rounded-xl border border-slate-300">
          <span className="text-xs font-mono text-slate-600">Missed Action:</span>
          <strong
            id="missed-minutes-badge"
            className="text-sm font-mono font-extrabold text-pl-purple"
          >
            {missedMinutes} missed {missedMinutes === 1 ? "minute" : "minutes"}
          </strong>
        </div>
      </div>

      {/* 5 Customization Controls Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-12 gap-4 items-stretch">
        {/* Control 1: Time Window Slider (4 cols on lg) */}
        <div className="lg:col-span-4 bg-slate-50 p-4 rounded-xl border border-slate-200 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between text-xs mb-2">
              <label
                htmlFor="stopped-minute-slider"
                className="font-mono font-bold uppercase text-slate-700"
              >
                01. Stopped Watching At:
              </label>
              <span className="font-mono font-extrabold text-pl-purple text-sm bg-purple-100 px-2 py-0.5 rounded border border-purple-300">
                <span id="stopped-minute-val">{clampedStoppedMinute}&apos;</span>{" "}
                → <span id="current-minute-val">{currentMinute}&apos;</span>
              </span>
            </div>
            <p className="text-[11px] text-slate-500 mb-3">
              Drag scrubber to the exact minute you stepped away:
            </p>
          </div>
          <div>
            <input
              id="stopped-minute-slider"
              type="range"
              min={1}
              max={maxStoppedMinute}
              disabled={currentMinute <= 1}
              value={clampedStoppedMinute}
              onChange={(e) =>
                onChangeStoppedMinute(parseInt(e.target.value, 10))
              }
              className="w-full"
            />
            <div className="flex justify-between text-[10px] font-mono text-slate-500 mt-2">
              <span>1&apos; KO</span>
              <span>25&apos;</span>
              <span>45&apos; HT</span>
              <span>70&apos;</span>
              <span>
                {currentMinute}&apos; {currentMinute >= 90 ? "FT" : "LIVE"}
              </span>
            </div>
          </div>
        </div>

        {/* Control 2: Audience Mode Selector (3 cols on lg) */}
        <div className="lg:col-span-3 bg-slate-50 p-4 rounded-xl border border-slate-200 flex flex-col justify-between">
          <div>
            <label className="block text-xs font-mono font-bold uppercase text-slate-700 mb-1">
              02. Commentator Persona:
            </label>
            <p className="text-[11px] text-slate-500 mb-2.5">
              Tailors vocabulary &amp; tactical depth:
            </p>
          </div>
          <div
            className="grid grid-cols-3 gap-1.5"
            role="group"
            aria-label="Audience Mode"
          >
            {(
              [
                { id: "casual", label: "Casual Fan" },
                { id: "analyst", label: "Analyst" },
                { id: "child", label: "Kids (6–10)" },
              ] as const
            ).map((item) => {
              const isSelected = mode === item.id;
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => onChangeMode(item.id)}
                  className={`mode-btn px-2 py-2.5 rounded-lg text-xs transition-all cursor-pointer ${
                    isSelected
                      ? "font-bold bg-pl-purple text-white shadow-sm"
                      : "font-semibold bg-white text-slate-700 hover:bg-slate-100 border border-slate-300"
                  }`}
                >
                  {item.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Control 3: Favorite Team Bias Toggle (3 cols on lg) */}
        <div className="lg:col-span-3 bg-slate-50 p-4 rounded-xl border border-slate-200 flex flex-col justify-between">
          <div>
            <label className="block text-xs font-mono font-bold uppercase text-slate-700 mb-1">
              03. Supporter Bias Lens:
            </label>
            <p className="text-[11px] text-slate-500 mb-2.5">
              Neutral studio vs. partisan (&ldquo;our lads&rdquo;):
            </p>
          </div>
          <div
            className="grid grid-cols-3 gap-1.5"
            role="group"
            aria-label="Club Bias"
          >
            {(
              [
                { id: "neutral", label: "Neutral 50/50" },
                { id: "arsenal", label: "Arsenal Fan" },
                { id: "liverpool", label: "Liverpool Fan" },
              ] as const
            ).map((item) => {
              const isSelected = bias === item.id;
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => onChangeBias(item.id)}
                  className={`bias-btn px-2 py-2.5 rounded-lg text-xs transition-all cursor-pointer ${
                    isSelected
                      ? "font-bold bg-pl-purple text-white shadow-sm"
                      : "font-semibold bg-white text-slate-700 hover:bg-slate-100 border border-slate-300"
                  }`}
                >
                  {item.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Controls 4 & 5 stacked: Summary Length + Language (2 cols on lg) */}
        <div className="lg:col-span-2 flex flex-col justify-between gap-2.5">
          <div className="bg-slate-50 p-3 rounded-xl border border-slate-200">
            <label
              htmlFor="length-select"
              className="block text-[10px] font-mono font-bold uppercase text-slate-500 mb-1"
            >
              04. Recap Depth:
            </label>
            <select
              id="length-select"
              value={length}
              onChange={(e) =>
                onChangeLength(e.target.value as "1m" | "5m" | "full")
              }
              className="w-full bg-white text-slate-900 text-xs font-semibold rounded-lg px-2 py-1.5 border border-slate-300 focus:outline-none focus:border-pl-purple"
            >
              <option value="1m">1 min Quick Sprint</option>
              <option value="5m">5 min Deep Catch-Up</option>
              <option value="full">Full Tactical Summary</option>
            </select>
          </div>
          <div className="bg-slate-50 p-3 rounded-xl border border-slate-200">
            <label
              htmlFor="lang-select"
              className="block text-[10px] font-mono font-bold uppercase text-slate-500 mb-1"
            >
              05. Audio Feed:
            </label>
            <select
              id="lang-select"
              value={lang}
              onChange={(e) => onChangeLang(e.target.value as "en" | "es")}
              className="w-full bg-white text-slate-900 text-xs font-semibold rounded-lg px-2 py-1.5 border border-slate-300 focus:outline-none focus:border-pl-purple"
            >
              <option value="en">English (British AI)</option>
              <option value="es">Español (México)</option>
            </select>
          </div>
        </div>
      </div>

      {/* Primary Action CTA Row */}
      <div className="mt-5 pt-4 border-t border-slate-200 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4">
        <div className="flex items-center gap-2 text-xs">
          <span className="text-slate-500 font-mono uppercase font-bold">
            ACTIVE FEED PROFILE:
          </span>
          <span
            className="px-2.5 py-1 rounded bg-purple-100 font-mono font-bold text-pl-purple"
            id="active-persona-pill"
          >
            {biasNames[bias]} • {modeNames[mode]} • {lenNames[length]} •{" "}
            {lang.toUpperCase()}
          </span>
        </div>

        <button
          id="catch-me-up-btn"
          type="button"
          disabled={isGenerating}
          onClick={onCatchMeUp}
          className="px-7 py-3.5 rounded-xl font-display font-black uppercase text-base sm:text-lg tracking-wider bg-gradient-to-r from-pl-purple via-purple-800 to-indigo-900 text-white shadow-lg hover:brightness-110 active:scale-[0.99] disabled:opacity-60 transition-all flex items-center justify-center gap-3 cursor-pointer"
        >
          <BallIcon className="w-5 h-5 text-mint-bright shrink-0" />
          <span id="catch-btn-text">
            {isGenerating
              ? `Generating Live Catch-Up (${clampedStoppedMinute}' → ${currentMinute}')...`
              : `Catch Me Up & Auto-Play Audio (${clampedStoppedMinute}' → ${currentMinute}')`}
          </span>
          <HeadphonesIcon className="w-5 h-5 text-mint-bright shrink-0" />
        </button>
      </div>
    </section>
  );
};

export default HeroConsole;
