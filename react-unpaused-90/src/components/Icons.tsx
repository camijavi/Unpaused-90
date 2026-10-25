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

export const ArsenalCrest: React.FC<IconProps> = ({
  className = "w-full h-full",
}) => (
  <svg viewBox="0 0 80 80" className={className} aria-label="Arsenal Crest">
    <defs>
      <linearGradient id="arsGrad" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stopColor="#EF4444" />
        <stop offset="100%" stopColor="#991B1B" />
      </linearGradient>
    </defs>
    <path
      d="M12 10 H68 V38 C68 58 40 72 40 72 C40 72 12 58 12 38 Z"
      fill="url(#arsGrad)"
      stroke="#FBBF24"
      strokeWidth="3.5"
    />
    <path
      d="M17 15 H63 V37 C63 53 40 65 40 65 C40 65 17 53 17 37 Z"
      fill="none"
      stroke="#1E3A8A"
      strokeWidth="2"
    />
    <rect
      x="22"
      y="36"
      width="34"
      height="6"
      rx="3"
      transform="rotate(-14 39 39)"
      fill="#FBBF24"
    />
    <circle
      cx="33"
      cy="45"
      r="6.5"
      fill="#1E3A8A"
      stroke="#FBBF24"
      strokeWidth="2.5"
    />
    <circle cx="33" cy="45" r="2" fill="#FBBF24" />
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
    <defs>
      <linearGradient id="livGrad" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stopColor="#E11D48" />
        <stop offset="100%" stopColor="#881337" />
      </linearGradient>
    </defs>
    <path
      d="M14 14 Q40 6 66 14 V40 C66 59 40 72 40 72 C40 72 14 59 14 40 Z"
      fill="url(#livGrad)"
      stroke="#FBBF24"
      strokeWidth="3"
    />
    <path
      d="M40 22 C33 26 26 32 28 42 C32 40 36 41 40 48 C44 41 48 40 52 42 C54 32 47 26 40 22 Z"
      fill="#FBBF24"
    />
    <circle cx="40" cy="20" r="3" fill="#FBBF24" />
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
