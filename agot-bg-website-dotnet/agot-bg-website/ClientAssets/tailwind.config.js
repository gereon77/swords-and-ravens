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
          // "Swords and Ravens" dark theme: a neutral charcoal-grey base (matching the previous
          // Django/Bootstrap dark theme testers were used to, since the original brown/wood-toned
          // base colors read as too dark/muddy), a steel-blue primary accent for default buttons
          // (kept visually distinct from the Discord "blurple" (#5865F2) and Ko-fi cyan-blue
          // (#29abe0) brand buttons rendered inline on Games/MyGames), aged crown-gold as the
          // secondary accent, dragon-fire crimson reserved for errors/danger, and a cold
          // Valyrian-steel/Stark blue-grey as the info accent.
          primary: "#375a7f",
          "primary-content": "#eef5fb",
          secondary: "#444444",
          "secondary-content": "#eef5fb",
          accent: "#4c6b8a",
          "accent-content": "#f5e9d9",
          neutral: "#2b2f33",
          "neutral-content": "#d8dadc",
          "base-100": "#222222",
          "base-200": "#303030",
          "base-300": "#444444",
          "base-content": "#e8e6e3",
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
