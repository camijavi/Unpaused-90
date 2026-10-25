import React, { useCallback, useEffect, useRef, useState } from "react";
import Navbar from "../components/Navbar";
import LiveGrounds from "../components/LiveGrounds";
import ScoreContainer, {
  formatTimelineEventTitle,
} from "../components/ScoreContainer";
import HeroConsole from "../components/HeroConsole";
import Cards from "../components/Cards";
import AudioPlayerDock from "../components/AudioPlayerDock";
import {
  explainTimelineEvent,
  fetchMatchById,
  fetchMatchInsights,
  fetchMatchTimeline,
  generateCatchUp,
  synthesizeAudio,
} from "../services/api";
import type {
  CatchUpResponse,
  EventAnimationPayload,
  GeneratedInsight,
  MatchData,
  TimelineEvent,
} from "../types/match";

const FALLBACK_MATCH: MatchData = {
  matchId: "PL2027-001",
  competition: "Premier League",
  season: "2026/27",
  stadium: "Emirates Stadium",
  status: "FullTime",
  currentMinute: 90,
  homeTeam: {
    teamId: "ARS",
    name: "Arsenal",
    coach: "Mikel Arteta",
    formation: "4-3-3",
    score: 2,
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
    shots: { home: 14, away: 11 },
    shotsOnTarget: { home: 6, away: 5 },
    passAccuracy: { home: 87, away: 84 },
    corners: { home: 7, away: 5 },
    fouls: { home: 9, away: 11 },
  },
  timeline: [
    { eventId: "EV001", minute: 12, eventType: "Shot", team: "ARS", player: "Kai Havertz", onTarget: true },
    { eventId: "EV002", minute: 24, eventType: "PressureSequence", team: "ARS", durationSeconds: 96, importanceScore: 75 },
    { eventId: "EV003", minute: 31, eventType: "Goal", team: "ARS", player: "Bukayo Saka", assist: "Martin Odegaard", importanceScore: 100 },
    { eventId: "EV004", minute: 43, eventType: "YellowCard", team: "LIV", player: "Alexis Mac Allister" },
    { eventId: "EV005", minute: 54, eventType: "Goal", team: "LIV", player: "Mohamed Salah", assist: "Dominik Szoboszlai", importanceScore: 95 },
    { eventId: "EV006", minute: 66, eventType: "Substitution", team: "ARS", playerOut: "Gabriel Martinelli", playerIn: "Leandro Trossard" },
    { eventId: "EV007", minute: 72, eventType: "PressureSequence", team: "ARS", durationSeconds: 114, importanceScore: 88 },
    { eventId: "EV008", minute: 79, eventType: "Goal", team: "ARS", player: "Kai Havertz", assist: "Bukayo Saka", importanceScore: 100 },
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
    {
      minute: 79,
      type: "WinningMoment",
      summary: "Arsenal regained the lead late in the second half.",
      reason: "Sustained wing pressure created a second-ball opportunity finished by Havertz.",
    },
  ],
};

function mapBiasToFavoriteTeam(bias: "neutral" | "arsenal" | "liverpool"): string {
  if (bias === "arsenal") return "Arsenal";
  if (bias === "liverpool") return "Liverpool";
  return "Neutral";
}

export const Dashboard: React.FC = () => {
  const [match, setMatch] = useState<MatchData | null>(FALLBACK_MATCH);
  const [timeline, setTimeline] = useState<TimelineEvent[]>(FALLBACK_MATCH.timeline);
  const [insights, setInsights] = useState<GeneratedInsight[]>(
    FALLBACK_MATCH.generatedInsights
  );

  // Hero Console state
  const [stoppedMinute, setStoppedMinute] = useState<number>(30);
  const [mode, setMode] = useState<"casual" | "analyst" | "child">("casual");
  const [bias, setBias] = useState<"neutral" | "arsenal" | "liverpool">("arsenal");
  const [length, setLength] = useState<"1m" | "5m" | "full">("1m");
  const [lang, setLang] = useState<"en" | "es">("en");

  // LiveCatchUp & Event Inspector state
  const [catchUp, setCatchUp] = useState<CatchUpResponse | null>(null);
  const [isGenerating, setIsGenerating] = useState<boolean>(false);
  const [statusBadgeText, setStatusBadgeText] = useState<string>(
    "Synced to 30'–90' Window"
  );
  const [selectedEvent, setSelectedEvent] = useState<TimelineEvent | null>(
    FALLBACK_MATCH.timeline[4]
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
  const [transcriptLine, setTranscriptLine] = useState<string>(
    "Click 'Catch Me Up & Auto-Play Audio' to generate your live personalized match recap."
  );
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [duration, setDuration] = useState<number>(60);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1.0);

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const currentBlobUrlRef = useRef<string | null>(null);
  const fxTimeoutRef = useRef<number | null>(null);
  const generationIdRef = useRef<number>(0);

  const currentMinute = match?.currentMinute ?? 90;

  // Flush and reset any playing audio so an old audio stream can never replay
  const resetAudioPlayer = useCallback(() => {
    const audio = audioRef.current;
    if (audio) {
      audio.pause();
      audio.currentTime = 0;
      audio.removeAttribute("src");
      audio.load();
    }
    if (currentBlobUrlRef.current) {
      URL.revokeObjectURL(currentBlobUrlRef.current);
      currentBlobUrlRef.current = null;
    }
    setIsPlaying(false);
    setCurrentTime(0);
  }, []);

  // Convert base64 MP3 data URI into a fresh Blob URL and auto-play
  const playFreshAudioDataUrl = useCallback(
    (dataUrl: string) => {
      resetAudioPlayer();
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
        audio
          .play()
          .then(() => setIsPlaying(true))
          .catch(() => setIsPlaying(false));
      }
    },
    [playbackSpeed, resetAudioPlayer]
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
      } catch {
        setEventExplanation(null);
      }
    },
    [bias, lang, match, mode]
  );

  // Generate Catch-Up intelligence cards + optionally synthesize & auto-play TTS audio
  const runCatchUp = useCallback(
    async (autoPlayAudio: boolean) => {
      const myGenId = ++generationIdRef.current;
      if (autoPlayAudio) {
        resetAudioPlayer();
      }
      setIsGenerating(true);
      setStatusBadgeText("Generating Live AI Catch-Up...");

      const activeMatchId = match?.matchId ?? "PL2027-001";
      const targetCurrentMin = match?.currentMinute ?? 90;

      try {
        // Step 1: Fetch the 4 intelligence cards first (includeAudio: false)
        const res = await generateCatchUp({
          matchId: activeMatchId,
          lastViewedMinute: stoppedMinute,
          currentMinute: targetCurrentMin,
          viewMode: mode,
          favoriteTeam: mapBiasToFavoriteTeam(bias),
          language: lang,
          length,
          includeAudio: false,
        });

        if (myGenId !== generationIdRef.current) return;

        setCatchUp(res);
        setIsGenerating(false);
        setStatusBadgeText(
          `Synced to ${stoppedMinute}'–${targetCurrentMin}' Window`
        );

        const assignedSpeaker =
          res.voice && res.voice.toLowerCase().includes("ada")
            ? "Ada"
            : "Ollie";
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

        // Step 2: Synthesize & auto-play audio if requested
        if (autoPlayAudio && narrationScript) {
          setIsSynthesizing(true);
          const audioRes = await synthesizeAudio({
            text: narrationScript,
            voice: res.voice ?? null,
            language: lang,
          });

          if (myGenId !== generationIdRef.current) return;
          setIsSynthesizing(false);

          if (audioRes.voice) {
            setSpeaker(
              audioRes.voice.toLowerCase().includes("ada") ? "Ada" : "Ollie"
            );
          }
          if (audioRes.audioUrl) {
            playFreshAudioDataUrl(audioRes.audioUrl);
          }
        }
      } catch {
        if (myGenId === generationIdRef.current) {
          setIsGenerating(false);
          setIsSynthesizing(false);
          setStatusBadgeText(
            `Synced to ${stoppedMinute}'–${targetCurrentMin}' Window`
          );
        }
      }
    },
    [
      bias,
      lang,
      length,
      match,
      mode,
      playFreshAudioDataUrl,
      resetAudioPlayer,
      stoppedMinute,
    ]
  );

  // Initial fetch of match data, timeline, insights, and initial text catch-up
  useEffect(() => {
    let cancelled = false;
    async function loadInitialData() {
      try {
        const [matchData, timelineData, insightsData] = await Promise.all([
          fetchMatchById("PL2027-001"),
          fetchMatchTimeline("PL2027-001"),
          fetchMatchInsights("PL2027-001"),
        ]);
        if (cancelled) return;
        setMatch(matchData);
        setTimeline(timelineData);
        setInsights(insightsData);
        const defaultEv =
          timelineData.find((e) => e.eventType === "Goal" && e.team === "LIV") ??
          timelineData[0] ??
          null;
        setSelectedEvent(defaultEv);
      } catch {
        // Fallback match data is already loaded in state
      }
    }
    loadInitialData();
    return () => {
      cancelled = true;
    };
  }, []);

  // Trigger initial text catch-up on mount so the 4 cards show real backend data immediately
  useEffect(() => {
    runCatchUp(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
      : transcriptLine;

    if (!narrationScript) return;

    const myGenId = ++generationIdRef.current;
    resetAudioPlayer();
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
      }
    } catch {
      if (myGenId === generationIdRef.current) {
        setIsSynthesizing(false);
      }
    }
  };

  const handleTogglePlay = () => {
    const audio = audioRef.current;
    if (!audio || !audio.src) {
      // If no audio has been synthesized yet, synthesize and play now
      runCatchUp(true);
      return;
    }
    if (audio.paused) {
      audio
        .play()
        .then(() => setIsPlaying(true))
        .catch(() => setIsPlaying(false));
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

      {/* 1. Black Broadcast Navbar with Sky MNF Slanted Bug (Brief C) */}
      <Navbar />

      {/* Main Container */}
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 pt-5 space-y-6">
        <div id="view-dashboard" className="space-y-6">
          {/* 2. Live Grounds Bar (SVG Ball Icon, Arsenal vs Liverpool Only) */}
          <LiveGrounds match={match} />

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

          {/* 4. Hero Console */}
          <HeroConsole
            stoppedMinute={stoppedMinute}
            currentMinute={currentMinute}
            mode={mode}
            bias={bias}
            length={length}
            lang={lang}
            isGenerating={isGenerating}
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
              runCatchUp(true);
            }}
          />

          {/* 5. LiveCatchUp 4 Intelligence Cards */}
          <Cards
            match={match}
            timeline={timeline}
            insights={insights}
            stoppedMinute={stoppedMinute}
            currentMinute={currentMinute}
            catchUp={catchUp}
            isGenerating={isGenerating}
            statusBadgeText={statusBadgeText}
          />
        </div>
      </main>

      {/* 6. Auto-Play Audio Recap Player Dock */}
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
      />

      {/* Hidden HTML5 Audio Element for Blob Playback */}
      <audio
        ref={audioRef}
        onTimeUpdate={() => {
          if (audioRef.current) {
            setCurrentTime(audioRef.current.currentTime);
          }
        }}
        onLoadedMetadata={() => {
          if (audioRef.current && Number.isFinite(audioRef.current.duration)) {
            setDuration(audioRef.current.duration);
          }
        }}
        onPlay={() => setIsPlaying(true)}
        onPause={() => setIsPlaying(false)}
        onEnded={() => setIsPlaying(false)}
        className="hidden"
      />
    </div>
  );
};

export default Dashboard;
