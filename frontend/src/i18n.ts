import { ref, watch } from "vue";
import english from "./locales/en.json";
export type Language = "zh-CN" | "en";
const storageKey = "cfis.language";
function storedLanguage(): Language {
  try {
    return localStorage.getItem(storageKey) === "en" ? "en" : "zh-CN";
  } catch {
    return "zh-CN";
  }
}
export const locale = ref<Language>(storedLanguage());
const catalog: Record<string, string> = english;
export function t(key: string | null | undefined): string {
  if (key == null) return "—";
  if (locale.value === "zh-CN") return key;
  return catalog[key] ?? catalog[key.trim().replace(/\s+/g, " ")] ?? key;
}
export function localizeMessage(value: string | null | undefined): string {
  if (!value || locale.value === "zh-CN") return value ?? "";
  if (catalog[value]) return catalog[value];
  const trace = value.match(/（追踪号：([^）]+)）/);
  const body = value.replace(/（追踪号：[^）]+）/g, "");
  const translated = t(body);
  if (!/[\u3400-\u9fff]/.test(translated))
    return translated + (trace ? ` (Trace ID: ${trace[1]})` : "");
  return (
    "Unable to complete the request. Check your input and permissions, then try again." +
    (trace ? ` (Trace ID: ${trace[1]})` : "")
  );
}
export function toggleLanguage() {
  locale.value = locale.value === "zh-CN" ? "en" : "zh-CN";
}
watch(
  locale,
  (value) => {
    document.documentElement.lang = value;
    document.documentElement.style.setProperty(
      "--table-scroll-hint",
      JSON.stringify(t("表格可左右滑动查看")),
    );
    document.title =
      value === "en"
        ? "CFIS · Financial workspace"
        : "CFIS · 企业财务信息管理系统";
    try {
      localStorage.setItem(storageKey, value);
    } catch {
      /* Session language remains available when storage is disabled. */
    }
  },
  { immediate: true, flush: "sync" },
);
window.addEventListener("storage", (event) => {
  if (event.key === storageKey)
    locale.value = event.newValue === "en" ? "en" : "zh-CN";
});
