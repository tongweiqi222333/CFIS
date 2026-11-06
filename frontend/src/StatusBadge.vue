<script setup lang="ts">
import { labelOf } from "./ui";
defineProps<{ value?: string | null }>();
</script>
<template>
  <span class="status-badge" :class="['status-' + value]">{{
    labelOf(value)
  }}</span>
</template>
