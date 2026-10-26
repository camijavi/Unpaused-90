import React, { useState } from "react";
import type {
  MatchData,
  TimelineEvent,
  PersonalizedSummaryResponse,
} from "../types/match";
import { useLanguage } from "../languages/LanguageContext";
import { BallIcon, HeadphonesIcon } from "./Icons";

export interface FinalScoreContainerProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  fullTimeSummary: PersonalizedSummaryResponse | null;
  isGeneratingSummary: boolean;
  onGenerateSummary: (
    autoPlayAudio: boolean,
    unlockedByGesture: boolean,
    length: "1m" | "5m" | "full"
  ) => void;
  onClose: () => void;
  onPlayAudio?: (story: string, voice?: string) => void;
  isPlayingAudio?: boolean;
}

export const FinalScoreContainer: React.FC<FinalScoreContainerProps> = ({
  match,
  timeline,
  fullTimeSummary,
  isGeneratingSummary,
  onGenerateSummary,
  onClose,
  onPlayAudio,
  isPlayingAudio = false,
}) => {
  const { t } = useLanguage();
  const [selectedLength, setSelectedLength] = useState<"1m" | "5m" | "full">("full");

  const homeTeamName = match?.homeTeam.name ?? "Arsenal";
  const awayTeamName = match?.awayTeam.name ?? "Liverpool";
  const homeScore = match?.homeTeam.score ?? 4;
  const awayScore = match?.awayTeam.score ?? 3;
  const homePossession = match?.statistics.possession.home ?? 60;
  const awayPossession = match?.statistics.possession.away ?? 40;

  const labels = t.dashboard.finalScoreContainer;

  const formattedStats = labels.statsSubtitle
    .replace("{events}", String(timeline.length))
    .replace("{home}", String(homePossession))
    .replace("{away}", String(awayPossession));

  const handleGenerateClick = (lengthToUse: "1m" | "5m" | "full" = selectedLength) => {
    setSelectedLength(lengthToUse);
    onGenerateSummary(true, true, lengthToUse);
  };

  const handleLengthSelect = (len: "1m" | "5m" | "full") => {
    setSelectedLength(len);
    // If summary is already displayed, automatically regenerate with the new selected length
    if (fullTimeSummary && !isGeneratingSummary) {
      onGenerateSummary(false, false, len);
    }
  };

  return (
    <section
      aria-label="Full-Time Match Summary"
      className="broadcast-slab rounded-2xl p-5 sm:p-6 border-2 border-emerald-500/50 bg-gradient-to-br from-white via-emerald-50/20 to-teal-50/30 shadow-xl relative overflow-hidden"
    >
      {/* Top Accent Gradient Bar */}
      <div className="h-1.5 w-full bg-gradient-to-r from-emerald-500 via-teal-500 to-indigo-600 absolute top-0 left-0" />

      {/* Header Row: Badge, Scoreboard, Close Button */}
      <div className="flex flex-wrap items-center justify-between gap-3 pb-4 mb-4 border-b border-slate-200">
        <div className="flex items-start sm:items-center gap-3">
          <span className="px-3 py-1 rounded-md bg-emerald-600 text-white font-mono text-xs font-extrabold uppercase tracking-wider shadow-sm flex items-center gap-1.5">
            <span className="w-2 h-2 rounded-full bg-white animate-pulse" />
            {labels.badge}
          </span>
          <div>
            <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900">
              <span className="text-emerald-700">{labels.finalScoreLabel}</span>{" "}
              {homeTeamName} {homeScore} – {awayScore} {awayTeamName}
            </h2>
            <p className="text-xs font-mono text-slate-600">{formattedStats}</p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          {/* Main Generate / Regenerate CTA Button */}
          <button
            type="button"
            disabled={isGeneratingSummary}
            onClick={() => handleGenerateClick(selectedLength)}
            className="px-4 py-2 rounded-xl font-display font-black uppercase text-xs sm:text-sm tracking-wider bg-emerald-600 hover:bg-emerald-700 active:scale-95 text-white shadow-md flex items-center gap-2 transition-all cursor-pointer disabled:opacity-60"
          >
            <BallIcon className="w-4 h-4 text-white shrink-0" />
            <span>
              {isGeneratingSummary
                ? labels.generatingBtn
                : fullTimeSummary
                  ? labels.regenerateBtn + " ↻"
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
            className="w-8 h-8 rounded-lg bg-white hover:bg-red-50 text-slate-500 hover:text-red-600 border border-slate-200 hover:border-red-300 flex items-center justify-center transition-colors cursor-pointer"
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

      {/* Control Toolbar: Length Selector */}
      <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
        {/* Length Selector Pills */}
        <div className="flex items-center gap-1.5 p-1 rounded-xl bg-slate-100 border border-slate-200">
          <button
            type="button"
            onClick={() => handleLengthSelect("1m")}
            className={`px-3 py-1 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer ${
              selectedLength === "1m"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            ⚡ {labels.lengthOptions["1m"]}
          </button>
          <button
            type="button"
            onClick={() => handleLengthSelect("5m")}
            className={`px-3 py-1 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer ${
              selectedLength === "5m"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            ⏱️ {labels.lengthOptions["5m"]}
          </button>
          <button
            type="button"
            onClick={() => handleLengthSelect("full")}
            className={`px-3 py-1 rounded-lg text-xs font-mono font-bold uppercase transition-all cursor-pointer ${
              selectedLength === "full"
                ? "bg-white text-emerald-700 shadow-sm border border-emerald-300"
                : "text-slate-600 hover:text-slate-900"
            }`}
          >
            📖 {labels.lengthOptions["full"]}
          </button>
        </div>
      </div>

      {/* Main Content Body */}
      {isGeneratingSummary ? (
        <div className="bg-white/90 backdrop-blur-sm rounded-xl p-8 border border-emerald-200 text-center flex flex-col items-center justify-center gap-3">
          <div className="w-8 h-8 border-3 border-emerald-600 border-t-transparent rounded-full animate-spin" />
          <p className="font-display font-bold uppercase tracking-wider text-xs sm:text-sm text-emerald-800">
            {labels.generatingBtn}
          </p>
        </div>
      ) : fullTimeSummary ? (
        <div className="bg-white/95 backdrop-blur-sm rounded-xl p-5 sm:p-6 border border-emerald-200 shadow-inner space-y-4">
          {/* Summary Metadata Bar */}
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs font-mono text-slate-500 pb-3 border-b border-emerald-100">
            <div className="flex items-center gap-2">
              <span className="font-extrabold text-emerald-800 uppercase px-2.5 py-0.5 rounded-md bg-emerald-50 border border-emerald-200">
                {labels.reportTitle}
              </span>
              <span className="text-slate-600">
                ({fullTimeSummary.mode} • {fullTimeSummary.favoriteTeam})
              </span>
            </div>

            <div className="flex items-center gap-3">
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

          {/* Story Paragraphs */}
          <div className="space-y-4 text-slate-800 font-sans text-sm sm:text-base leading-relaxed whitespace-pre-line">
            {fullTimeSummary.story}
          </div>
        </div>
      ) : (
        <div className="bg-white/70 backdrop-blur-sm rounded-xl p-6 sm:p-8 border border-emerald-200/80 text-center flex flex-col items-center justify-center gap-3">
          <div className="w-12 h-12 rounded-full bg-emerald-100 flex items-center justify-center text-emerald-600">
            <BallIcon className="w-6 h-6" />
          </div>
          <div className="max-w-md">
            <p className="text-sm sm:text-base font-bold text-slate-900">
              {labels.reportTitle}
            </p>
            <p className="text-xs sm:text-sm text-slate-600 mt-1">
              {formattedStats}
            </p>
          </div>
          <button
            type="button"
            onClick={() => handleGenerateClick(selectedLength)}
            className="mt-1 px-4 py-2 rounded-xl font-display font-black uppercase text-xs sm:text-sm tracking-wider bg-emerald-600 hover:bg-emerald-700 text-white shadow-md flex items-center gap-2 transition-all cursor-pointer active:scale-95"
          >
            <BallIcon className="w-4 h-4 text-white shrink-0" />
            <span>{labels.generateBtn}</span>
            <HeadphonesIcon className="w-4 h-4 text-white shrink-0" />
          </button>
        </div>
      )}
    </section>
  );
};
