import React, { useCallback, useEffect, useRef, useState } from "react";
import Navbar from "../components/Navbar";
import LiveGrounds from "../components/LiveGrounds";
import ScoreContainer, {
  formatTimelineEventTitle,
  mapEventTypeToAnimationKind,
} from "../components/ScoreContainer";
import HeroConsole from "../components/HeroConsole";
import Cards from "../components/Cards";
import AudioPlayerDock from "../components/AudioPlayerDock";
import { BallIcon, HeadphonesIcon } from "../components/Icons";
import { useLanguage } from "../languages/LanguageContext";
import {
  explainTimelineEvent,
  fetchMatchById,
  generateCatchUp,
  generatePersonalizedSummary,
  resetMatchSimulation,
  synthesizeAudio,
} from "../services/api";
import type {
  CatchUpResponse,
  EventAnimationPayload,
  GeneratedInsight,
  MatchData,
  PersonalizedSummaryResponse,
  TimelineEvent,
} from "../types/match";

// Tiny 1-sample silent WAV used to synchronously unlock the HTML5 <audio> element on user click
const SILENT_UNLOCK_WAV =
  "data:audio/wav;base64,UklGRigAAABXQVZFZm10IBIAAAABAAEARKwAAIhYAQACABAAAABkYXRhAgAAAAEA";

// Maps any backend/native voice name to the co-host persona shown in the Audio Dock
const FEMALE_VOICE_HINTS = ["ada", "dalia", "elvira", "sabina", "helena", "paulina", "monica", "female"];
function speakerFromVoice(
  voice: string | null | undefined,
  fallback: "Ollie" | "Ada" = "Ollie"
): "Ollie" | "Ada" {
  if (!voice) return fallback;
  const v = voice.toLowerCase();
  return FEMALE_VOICE_HINTS.some((hint) => v.includes(hint)) ? "Ada" : "Ollie";
}

const FALLBACK_MATCH: MatchData = {
  matchId: "PL2027-001",
  competition: "Premier League",
  season: "2026/27",
  stadium: "Emirates Stadium",
  status: "2nd Half",
  currentMinute: 60,
  homeTeam: {
    teamId: "ARS",
    name: "Arsenal",
    coach: "Mikel Arteta",
    formation: "4-3-3",
    score: 1,
    startingXI: [
      { playerId: "ARS01", name: "David Raya", number: 1, position: "GK", captain: false },
      { playerId: "ARS02", name: "Ben White", number: 4, position: "RB", captain: false },
      { playerId: "ARS03", name: "William Saliba", number: 2, position: "CB", captain: false },
      { playerId: "ARS04", name: "Gabriel Magalhaes", number: 6, position: "CB", captain: false },
      { playerId: "ARS05", name: "Oleksandr Zinchenko", number: 35, position: "LB", captain: false },
      { playerId: "ARS06", name: "Declan Rice", number: 41, position: "CM", captain: false },
      { playerId: "ARS07", name: "Martin Odegaard", number: 8, position: "CM", captain: true },
      { playerId: "ARS08", name: "Kai Havertz", number: 29, position: "CM", captain: false },
      { playerId: "ARS09", name: "Bukayo Saka", number: 7, position: "RW", captain: false },
      { playerId: "ARS10", name: "Gabriel Jesus", number: 9, position: "ST", captain: false },
      { playerId: "ARS11", name: "Gabriel Martinelli", number: 11, position: "LW", captain: false },
    ],
    substitutes: [
      { playerId: "ARS12", name: "Aaron Ramsdale", number: 32, position: "GK" },
      { playerId: "ARS13", name: "Jakub Kiwior", number: 15, position: "CB" },
      { playerId: "ARS14", name: "Jorginho", number: 20, position: "CM" },
      { playerId: "ARS15", name: "Leandro Trossard", number: 19, position: "LW" },
      { playerId: "ARS16", name: "Eddie Nketiah", number: 14, position: "ST" },
    ],
  },
  awayTeam: {
    teamId: "LIV",
    name: "Liverpool",
    coach: "Arne Slot",
    formation: "4-2-3-1",
    score: 1,
    startingXI: [
      { playerId: "LIV01", name: "Alisson Becker", number: 1, position: "GK", captain: false },
      { playerId: "LIV02", name: "Trent Alexander-Arnold", number: 66, position: "RB", captain: false },
      { playerId: "LIV03", name: "Ibrahima Konate", number: 5, position: "CB", captain: false },
      { playerId: "LIV04", name: "Virgil van Dijk", number: 4, position: "CB", captain: true },
      { playerId: "LIV05", name: "Andrew Robertson", number: 26, position: "LB", captain: false },
      { playerId: "LIV06", name: "Alexis Mac Allister", number: 10, position: "CM", captain: false },
      { playerId: "LIV07", name: "Dominik Szoboszlai", number: 8, position: "CM", captain: false },
      { playerId: "LIV08", name: "Mohamed Salah", number: 11, position: "RW", captain: false },
      { playerId: "LIV09", name: "Curtis Jones", number: 17, position: "AM", captain: false },
      { playerId: "LIV10", name: "Luis Diaz", number: 7, position: "LW", captain: false },
      { playerId: "LIV11", name: "Darwin Nunez", number: 9, position: "ST", captain: false },
    ],
    substitutes: [
      { playerId: "LIV12", name: "Caoimhin Kelleher", number: 62, position: "GK" },
      { playerId: "LIV13", name: "Joe Gomez", number: 2, position: "CB" },
      { playerId: "LIV14", name: "Wataru Endo", number: 3, position: "CM" },
      { playerId: "LIV15", name: "Cody Gakpo", number: 18, position: "FW" },
      { playerId: "LIV16", name: "Harvey Elliott", number: 19, position: "AM" },
    ],
  },
  statistics: {
    possession: { home: 54, away: 46 },
    shots: { home: 9, away: 7 },
    shotsOnTarget: { home: 4, away: 3 },
    passAccuracy: { home: 87, away: 84 },
    corners: { home: 5, away: 3 },
    fouls: { home: 6, away: 7 },
  },
  timeline: [
    { eventId: "EV001", minute: 12, eventType: "Shot", team: "ARS", player: "Kai Havertz", onTarget: true },
    { eventId: "EV002", minute: 24, eventType: "PressureSequence", team: "ARS", durationSeconds: 96, importanceScore: 75 },
    { eventId: "EV003", minute: 31, eventType: "Goal", team: "ARS", player: "Bukayo Saka", assist: "Martin Odegaard", importanceScore: 100 },
    { eventId: "EV004", minute: 43, eventType: "YellowCard", team: "LIV", player: "Alexis Mac Allister" },
    { eventId: "EV005", minute: 54, eventType: "Goal", team: "LIV", player: "Mohamed Salah", assist: "Dominik Szoboszlai", importanceScore: 95 },
  ],
  generatedInsights: [
    {
      minute: 31,
      type: "GoalImpact",
      summary: "Arsenal opened the scoring after a sustained spell of pressure in Liverpool's defensive third.",
      reason: "Odegaard found space between the lines and assisted Saka inside the box.",
    },
    {
      minute: 54,
      type: "MomentumShift",
      summary: "Liverpool equalized through a fast transition led by Szoboszlai and Salah.",
      reason: "Arsenal's high defensive line left space behind Zinchenko.",
    },
  ],
};

function mapBiasToFavoriteTeam(bias: "neutral" | "arsenal" | "liverpool"): string {
  if (bias === "arsenal") return "Arsenal";
  if (bias === "liverpool") return "Liverpool";
  return "Neutral";
}

export const Dashboard: React.FC = () => {
  const { setLanguage: setGlobalLanguage, t } = useLanguage();
  const [match, setMatch] = useState<MatchData | null>(FALLBACK_MATCH);
  const [timeline, setTimeline] = useState<TimelineEvent[]>(FALLBACK_MATCH.timeline);
  const [insights, setInsights] = useState<GeneratedInsight[]>(
    FALLBACK_MATCH.generatedInsights
  );
  const [initialStartMinute, setInitialStartMinute] = useState<number | null>(null);
  const [subSeconds, setSubSeconds] = useState<number>(0);

  // Hero Console state
  const [stoppedMinute, setStoppedMinute] = useState<number>(30);
  const [mode, setMode] = useState<"casual" | "analyst" | "child">("casual");
  const [bias, setBias] = useState<"neutral" | "arsenal" | "liverpool">("neutral");
  const [length, setLength] = useState<"1m" | "5m" | "full">("1m");
  const [lang, setLang] = useState<"en" | "es">("en");

  // Align global language with audio feed selection, eventhough user can select a different value than their global language value
  const handleGlobalLanguageChange = useCallback(
    (newLang: "en" | "es") => {
      setGlobalLanguage(newLang);
      setLang(newLang);
    },
    [setGlobalLanguage]
  );

  // Visibility state: Hidden by default until user clicks "Catch Me Up & Auto-Play Audio"
  const [showCatchUpDeck, setShowCatchUpDeck] = useState<boolean>(false);
  const [showAudioDock, setShowAudioDock] = useState<boolean>(false);

  // End-of-Match (90' FullTime) Summary state (Step 6)
  const [fullTimeSummary, setFullTimeSummary] =
    useState<PersonalizedSummaryResponse | null>(null);
  const [isGeneratingSummary, setIsGeneratingSummary] = useState<boolean>(false);
  const [showFullTimeBanner, setShowFullTimeBanner] = useState<boolean>(true);

  // LiveCatchUp & Event Inspector state
  const [catchUp, setCatchUp] = useState<CatchUpResponse | null>(null);
  const [isGenerating, setIsGenerating] = useState<boolean>(false);
  const [generatingWindow, setGeneratingWindow] = useState<{
    stopped: number;
    current: number;
  } | null>(null);
  const [statusBadgeText, setStatusBadgeText] = useState<string>(
    "Synced to Live Match Window"
  );
  const [selectedEvent, setSelectedEvent] = useState<TimelineEvent | null>(
    FALLBACK_MATCH.timeline[FALLBACK_MATCH.timeline.length - 1] ?? null
  );
  const [eventExplanation, setEventExplanation] = useState<{
    title: string;
    explanation: string;
    loading?: boolean;
  } | null>(null);
  const [activeAnimation, setActiveAnimation] =
    useState<EventAnimationPayload | null>(null);

  // Audio Player state
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [isSynthesizing, setIsSynthesizing] = useState<boolean>(false);
  const [speaker, setSpeaker] = useState<"Ollie" | "Ada">("Ollie");
  const speakerRef = useRef<"Ollie" | "Ada">("Ollie");
  useEffect(() => {
    speakerRef.current = speaker;
  }, [speaker]);
  const [transcriptLine, setTranscriptLine] = useState<string>(
    "Generating your live personalized match recap..."
  );
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [duration, setDuration] = useState<number>(60);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1.0);

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const currentBlobUrlRef = useRef<string | null>(null);
  const speechUtteranceRef = useRef<SpeechSynthesisUtterance | null>(null);
  const speechTickerRef = useRef<number | null>(null);
  // Pause-aware clock for native speech: only accumulates elapsed time while actually speaking
  const speechClockRef = useRef<{
    elapsed: number;
    lastTick: number;
    paused: boolean;
    rate: number;
  }>({ elapsed: 0, lastTick: 0, paused: true, rate: 1 });
  const fxTimeoutRef = useRef<number | null>(null);
  const generationIdRef = useRef<number>(0);
  const hasInitializedRef = useRef<boolean>(false);
  const seenEventIdsRef = useRef<Set<string>>(new Set());
  const prevMinuteRef = useRef<number>(60);
  const minuteStartTimeRef = useRef<number>(Date.now());
  const SECONDS_PER_SIMULATED_MINUTE = 30.0;

  const currentMinute = match?.currentMinute ?? 60;
  const isFullTime = currentMinute >= 90 || match?.status === "FullTime";

  // Stops any currently playing audio and revokes old Blob URLs so stale audio never replays
  const stopAndRevokeCurrentAudio = useCallback(() => {
    const audio = audioRef.current;
    if (audio) {
      audio.pause();
      audio.currentTime = 0;
    }
    if (currentBlobUrlRef.current) {
      URL.revokeObjectURL(currentBlobUrlRef.current);
      currentBlobUrlRef.current = null;
    }
    if (typeof window !== "undefined" && "speechSynthesis" in window) {
      window.speechSynthesis.cancel();
    }
    if (speechTickerRef.current) {
      window.clearInterval(speechTickerRef.current);
      speechTickerRef.current = null;
    }
    speechUtteranceRef.current = null;
    speechClockRef.current = { elapsed: 0, lastTick: 0, paused: true, rate: 1 };
    setIsPlaying(false);
    setCurrentTime(0);
  }, []);

  // Synchronously unlocks the <audio> element during a user click gesture
  const unlockAudioElementOnGesture = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    stopAndRevokeCurrentAudio();
    audio.src = SILENT_UNLOCK_WAV;
    audio
      .play()
      .then(() => {
        audio.pause();
        audio.currentTime = 0;
      })
      .catch(() => {});
  }, [stopAndRevokeCurrentAudio]);

  // Converts base64 MP3 data URI into a fresh Blob URL and auto-plays it
  const playFreshAudioDataUrl = useCallback(
    (dataUrl: string) => {
      stopAndRevokeCurrentAudio();

      const base64Index = dataUrl.indexOf("base64,");
      const base64 =
        base64Index !== -1 ? dataUrl.substring(base64Index + 7) : dataUrl;
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }
      const blob = new Blob([bytes], { type: "audio/mpeg" });
      const blobUrl = URL.createObjectURL(blob);
      currentBlobUrlRef.current = blobUrl;

      const audio = audioRef.current;
      if (audio) {
        audio.src = blobUrl;
        audio.playbackRate = playbackSpeed;
        audio.load();
        const playPromise = audio.play();
        if (playPromise !== undefined) {
          playPromise
            .then(() => {
              setIsPlaying(true);
            })
            .catch((err) => {
              console.warn(
                "Audio ready! Click Play in the bottom bar if browser blocked autoplay:",
                err
              );
              setIsPlaying(false);
            });
        }
      }
    },
    [playbackSpeed, stopAndRevokeCurrentAudio]
  );

  // Instant browser-native speech synthesis fallback (runs 100% locally with 0ms latency)
  const playBrowserSpeech = useCallback(
    (text: string, language: "en" | "es", voiceName?: string | null) => {
      stopAndRevokeCurrentAudio();
      if (typeof window === "undefined" || !("speechSynthesis" in window)) {
        return;
      }

      const utterance = new SpeechSynthesisUtterance(text);
      speechUtteranceRef.current = utterance;

      const voices = window.speechSynthesis.getVoices();
      const isSpanish = language === "es";
      const targetPrefix = isSpanish ? "es" : "en";
      const persona = speakerFromVoice(voiceName, speakerRef.current);
      const prefersFemale = persona === "Ada";
      setSpeaker(persona);

      const femaleNames = isSpanish
        ? ["dalia", "paulina", "monica", "mónica", "sabina", "helena", "elvira", "laura", "female", "mujer"]
        : ["ada", "sonia", "libby", "serena", "kate", "martha", "female", "susan", "hazel"];
      const maleNames = isSpanish
        ? ["jorge", "juan", "diego", "pablo", "raul", "álvaro", "alvaro", "carlos", "hombre"]
        : ["ollie", "ryan", "daniel", "arthur", "george", "oliver", "david", "thomas"];
      // Neural/online voices sound far more natural than the legacy compact system voices
      const qualityScore = (v: SpeechSynthesisVoice) => {
        const n = v.name.toLowerCase();
        let score = 0;
        if (/natural|neural|online/.test(n)) score += 4;
        if (/premium|enhanced|google/.test(n)) score += 3;
        if (isSpanish && v.lang.toLowerCase() === "es-mx") score += 1;
        if (!isSpanish && v.lang.toLowerCase() === "en-gb") score += 1;
        return score;
      };
      const isGenderMatch = (v: SpeechSynthesisVoice) => {
        const n = v.name.toLowerCase();
        // NOTE: never match on "male" — it is a substring of "female"
        if (prefersFemale) return femaleNames.some((f) => n.includes(f));
        return (
          maleNames.some((m) => n.includes(m)) || /\bmale\b/.test(n)
        ) && !femaleNames.some((f) => n.includes(f));
      };

      const languageVoices = voices
        .filter((v) => v.lang.toLowerCase().startsWith(targetPrefix))
        .sort((a, b) => qualityScore(b) - qualityScore(a));
      const matchedVoice =
        languageVoices.find(isGenderMatch) ?? languageVoices[0];

      if (matchedVoice) {
        utterance.voice = matchedVoice;
      }
      utterance.lang = matchedVoice?.lang ?? (isSpanish ? "es-MX" : "en-GB");
      utterance.rate = playbackSpeed;
      utterance.pitch = 1.0;

      const words = text.split(/\s+/).filter(Boolean).length;
      const estimatedSec = Math.max(10, Math.round(words / 2.6));
      setDuration(estimatedSec);
      setCurrentTime(0);

      const clearTicker = () => {
        if (speechTickerRef.current) {
          window.clearInterval(speechTickerRef.current);
          speechTickerRef.current = null;
        }
      };

      utterance.onstart = () => {
        setIsPlaying(true);
        speechClockRef.current = {
          elapsed: 0,
          lastTick: Date.now(),
          paused: false,
          rate: playbackSpeed,
        };
        clearTicker();
        speechTickerRef.current = window.setInterval(() => {
          const clock = speechClockRef.current;
          const now = Date.now();
          if (!clock.paused) {
            clock.elapsed += ((now - clock.lastTick) / 1000) * clock.rate;
          }
          clock.lastTick = now;
          setCurrentTime(Math.min(estimatedSec, clock.elapsed));
        }, 250);
      };

      // Some browsers emit these when speechSynthesis.pause()/resume() is called
      utterance.onpause = () => {
        speechClockRef.current.paused = true;
        setIsPlaying(false);
      };
      utterance.onresume = () => {
        speechClockRef.current.lastTick = Date.now();
        speechClockRef.current.paused = false;
        setIsPlaying(true);
      };

      utterance.onend = () => {
        setIsPlaying(false);
        setCurrentTime(estimatedSec);
        speechClockRef.current.paused = true;
        clearTicker();
      };

      utterance.onerror = () => {
        setIsPlaying(false);
        speechClockRef.current.paused = true;
        clearTicker();
      };

      window.speechSynthesis.speak(utterance);
    },
    [playbackSpeed, stopAndRevokeCurrentAudio]
  );

  // Trigger a broadcast event animation overlay for 2.5s
  const handleTriggerAnimation = useCallback(
    (payload: EventAnimationPayload) => {
      if (fxTimeoutRef.current) {
        window.clearTimeout(fxTimeoutRef.current);
      }
      setActiveAnimation(payload);
      fxTimeoutRef.current = window.setTimeout(() => {
        setActiveAnimation(null);
      }, 2500);
    },
    []
  );

  // Select a timeline pin and fetch its live tactical explanation from /api/v1/explain
  const handleSelectTimelineEvent = useCallback(
    async (ev: TimelineEvent) => {
      setSelectedEvent(ev);
      const activeMatchId = match?.matchId ?? "PL2027-001";
      const homeCode = match?.homeTeam.teamId ?? "ARS";
      const homeName = match?.homeTeam.name ?? "Arsenal";
      const awayName = match?.awayTeam.name ?? "Liverpool";
      const formatted = formatTimelineEventTitle(
        ev,
        homeCode,
        homeName,
        awayName
      );

      setEventExplanation({
        title: formatted.title,
        explanation: "",
        loading: true,
      });

      try {
        const res = await explainTimelineEvent({
          matchId: activeMatchId,
          eventId: ev.eventId,
          viewMode: mode,
          favoriteTeam: mapBiasToFavoriteTeam(bias),
          language: lang,
        });
        setEventExplanation({
          title: res.title || formatted.title,
          explanation: res.explanation,
          loading: false,
        });
      } catch (err) {
        console.error("Failed to explain timeline event:", err);
        setEventExplanation(null);
      }
    },
    [bias, lang, match, mode]
  );

  // Step 1 & Step 2: Initialize or Reset Match Simulation at a random minute (1..90)
  const handleInitializeOrResetSimulation = useCallback(async () => {
    try {
      const matchData = await resetMatchSimulation("PL2027-001");
      setMatch(matchData);
      setTimeline(matchData.timeline);
      setInsights(matchData.generatedInsights);
      setInitialStartMinute(matchData.currentMinute);
      setSubSeconds(0);
      prevMinuteRef.current = matchData.currentMinute;
      minuteStartTimeRef.current = Date.now();

      // Record already-elapsed events at the starting minute so only new live events fire animations
      seenEventIdsRef.current = new Set(matchData.timeline.map((e) => e.eventId));

      // Default "Stopped Watching At" to half of the random starting minute (at least 1')
      const defaultStopped = Math.max(1, Math.floor(matchData.currentMinute / 2));
      setStoppedMinute(defaultStopped);

      const latestEv =
        matchData.timeline[matchData.timeline.length - 1] ?? null;
      setSelectedEvent(latestEv);
      setEventExplanation(null);
      setFullTimeSummary(null);
      setShowFullTimeBanner(true);
    } catch (err) {
      console.error("Failed to initialize/reset live match simulation:", err);
    }
  }, []);

  // Initialize a random starting minute (1..90) on mount
  useEffect(() => {
    if (hasInitializedRef.current) return;
    hasInitializedRef.current = true;
    handleInitializeOrResetSimulation();
  }, [handleInitializeOrResetSimulation]);

  // Broadcast clock simulated seconds ticker (00..59) synchronized to match minute progression
  useEffect(() => {
    if (isFullTime) {
      setSubSeconds(0);
      return;
    }
    const timer = window.setInterval(() => {
      const elapsedRealSec = (Date.now() - minuteStartTimeRef.current) / 1000;
      const simulatedSec = Math.min(
        59,
        Math.floor((elapsedRealSec / SECONDS_PER_SIMULATED_MINUTE) * 60)
      );
      setSubSeconds(simulatedSec);
    }, 150);
    return () => window.clearInterval(timer);
  }, [isFullTime]);

  // Step 3, 4 & 5: Poll backend every 2s to synchronize live clock, score, stats, and auto-trigger event animations
  useEffect(() => {
    if (isFullTime) return;

    const pollInterval = window.setInterval(async () => {
      try {
        const liveMatch = await fetchMatchById("PL2027-001");
        setMatch(liveMatch);
        setTimeline(liveMatch.timeline);
        setInsights(liveMatch.generatedInsights);

        if (liveMatch.currentMinute !== prevMinuteRef.current) {
          prevMinuteRef.current = liveMatch.currentMinute;
          minuteStartTimeRef.current = Date.now();
          setSubSeconds(0);
        }

        // Detect any newly occurred events as the simulated match clock advances
        const newEvents = liveMatch.timeline.filter(
          (ev) => !seenEventIdsRef.current.has(ev.eventId)
        );

        if (newEvents.length > 0) {
          for (const ev of newEvents) {
            seenEventIdsRef.current.add(ev.eventId);
          }

          // Focus the newest event in the inspector and trigger its broadcast animation
          const latestNew = newEvents[newEvents.length - 1];
          setSelectedEvent(latestNew);

          const homeCode = liveMatch.homeTeam.teamId ?? "ARS";
          const homeName = liveMatch.homeTeam.name ?? "Arsenal";
          const awayName = liveMatch.awayTeam.name ?? "Liverpool";
          const info = formatTimelineEventTitle(
            latestNew,
            homeCode,
            homeName,
            awayName
          );
          const kind = mapEventTypeToAnimationKind(latestNew.eventType);

          handleTriggerAnimation({
            kind,
            min: latestNew.minute,
            club: info.clubName,
            player: info.playerLine,
            sub: info.title,
          });
        }
      } catch (err) {
        console.error("Live match polling error:", err);
      }
    }, 2000);

    return () => window.clearInterval(pollInterval);
  }, [handleTriggerAnimation, isFullTime]);

  // Step 6: Generate Full-Time Match Summary (`POST /api/v1/personalized-summary`)
  const handleGenerateFullTimeSummary = useCallback(
    async (autoPlayAudio: boolean, unlockedByGesture = false) => {
      const myGenId = ++generationIdRef.current;

      if (unlockedByGesture && autoPlayAudio) {
        unlockAudioElementOnGesture();
      } else if (autoPlayAudio) {
        stopAndRevokeCurrentAudio();
      }

      setIsGeneratingSummary(true);
      setShowFullTimeBanner(true);
      if (autoPlayAudio) {
        setShowAudioDock(true);
        setTranscriptLine("Generating Full-Time 90' Match Summary...");
      }

      try {
        const res = await generatePersonalizedSummary({
          matchId: match?.matchId ?? "PL2027-001",
          mode,
          favoriteTeam: mapBiasToFavoriteTeam(bias),
          language: lang,
          length,
          includeAudio: false,
        });

        if (myGenId !== generationIdRef.current) return;
        setFullTimeSummary(res);
        setIsGeneratingSummary(false);

        const assignedSpeaker = speakerFromVoice(res.voice);
        setSpeaker(assignedSpeaker);

        if (res.story) {
          setTranscriptLine(res.story);
        }

        if (autoPlayAudio && res.story) {
          setIsSynthesizing(true);
          try {
            const audioRes = await synthesizeAudio({
              text: res.story,
              voice: res.voice ?? null,
              language: lang,
            });
            if (myGenId !== generationIdRef.current) return;
            setIsSynthesizing(false);
            if (audioRes.audioUrl) {
              playFreshAudioDataUrl(audioRes.audioUrl);
            } else {
              playBrowserSpeech(res.story, lang, audioRes.voice);
            }
          } catch (audioErr) {
            console.warn("Full-time summary audio fallback to native speech:", audioErr);
            if (myGenId === generationIdRef.current) {
              setIsSynthesizing(false);
              playBrowserSpeech(res.story, lang, res.voice);
            }
          }
        }
      } catch (err) {
        console.error("Failed to generate full-time match summary:", err);
        if (myGenId === generationIdRef.current) {
          setIsGeneratingSummary(false);
        }
      }
    },
    [
      bias,
      lang,
      length,
      match?.matchId,
      mode,
      playBrowserSpeech,
      playFreshAudioDataUrl,
      stopAndRevokeCurrentAudio,
      unlockAudioElementOnGesture,
    ]
  );

  // Generate Catch-Up intelligence cards + synthesize & auto-play TTS audio
  const runCatchUp = useCallback(
    async (autoPlayAudio: boolean, unlockedByGesture = false) => {
      const myGenId = ++generationIdRef.current;

      // Reveal both the Live Catch-Up Deck and the Audio Dock when triggered
      setShowCatchUpDeck(true);
      setShowAudioDock(true);

      if (unlockedByGesture) {
        unlockAudioElementOnGesture();
      } else if (autoPlayAudio) {
        stopAndRevokeCurrentAudio();
      }

      setIsGenerating(true);
      setStatusBadgeText(
        lang === "es"
          ? "Generando Resumen Catch-Up con IA..."
          : "Generating Live AI Catch-Up..."
      );
      setTranscriptLine(
        lang === "es"
          ? "Generando tu resumen de partido en vivo..."
          : "Generating your live personalized match recap..."
      );

      const activeMatchId = match?.matchId ?? "PL2027-001";
      const targetCurrentMin = match?.currentMinute ?? 90;
      const clampedStopped = Math.min(
        stoppedMinute,
        Math.max(1, targetCurrentMin - 1)
      );

      // Lock the generating window immediately so the button display NEVER drifts or updates
      setGeneratingWindow({ stopped: clampedStopped, current: targetCurrentMin });

      let res: CatchUpResponse;
      try {
        // Step 1: Fetch the 4 intelligence cards first (includeAudio: false)
        res = await generateCatchUp({
          matchId: activeMatchId,
          lastViewedMinute: clampedStopped,
          currentMinute: targetCurrentMin,
          viewMode: mode,
          favoriteTeam: mapBiasToFavoriteTeam(bias),
          language: lang,
          length,
          includeAudio: false,
        });
      } catch (err) {
        console.error("Error in generateCatchUp:", err);
        if (myGenId === generationIdRef.current) {
          setIsGenerating(false);
          setIsSynthesizing(false);
          setGeneratingWindow(null);
          const msg = err instanceof Error ? err.message : String(err);
          setStatusBadgeText(
            lang === "es"
              ? "Error en Solicitud Catch-Up"
              : "Catch-Up Request Failed"
          );
          setTranscriptLine(`Catch-Up error: ${msg}`);
        }
        return;
      } finally {
        setGeneratingWindow(null);
      }

      if (myGenId !== generationIdRef.current) return;

      setCatchUp(res);
      setIsGenerating(false);
      setStatusBadgeText(
        lang === "es"
          ? `Sincronizado a ventana ${clampedStopped}'–${targetCurrentMin}'`
          : `Synced to ${clampedStopped}'–${targetCurrentMin}' Window`
      );

      const assignedSpeaker = speakerFromVoice(res.voice);
      setSpeaker(assignedSpeaker);

      const narrationScript = [
        res.whatHappened,
        res.whyItMatters,
        res.momentumChanges,
        res.whatToWatchNext,
      ]
        .filter(Boolean)
        .join("\n\n");

      if (narrationScript) {
        setTranscriptLine(res.whatHappened);
      }

      // Step 2: Synthesize & auto-play audio
      if (autoPlayAudio && narrationScript) {
        setIsSynthesizing(true);
        setStatusBadgeText(
          lang === "es"
            ? "Sintetizando Audio AI en Español..."
            : "Synthesizing British AI Audio..."
        );
        try {
          const audioRes = await synthesizeAudio({
            text: narrationScript,
            voice: res.voice ?? null,
            language: lang,
          });

          if (myGenId !== generationIdRef.current) return;
          setIsSynthesizing(false);
          setStatusBadgeText(
            lang === "es"
              ? `Sincronizado a ventana ${clampedStopped}'–${targetCurrentMin}'`
              : `Synced to ${clampedStopped}'–${targetCurrentMin}' Window`
          );

          if (audioRes.voice) {
            setSpeaker(speakerFromVoice(audioRes.voice, assignedSpeaker));
          }
          if (audioRes.audioUrl) {
            playFreshAudioDataUrl(audioRes.audioUrl);
          } else {
            playBrowserSpeech(narrationScript, lang, audioRes.voice);
          }
        } catch (audioErr) {
          console.warn("Cloud audio fallback to native speech:", audioErr);
          if (myGenId === generationIdRef.current) {
            setIsSynthesizing(false);
            setStatusBadgeText(
              lang === "es"
                ? `Sincronizado a ventana ${clampedStopped}'–${targetCurrentMin}'`
                : `Synced to ${clampedStopped}'–${targetCurrentMin}' Window`
            );
            playBrowserSpeech(narrationScript, lang, res.voice);
          }
        }
      }
    },
    [
      bias,
      lang,
      length,
      match,
      mode,
      playBrowserSpeech,
      playFreshAudioDataUrl,
      stopAndRevokeCurrentAudio,
      stoppedMinute,
      unlockAudioElementOnGesture,
    ]
  );

  // Handle "Pass Mic to Ada/Ollie" button in Audio Dock
  const handleSwitchSpeaker = async () => {
    const nextSpeaker = speaker === "Ollie" ? "Ada" : "Ollie";
    setSpeaker(nextSpeaker);
    const nextVoice =
      nextSpeaker === "Ada"
        ? "en-GB-AdaMultilingualNeural"
        : "en-GB-OllieMultilingualNeural";

    const narrationScript = catchUp
      ? [
          catchUp.whatHappened,
          catchUp.whyItMatters,
          catchUp.momentumChanges,
          catchUp.whatToWatchNext,
        ]
          .filter(Boolean)
          .join("\n\n")
      : fullTimeSummary?.story || transcriptLine;

    if (!narrationScript) return;

    const myGenId = ++generationIdRef.current;
    unlockAudioElementOnGesture();
    setIsSynthesizing(true);
    try {
      const audioRes = await synthesizeAudio({
        text: narrationScript,
        voice: nextVoice,
        language: lang,
      });
      if (myGenId !== generationIdRef.current) return;
      setIsSynthesizing(false);
      if (audioRes.audioUrl) {
        playFreshAudioDataUrl(audioRes.audioUrl);
      } else {
        playBrowserSpeech(narrationScript, lang, nextVoice);
      }
    } catch (err) {
      console.warn("Failed to switch speaker audio via cloud, falling back to native speech:", err);
      if (myGenId === generationIdRef.current) {
        setIsSynthesizing(false);
        playBrowserSpeech(narrationScript, lang, nextVoice);
      }
    }
  };

  const handleTogglePlay = () => {
    // If Web Speech API is speaking or paused
    if (
      typeof window !== "undefined" &&
      "speechSynthesis" in window &&
      window.speechSynthesis.speaking
    ) {
      if (window.speechSynthesis.paused) {
        speechClockRef.current.lastTick = Date.now();
        speechClockRef.current.paused = false;
        window.speechSynthesis.resume();
        setIsPlaying(true);
      } else {
        speechClockRef.current.paused = true;
        window.speechSynthesis.pause();
        setIsPlaying(false);
      }
      return;
    }

    const audio = audioRef.current;
    if (
      !audio ||
      !currentBlobUrlRef.current ||
      audio.src === SILENT_UNLOCK_WAV
    ) {
      runCatchUp(true, true);
      return;
    }
    if (audio.paused) {
      audio
        .play()
        .then(() => setIsPlaying(true))
        .catch((err) => {
          console.error("Audio play failed:", err);
          setIsPlaying(false);
        });
    } else {
      audio.pause();
      setIsPlaying(false);
    }
  };

  const handleCycleSpeed = () => {
    const speeds = [1.0, 1.25, 1.5, 2.0];
    const next = speeds[(speeds.indexOf(playbackSpeed) + 1) % speeds.length];
    setPlaybackSpeed(next);
    if (audioRef.current) {
      audioRef.current.playbackRate = next;
    }
  };

  const handleCloseAudioDock = () => {
    stopAndRevokeCurrentAudio();
    setShowAudioDock(false);
  };

  return (
    <div className="min-h-screen pb-36 selection:bg-pl-purple selection:text-white pitch-texture">
      {/* Ambient Daylight Broadcast Glows */}
      <div
        id="ambient-glow-left"
        className="fixed -top-36 -left-36 w-[28rem] h-[28rem] rounded-full bg-purple-300/35 blur-3xl pointer-events-none transition-all duration-700"
      ></div>
      <div
        id="ambient-glow-right"
        className="fixed top-1/3 -right-36 w-[28rem] h-[28rem] rounded-full bg-teal-300/35 blur-3xl pointer-events-none transition-all duration-700"
      ></div>

      {/* 1. Midnight Obsidian Broadcast Navbar with Sky MNF Slanted Bug (Brief C) + Live Clock */}
      <Navbar
        currentMinute={currentMinute}
        subSeconds={subSeconds}
        status={match?.status}
        onResetSimulation={handleInitializeOrResetSimulation}
        onChangeLanguage={handleGlobalLanguageChange}
      />

      {/* Main Container */}
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 pt-5 space-y-6">
        <div id="view-dashboard" className="space-y-6">
          {/* 2. Live Grounds Bar (SVG Ball Icon, Arsenal vs Liverpool + Simulation Reset) */}
          <LiveGrounds
            match={match}
            initialStartMinute={initialStartMinute}
            onResetSimulation={handleInitializeOrResetSimulation}
          />

          {/* 3. ScoreContainer + Real Match Timeline + Separate Event Animations */}
          <ScoreContainer
            match={match}
            timeline={timeline}
            insights={insights}
            stoppedMinute={stoppedMinute}
            currentMinute={currentMinute}
            selectedEvent={selectedEvent}
            eventExplanation={eventExplanation}
            activeAnimation={activeAnimation}
            onTriggerAnimation={handleTriggerAnimation}
            onSelectTimelineEvent={handleSelectTimelineEvent}
          />

          {/* Step 6: End-of-Match (90' FullTime) Final Score & Match Summary Card */}
          {isFullTime && showFullTimeBanner && (
            <section
              aria-label="Full-Time Match Summary"
              className="broadcast-slab rounded-2xl p-5 sm:p-6 border-2 border-emerald-500/40 bg-gradient-to-br from-white via-emerald-50/30 to-teal-50/40 relative"
            >
              <div className="flex flex-wrap items-center justify-between gap-3 pb-4 mb-4 border-b border-slate-200">
                <div className="flex items-center gap-3">
                  <span className="px-3 py-1 rounded-md bg-emerald-600 text-white font-mono text-xs font-extrabold uppercase tracking-wider shadow-sm">
                    {t.dashboard.fullTimeBanner.title}
                  </span>
                  <div>
                    <h2 className="text-xl sm:text-2xl font-display font-black uppercase tracking-wide text-slate-900">
                      Final Score: {match?.homeTeam.name ?? "Arsenal"}{" "}
                      {match?.homeTeam.score ?? 2} –{" "}
                      {match?.awayTeam.score ?? 1}{" "}
                      {match?.awayTeam.name ?? "Liverpool"}
                    </h2>
                    <p className="text-xs font-mono text-slate-600">
                      All 90 minutes &amp; scheduled events processed •{" "}
                      {timeline.length} key events • Possession{" "}
                      {match?.statistics.possession.home ?? 54}%–
                      {match?.statistics.possession.away ?? 46}%
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    disabled={isGeneratingSummary}
                    onClick={() => handleGenerateFullTimeSummary(true, true)}
                    className="px-4 py-2 rounded-xl font-display font-black uppercase text-sm tracking-wider bg-emerald-600 hover:bg-emerald-700 text-white shadow flex items-center gap-2 transition-all cursor-pointer disabled:opacity-60"
                  >
                    <BallIcon className="w-4 h-4 text-white shrink-0" />
                    <span>
                      {isGeneratingSummary
                        ? t.dashboard.fullTimeBanner.generatingBtn
                        : fullTimeSummary
                          ? t.dashboard.fullTimeBanner.generateBtn + " ↻"
                          : t.dashboard.fullTimeBanner.generateBtn}
                    </span>
                    <HeadphonesIcon className="w-4 h-4 text-white shrink-0" />
                  </button>

                  <button
                    type="button"
                    onClick={() => setShowFullTimeBanner(false)}
                    aria-label="Close Full-Time Summary"
                    title="Close Full-Time Summary"
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

              {fullTimeSummary ? (
                <div className="bg-white/90 rounded-xl p-4 sm:p-5 border border-emerald-200 shadow-inner space-y-3">
                  <div className="flex flex-wrap items-center justify-between gap-2 text-xs font-mono text-slate-500">
                    <span className="font-bold text-emerald-700 uppercase">
                      90-Minute Personalized Match Report ({fullTimeSummary.mode}{" "}
                      • {fullTimeSummary.favoriteTeam})
                    </span>
                    <span>
                      {fullTimeSummary.wordCount} words • ~
                      {fullTimeSummary.estimatedDurationSeconds}s audio
                    </span>
                  </div>
                  <div className="text-sm sm:text-base text-slate-800 leading-relaxed whitespace-pre-line">
                    {fullTimeSummary.story}
                  </div>
                </div>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                  {insights.map((ins, idx) => (
                    <div
                      key={`${ins.minute}-${idx}`}
                      className="bg-white rounded-xl p-3.5 border border-slate-200 shadow-sm"
                    >
                      <div className="flex items-center justify-between text-xs font-mono mb-1">
                        <span className="font-extrabold text-pl-purple">
                          {ins.minute}&apos; • {ins.type}
                        </span>
                      </div>
                      <p className="text-xs sm:text-sm font-bold text-slate-900">
                        {ins.summary}
                      </p>
                      <p className="text-xs text-slate-600 mt-1">
                        {ins.reason}
                      </p>
                    </div>
                  ))}
                </div>
              )}
            </section>
          )}

          {/* 4. Hero Console */}
          <HeroConsole
            stoppedMinute={stoppedMinute}
            currentMinute={currentMinute}
            mode={mode}
            bias={bias}
            length={length}
            lang={lang}
            isGenerating={isGenerating}
            generatingWindow={generatingWindow}
            onChangeStoppedMinute={setStoppedMinute}
            onChangeMode={setMode}
            onChangeBias={setBias}
            onChangeLength={setLength}
            onChangeLang={setLang}
            onCatchMeUp={() => {
              const goalEv =
                timeline.find(
                  (e) =>
                    e.eventType === "Goal" &&
                    e.minute >= stoppedMinute &&
                    e.minute <= currentMinute
                ) ?? timeline.find((e) => e.eventType === "Goal");
              if (goalEv) {
                const info = formatTimelineEventTitle(
                  goalEv,
                  match?.homeTeam.teamId ?? "ARS",
                  match?.homeTeam.name ?? "Arsenal",
                  match?.awayTeam.name ?? "Liverpool"
                );
                handleTriggerAnimation({
                  kind: "goal",
                  min: goalEv.minute,
                  club: info.clubName,
                  player: info.playerLine,
                  sub: info.title,
                });
              }
              runCatchUp(true, true);
            }}
          />

          {/* 5. LiveCatchUp 4 Intelligence Cards (Shown only after Catch Me Up is clicked, dismissible via X button) */}
          {showCatchUpDeck && (
            <Cards
              match={match}
              timeline={timeline}
              insights={insights}
              stoppedMinute={stoppedMinute}
              currentMinute={currentMinute}
              catchUp={catchUp}
              isGenerating={isGenerating}
              statusBadgeText={statusBadgeText}
              onClose={() => setShowCatchUpDeck(false)}
            />
          )}
        </div>
      </main>

      {/* 6. Auto-Play Audio Recap Player Dock (Shown only after Catch Me Up is clicked, dismissible via X button) */}
      {showAudioDock && (
        <AudioPlayerDock
          isPlaying={isPlaying}
          isSynthesizing={isSynthesizing}
          speaker={speaker}
          transcriptLine={transcriptLine}
          currentTime={currentTime}
          duration={duration}
          playbackSpeed={playbackSpeed}
          onTogglePlay={handleTogglePlay}
          onSwitchSpeaker={handleSwitchSpeaker}
          onCycleSpeed={handleCycleSpeed}
          onClose={handleCloseAudioDock}
        />
      )}

      {/* Hidden HTML5 Audio Element for Blob Playback */}
      <audio
        ref={audioRef}
        onTimeUpdate={() => {
          if (
            audioRef.current &&
            audioRef.current.src !== SILENT_UNLOCK_WAV
          ) {
            setCurrentTime(audioRef.current.currentTime);
          }
        }}
        onLoadedMetadata={() => {
          if (
            audioRef.current &&
            audioRef.current.src !== SILENT_UNLOCK_WAV &&
            Number.isFinite(audioRef.current.duration) &&
            audioRef.current.duration > 0.5
          ) {
            setDuration(audioRef.current.duration);
          }
        }}
        onPlay={() => {
          if (audioRef.current?.src !== SILENT_UNLOCK_WAV) {
            setIsPlaying(true);
          }
        }}
        onPause={() => setIsPlaying(false)}
        onEnded={() => setIsPlaying(false)}
        className="hidden"
      />
    </div>
  );
};

export default Dashboard;
