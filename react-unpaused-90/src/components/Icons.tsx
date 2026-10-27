import React from "react";

interface IconProps {
  className?: string;
}

export const BallIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <circle
      cx="12"
      cy="12"
      r="9.5"
      fill="currentColor"
      fillOpacity="0.12"
      stroke="currentColor"
      strokeWidth="1.8"
    />
    <polygon
      points="12,7.2 15.2,9.5 14,13.3 10,13.3 8.8,9.5"
      fill="currentColor"
    />
    <path
      d="M12 2.5V7.2M15.2 9.5L20.5 7.8M14 13.3L17.6 18.8M10 13.3L6.4 18.8M8.8 9.5L3.5 7.8"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
    />
  </svg>
);

export const YellowCardIcon: React.FC<IconProps> = ({
  className = "w-3.5 h-4",
}) => (
  <svg
    viewBox="0 0 16 20"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <rect
      x="2"
      y="1.5"
      width="12"
      height="17"
      rx="2"
      fill="#FACC15"
      stroke="#713F12"
      strokeWidth="1.3"
    />
    <line
      x1="4.5"
      y1="5.5"
      x2="11.5"
      y2="5.5"
      stroke="#713F12"
      strokeOpacity="0.45"
      strokeWidth="1.2"
      strokeLinecap="round"
    />
  </svg>
);

export const RedCardIcon: React.FC<IconProps> = ({
  className = "w-3.5 h-4",
}) => (
  <svg
    viewBox="0 0 16 20"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <rect
      x="2"
      y="1.5"
      width="12"
      height="17"
      rx="2"
      fill="#EF4444"
      stroke="#FFFFFF"
      strokeWidth="1.3"
    />
    <line
      x1="4.5"
      y1="5.5"
      x2="11.5"
      y2="5.5"
      stroke="#FFFFFF"
      strokeOpacity="0.55"
      strokeWidth="1.2"
      strokeLinecap="round"
    />
  </svg>
);

export const SubIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <path
      d="M7 4L3 8L7 12"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <path
      d="M3 8H16C18.2091 8 20 9.79086 20 12"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
    />
    <path
      d="M17 20L21 16L17 12"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <path
      d="M21 16H8C5.79086 16 4 14.2091 4 12"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
    />
  </svg>
);

export const HeadphonesIcon: React.FC<IconProps> = ({
  className = "w-4 h-4",
}) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <path
      d="M4 15V12C4 7.58172 7.58172 4 12 4C16.4183 4 20 7.58172 20 12V15"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
    />
    <rect
      x="2.5"
      y="13"
      width="4"
      height="7"
      rx="2"
      fill="currentColor"
    />
    <rect
      x="17.5"
      y="13"
      width="4"
      height="7"
      rx="2"
      fill="currentColor"
    />
  </svg>
);

export const TimerIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <circle cx="12" cy="13" r="8" stroke="currentColor" strokeWidth="2" />
    <path
      d="M12 9V13L15 15"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <path
      d="M12 2V5M10 2H14"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
    />
  </svg>
);

export const GlobeIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    className={className}
    aria-hidden="true"
  >
    <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.8" />
    <path
      d="M3.6 9H20.4M3.6 15H20.4"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
    />
    <path
      d="M11.5 3C9 6.5 7.5 9.2 7.5 12C7.5 14.8 9 17.5 11.5 21M12.5 3C15 6.5 16.5 9.2 16.5 12C16.5 14.8 15 17.5 12.5 21"
      stroke="currentColor"
      strokeWidth="1.8"
    />
  </svg>
);

export const ChevronDownIcon: React.FC<IconProps> = ({
  className = "w-4 h-4",
}) => (
  <svg
    viewBox="0 0 20 20"
    fill="currentColor"
    className={className}
    aria-hidden="true"
  >
    <path
      fillRule="evenodd"
      d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z"
      clipRule="evenodd"
    />
  </svg>
);

export const CheckIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg
    viewBox="0 0 20 20"
    fill="currentColor"
    className={className}
    aria-hidden="true"
  >
    <path
      fillRule="evenodd"
      d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
      clipRule="evenodd"
    />
  </svg>
);

export const ArsenalCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Arsenal Crest">
    {/* Shield Base in iconic Arsenal Red */}
    <path
      d="M12 10 H68 V38 C68 58 40 72 40 72 C40 72 12 58 12 38 Z"
      fill="#DC2626"
      stroke="#FBBF24"
      strokeWidth="3.5"
    />
    {/* Inner Shield in Rich Crimson Red with Navy Trim */}
    <path
      d="M17 15 H63 V37 C63 53 40 65 40 65 C40 65 17 53 17 37 Z"
      fill="#B91C1C"
      stroke="#1E3A8A"
      strokeWidth="2"
    />
    {/* Golden Cannon Barrel */}
    <rect
      x="22"
      y="36"
      width="34"
      height="6"
      rx="3"
      transform="rotate(-14 39 39)"
      fill="#FBBF24"
    />
    {/* Cannon Wheel */}
    <circle
      cx="33"
      cy="45"
      r="6.5"
      fill="#1E3A8A"
      stroke="#FBBF24"
      strokeWidth="2.5"
    />
    <circle cx="33" cy="45" r="2" fill="#FBBF24" />
    {/* Club Name */}
    <text
      x="40"
      y="26"
      textAnchor="middle"
      fill="#FFFFFF"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="13"
      letterSpacing="1.5"
    >
      ARSENAL
    </text>
  </svg>
);

export const LiverpoolCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Liverpool Crest">
    {/* Shield Base in iconic Liverpool Crimson Red */}
    <path
      d="M14 14 Q40 6 66 14 V40 C66 59 40 72 40 72 C40 72 14 59 14 40 Z"
      fill="#DC2626"
      stroke="#FBBF24"
      strokeWidth="3"
    />
    {/* Inner Shield in Darker Red Highlight */}
    <path
      d="M18 18 Q40 12 62 18 V39 C62 55 40 66 40 66 C40 66 18 55 18 39 Z"
      fill="#991B1B"
    />
    {/* Liverbird in Golden Amber */}
    <path
      d="M40 22 C33 26 26 32 28 42 C32 40 36 41 40 48 C44 41 48 40 52 42 C54 32 47 26 40 22 Z"
      fill="#FBBF24"
    />
    <circle cx="40" cy="20" r="3" fill="#FBBF24" />
    {/* Club Initials */}
    <text
      x="40"
      y="61"
      textAnchor="middle"
      fill="#FBBF24"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="12"
      letterSpacing="1.5"
    >
      L.F.C.
    </text>
  </svg>
);

export const ManCityCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Manchester City Crest">
    {/* Outer Circular Shield Base */}
    <circle cx="40" cy="40" r="37" fill="#1C2C5B" stroke="#DAA520" strokeWidth="2.5" />
    <circle cx="40" cy="40" r="33" fill="#FFFFFF" />
    {/* Outer Text Ring */}
    <circle cx="40" cy="40" r="28" fill="#6CABDD" stroke="#1C2C5B" strokeWidth="1.8" />
    {/* Inner Shield */}
    <path
      d="M26 26 H54 V44 C54 53 40 60 40 60 C40 60 26 53 26 44 Z"
      fill="#FFFFFF"
      stroke="#1C2C5B"
      strokeWidth="1.6"
    />
    {/* Upper Field: Golden Ship in Full Sail */}
    <path d="M28 28 H52 V38 H28 Z" fill="#6CABDD" />
    <path
      d="M32 36 C35 37 45 37 48 36 L46 38 C42 39 38 39 34 38 Z"
      fill="#DAA520"
    />
    <path d="M35 30 L40 29 L39 35 L35 35 Z M41 29 L46 30 L45 35 L41 35 Z" fill="#DAA520" />
    {/* Lower Field: 3 Diagonal Stripes & Red Rose */}
    <line x1="30" y1="48" x2="38" y2="40" stroke="#DAA520" strokeWidth="1.5" />
    <line x1="36" y1="52" x2="44" y2="44" stroke="#DAA520" strokeWidth="1.5" />
    <line x1="42" y1="56" x2="50" y2="48" stroke="#DAA520" strokeWidth="1.5" />
    <circle cx="40" cy="47" r="3" fill="#E11D48" stroke="#FFFFFF" strokeWidth="0.8" />
    {/* Ring Text */}
    <text
      x="40"
      y="18"
      textAnchor="middle"
      fill="#1C2C5B"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="7.5"
      letterSpacing="1"
    >
      MANCHESTER
    </text>
    <text
      x="40"
      y="70"
      textAnchor="middle"
      fill="#1C2C5B"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="8.5"
      letterSpacing="1.5"
    >
      CITY
    </text>
  </svg>
);

export const ChelseaCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Chelsea FC Crest">
    {/* Outer Royal Blue Circle */}
    <circle cx="40" cy="40" r="37" fill="#034694" stroke="#D1AB3E" strokeWidth="2.5" />
    {/* White Ring */}
    <circle cx="40" cy="40" r="30" fill="#FFFFFF" stroke="#034694" strokeWidth="1.6" />
    {/* Inner Royal Blue Field */}
    <circle cx="40" cy="40" r="23" fill="#034694" />
    {/* Lion Rampant in Gold/White holding Abbot's Staff */}
    <path
      d="M37 28 C39 26 44 26 45 30 C47 34 43 36 45 40 C49 38 52 42 50 45 C47 48 45 53 47 57 L41 57 C39 53 37 50 39 46 C35 48 32 44 33 40 C35 36 34 32 37 28 Z"
      fill="#FFFFFF"
    />
    <line x1="46" y1="26" x2="46" y2="58" stroke="#D1AB3E" strokeWidth="2" strokeLinecap="round" />
    {/* Red Roses on left and right */}
    <circle cx="17" cy="40" r="2.5" fill="#DC2626" />
    <circle cx="63" cy="40" r="2.5" fill="#DC2626" />
    {/* Text */}
    <text
      x="40"
      y="18"
      textAnchor="middle"
      fill="#D1AB3E"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="8"
      letterSpacing="1.2"
    >
      CHELSEA
    </text>
    <text
      x="40"
      y="69"
      textAnchor="middle"
      fill="#D1AB3E"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="6"
      letterSpacing="1"
    >
      FOOTBALL CLUB
    </text>
  </svg>
);

export const ManUnitedCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Manchester United Crest">
    {/* Top Yellow Ribbon */}
    <path d="M14 11 H66 L60 21 H20 Z" fill="#FBE122" stroke="#000000" strokeWidth="1.2" />
    <text
      x="40"
      y="18.5"
      textAnchor="middle"
      fill="#000000"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="7.5"
      letterSpacing="1.2"
    >
      MANCHESTER
    </text>
    {/* Bottom Yellow Ribbon */}
    <path d="M14 69 H66 L60 59 H20 Z" fill="#FBE122" stroke="#000000" strokeWidth="1.2" />
    <text
      x="40"
      y="66.5"
      textAnchor="middle"
      fill="#000000"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="8"
      letterSpacing="1.5"
    >
      UNITED
    </text>
    {/* Center Shield */}
    <path
      d="M20 22 H60 V43 C60 55 40 64 40 64 C40 64 20 55 20 43 Z"
      fill="#DA291C"
      stroke="#FBE122"
      strokeWidth="2.5"
    />
    {/* Ship on Upper Yellow Field */}
    <path d="M23 24 H57 V36 H23 Z" fill="#FBE122" />
    <path
      d="M28 34 C31 35 49 35 52 34 L50 36 C45 37 35 37 30 36 Z"
      fill="#DA291C"
    />
    <path d="M31 27 L36 26 L35 32 L31 32 Z M37 26 L43 25 L42 32 L37 32 Z M44 26 L49 27 L48 32 L44 32 Z" fill="#DA291C" />
    {/* Red Devil with Pitchfork */}
    <path
      d="M36 40 C37 38 43 38 44 40 C47 43 45 48 46 51 C48 53 45 56 42 56 C39 56 36 53 38 51 C39 48 37 43 40 40 Z"
      fill="#FBE122"
    />
    {/* Pitchfork / Trident */}
    <line x1="49" y1="38" x2="49" y2="55" stroke="#FBE122" strokeWidth="1.5" />
    <path d="M47 38 H51 M47 38 L47 35 M51 38 L51 35 M49 38 L49 34" stroke="#FBE122" strokeWidth="1.2" />
  </svg>
);

export const TottenhamCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Tottenham Hotspur Crest">
    {/* Shield Base in Deep Navy */}
    <path
      d="M16 12 H64 V40 C64 58 40 70 40 70 C40 70 16 58 16 40 Z"
      fill="#132257"
      stroke="#FFFFFF"
      strokeWidth="2.5"
    />
    <path
      d="M20 16 H60 V39 C60 54 40 65 40 65 C40 65 20 54 20 39 Z"
      fill="#0F172A"
    />
    {/* Fighting Cockerel standing tall */}
    <path
      d="M38 19 C41 18 43 20 44 23 C46 22 47 24 45 26 C48 29 45 35 44 39 C46 38 48 36 49 33 C49 38 47 42 43 45 L43 48 L46 48 M40 45 L39 48 L37 48 C37 44 35 38 37 34 C35 32 34 27 38 24 Z"
      fill="#FFFFFF"
    />
    {/* Comb and Wattle */}
    <circle cx="43" cy="20" r="1.5" fill="#EF4444" />
    {/* Vintage Football */}
    <circle cx="40" cy="54" r="8" fill="#FFFFFF" stroke="#132257" strokeWidth="1.2" />
    <path d="M34 54 C37 56 43 56 46 54 M40 46 C41 50 41 58 40 62" stroke="#132257" strokeWidth="1" />
    {/* Text */}
    <text
      x="40"
      y="24"
      textAnchor="middle"
      fill="#94A3B8"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="6"
      letterSpacing="1"
    >
      SPURS
    </text>
  </svg>
);

export const NewcastleCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Newcastle United Crest">
    {/* Shield Base in Slate / Black */}
    <path
      d="M16 18 H64 V44 C64 60 40 70 40 70 C40 70 16 60 16 44 Z"
      fill="#1E293B"
      stroke="#F59E0B"
      strokeWidth="2.8"
    />
    {/* Black & White Vertical Stripes */}
    <path d="M22 20 H30 V50 C26 47 23 44 22 41 Z" fill="#FFFFFF" />
    <path d="M35 20 H45 V63 C43 62 41 61 40 60 C39 61 37 62 35 63 Z" fill="#FFFFFF" />
    <path d="M50 20 H58 V41 C57 44 54 47 50 50 Z" fill="#FFFFFF" />
    {/* Blue Ribbon / Chief */}
    <rect x="18" y="20" width="44" height="10" fill="#0284C7" />
    <text
      x="40"
      y="27.5"
      textAnchor="middle"
      fill="#FFFFFF"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="7"
      letterSpacing="1"
    >
      NEWCASTLE
    </text>
    {/* Castle / Turret on top */}
    <path d="M35 11 H45 V18 H35 Z M38 7 H42 V11 H38 Z" fill="#F59E0B" />
  </svg>
);

export const AstonVillaCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Aston Villa Crest">
    {/* Claret Shield Base */}
    <path
      d="M16 12 H64 V42 C64 58 40 70 40 70 C40 70 16 58 16 42 Z"
      fill="#670E36"
      stroke="#FEE12B"
      strokeWidth="2.8"
    />
    {/* Sky Blue Inner Shield */}
    <path
      d="M20 16 H60 V40 C60 54 40 64 40 64 C40 64 20 54 20 40 Z"
      fill="#95BFE5"
    />
    {/* European Cup Winner White Star */}
    <polygon
      points="28,21 29.5,25.5 34,25.5 30.5,28 32,32.5 28,29.5 24,32.5 25.5,28 22,25.5 26.5,25.5"
      fill="#FFFFFF"
      stroke="#670E36"
      strokeWidth="0.5"
    />
    {/* Fierce Yellow Lion Rampant */}
    <path
      d="M44 26 C46 24 51 24 52 28 C54 32 50 34 52 38 C56 36 59 40 57 43 C54 46 52 51 54 55 L48 55 C46 51 44 48 46 44 C42 46 39 42 40 38 C42 34 41 30 44 26 Z"
      fill="#FEE12B"
    />
    {/* Text AVFC */}
    <text
      x="40"
      y="24"
      textAnchor="middle"
      fill="#FEE12B"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="8"
      letterSpacing="1.5"
    >
      AVFC
    </text>
  </svg>
);

export const BrightonCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Brighton & Hove Albion Crest">
    {/* Outer Royal Blue Ring */}
    <circle cx="40" cy="40" r="37" fill="#0057B8" stroke="#FFFFFF" strokeWidth="2.5" />
    {/* White Ring */}
    <circle cx="40" cy="40" r="33" fill="#FFFFFF" />
    {/* Inner Deep Blue Field */}
    <circle cx="40" cy="40" r="26" fill="#0057B8" />
    {/* Soaring Seagull in Flight */}
    <path
      d="M24 43 C29 36 37 33 46 37 C42 39 40 42 41 45 C44 40 48 37 54 38 C57 39 59 41 60 43 C54 44 48 48 43 51 C37 50 31 47 24 43 Z"
      fill="#FFFFFF"
    />
    {/* Yellow Beak and Eye */}
    <polygon points="24,43 20,44 23,46" fill="#FFCD00" />
    <circle cx="27" cy="42" r="1.2" fill="#FFCD00" />
    {/* Outer Text */}
    <text
      x="40"
      y="18"
      textAnchor="middle"
      fill="#0057B8"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="6.5"
      letterSpacing="0.8"
    >
      BRIGHTON
    </text>
    <text
      x="40"
      y="69"
      textAnchor="middle"
      fill="#0057B8"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="6"
      letterSpacing="0.8"
    >
      HOVE ALBION
    </text>
  </svg>
);

export const EvertonCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Everton Crest">
    {/* Royal Blue Shield */}
    <path
      d="M16 12 H64 V42 C64 58 40 70 40 70 C40 70 16 58 16 42 Z"
      fill="#003399"
      stroke="#FFFFFF"
      strokeWidth="2.5"
    />
    <path
      d="M20 16 H60 V40 C60 54 40 64 40 64 C40 64 20 54 20 40 Z"
      fill="#002266"
    />
    {/* Prince Rupert's Tower in Center */}
    {/* Tower Base & Walls */}
    <path d="M35 32 H45 V48 H35 Z" fill="#FFFFFF" />
    <path d="M32 48 H48 V52 H32 Z" fill="#FFFFFF" />
    {/* Conical Roof */}
    <path d="M33 32 L40 22 L47 32 Z" fill="#FFFFFF" />
    <rect x="39" y="19" width="2" height="4" fill="#FFFFFF" />
    {/* Door & Window */}
    <rect x="38" y="42" width="4" height="6" rx="2" fill="#003399" />
    <circle cx="40" cy="29" r="1.8" fill="#003399" />
    {/* Laurel Wreaths */}
    <path d="M25 36 C24 42 27 48 31 51 M55 36 C56 42 53 48 49 51" stroke="#F59E0B" strokeWidth="1.8" strokeLinecap="round" />
    {/* Text */}
    <text
      x="40"
      y="62"
      textAnchor="middle"
      fill="#FFFFFF"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="7"
      letterSpacing="1.2"
    >
      EVERTON
    </text>
    <text
      x="40"
      y="18"
      textAnchor="middle"
      fill="#F59E0B"
      fontFamily="Barlow Condensed, sans-serif"
      fontWeight="900"
      fontSize="7"
      letterSpacing="1"
    >
      1878
    </text>
  </svg>
);

/**
 * Resolves the appropriate crest SVG based on teamId or team name across all 10 Premier League clubs.
 */
export function renderTeamCrest(
  teamId?: string,
  teamName?: string,
  className = "w-14 h-14 sm:w-16 sm:h-16"
) {
  const code = (teamId ?? "").toUpperCase();
  const name = (teamName ?? "").toLowerCase();

  if (code === "ARS" || name.includes("arsenal")) {
    return <ArsenalCrest className={className} />;
  }
  if (code === "LIV" || name.includes("liverpool")) {
    return <LiverpoolCrest className={className} />;
  }
  if (code === "MCI" || name.includes("manchester city") || name.includes("man city") || name === "city") {
    return <ManCityCrest className={className} />;
  }
  if (code === "CHE" || name.includes("chelsea")) {
    return <ChelseaCrest className={className} />;
  }
  if (code === "MUN" || name.includes("manchester united") || name.includes("man united") || name.includes("man utd")) {
    return <ManUnitedCrest className={className} />;
  }
  if (code === "TOT" || name.includes("tottenham") || name.includes("spurs")) {
    return <TottenhamCrest className={className} />;
  }
  if (code === "NEW" || name.includes("newcastle")) {
    return <NewcastleCrest className={className} />;
  }
  if (code === "AVL" || name.includes("aston villa") || name.includes("villa")) {
    return <AstonVillaCrest className={className} />;
  }
  if (code === "BHA" || name.includes("brighton")) {
    return <BrightonCrest className={className} />;
  }
  if (code === "EVE" || name.includes("everton")) {
    return <EvertonCrest className={className} />;
  }

  // Dynamic fallback crest with team initials & gold border
  const initials = (name || code || "FC")
    .split(/\s+/)
    .map((w) => w[0])
    .join("")
    .slice(0, 3)
    .toUpperCase();

  return (
    <div
      className={`${className} rounded-full bg-gradient-to-br from-indigo-700 via-purple-800 to-slate-900 border-2 border-amber-400 flex items-center justify-center text-white font-display font-black text-sm shadow-md`}
    >
      {initials}
    </div>
  );
}

export const TargetIcon: React.FC<IconProps> = ({ className = "w-3 h-3" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="2" />
    <circle cx="12" cy="12" r="4" stroke="currentColor" strokeWidth="2" />
    <path d="M12 2V6M12 18V22M2 12H6M18 12H22" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
  </svg>
);

export const FilterIcon: React.FC<IconProps> = ({ className = "w-3.5 h-3.5" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <path d="M3 5H21M6 12H18M10 19H14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const FlashIcon: React.FC<IconProps> = ({ className = "w-3.5 h-3.5" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <path
      d="M13 2L3 14H12L11 22L21 10H12L13 2Z"
      fill="currentColor"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  </svg>
);

export const BookIcon: React.FC<IconProps> = ({ className = "w-3.5 h-3.5" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <path
      d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <path
      d="M6.5 2H20V22H6.5A2.5 2.5 0 0 1 4 19.5V4.5A2.5 2.5 0 0 1 6.5 2Z"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <path
      d="M8 7H16M8 11H14"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
    />
  </svg>
);

export const InfoIcon: React.FC<IconProps> = ({ className = "w-4 h-4" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="2" />
    <line x1="12" y1="16" x2="12" y2="12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
    <circle cx="12" cy="8" r="1" fill="currentColor" />
  </svg>
);

export const DiceIcon: React.FC<IconProps> = ({ className = "w-3.5 h-3.5" }) => (
  <svg viewBox="0 0 24 24" fill="none" className={className} aria-hidden="true">
    <rect
      x="3"
      y="3"
      width="18"
      height="18"
      rx="4"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <circle cx="8" cy="8" r="1.5" fill="currentColor" />
    <circle cx="16" cy="8" r="1.5" fill="currentColor" />
    <circle cx="12" cy="12" r="1.5" fill="currentColor" />
    <circle cx="8" cy="16" r="1.5" fill="currentColor" />
    <circle cx="16" cy="16" r="1.5" fill="currentColor" />
  </svg>
);


