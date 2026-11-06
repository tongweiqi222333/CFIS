<script setup lang="ts">
import { locale } from "./i18n";
defineProps<{ value?: string | null }>();
function display(value?: string | null) {
  if (!value) return "—";
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString(locale.value === "en" ? "en-GB" : "zh-CN", {
        hour12: false,
      });
}
</script>
<template>
  <time :datetime="value || undefined" :title="value || undefined">{{
    display(value)
  }}</time>
</template>
