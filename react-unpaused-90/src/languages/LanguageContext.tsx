import React, {
  createContext,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import en from "./en.json";
import es from "./es.json";

export type Language = "en" | "es";
export type Translations = typeof en;

interface LanguageContextType {
  language: Language;
  setLanguage: (lang: Language) => void;
  toggleLanguage: () => void;
  t: Translations;
}

const translationsMap: Record<Language, Translations> = { en, es };

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

interface LanguageProviderProps {
  children: ReactNode;
  currentLanguage?: Language;
  onChangeLanguage?: (lang: Language) => void;
}

export const LanguageProvider: React.FC<LanguageProviderProps> = ({
  children,
  currentLanguage,
  onChangeLanguage,
}) => {
  const [internalLanguage, setInternalLanguage] = useState<Language>("en");

  // Controlled or uncontrolled language
  const language = currentLanguage ?? internalLanguage;

  const setLanguage = (newLang: Language) => {
    setInternalLanguage(newLang);
    onChangeLanguage?.(newLang);
  };

  const toggleLanguage = () => {
    const next = language === "en" ? "es" : "en";
    setLanguage(next);
  };

  const t = useMemo(() => translationsMap[language] ?? translationsMap.en, [language]);

  return (
    <LanguageContext.Provider
      value={{
        language,
        setLanguage,
        toggleLanguage,
        t,
      }}
    >
      {children}
    </LanguageContext.Provider>
  );
};

export const useLanguage = (): LanguageContextType => {
  const context = useContext(LanguageContext);
  if (!context) {
    throw new Error("useLanguage must be used within a LanguageProvider");
  }
  return context;
};
