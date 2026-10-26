import React, { useEffect, useRef, useState } from "react";
import { CheckIcon, ChevronDownIcon, GlobeIcon } from "./Icons";
import { useLanguage } from "../languages/LanguageContext";

interface NavbarProps {
  currentMinute?: number;
  subSeconds?: number;
  status?: string;
  onResetSimulation?: () => void;
  onChangeLanguage?: (lang: "en" | "es") => void;
}

export const Navbar: React.FC<NavbarProps> = ({
  currentMinute = 60,
  subSeconds = 0,
  status = "2nd Half",
  onResetSimulation,
  onChangeLanguage,
}) => {
  const { language, setLanguage, t } = useLanguage();
  const [isLangOpen, setIsLangOpen] = useState(false);
  const langDropdownRef = useRef<HTMLDivElement | null>(null);

  const isFullTime = currentMinute >= 90 || status === "FullTime";
  const mm = String(Math.min(90, Math.max(1, currentMinute))).padStart(2, "0");
  const ss = isFullTime ? "00" : String(subSeconds % 60).padStart(2, "0");

  const handleSelectLanguage = (newLang: "en" | "es") => {
    setLanguage(newLang);
    onChangeLanguage?.(newLang);
    setIsLangOpen(false);
  };

  // Close dropdown on outside click or Escape
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        langDropdownRef.current &&
        !langDropdownRef.current.contains(e.target as Node)
      ) {
        setIsLangOpen(false);
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setIsLangOpen(false);
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
              {t.navbar.insideTheGame}
            </span>
            <span className="text-xs font-medium text-slate-300 italic">
              &ldquo;{t.navbar.tagline}&rdquo;
            </span>
          </div>
        </div>

        {/* Right: Live Simulation Clock Pill + Language Switcher + Reset Simulation Button */}
        <div className="flex items-center gap-2.5">
          {/* Global Language Selector Dropdown */}
          <div ref={langDropdownRef} className="relative">
            <button
              type="button"
              id="navbar-language-btn"
              aria-haspopup="listbox"
              aria-expanded={isLangOpen}
              onClick={() => setIsLangOpen((prev) => !prev)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border text-xs font-mono font-bold transition-all cursor-pointer shadow-xs ${
                isLangOpen
                  ? "bg-pl-purple text-white border-mint-bright/60 ring-2 ring-mint-bright/30"
                  : "bg-white/[0.08] hover:bg-white/[0.16] text-slate-100 border-white/20"
              }`}
            >
              <GlobeIcon className="w-3.5 h-3.5 text-mint-bright shrink-0" />
              <span className="uppercase tracking-wider">
                {language === "es"
                  ? t.navbar.spanishBadge
                  : t.navbar.englishBadge}
              </span>
              <span className="text-[10px] text-slate-300 hidden md:inline">
                ({language === "es" ? t.navbar.spanish : t.navbar.english})
              </span>
              <ChevronDownIcon
                className={`w-3.5 h-3.5 text-slate-300 transition-transform duration-200 shrink-0 ${
                  isLangOpen ? "rotate-180 text-mint-bright" : ""
                }`}
              />
            </button>

            {/* Dropdown Floating Menu */}
            {isLangOpen && (
              <div
                role="listbox"
                aria-label={t.navbar.language}
                className="absolute right-0 mt-2 w-44 z-50 bg-[#161D2F] border border-white/20 rounded-xl shadow-2xl p-1.5 space-y-1 backdrop-blur-xl animate-fade-in"
              >
                <div className="px-2.5 py-1 text-[10px] font-mono font-bold uppercase tracking-widest text-slate-400 border-b border-white/10">
                  {t.navbar.language}
                </div>

                <button
                  type="button"
                  role="option"
                  aria-selected={language === "en"}
                  onClick={() => handleSelectLanguage("en")}
                  className={`w-full text-left px-2.5 py-2 rounded-lg text-xs font-mono font-bold transition-all flex items-center justify-between cursor-pointer ${
                    language === "en"
                      ? "bg-pl-purple text-white border border-mint-bright/40 shadow-sm"
                      : "text-slate-200 hover:bg-white/[0.08]"
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-mint-bright font-extrabold text-[11px]">
                      {t.navbar.englishBadge}
                    </span>
                    <span>{t.navbar.english}</span>
                  </div>
                  {language === "en" && (
                    <CheckIcon className="w-4 h-4 text-mint-bright shrink-0" />
                  )}
                </button>

                <button
                  type="button"
                  role="option"
                  aria-selected={language === "es"}
                  onClick={() => handleSelectLanguage("es")}
                  className={`w-full text-left px-2.5 py-2 rounded-lg text-xs font-mono font-bold transition-all flex items-center justify-between cursor-pointer ${
                    language === "es"
                      ? "bg-pl-purple text-white border border-mint-bright/40 shadow-sm"
                      : "text-slate-200 hover:bg-white/[0.08]"
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-mint-bright font-extrabold text-[11px]">
                      {t.navbar.spanishBadge}
                    </span>
                    <span>{t.navbar.spanish}</span>
                  </div>
                  {language === "es" && (
                    <CheckIcon className="w-4 h-4 text-mint-bright shrink-0" />
                  )}
                </button>
              </div>
            )}
          </div>

          {onResetSimulation && (
            <button
              type="button"
              onClick={onResetSimulation}
              title={t.navbar.newRandomMatchTitle}
              className="hidden md:inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-white/[0.06] hover:bg-white/[0.14] text-mint-bright border border-mint-bright/30 text-[11px] font-mono font-bold uppercase tracking-wider transition-all cursor-pointer"
            >
              <span>{t.navbar.newRandomMatch}</span>
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
              {isFullTime
                ? t.navbar.fullTime
                : `${t.navbar.live} ${mm}:${ss}`}
            </span>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Navbar;
