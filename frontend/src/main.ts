import { createApp } from "vue";
import { createPinia } from "pinia";
import {
  createRouter,
  createWebHistory,
  type RouteRecordRaw,
} from "vue-router";
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElTable,
  ElTableColumn,
  ElSelect,
  ElOption,
} from "element-plus";
import "element-plus/dist/index.css";
import App from "./App.vue";
import "./theme.css";
import StatusBadge from "./StatusBadge.vue";
import TimeText from "./TimeText.vue";
const app = createApp(App);
app.component("TimeText", TimeText);
app.component("StatusBadge", StatusBadge);
for (const component of [
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElTable,
  ElTableColumn,
  ElSelect,
  ElOption,
])
  app.component(component.name!, component);
app
  .use(createPinia())
  .use(
    createRouter({
      history: createWebHistory(),
      scrollBehavior(to, from, saved) {
        return saved ?? (to.path !== from.path ? { top: 0 } : false);
      },
      routes: [
        ...[
          "/",
          "/finance",
          "/business",
          "/reports",
          "/accounting",
          "/attachments",
          "/settings",
          "/audit",
          "/help",
        ].map((path) => ({ path, component: App })),
        { path: "/:pathMatch(.*)*", redirect: "/" },
      ] as RouteRecordRaw[],
    }),
  )
  .mount("#app");
