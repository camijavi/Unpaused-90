import React, { useState } from "react";
import type {
  MatchData,
  TimelineEvent,
  GeneratedInsight,
  PersonalizedSummaryResponse,
} from "../types/match";
import { useLanguage } from "../languages/LanguageContext";
import { BallIcon, HeadphonesIcon } from "./Icons";

export interface FinalScoreContainerProps {
  match: MatchData | null;
  timeline: TimelineEvent[];
  insights: GeneratedInsight[];
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

// Curated Spanish translations for the 9 key match moments
const SPANISH_INSIGHT_MAP: Record<
  number,
  { summary: string; reason: string; typeEs: string }
> = {
  9: {
    typeEs: "Impacto de Gol",
    summary:
      "Gabriel Martinelli abre el marcador temprano para Arsenal tras un pase filtrado de Ødegaard.",
    reason:
      "Ødegaard encontró espacio entre líneas para habilitar a Martinelli a la espalda de Alexander-Arnold.",
  },
  19: {
    typeEs: "Cambio de Impulso",
    summary:
      "Mohamed Salah convierte con calma desde el punto penal para igualar el marcador 1-1.",
    reason:
      "Salah provocó una barrida tardía en el área penal y colocó el balón en la esquina inferior derecha.",
  },
  24: {
    typeEs: "Ventaja Táctica",
    summary:
      "Luis Díaz adelanta 2-1 a Liverpool tras un centro milimétrico de Trent Alexander-Arnold.",
    reason:
      "Una transición vertiginosa por la banda derecha de Liverpool sorprendió a la zaga de Arsenal adelantada.",
  },
  35: {
    typeEs: "Impacto Disciplinario",
    summary:
      "Ibrahima Konaté es expulsado por doble tarjeta amarilla, dejando a Liverpool con 10 hombres.",
    reason:
      "Una falta táctica para cortar un contragolpe prometedor de Arsenal deja a Liverpool en inferioridad numérica.",
  },
  39: {
    typeEs: "Impacto de Gol",
    summary: "Bukayo Saka empata 2-2 para Arsenal antes del descanso.",
    reason:
      "Aprovechando el espacio tras la expulsión de Konaté, Declan Rice asistió a Saka en los linderos del área chica.",
  },
  45: {
    typeEs: "Impacto Disciplinario",
    summary:
      "Mikel Merino recibe tarjeta roja directa para Arsenal, nivelando el partido a 10 contra 10.",
    reason:
      "Una entrada dura en el centro del campo motivó al silbante a mostrar la roja al filo del medio tiempo.",
  },
  54: {
    typeEs: "Cambio de Impulso",
    summary:
      "Darwin Núñez recupera la ventaja 3-2 para Liverpool con una definición quirúrgica.",
    reason:
      "Salah aguantó el balón y habilitó a Núñez en la fisura de la defensa Gunner.",
  },
  70: {
    typeEs: "Impacto de Gol",
    summary:
      "Bukayo Saka anota su doblete desde los once pasos para poner el 3-3 de Arsenal.",
    reason:
      "Saka forzó el penal tras una sostenida presión ofensiva y engañó completamente a Alisson.",
  },
  85: {
    typeEs: "Momento Decisivo",
    summary:
      "Kai Havertz anota el gol del triunfo al minuto 85 para darle a Arsenal una sensacional victoria 4-3.",
    reason:
      "Un centro venenoso de Leandro Trossard fue conectado con fuerza por Havertz en el corazón del área chica.",
  },
};

const INSIGHT_TYPE_COLORS: Record<string, { badge: string; border: string }> = {
  GoalImpact: {
    badge: "bg-emerald-100 text-emerald-800 border-emerald-300",
    border: "border-emerald-200 hover:border-emerald-400",
  },
  MomentumShift: {
    badge: "bg-sky-100 text-sky-800 border-sky-300",
    border: "border-sky-200 hover:border-sky-400",
  },
  TacticalAdvantage: {
    badge: "bg-indigo-100 text-indigo-800 border-indigo-300",
    border: "border-indigo-200 hover:border-indigo-400",
  },
  DisciplinaryImpact: {
    badge: "bg-rose-100 text-rose-800 border-rose-300",
    border: "border-rose-200 hover:border-rose-400",
  },
  WinningMoment: {
    badge: "bg-amber-100 text-amber-900 border-amber-300",
    border: "border-amber-300 hover:border-amber-400",
  },
};

export const FinalScoreContainer: React.FC<FinalScoreContainerProps> = ({
  match,
  timeline,
  insights,
  fullTimeSummary,
  isGeneratingSummary,
  onGenerateSummary,
  onClose,
  onPlayAudio,
  isPlayingAudio = false,
}) => {
  const { t, language } = useLanguage();
  const [selectedLength, setSelectedLength] = useState<"1m" | "5m" | "full">("full");
  const [activeTab, setActiveTab] = useState<"story" | "insights">("story");

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
    setActiveTab("story");
    onGenerateSummary(true, true, lengthToUse);
  };

  const handleLengthSelect = (len: "1m" | "5m" | "full") => {
    setSelectedLength(len);
    // If summary is already displayed, automatically regenerate with the new selected length
    if (fullTimeSummary && !isGeneratingSummary) {
      onGenerateSummary(false, false, len);
    }
  };

  // Helper to translate insight type
  const getInsightTypeLabel = (type: string): string => {
    if (language === "es") {
      const mapped = labels.insightTypes[type as keyof typeof labels.insightTypes];
      return mapped ?? type;
    }
    return type;
  };

  // Helper to get translated insight content
  const getInsightContent = (ins: GeneratedInsight) => {
    if (language === "es" && SPANISH_INSIGHT_MAP[ins.minute]) {
      const item = SPANISH_INSIGHT_MAP[ins.minute];
      return {
        type: item.typeEs,
        summary: item.summary,
        reason: item.reason,
      };
    }
    return {
      type: getInsightTypeLabel(ins.type),
      summary: ins.summary,
      reason: ins.reason,
    };
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

      {/* Control Toolbar: Length Selector + View Switcher */}
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

        {/* View Switcher Tabs (when summary is available) */}
        {fullTimeSummary && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setActiveTab("story")}
              className={`px-3 py-1 rounded-lg text-xs font-bold font-sans transition-all cursor-pointer ${
                activeTab === "story"
                  ? "bg-emerald-600 text-white shadow-sm"
                  : "bg-white text-slate-700 border border-slate-200 hover:bg-slate-50"
              }`}
            >
              📖 {labels.viewStory}
            </button>
            <button
              type="button"
              onClick={() => setActiveTab("insights")}
              className={`px-3 py-1 rounded-lg text-xs font-bold font-sans transition-all cursor-pointer ${
                activeTab === "insights"
                  ? "bg-emerald-600 text-white shadow-sm"
                  : "bg-white text-slate-700 border border-slate-200 hover:bg-slate-50"
              }`}
            >
              ⚡ {labels.viewInsights} ({insights.length})
            </button>
          </div>
        )}
      </div>

      {/* Main Content Body */}
      {fullTimeSummary && activeTab === "story" ? (
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
        /* Key Moments (Insights) Grid View */
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3.5">
          {insights.map((ins, idx) => {
            const content = getInsightContent(ins);
            const style = INSIGHT_TYPE_COLORS[ins.type] ?? {
              badge: "bg-slate-100 text-slate-800 border-slate-300",
              border: "border-slate-200 hover:border-slate-400",
            };

            return (
              <div
                key={`${ins.minute}-${idx}`}
                className={`bg-white rounded-xl p-4 border ${style.border} shadow-sm hover:shadow-md transition-all flex flex-col justify-between`}
              >
                <div>
                  <div className="flex items-center justify-between text-xs font-mono mb-2">
                    <span className="font-black text-slate-900 bg-slate-100 px-2 py-0.5 rounded">
                      {ins.minute}&apos;
                    </span>
                    <span
                      className={`px-2 py-0.5 rounded-md text-[11px] font-bold border ${style.badge}`}
                    >
                      {content.type}
                    </span>
                  </div>
                  <p className="text-xs sm:text-sm font-bold text-slate-900 leading-snug">
                    {content.summary}
                  </p>
                </div>
                <p className="text-xs text-slate-600 mt-2.5 pt-2 border-t border-slate-100 leading-relaxed">
                  {content.reason}
                </p>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
};
