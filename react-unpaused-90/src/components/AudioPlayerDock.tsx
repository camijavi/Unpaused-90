import React from "react";
import { HeadphonesIcon } from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

interface AudioPlayerDockProps {
  isPlaying: boolean;
  isSynthesizing: boolean;
  speaker: "Ollie" | "Ada";
  transcriptLine: string;
  currentTime: number;
  duration: number;
  playbackSpeed: number;
  onTogglePlay: () => void;
  onSwitchSpeaker: () => void;
  onCycleSpeed: () => void;
  onClose: () => void;
}

function formatSeconds(sec: number): string {
  if (!Number.isFinite(sec) || sec < 0) return "00:00";
  const mins = Math.floor(sec / 60);
  const secs = Math.floor(sec % 60);
  return `${mins.toString().padStart(2, "0")}:${secs
    .toString()
    .padStart(2, "0")}`;
}

export const AudioPlayerDock: React.FC<AudioPlayerDockProps> = ({
  isPlaying,
  isSynthesizing,
  speaker,
  transcriptLine,
  currentTime,
  duration,
  playbackSpeed,
  onTogglePlay,
  onSwitchSpeaker,
  onCycleSpeed,
  onClose,
}) => {
  const { t } = useLanguage();
  const progressPct =
    duration > 0 ? Math.min(100, (currentTime / duration) * 100) : 0;

  const speakerLabel =
    speaker === "Ollie" ? t.audioDock.ollie : t.audioDock.ada;

  return (
    <footer
      aria-label={t.audioDock.title}
      className="fixed bottom-0 inset-x-0 z-50 bg-white/95 backdrop-blur-2xl border-t-2 border-pl-purple/25 shadow-[0_-10px_30px_rgba(15,23,42,0.1)]"
    >
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-3 flex flex-col sm:flex-row items-center justify-between gap-3">
        {/* Left: Play/Pause + Dual AI Co-Host Avatars */}
        <div className="flex items-center gap-3.5 w-full sm:w-auto flex-1 min-w-0">
          <button
            id="audio-play-toggle"
            type="button"
            onClick={onTogglePlay}
            disabled={isSynthesizing}
            aria-label={isPlaying ? t.audioDock.paused : t.audioDock.playing}
            className="w-11 h-11 rounded-xl bg-pl-purple text-mint-bright font-black flex items-center justify-center shrink-0 shadow-md hover:scale-105 transition-transform cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <span id="audio-play-icon" className="text-base">
              {isPlaying ? "❚❚" : "▶"}
            </span>
          </button>

          {/* Co-Host Avatar Pill — active co-host is raised, ringed and pulses while speaking */}
          <div
            className="hidden md:flex items-center -space-x-2 shrink-0"
            role="group"
            aria-label={`${speakerLabel}${isPlaying ? ` • ${t.audioDock.playing}` : ""}`}
          >
            {(
              [
                {
                  id: "Ollie" as const,
                  initials: "OL",
                  title: t.audioDock.ollie,
                  gradient: "from-teal-400 to-emerald-600",
                },
                {
                  id: "Ada" as const,
                  initials: "AD",
                  title: t.audioDock.ada,
                  gradient: "from-rose-500 to-pl-purple",
                },
              ]
            ).map((host) => {
              const isActive = speaker === host.id;
              const isSpeaking = isActive && isPlaying;
              return (
                <div
                  key={host.id}
                  data-active={isActive}
                  data-speaking={isSpeaking}
                  aria-current={isActive ? "true" : undefined}
                  title={host.title}
                  className={`relative w-8 h-8 rounded-full bg-gradient-to-br ${
                    host.gradient
                  } border-2 flex items-center justify-center text-[11px] font-mono font-extrabold text-white shadow transition-all duration-300 ${
                    isActive
                      ? "z-10 scale-110 border-pl-purple ring-2 ring-mint opacity-100"
                      : "z-0 scale-90 border-white opacity-50 grayscale"
                  }`}
                >
                  {isSpeaking && (
                    <span
                      aria-hidden="true"
                      className="absolute inset-0 rounded-full ring-2 ring-mint animate-ping"
                    />
                  )}
                  {host.initials}
                </div>
              );
            })}
          </div>

          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="text-xs font-mono font-extrabold uppercase tracking-wider text-pl-purple flex items-center gap-1.5">
                <HeadphonesIcon className="w-3.5 h-3.5 text-pl-purple shrink-0" />
                <span id="active-speaker-badge">
                  {speakerLabel}
                  {isSynthesizing ? ` • ${t.audioDock.generatingAudio}` : ""}
                </span>
              </span>
              <button
                id="switch-speaker-btn"
                type="button"
                onClick={onSwitchSpeaker}
                disabled={isSynthesizing}
                className="text-[11px] font-mono font-bold px-2 py-0.5 rounded bg-slate-100 hover:bg-slate-200 text-slate-700 border border-slate-300 transition-colors cursor-pointer disabled:opacity-50"
              >
                Pass Mic to {speaker === "Ollie" ? "Ada" : "Ollie"} ↻
              </button>
            </div>
            <p
              id="audio-transcript-line"
              className="text-xs sm:text-sm text-slate-700 truncate mt-0.5 font-medium"
            >
              &ldquo;{transcriptLine}&rdquo;
            </p>
          </div>
        </div>

        {/* Right: Animated Soundwave + Scrubber + Speed Control + Close (X) Button */}
        <div className="flex items-center justify-between sm:justify-end gap-3 w-full sm:w-auto">
          {/* Animated Equalizer Bars */}
          <div id="waveform-bars" className="flex items-end gap-1 h-6 px-2">
            {[
              { color: "bg-pl-purple", height: "h-5", delay: "0s" },
              { color: "bg-teal-500", height: "h-6", delay: "0.2s" },
              { color: "bg-pl-purple", height: "h-4", delay: "0.4s" },
              { color: "bg-red-500", height: "h-6", delay: "0.1s" },
              { color: "bg-teal-500", height: "h-5", delay: "0.3s" },
              { color: "bg-pl-purple", height: "h-3", delay: "0.5s" },
            ].map((bar, idx) => (
              <span
                key={idx}
                className={`wave-bar w-1 ${bar.color} ${
                  bar.height
                } rounded-full ${isPlaying ? "" : "wave-paused"}`}
                style={{ animationDelay: bar.delay }}
              ></span>
            ))}
          </div>

          <div className="flex items-center gap-2">
            <span
              id="audio-time-elapsed"
              className="text-xs font-mono font-bold text-pl-purple"
            >
              {formatSeconds(currentTime)}
            </span>
            <div className="w-24 sm:w-32 h-2 bg-slate-200 rounded-full overflow-hidden">
              <div
                id="audio-progress-fill"
                className="h-full bg-gradient-to-r from-pl-purple to-teal-500 transition-all duration-200"
                style={{ width: `${progressPct}%` }}
              ></div>
            </div>
            <span
              id="audio-time-total"
              className="text-xs font-mono text-slate-500"
            >
              {formatSeconds(duration)}
            </span>
          </div>

          <button
            id="audio-speed-btn"
            type="button"
            onClick={onCycleSpeed}
            className="px-2.5 py-1 rounded-lg bg-slate-100 border border-slate-300 text-xs font-mono font-bold text-pl-purple hover:border-pl-purple cursor-pointer"
          >
            {playbackSpeed.toFixed(2).replace(".00", ".0")}x
          </button>

          <button
            type="button"
            onClick={onClose}
            aria-label={t.audioDock.dismiss}
            title={t.audioDock.dismiss}
            className="w-8 h-8 rounded-lg bg-slate-100 hover:bg-red-50 text-slate-600 hover:text-red-600 border border-slate-300 hover:border-red-300 flex items-center justify-center font-mono text-sm font-bold transition-colors cursor-pointer shrink-0"
          >
            ✕
          </button>
        </div>
      </div>
    </footer>
  );
};

export default AudioPlayerDock;
