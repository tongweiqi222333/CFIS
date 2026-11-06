<script setup lang="ts">
import { locale, toggleLanguage } from "./i18n";
</script>
<template>
  <button
    class="language-switch"
    :aria-label="locale === 'en' ? 'Switch to Chinese' : 'Switch to English'"
    @click="toggleLanguage"
  >
    {{ locale === "en" ? "中文" : "English" }}
  </button>
</template>
