<script setup lang="ts">
defineProps<{ name: string }>();
const paths: Record<string, string> = {
  home: "M3 10 12 3l9 7v10H3z M9 20v-7h6v7",
  finance: "M3 7h18v13H3z M3 7l14-4v4 M16 12h5v4h-5z",
  business: "M8 6V3h8v3 M3 6h18v14H3z M3 11h18 M10 11v3h4v-3",
  reports: "M4 3v18h17 M8 16v-5 M13 16V6 M18 16v-8",
  accounting: "M5 3h14v18H5z M8 7h8 M8 11h8 M8 15h3 M14 15h2 M8 18h3",
  attachments: "m9 13 6-6a3 3 0 0 1 4 4l-8 8a5 5 0 0 1-7-7l9-9",
  settings:
    "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M5 5l2 2 M17 17l2 2 M5 19l2-2 M17 7l2-2",
  audit: "M6 3h12v18H6z M9 7h6 M9 11h6 M9 15l2 2 4-4",
  help: "M9 8a3 3 0 1 1 5 2c-2 1-2 2-2 3 M12 17h.01 M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20",
  menu: "M4 6h16 M4 12h16 M4 18h16",
  arrow: "m9 5 7 7-7 7",
  shield: "m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6z M8 12l3 3 5-6",
};
</script>
<template>
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path :d="paths[name] ?? paths.home" />
  </svg>
</template>
