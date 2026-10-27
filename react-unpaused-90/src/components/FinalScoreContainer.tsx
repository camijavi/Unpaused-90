import React, { useEffect, useRef, useState } from "react";
import type {
  MatchData,
  TimelineEvent,
  PersonalizedSummaryResponse,
} from "../types/match";
import { useLanguage } from "../languages/LanguageContext";
import {
  BallIcon,
  CheckIcon,
  ChevronDownIcon,
  HeadphonesIcon,
  TimerIcon,
  FlashIcon,
  BookIcon,
  InfoIcon,
} from "./Icons";

export interface FinalScoreContainerProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  mode?: "casual" | "analyst" | "child";
  bias?: string;
  fullTimeSummary: PersonalizedSummaryResponse | null;
  isGeneratingSummary: boolean;
  onGenerateSummary: (
    autoPlayAudio: boolean,
    unlockedByGesture: boolean,
    length: "1m" | "5m" | "full",
    langOverride?: "en" | "es",
    modeOverride?: "casual" | "analyst" | "child",
    biasOverride?: string
  ) => void;
  onClose: () => void;
  onPlayAudio?: (story: string, voice?: string) => void;
  isPlayingAudio?: boolean;
  onChangeMode?: (mode: "casual" | "analyst" | "child") => void;
  onChangeBias?: (bias: string) => void;
}

export const FinalScoreContainer: React.FC<FinalScoreContainerProps> = ({
  match,
  timeline,
  mode = "casual",
  bias = "neutral",
  fullTimeSummary,
  isGeneratingSummary,
  onGenerateSummary,
  onClose,
  onPlayAudio,
  isPlayingAudio = false,
  onChangeMode,
  onChangeBias,
}) => {
  const { t, language } = useLanguage();
  const [selectedLength, setSelectedLength] = useState<"1m" | "5m" | "full">("full");
  const [openDropdown, setOpenDropdown] = useState<"persona" | "bias" | null>(null);

  const personaRef = useRef<HTMLDivElement | null>(null);
  const biasRef = useRef<HTMLDivElement | null>(null);

  // Close dropdowns on outside click or Escape
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as Node;
      if (
        personaRef.current &&
        !personaRef.current.contains(target) &&
        biasRef.current &&
        !biasRef.current.contains(target)
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

  const homeTeamName = match?.homeTeam?.name ?? "Home";
  const awayTeamName = match?.awayTeam?.name ?? "Away";
  const homeScore = match?.homeTeam?.score ?? 0;
  const awayScore = match?.awayTeam?.score ?? 0;
  const homePossession = match?.statistics?.possession?.home ?? 50;
  const awayPossession = match?.statistics?.possession?.away ?? 50;

  const labels = t.dashboard.finalScoreContainer;

  const formattedStats = labels.statsSubtitle
    .replace("{events}", String(timeline.length))
    .replace("{home}", String(homePossession))
    .replace("{away}", String(awayPossession));

  const isSpanish = language === "es";

  const homeLabel = homeTeamName
    ? isSpanish
      ? `Fan de ${homeTeamName}`
      : `${homeTeamName} Fan`
    : t.heroConsole.controls.bias.arsenal;

  const awayLabel = awayTeamName
    ? isSpanish
      ? `Fan de ${awayTeamName}`
      : `${awayTeamName} Fan`
    : t.heroConsole.controls.bias.liverpool;

  const personaOptions = [
    {
      id: "casual" as const,
      label: t.heroConsole.controls.persona.casual.label,
      sub: t.heroConsole.controls.persona.casual.sub,
    },
    {
      id: "analyst" as const,
      label: t.heroConsole.controls.persona.analyst.label,
      sub: t.heroConsole.controls.persona.analyst.sub,
    },
    {
      id: "child" as const,
      label: t.heroConsole.controls.persona.child.label,
      sub: t.heroConsole.controls.persona.child.sub,
    },
  ];

  const currentPersona =
    personaOptions.find((p) => p.id === mode) ?? personaOptions[0];

  const normalizedBiasKey = (() => {
    const b = (bias ?? "").toLowerCase();
    if (!b || b === "neutral" || b === "none") return "neutral";
    if (b === "home" || b === homeTeamName.toLowerCase()) return "home";
    if (b === "away" || b === awayTeamName.toLowerCase()) return "away";
    if (b.includes(homeTeamName.toLowerCase()) || homeTeamName.toLowerCase().includes(b)) return "home";
    if (b.includes(awayTeamName.toLowerCase()) || awayTeamName.toLowerCase().includes(b)) return "away";
    return b;
  })();

  const biasOptions = [
    {
      id: "neutral",
      label: t.heroConsole.controls.bias.neutral,
      sub: isSpanish ? "Cobertura equilibrada 50/50" : "Balanced 50/50 broadcast",
    },
    {
      id: "home",
      label: homeLabel,
      sub: isSpanish
        ? `Perspectiva pro-${homeTeamName}`
        : `Pro-${homeTeamName} perspective`,
    },
    {
      id: "away",
      label: awayLabel,
      sub: isSpanish
        ? `Perspectiva pro-${awayTeamName}`
        : `Pro-${awayTeamName} perspective`,
    },
  ];

  const currentBias =
    biasOptions.find((b) => b.id === normalizedBiasKey) ?? biasOptions[0];

  const handleModeChange = (newMode: "casual" | "analyst" | "child") => {
    onChangeMode?.(newMode);
    if (fullTimeSummary && !isGeneratingSummary) {
      onGenerateSummary(false, false, selectedLength, undefined, newMode, bias);
    }
  };

  const handleBiasChange = (newBiasKey: string) => {
    onChangeBias?.(newBiasKey);
    if (fullTimeSummary && !isGeneratingSummary) {
      onGenerateSummary(false, false, selectedLength, undefined, mode, newBiasKey);
    }
  };

  const handleGenerateClick = (lengthToUse: "1m" | "5m" | "full" = selectedLength) => {
    setSelectedLength(lengthToUse);
    onGenerateSummary(true, true, lengthToUse, undefined, mode, bias);
  };

  const handleLengthSelect = (len: "1m" | "5m" | "full") => {
    setSelectedLength(len);
    // If summary is already displayed, automatically regenerate with the new selected length
    if (fullTimeSummary && !isGeneratingSummary) {
      onGenerateSummary(false, false, len, undefined, mode, bias);
    }
  };

  return (
    <section
      aria-label="Full-Time Match Summary"
      className="broadcast-slab rounded-2xl p-4 sm:p-6 border-2 border-emerald-500/50 bg-gradient-to-br from-white via-emerald-50/20 to-teal-50/30 shadow-xl relative overflow-hidden w-full max-w-full"
    >
      {/* Top Accent Gradient Bar */}
      <div className="h-1.5 w-full bg-gradient-to-r from-emerald-500 via-teal-500 to-indigo-600 absolute top-0 left-0" />

      {/* Header Row: Left-Aligned Scoreboard + Action Buttons */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 mb-4 border-b border-slate-200">
        <div className="text-left">
          <h2 className="text-lg sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900">
            <span className="text-emerald-700">{labels.finalScoreLabel}</span>{" "}
            {homeTeamName} {homeScore} – {awayScore} {awayTeamName}
          </h2>
          <p className="text-xs sm:text-sm font-mono text-slate-600 mt-0.5">
            {formattedStats}
          </p>
        </div>

        <div className="flex items-center gap-2 self-start sm:self-auto shrink-0">
          {/* Main Generate / Regenerate CTA Button */}
          <button
            type="button"
            disabled={isGeneratingSummary}
            onClick={() => handleGenerateClick(selectedLength)}
            className="px-3.5 sm:px-4 py-2 rounded-xl font-display font-black uppercase text-xs sm:text-sm tracking-wider bg-emerald-600 hover:bg-emerald-700 active:scale-95 text-white shadow-md flex items-center gap-2 transition-all cursor-pointer disabled:opacity-60"
          >
            <BallIcon className="w-4 h-4 text-white shrink-0" />
            <span>
              {isGeneratingSummary
                ? labels.generatingBtn
                : fullTimeSummary
                  ? labels.regenerateBtn
                  : labels.generateBtn}
            </span>
            <HeadphonesIcon className="w-4 h-4 text-white shrink-0" />
          </button>

          {/* Dismiss button */}
          <button
            type="button"
            onClick={onClose}
            aria-label={t.common.close}
            title={t.common.close}
            className="w-8 h-8 rounded-lg bg-white hover:bg-red-50 text-slate-500 hover:text-red-600 border border-slate-200 hover:border-red-300 flex items-center justify-center transition-colors cursor-pointer shrink-0"
          >
            <svg
              className="w-4 h-4"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>
      </div>

      {/* Persona & Bias Selector Callout */}
      <div className="mb-4 p-3 sm:p-3.5 rounded-xl bg-gradient-to-r from-emerald-50/90 via-teal-50/70 to-slate-50 border border-emerald-200/90 shadow-2xs flex flex-col md:flex-row md:items-center justify-between gap-3 relative z-30">
        <div className="flex items-start sm:items-center gap-2.5">
          <div className="p-1.5 rounded-lg bg-emerald-600/10 text-emerald-700 shrink-0 mt-0.5 sm:mt-0">
            <InfoIcon className="w-4 h-4" />
          </div>
          <p className="text-xs sm:text-xs+ text-slate-700 leading-snug">
            {labels.filterReminder}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2 shrink-0 self-start md:self-center font-mono text-[11px]">
          {/* Custom Persona Dropdown */}
          <div ref={personaRef} className="relative">
            <button
              id="final-persona-select-btn"
              type="button"
              aria-haspopup="listbox"
              aria-expanded={openDropdown === "persona"}
              aria-label={labels.personaLabel}
              onClick={() =>
                setOpenDropdown((prev) => (prev === "persona" ? null : "persona"))
              }
              className={`inline-flex items-center gap-2 px-3 py-1.5 rounded-xl bg-white border text-emerald-950 font-semibold shadow-2xs transition-all cursor-pointer focus:outline-none focus:ring-2 focus:ring-emerald-500/20 ${
                openDropdown === "persona"
                  ? "border-emerald-500 ring-2 ring-emerald-500/20 bg-emerald-50/30"
                  : "border-emerald-300 hover:border-emerald-400 hover:bg-emerald-50/20"
              }`}
            >
              <span className="text-emerald-700 uppercase tracking-wider text-[10px] font-mono font-extrabold shrink-0">
                {labels.personaLabel}:
              </span>
              <span className="text-slate-900 font-bold text-xs truncate max-w-[130px] sm:max-w-[170px]">
                {currentPersona.label}
              </span>
              <ChevronDownIcon
                className={`w-3.5 h-3.5 text-emerald-600 transition-transform duration-200 shrink-0 ${
                  openDropdown === "persona" ? "rotate-180" : ""
                }`}
              />
            </button>

            {/* Persona Popover Menu */}
            {openDropdown === "persona" && (
              <div
                role="listbox"
                aria-label={labels.personaLabel}
                className="absolute left-0 sm:left-auto sm:right-0 top-full mt-1.5 z-50 w-[240px] sm:w-[260px] bg-white rounded-xl border border-emerald-200 shadow-2xl p-1.5 space-y-1 backdrop-blur-md anim-fade-in"
              >
                <div className="px-2.5 py-1 border-b border-emerald-100 text-[10px] font-mono font-extrabold uppercase tracking-wider text-emerald-700 flex items-center justify-between">
                  <span>{labels.personaLabel}</span>
                  <span className="text-[9px] text-slate-400 font-normal">
                    {personaOptions.length} {isSpanish ? "opciones" : "options"}
                  </span>
                </div>
                {personaOptions.map((opt) => {
                  const isSelected = mode === opt.id;
                  return (
                    <button
                      key={opt.id}
                      id={`final-persona-opt-${opt.id}`}
                      type="button"
                      role="option"
                      aria-selected={isSelected}
                      onClick={() => {
                        handleModeChange(opt.id);
                        setOpenDropdown(null);
                      }}
                      className={`w-full text-left px-2.5 py-2 rounded-lg text-xs transition-all flex items-center justify-between gap-2.5 cursor-pointer ${
                        isSelected
                          ? "bg-emerald-600 text-white font-bold shadow-xs"
                          : "text-slate-800 hover:bg-emerald-50/80 font-semibold"
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-xs">{opt.label}</div>
                        <div
                          className={`text-[10px] truncate mt-0.5 ${
                            isSelected ? "text-emerald-100" : "text-slate-500"
                          }`}
                        >
                          {opt.sub}
                        </div>
                      </div>
                      {isSelected && (
                        <CheckIcon className="w-4 h-4 text-white shrink-0" />
                      )}
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          {/* Custom Bias Dropdown */}
          <div ref={biasRef} className="relative">
            <button
              id="final-bias-select-btn"
              type="button"
              aria-haspopup="listbox"
              aria-expanded={openDropdown === "bias"}
              aria-label={labels.biasLabel}
              onClick={() =>
                setOpenDropdown((prev) => (prev === "bias" ? null : "bias"))
              }
              className={`inline-flex items-center gap-2 px-3 py-1.5 rounded-xl bg-white border text-teal-950 font-semibold shadow-2xs transition-all cursor-pointer focus:outline-none focus:ring-2 focus:ring-teal-500/20 ${
                openDropdown === "bias"
                  ? "border-teal-500 ring-2 ring-teal-500/20 bg-teal-50/30"
                  : "border-teal-300 hover:border-teal-400 hover:bg-teal-50/20"
              }`}
            >
              <span className="text-teal-700 uppercase tracking-wider text-[10px] font-mono font-extrabold shrink-0">
                {labels.biasLabel}:
              </span>
              <span className="text-slate-900 font-bold text-xs truncate max-w-[130px] sm:max-w-[170px]">
                {currentBias.label}
              </span>
              <ChevronDownIcon
                className={`w-3.5 h-3.5 text-teal-600 transition-transform duration-200 shrink-0 ${
                  openDropdown === "bias" ? "rotate-180" : ""
                }`}
              />
            </button>

            {/* Bias Popover Menu */}
            {openDropdown === "bias" && (
              <div
                role="listbox"
                aria-label={labels.biasLabel}
                className="absolute right-0 top-full mt-1.5 z-50 w-[240px] sm:w-[260px] bg-white rounded-xl border border-teal-200 shadow-2xl p-1.5 space-y-1 backdrop-blur-md anim-fade-in"
              >
                <div className="px-2.5 py-1 border-b border-teal-100 text-[10px] font-mono font-extrabold uppercase tracking-wider text-teal-700 flex items-center justify-between">
                  <span>{labels.biasLabel}</span>
                  <span className="text-[9px] text-slate-400 font-normal">
                    {biasOptions.length} {isSpanish ? "opciones" : "options"}
                  </span>
                </div>
                {biasOptions.map((opt) => {
                  const isSelected = normalizedBiasKey === opt.id;
                  return (
                    <button
                      key={opt.id}
                      id={`final-bias-opt-${opt.id}`}
                      type="button"
                      role="option"
                      aria-selected={isSelected}
                      onClick={() => {
                        handleBiasChange(opt.id);
                        setOpenDropdown(null);
                      }}
                      className={`w-full text-left px-2.5 py-2 rounded-lg text-xs transition-all flex items-center justify-between gap-2.5 cursor-pointer ${
                        isSelected
                          ? "bg-teal-600 text-white font-bold shadow-xs"
                          : "text-slate-800 hover:bg-teal-50/80 font-semibold"
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-xs">{opt.label}</div>
                        <div
                          className={`text-[10px] truncate mt-0.5 ${
                            isSelected ? "text-teal-100" : "text-slate-500"
                          }`}
                        >
                          {opt.sub}
                        </div>
                      </div>
                      {isSelected && (
                        <CheckIcon className="w-4 h-4 text-white shrink-0" />
                      )}
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Control Toolbar: Length Selector with Icons */}
      <div className="flex flex-wrap items-center gap-2 mb-4">
        {/* Length Selector Pills */}
        <div className="flex flex-wrap items-center gap-1.5 p-1 rounded-xl bg-slate-100 border border-slate-200 w-full sm:w-auto">
          <button
            type="button"
            onClick={() => handleLengthSelect("1m")}
            className={`flex-1 sm:flex-initial px-3 py-1.5 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer flex items-center justify-center gap-1.5 ${
              selectedLength === "1m"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            <FlashIcon className="w-3.5 h-3.5 shrink-0" />
            <span>{labels.lengthOptions["1m"]}</span>
          </button>
          <button
            type="button"
            onClick={() => handleLengthSelect("5m")}
            className={`flex-1 sm:flex-initial px-3 py-1.5 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer flex items-center justify-center gap-1.5 ${
              selectedLength === "5m"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            <TimerIcon className="w-3.5 h-3.5 shrink-0" />
            <span>{labels.lengthOptions["5m"]}</span>
          </button>
          <button
            type="button"
            onClick={() => handleLengthSelect("full")}
            className={`flex-1 sm:flex-initial px-3 py-1.5 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer flex items-center justify-center gap-1.5 ${
              selectedLength === "full"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            <BookIcon className="w-3.5 h-3.5 shrink-0" />
            <span>{labels.lengthOptions["full"]}</span>
          </button>
        </div>
      </div>

      {/* Main Content Body */}
      {isGeneratingSummary ? (
        <div className="bg-white/90 backdrop-blur-sm rounded-xl p-6 sm:p-8 border border-emerald-200 text-center flex flex-col items-center justify-center gap-3 w-full">
          <div className="w-8 h-8 border-3 border-emerald-600 border-t-transparent rounded-full animate-spin" />
          <p className="font-display font-bold uppercase tracking-wider text-xs sm:text-sm text-emerald-800">
            {labels.generatingBtn}
          </p>
        </div>
      ) : fullTimeSummary ? (
        <div className="bg-white/95 backdrop-blur-sm rounded-xl p-4 sm:p-6 border border-emerald-200 shadow-inner flex flex-col w-full">
          {/* Summary Metadata Bar */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 text-xs font-mono text-slate-500 pb-3 border-b border-emerald-100 shrink-0">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-extrabold text-emerald-800 uppercase px-2.5 py-0.5 rounded-md bg-emerald-50 border border-emerald-200">
                {labels.reportTitle}
              </span>
              <span className="text-slate-600">
                ({fullTimeSummary.mode} • {fullTimeSummary.favoriteTeam})
              </span>
            </div>

            <div className="flex flex-wrap items-center gap-2.5">
              <span className="px-2 py-0.5 rounded bg-slate-100 text-slate-700 font-bold">
                {fullTimeSummary.wordCount} {labels.words} • ~
                {fullTimeSummary.estimatedDurationSeconds}
                {labels.audioEstimate}
              </span>

              {onPlayAudio && fullTimeSummary.story && (
                <button
                  type="button"
                  onClick={() =>
                    onPlayAudio(fullTimeSummary.story, fullTimeSummary.voice)
                  }
                  className="px-3 py-1 rounded-lg bg-teal-600 hover:bg-teal-700 text-white font-bold text-xs flex items-center gap-1.5 transition-all cursor-pointer shadow-sm"
                >
                  <HeadphonesIcon className="w-3.5 h-3.5 shrink-0" />
                  <span>
                    {isPlayingAudio ? labels.playingAudio : labels.playAudio}
                  </span>
                </button>
              )}
            </div>
          </div>

          {/* Story Paragraphs with Smooth Internal Scroller */}
          <div
            id="final-score-story-scroller"
            className="mt-4 space-y-4 text-slate-800 font-sans text-sm sm:text-base leading-relaxed whitespace-pre-line max-h-[340px] sm:max-h-[420px] overflow-y-auto pr-3 custom-scrollbar"
          >
            {fullTimeSummary.story}
          </div>
        </div>
      ) : (
        <div className="bg-white/70 backdrop-blur-sm rounded-xl p-5 sm:p-8 border border-emerald-200/80 text-center flex flex-col items-center justify-center gap-3 w-full">
          <div className="w-12 h-12 rounded-full bg-emerald-100 flex items-center justify-center text-emerald-600">
            <BallIcon className="w-6 h-6" />
          </div>
          <div className="max-w-lg px-2">
            <p className="text-sm sm:text-base font-bold text-slate-900 font-display uppercase tracking-wide">
              {labels.reportTitle}
            </p>
            <p className="text-xs sm:text-sm text-slate-600 mt-1 font-mono">
              {formattedStats}
            </p>
            <p className="text-xs text-slate-500 mt-2">
              {labels.filterReminderShort}
            </p>
          </div>
          <button
            type="button"
            onClick={() => handleGenerateClick(selectedLength)}
            className="mt-1 px-4 py-2.5 rounded-xl font-display font-black uppercase text-xs sm:text-sm tracking-wider bg-emerald-600 hover:bg-emerald-700 text-white shadow-md flex items-center gap-2 transition-all cursor-pointer active:scale-95 max-w-full"
          >
            <BallIcon className="w-4 h-4 text-white shrink-0" />
            <span className="truncate">{labels.generateBtn}</span>
            <HeadphonesIcon className="w-4 h-4 text-white shrink-0" />
          </button>
        </div>
      )}
    </section>
  );
};
