/** @type {import('tailwindcss').Config} */
module.exports = {
  // Scans the actual Razor markup that ships classes — includes the scaffolded Identity Area
  // pages too, since those still use plain HTML/DaisyUI-compatible class names (see app.css's
  // "legacy scaffold compatibility" layer for the handful of Bootstrap-only names that needed a
  // shim instead of a full rewrite).
  content: ["../Pages/**/*.cshtml", "../Areas/**/*.cshtml"],
  theme: {
    extend: {},
  },
  plugins: [require("daisyui")],
  daisyui: {
    themes: [
      {
        swordsandravens: {
          // "Swords and Ravens" dark theme: near-black war-room stone/wood as the base, deep
          // dragon-fire crimson as the primary accent, aged crown-gold as the secondary accent,
          // and a cold Valyrian-steel/Stark blue-grey as the tertiary accent.
          primary: "#8a1f2b",
          "primary-content": "#f5e9d9",
          secondary: "#b3892f",
          "secondary-content": "#1c1712",
          accent: "#4c6b8a",
          "accent-content": "#f5e9d9",
          neutral: "#221c17",
          "neutral-content": "#cbbfa8",
          "base-100": "#14100d",
          "base-200": "#1c1712",
          "base-300": "#2a221a",
          "base-content": "#e7dcc7",
          info: "#4c6b8a",
          success: "#4f7a52",
          warning: "#b3892f",
          error: "#9c2b2b",
        },
      },
    ],
    darkTheme: "swordsandravens",
  },
};
