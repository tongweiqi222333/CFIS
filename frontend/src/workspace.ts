import { defineStore } from "pinia";
import { ref } from "vue";
export type Organization = { id: string; name: string; role: string };
export const useWorkspace = defineStore("workspace", () => {
  const loggedIn = ref(false),
    selected = ref(""),
    organizations = ref<Organization[]>([]);
  return { loggedIn, selected, organizations };
});
