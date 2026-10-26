import React, { useEffect, useRef, useState } from "react";
import {
  BallIcon,
  CheckIcon,
  ChevronDownIcon,
  GlobeIcon,
  HeadphonesIcon,
  TimerIcon,
} from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

interface HeroConsoleProps {
  stoppedMinute: number;
  currentMinute: number;
  mode: "casual" | "analyst" | "child";
  bias: "neutral" | "arsenal" | "liverpool";
  length: "1m" | "5m" | "full";
  lang: "en" | "es";
  isGenerating: boolean;
  generatingWindow?: { stopped: number; current: number } | null;
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
  generatingWindow,
  onChangeStoppedMinute,
  onChangeMode,
  onChangeBias,
  onChangeLength,
  onChangeLang,
  onCatchMeUp,
}) => {
  const { t } = useLanguage();
  const [openDropdown, setOpenDropdown] = useState<"length" | "lang" | null>(
    null
  );
  const lengthRef = useRef<HTMLDivElement | null>(null);
  const langRef = useRef<HTMLDivElement | null>(null);

  const maxStoppedMinute = Math.max(1, currentMinute - 1);
  const clampedStoppedMinute = Math.min(stoppedMinute, maxStoppedMinute);
  const missedMinutes = Math.max(0, currentMinute - clampedStoppedMinute);

  const recapDepthOptions = [
    {
      id: "1m" as const,
      label: t.heroConsole.controls.recapDepth.options.sprint.label,
      shortLabel: t.heroConsole.controls.recapDepth.options.sprint.shortLabel,
      sub: t.heroConsole.controls.recapDepth.options.sprint.sub,
      badge: t.heroConsole.controls.recapDepth.options.sprint.badge,
    },
    {
      id: "5m" as const,
      label: t.heroConsole.controls.recapDepth.options.deep.label,
      shortLabel: t.heroConsole.controls.recapDepth.options.deep.shortLabel,
      sub: t.heroConsole.controls.recapDepth.options.deep.sub,
      badge: t.heroConsole.controls.recapDepth.options.deep.badge,
    },
    {
      id: "full" as const,
      label: t.heroConsole.controls.recapDepth.options.full.label,
      shortLabel: t.heroConsole.controls.recapDepth.options.full.shortLabel,
      sub: t.heroConsole.controls.recapDepth.options.full.sub,
      badge: t.heroConsole.controls.recapDepth.options.full.badge,
    },
  ];

  const audioFeedOptions = [
    {
      id: "en" as const,
      label: t.heroConsole.controls.audioFeed.options.en.label,
      shortLabel: t.heroConsole.controls.audioFeed.options.en.shortLabel,
      sub: t.heroConsole.controls.audioFeed.options.en.sub,
      badge: t.heroConsole.controls.audioFeed.options.en.badge,
    },
    {
      id: "es" as const,
      label: t.heroConsole.controls.audioFeed.options.es.label,
      shortLabel: t.heroConsole.controls.audioFeed.options.es.shortLabel,
      sub: t.heroConsole.controls.audioFeed.options.es.sub,
      badge: t.heroConsole.controls.audioFeed.options.es.badge,
    },
  ];

  const modeNames: Record<typeof mode, string> = {
    casual: t.heroConsole.controls.persona.casual.label,
    analyst: t.heroConsole.controls.persona.analyst.label,
    child: t.heroConsole.controls.persona.child.label,
  };
  const biasNames: Record<typeof bias, string> = {
    neutral: t.heroConsole.controls.bias.neutral,
    arsenal: t.heroConsole.controls.bias.arsenal,
    liverpool: t.heroConsole.controls.bias.liverpool,
  };
  const lenNames: Record<typeof length, string> = {
    "1m": t.heroConsole.controls.recapDepth.options.sprint.shortLabel,
    "5m": t.heroConsole.controls.recapDepth.options.deep.shortLabel,
    full: t.heroConsole.controls.recapDepth.options.full.shortLabel,
  };

  const currentLengthObj =
    recapDepthOptions.find((opt) => opt.id === length) ?? recapDepthOptions[0];

  const currentLangObj =
    audioFeedOptions.find((opt) => opt.id === lang) ?? audioFeedOptions[0];

  // Close dropdowns on outside click or Escape
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as Node;
      if (
        lengthRef.current &&
        !lengthRef.current.contains(target) &&
        langRef.current &&
        !langRef.current.contains(target)
      ) {
        setOpenDropdown(null);
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpenDropdown(null);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  return (
    <section
      aria-label="Unpause Catch-Up Control Bar"
      className="broadcast-slab rounded-2xl p-5 sm:p-6 relative"
    >
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 mb-5 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="w-2 h-8 bg-pl-purple rounded-sm"></div>
          <div>
            <span className="text-[11px] font-mono font-bold uppercase tracking-widest text-teal-700">
              {t.heroConsole.badge}
            </span>
            <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900">
              {t.heroConsole.title}
            </h2>
          </div>
        </div>
        <div className="flex items-center gap-2 bg-slate-100 px-3.5 py-2 rounded-xl border border-slate-300">
          <span className="text-xs font-mono text-slate-600">
            {t.heroConsole.missedAction}
          </span>
          <strong
            id="missed-minutes-badge"
            className="text-sm font-mono font-extrabold text-pl-purple"
          >
            {missedMinutes}{" "}
            {missedMinutes === 1
              ? t.heroConsole.missedMinute
              : t.heroConsole.missedMinutes}
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
                {t.heroConsole.controls.timeWindow.number}:
              </label>
              <span className="font-mono font-extrabold text-pl-purple text-sm bg-purple-100 px-2 py-0.5 rounded border border-purple-300">
                <span id="stopped-minute-val">{clampedStoppedMinute}&apos;</span>{" "}
                → <span id="current-minute-val">{currentMinute}&apos;</span>
              </span>
            </div>
            <p className="text-[11px] text-slate-500 mb-3">
              {t.heroConsole.controls.timeWindow.sub}:
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
              <span>{t.heroConsole.controls.timeWindow.minLabel}</span>
              <span>25&apos;</span>
              <span>45&apos; HT</span>
              <span>70&apos;</span>
              <span>
                {currentMinute}&apos;{" "}
                {currentMinute >= 90
                  ? t.scoreContainer.halves.fullTime
                  : t.heroConsole.controls.timeWindow.liveNow}
              </span>
            </div>
          </div>
        </div>

        {/* Control 2: Audience Mode Selector (3 cols on lg) */}
        <div className="lg:col-span-3 bg-slate-50 p-4 rounded-xl border border-slate-200 flex flex-col justify-between">
          <div>
            <label className="block text-xs font-mono font-bold uppercase text-slate-700 mb-1">
              {t.heroConsole.controls.persona.number}:
            </label>
            <p className="text-[11px] text-slate-500 mb-2.5">
              {t.heroConsole.controls.persona.sub}:
            </p>
          </div>
          <div
            className="grid grid-cols-3 gap-1.5"
            role="group"
            aria-label="Audience Mode"
          >
            {(
              [
                {
                  id: "casual",
                  label: t.heroConsole.controls.persona.casual.label,
                },
                {
                  id: "analyst",
                  label: t.heroConsole.controls.persona.analyst.label,
                },
                {
                  id: "child",
                  label: t.heroConsole.controls.persona.child.label,
                },
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

        {/* Control 3: Supporter Bias Neutral/Arsenal/Liverpool (3 cols on lg) */}
        <div className="lg:col-span-3 bg-slate-50 p-4 rounded-xl border border-slate-200 flex flex-col justify-between">
          <div>
            <label className="block text-xs font-mono font-bold uppercase text-slate-700 mb-1">
              {t.heroConsole.controls.bias.number}:
            </label>
            <p className="text-[11px] text-slate-500 mb-2.5">
              {t.heroConsole.controls.bias.sub}:
            </p>
          </div>
          <div
            className="grid grid-cols-3 gap-1.5"
            role="group"
            aria-label="Supporter Bias"
          >
            {(
              [
                {
                  id: "neutral",
                  label: t.heroConsole.controls.bias.neutral,
                },
                {
                  id: "arsenal",
                  label: t.heroConsole.controls.bias.arsenal,
                },
                {
                  id: "liverpool",
                  label: t.heroConsole.controls.bias.liverpool,
                },
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

        {/* Controls 4 & 5: Enhanced Custom Broadcast Dropdowns (2 cols on lg) */}
        <div className="lg:col-span-2 flex flex-col justify-between gap-2.5">
          {/* Custom Dropdown 04: Recap Depth */}
          <div
            ref={lengthRef}
            className="relative bg-slate-50 p-3 rounded-xl border border-slate-200 hover:border-slate-300 transition-colors"
          >
            <div className="flex items-center justify-between mb-1.5">
              <label
                id="length-dropdown-label"
                className="text-[10px] font-mono font-bold uppercase text-slate-500 flex items-center gap-1.5"
              >
                <TimerIcon className="w-3.5 h-3.5 text-pl-purple shrink-0" />
                <span>{t.heroConsole.controls.recapDepth.number}</span>
              </label>
              <span className="text-[9px] font-mono font-extrabold uppercase px-1.5 py-0.5 rounded bg-purple-100 text-pl-purple border border-purple-200">
                {currentLengthObj.badge}
              </span>
            </div>

            <button
              id="length-dropdown-btn"
              type="button"
              aria-haspopup="listbox"
              aria-expanded={openDropdown === "length"}
              aria-labelledby="length-dropdown-label"
              onClick={() =>
                setOpenDropdown((prev) => (prev === "length" ? null : "length"))
              }
              className={`w-full bg-white text-slate-900 text-xs font-bold rounded-lg px-2.5 py-2 border shadow-xs flex items-center justify-between transition-all cursor-pointer focus:outline-none focus:ring-2 focus:ring-pl-purple/30 ${
                openDropdown === "length"
                  ? "border-pl-purple ring-2 ring-pl-purple/20 bg-purple-50/20"
                  : "border-slate-300 hover:border-pl-purple/60 hover:bg-slate-50/80"
              }`}
            >
              <span className="truncate">{currentLengthObj.shortLabel}</span>
              <ChevronDownIcon
                className={`w-3.5 h-3.5 text-slate-400 transition-transform duration-200 shrink-0 ml-1.5 ${
                  openDropdown === "length" ? "rotate-180 text-pl-purple" : ""
                }`}
              />
            </button>

            {/* Floating Popover Menu */}
            {openDropdown === "length" && (
              <div
                role="listbox"
                aria-labelledby="length-dropdown-label"
                className="absolute left-0 right-0 sm:right-auto sm:w-[240px] top-full mt-1.5 z-50 bg-white rounded-xl border border-slate-200 shadow-2xl p-1.5 space-y-1 backdrop-blur-md anim-fade-in"
              >
                <div className="px-2 py-1 border-b border-slate-100 text-[10px] font-mono font-bold uppercase text-slate-400">
                  {t.heroConsole.controls.recapDepth.title}
                </div>
                {recapDepthOptions.map((opt) => {
                  const isSelected = length === opt.id;
                  return (
                    <button
                      key={opt.id}
                      role="option"
                      aria-selected={isSelected}
                      type="button"
                      onClick={() => {
                        onChangeLength(opt.id);
                        setOpenDropdown(null);
                      }}
                      className={`w-full text-left px-2.5 py-2 rounded-lg text-xs transition-all flex items-center justify-between gap-2 cursor-pointer ${
                        isSelected
                          ? "bg-pl-purple text-white font-bold shadow-sm"
                          : "text-slate-800 hover:bg-slate-100 font-semibold"
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-1.5">
                          <span className="truncate">{opt.label}</span>
                          <span
                            className={`text-[9px] font-mono font-extrabold px-1.5 py-0.5 rounded shrink-0 ${
                              isSelected
                                ? "bg-white/20 text-mint-bright"
                                : "bg-slate-100 text-slate-600 border border-slate-200"
                            }`}
                          >
                            {opt.badge}
                          </span>
                        </div>
                        <p
                          className={`text-[10px] mt-0.5 truncate ${
                            isSelected ? "text-purple-200" : "text-slate-500"
                          }`}
                        >
                          {opt.sub}
                        </p>
                      </div>
                      {isSelected && (
                        <CheckIcon className="w-4 h-4 text-mint-bright shrink-0" />
                      )}
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          {/* Custom Dropdown 05: Audio Feed */}
          <div
            ref={langRef}
            className="relative bg-slate-50 p-3 rounded-xl border border-slate-200 hover:border-slate-300 transition-colors"
          >
            <div className="flex items-center justify-between mb-1.5">
              <label
                id="lang-dropdown-label"
                className="text-[10px] font-mono font-bold uppercase text-slate-500 flex items-center gap-1.5"
              >
                <GlobeIcon className="w-3.5 h-3.5 text-pl-purple shrink-0" />
                <span>{t.heroConsole.controls.audioFeed.number}</span>
              </label>
              <span className="text-[9px] font-mono font-extrabold uppercase px-1.5 py-0.5 rounded bg-purple-100 text-pl-purple border border-purple-200">
                {currentLangObj.badge}
              </span>
            </div>

            <button
              id="lang-dropdown-btn"
              type="button"
              aria-haspopup="listbox"
              aria-expanded={openDropdown === "lang"}
              aria-labelledby="lang-dropdown-label"
              onClick={() =>
                setOpenDropdown((prev) => (prev === "lang" ? null : "lang"))
              }
              className={`w-full bg-white text-slate-900 text-xs font-bold rounded-lg px-2.5 py-2 border shadow-xs flex items-center justify-between transition-all cursor-pointer focus:outline-none focus:ring-2 focus:ring-pl-purple/30 ${
                openDropdown === "lang"
                  ? "border-pl-purple ring-2 ring-pl-purple/20 bg-purple-50/20"
                  : "border-slate-300 hover:border-pl-purple/60 hover:bg-slate-50/80"
              }`}
            >
              <div className="flex items-center gap-1.5 min-w-0">
                <HeadphonesIcon className="w-3.5 h-3.5 text-pl-purple shrink-0" />
                <span className="truncate">{currentLangObj.shortLabel}</span>
              </div>
              <ChevronDownIcon
                className={`w-3.5 h-3.5 text-slate-400 transition-transform duration-200 shrink-0 ml-1.5 ${
                  openDropdown === "lang" ? "rotate-180 text-pl-purple" : ""
                }`}
              />
            </button>

            {/* Floating Popover Menu */}
            {openDropdown === "lang" && (
              <div
                role="listbox"
                aria-labelledby="lang-dropdown-label"
                className="absolute left-0 right-0 sm:right-auto sm:w-[240px] top-full mt-1.5 z-50 bg-white rounded-xl border border-slate-200 shadow-2xl p-1.5 space-y-1 backdrop-blur-md anim-fade-in"
              >
                <div className="px-2 py-1 border-b border-slate-100 text-[10px] font-mono font-bold uppercase text-slate-400">
                  {t.heroConsole.controls.audioFeed.title}
                </div>
                {audioFeedOptions.map((opt) => {
                  const isSelected = lang === opt.id;
                  return (
                    <button
                      key={opt.id}
                      role="option"
                      aria-selected={isSelected}
                      type="button"
                      onClick={() => {
                        onChangeLang(opt.id);
                        setOpenDropdown(null);
                      }}
                      className={`w-full text-left px-2.5 py-2 rounded-lg text-xs transition-all flex items-center justify-between gap-2 cursor-pointer ${
                        isSelected
                          ? "bg-pl-purple text-white font-bold shadow-sm"
                          : "text-slate-800 hover:bg-slate-100 font-semibold"
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-1.5">
                          <span className="truncate">{opt.label}</span>
                          <span
                            className={`text-[9px] font-mono font-extrabold px-1.5 py-0.5 rounded shrink-0 ${
                              isSelected
                                ? "bg-white/20 text-mint-bright"
                                : "bg-slate-100 text-slate-600 border border-slate-200"
                            }`}
                          >
                            {opt.badge}
                          </span>
                        </div>
                        <p
                          className={`text-[10px] mt-0.5 truncate ${
                            isSelected ? "text-purple-200" : "text-slate-500"
                          }`}
                        >
                          {opt.sub}
                        </p>
                      </div>
                      {isSelected && (
                        <CheckIcon className="w-4 h-4 text-mint-bright shrink-0" />
                      )}
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Primary Action CTA Row */}
      <div className="mt-5 pt-4 border-t border-slate-200 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4">
        <div className="flex items-center gap-2 text-xs">
          <span className="text-slate-500 font-mono uppercase font-bold">
            {t.heroConsole.cta.activeProfile}
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
              ? `${t.heroConsole.cta.generatingBtn} (${generatingWindow ? generatingWindow.stopped : clampedStoppedMinute}' → ${generatingWindow ? generatingWindow.current : currentMinute}')...`
              : `${t.heroConsole.cta.generateBtn} (${clampedStoppedMinute}' → ${currentMinute}')`}
          </span>
          <HeadphonesIcon className="w-5 h-5 text-mint-bright shrink-0" />
        </button>
      </div>
    </section>
  );
};

export default HeroConsole;
