<script setup lang="ts">
import { t, localizeMessage, locale } from "./i18n";
import { ref, defineAsyncComponent, onMounted, computed, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { storeToRefs } from "pinia";
import { useWorkspace } from "./workspace";
import FinancePanel from "./FinancePanel.vue";
import axios from "axios";
import LanguageSwitch from "./LanguageSwitch.vue";
import { ElConfigProvider } from "element-plus";
import enLocale from "element-plus/es/locale/lang/en";
import zhLocale from "element-plus/es/locale/lang/zh-cn";
import AppIcon from "./AppIcon.vue";
import StatusBadge from "./StatusBadge.vue";
import { labelOf, errorText } from "./ui";
const OverviewPanel = defineAsyncComponent(() => import("./OverviewPanel.vue"));

const AuditPanel = defineAsyncComponent(() => import("./AuditPanel.vue"));
const AttachmentPanel = defineAsyncComponent(
  () => import("./AttachmentPanel.vue"),
);
const route = useRoute(),
  router = useRouter();
const { loggedIn, selected, organizations } = storeToRefs(useWorkspace());
const guide = ref(true),
  mobileOpen = ref(false),
  failed = ref(false);
const currentOrg = computed(() =>
  organizations.value.find((x) => x.id === selected.value),
);
const pageTitles: Record<string, [string, string]> = {
  "/": ["工作空间", "掌握资金动态，从这里开始今天的财务工作。"],
  "/finance": ["资金管理", "管理资金账户，登记收支与转账，追溯每一笔流水。"],
  "/business": ["往来与预算", "跟进客户供应商、应收应付及预算执行。"],
  "/reports": ["财务报表", "用一致的统计口径查看趋势、账龄和预算。"],
  "/accounting": ["会计管理", "从来源到凭证，完成过账、核对与期间结账。"],
  "/attachments": ["业务附件", "将原始凭据安全关联到对应业务。"],
  "/settings": ["企业设置", "管理企业、成员权限与账户安全。"],
  "/audit": ["操作审计", "按操作记录追溯业务变化。"],
  "/help": ["使用帮助", "从基础配置到日常办理，一步步熟悉 CFIS。"],
};
const pageMeta = computed(() => pageTitles[route.path] ?? pageTitles["/"]!);
const icons = [
  "home",
  "finance",
  "business",
  "reports",
  "accounting",
  "attachments",
  "settings",
  "audit",
  "help",
];
watch(
  () => route.fullPath,
  () => {
    mobileOpen.value = false;
    message.value = "";
  },
);
const nav = [
  ["/", "工作空间"],
  ["/finance", "资金管理"],
  ["/business", "往来与预算"],
  ["/reports", "财务报表"],
  ["/accounting", "会计管理"],
  ["/attachments", "业务附件"],
  ["/settings", "企业设置"],
  ["/audit", "操作审计"],
  ["/help", "使用帮助"],
];
const BusinessPanel = defineAsyncComponent(() => import("./BusinessPanel.vue"));
const ReportsPanel = defineAsyncComponent(() => import("./ReportsPanel.vue"));
const SecurityPanel = defineAsyncComponent(() => import("./SecurityPanel.vue"));
const AccountingPanel = defineAsyncComponent(
  () => import("./AccountingPanel.vue"),
);

const financeVersion = ref(0);
const api = axios.create({ baseURL: "/api/v1" });
const allowRegistration = ref(false);
onMounted(async () => {
  try {
    allowRegistration.value = (
      await api.get("/auth/options")
    ).data.allowRegistration;
  } catch {
    message.value = "无法读取登录配置，请稍后刷新";
  }
});
let refreshToken = "";
let refreshing: Promise<string> | null = null;
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const config = error.config;
    if (
      error.response?.status !== 401 ||
      !refreshToken ||
      config._retried ||
      config.url?.startsWith("/auth/")
    )
      throw error;
    config._retried = true;
    try {
      refreshing ??= axios
        .post("/api/v1/auth/refresh", { refreshToken })
        .then(({ data }) => {
          refreshToken = data.refreshToken;
          api.defaults.headers.common.Authorization =
            "Bearer " + data.accessToken;
          return data.accessToken;
        })
        .finally(() => {
          refreshing = null;
        });
      config.headers.Authorization = "Bearer " + (await refreshing);
      return await api.request(config);
    } catch (e) {
      refreshToken = "";
      loggedIn.value = false;
      organizations.value = [];
      selected.value = "";
      members.value = [];
      memberEmail.value = "";
      memberRole.value = 3;
      throw e;
    }
  },
);
const email = ref(""),
  password = ref(""),
  name = ref(""),
  message = ref("");
const members = ref<{ email: string; role: string }[]>([]),
  memberEmail = ref(""),
  memberRole = ref(3),
  busy = ref(false);
async function select(id: string) {
  if (!id) return;
  if (!organizations.value.some((x) => x.id === id)) {
    const r = (await api.get("/organizations/" + id)).data;
    organizations.value.push(r);
  }
  selected.value = id;
  await router.replace({ path: route.path, query: { org: id } });
  members.value = [];
  const org = organizations.value.find((x) => x.id === id);
  if (org && ["Owner", "Admin"].includes(org.role)) {
    const result = (await api.get("/organizations/" + id + "/members")).data;
    if (selected.value === id) members.value = result;
  }
}
async function addMember() {
  await api.post("/organizations/" + selected.value + "/members", {
    email: memberEmail.value,
    role: memberRole.value,
  });
  memberEmail.value = "";
  await select(selected.value);
}

async function run(fn: () => Promise<unknown>) {
  if (busy.value) return;
  busy.value = true;
  failed.value = false;
  message.value = "";
  try {
    await fn();
    message.value ||= "操作成功";
  } catch (e) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function load() {
  organizations.value = (
    await api.get("/organizations", { params: { pageSize: 100 } })
  ).data.items;
}
async function login() {
  const { data } = await api.post("/auth/login", {
    email: email.value,
    password: password.value,
  });
  refreshToken = data.refreshToken;
  api.defaults.headers.common.Authorization = "Bearer " + data.accessToken;
  loggedIn.value = true;
  await load();
  const org = String(route.query.org ?? "");
  if (org) {
    try {
      await select(org);
    } catch {
      selected.value = "";
      await router.replace({ path: "/", query: {} });
      message.value = "该企业已不可访问，请重新选择企业。";
    }
  }
}
async function demo() {
  const r = await api.post("/demo");
  await load();
  await select(r.data.id);
}
async function create() {
  if (!name.value.trim()) throw new Error("请填写企业名称");
  await api.post("/organizations", { name: name.value.trim() });
  name.value = "";
  await load();
}
async function logout() {
  await api.post("/session/logout");
  refreshToken = "";
  delete api.defaults.headers.common.Authorization;
  loggedIn.value = false;
  organizations.value = [];
  selected.value = "";
  members.value = [];
  memberEmail.value = "";
  memberRole.value = 3;
  password.value = "";
}
</script>
<template>
  <el-config-provider :locale="locale === 'en' ? enLocale : zhLocale">
    <a href="#page-content" class="skip-link">{{ t("跳到主要内容") }}</a>
    <div v-if="!loggedIn" class="auth-layout">
      <aside class="auth-story">
        <div class="brand">
          <span class="brand-mark">C</span>
          <div>
            <strong>CFIS</strong><small>{{ t("企业财务信息管理系统") }}</small>
          </div>
        </div>
        <div class="auth-intro">
          <span class="eyebrow">YOUR FINANCIAL WORKSPACE</span>
          <h1>{{ t("让每笔账务") }}<br />{{ t("清晰可见。") }}</h1>
          <p>
            {{ t("连接资金、业务与会计，") }}<br />{{
              t("在一个工作空间里掌握财务全貌。")
            }}
          </p>
          <div class="auth-features">
            <div>
              <AppIcon name="finance" /><span>{{ t("资金与业务协同") }}</span>
            </div>
            <div>
              <AppIcon name="accounting" /><span>{{
                t("凭证与来源可追溯")
              }}</span>
            </div>
            <div>
              <AppIcon name="shield" /><span>{{
                t("按企业与角色管理权限")
              }}</span>
            </div>
          </div>
        </div>
        <small class="auth-foot"
          >Corporate Financial Information System · v1.0</small
        >
      </aside>
      <LanguageSwitch class="auth-language-switch" />
      <main id="page-content" class="auth-main">
        <div class="auth-box">
          <span class="eyebrow">{{ t("欢迎使用 CFIS") }}</span>
          <h2>{{ t("登录你的工作空间") }}</h2>
          <p class="muted">
            {{
              allowRegistration
                ? t("使用账户邮箱继续；首次使用可先注册。")
                : t("请使用管理员提供的账户邮箱登录。")
            }}
          </p>
          <el-form label-position="top" @submit.prevent="run(login)"
            ><el-form-item :label="t('邮箱')"
              ><el-input
                :aria-label="t('邮箱')"
                v-model="email"
                type="email"
                autocomplete="username"
                :placeholder="t('请输入账户邮箱')"
                :disabled="busy" /></el-form-item
            ><el-form-item :label="t('密码')"
              ><el-input
                :aria-label="t('密码')"
                v-model="password"
                type="password"
                show-password
                autocomplete="current-password"
                :placeholder="t('请输入密码')"
                :disabled="busy"
            /></el-form-item>
            <p class="field-hint">
              {{ t("注册密码至少12位，包含大小写字母、数字和符号。") }}
            </p>
            <div class="auth-actions">
              <el-button :loading="busy" type="primary" native-type="submit">{{
                t("登录")
              }}</el-button
              ><el-button
                v-if="allowRegistration"
                :disabled="busy"
                @click="
                  run(() => api.post('/auth/register', { email, password }))
                "
                >{{ t("注册") }}</el-button
              >
            </div></el-form
          >
          <p
            role="status"
            class="feedback"
            :class="{ error: failed }"
            aria-live="polite"
          >
            {{ localizeMessage(message) }}
          </p>
          <div class="auth-support">
            <SecurityPanel
              :api="api"
              :logged-in="false"
              organization-id=""
              role="Viewer"
            />
          </div>
        </div>
      </main>
    </div>
    <div
      v-else
      class="app-layout"
      @keydown.esc="mobileOpen = false"
      :class="{ 'nav-open': mobileOpen }"
    >
      <button
        v-if="mobileOpen"
        class="nav-scrim"
        :aria-label="t('关闭导航')"
        @click="mobileOpen = false"
      ></button>
      <aside class="sidebar">
        <RouterLink
          :to="{ path: '/', query: selected ? { org: selected } : {} }"
          class="brand"
          ><span class="brand-mark">C</span>
          <div>
            <strong>CFIS</strong><small>{{ t("企业财务工作台") }}</small>
          </div></RouterLink
        >
        <p class="nav-caption">{{ t("工作空间") }}</p>
        <nav :aria-label="t('主导航')">
          <RouterLink
            v-for="(item, index) in nav"
            :key="item[0]"
            @click="mobileOpen = false"
            :to="{ path: item[0], query: selected ? { org: selected } : {} }"
            ><AppIcon :name="icons[index]!" /><span>{{ t(item[1]) }}</span
            ><span class="nav-indicator"></span
          ></RouterLink>
        </nav>
        <div class="sidebar-footer">
          <span class="online-dot"></span><span>{{ t("本地工作空间") }}</span
          ><small>v1.0</small>
        </div>
      </aside>
      <div class="app-main">
        <header class="topbar">
          <div class="topbar-left">
            <button
              class="icon-button mobile-menu"
              :aria-label="t('打开导航')"
              :aria-expanded="mobileOpen"
              @click="mobileOpen = !mobileOpen"
            >
              <AppIcon name="menu" /></button
            ><span class="breadcrumb"
              >CFIS <span>/</span> {{ t(pageMeta[0]) }}</span
            >
          </div>
          <div class="topbar-right">
            <LanguageSwitch />
            <label class="org-switch"
              ><span>{{ t("当前企业") }}</span
              ><select
                :aria-label="t('切换企业')"
                :value="selected"
                :disabled="busy || !organizations.length"
                @change="
                  run(() => select(($event.target as HTMLSelectElement).value))
                "
              >
                <option value="" disabled>{{ t("请选择企业") }}</option>
                <option
                  v-for="org in organizations"
                  :key="org.id"
                  :value="org.id"
                >
                  {{ org.name }}
                </option>
              </select></label
            ><span v-if="currentOrg" class="role-chip">{{
              labelOf(currentOrg.role)
            }}</span
            ><button
              class="logout-button"
              :disabled="busy"
              @click="run(logout)"
            >
              {{ t("退出登录") }}
            </button>
          </div>
        </header>
        <main id="page-content" class="page-content">
          <div class="page-heading">
            <div>
              <span class="eyebrow">{{
                route.path === "/" ? t("财务工作台") : "CFIS WORKSPACE"
              }}</span>
              <h1>{{ t(pageMeta[0]) }}</h1>
              <p>{{ t(pageMeta[1]) }}</p>
            </div>
            <span v-if="currentOrg" class="context-chip"
              ><span class="online-dot"></span>{{ currentOrg.name }}</span
            >
          </div>
          <p
            role="status"
            class="feedback"
            :class="{ error: failed }"
            aria-live="polite"
          >
            {{ localizeMessage(message) }}
          </p>
          <OverviewPanel
            v-if="selected && route.path === '/'"
            :key="selected"
            :api="api"
            :organization-id="selected"
          />
          <template v-if="route.path === '/' || route.path === '/settings'">
            <div class="workspace-grid">
              <section class="surface enterprise-list">
                <div class="section-heading">
                  <div>
                    <h2>{{ t("我的企业") }}</h2>
                    <p>{{ t("选择企业后开始办理，数据按企业隔离。") }}</p>
                  </div>
                  <span class="count-chip"
                    >{{ organizations.length }} {{ t("家") }}</span
                  >
                </div>
                <el-table
                  :data="organizations"
                  @row-click="
                    (row: { id: string }) => run(() => select(row.id))
                  "
                  highlight-current-row
                  row-key="id"
                  :empty-text="t('暂无企业，请先创建或生成演示账套')"
                  ><el-table-column
                    prop="name"
                    :label="t('企业名称')"
                    min-width="220"
                  /><el-table-column prop="role" :label="t('角色')" width="120"
                    ><template #default="s">{{
                      labelOf(s.row.role)
                    }}</template></el-table-column
                  ></el-table
                >
              </section>
              <section class="surface create-enterprise">
                <h2>{{ t("创建企业") }}</h2>
                <p>{{ t("为新的业务建立独立账套。") }}</p>
                <label class="field"
                  ><span>{{ t("企业名称") }}</span
                  ><el-input
                    v-model="name"
                    maxlength="200"
                    :placeholder="t('企业名称')"
                    :disabled="busy"
                    @keyup.enter="run(create)" /></label
                ><el-button
                  :loading="busy"
                  type="primary"
                  @click="run(create)"
                  >{{ t("创建") }}</el-button
                >
              </section>
            </div>
            <div v-if="currentOrg" class="enterprise-context">
              <h2>{{ t("当前企业：") }}{{ currentOrg.name }}</h2>
              <StatusBadge :value="currentOrg.role" />
            </div>
          </template>
          <template v-if="selected">
            <FinancePanel
              v-if="route.path === '/finance'"
              :key="selected + financeVersion"
              :api="api"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
            />
            <BusinessPanel
              v-if="route.path === '/business'"
              :key="selected"
              :api="api"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
              @changed="financeVersion++"
            />
            <ReportsPanel
              v-if="route.path === '/reports'"
              :key="selected"
              :api="api"
              :organization-id="selected"
            />
            <AccountingPanel
              v-if="route.path === '/accounting'"
              :key="selected"
              :api="api"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
            />
            <AttachmentPanel
              v-if="route.path === '/attachments'"
              :key="selected"
              :api="api"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
            />
            <AuditPanel
              v-if="route.path === '/audit'"
              :key="selected"
              :api="api"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
            />
          </template>
          <section
            v-else-if="!['/', '/settings', '/help'].includes(route.path)"
            class="empty-state surface"
          >
            <AppIcon name="business" />
            <h2>{{ t("先选择一家企业") }}</h2>
            <p>{{ t("业务数据需要在企业工作空间内查看和办理。") }}</p>
            <RouterLink to="/" class="button-link">{{
              t("前往工作空间")
            }}</RouterLink>
          </section>
          <template v-if="route.path === '/settings'"
            ><section
              v-if="
                selected && ['Owner', 'Admin'].includes(currentOrg?.role ?? '')
              "
              class="surface"
            >
              <div class="section-heading">
                <div>
                  <h2>{{ t("企业成员") }}</h2>
                  <p>{{ t("角色调整和移除在后续请求中立即生效。") }}</p>
                </div>
                <span class="count-chip"
                  >{{ members.length }} {{ t("位成员") }}</span
                >
              </div>
              <el-table :data="members" :empty-text="t('暂无成员')"
                ><el-table-column
                  prop="email"
                  :label="t('成员邮箱')"
                /><el-table-column prop="role" :label="t('角色')"
                  ><template #default="s">{{
                    labelOf(s.row.role)
                  }}</template></el-table-column
                ></el-table
              >
              <div class="form-grid">
                <label class="field"
                  ><span>{{ t("已注册账户邮箱") }}</span
                  ><el-input
                    v-model="memberEmail"
                    :placeholder="t('已注册成员邮箱')"
                    :aria-label="t('成员邮箱')"
                    :disabled="busy" /></label
                ><label class="field"
                  ><span>{{ t("成员角色") }}</span
                  ><el-select v-model="memberRole" :disabled="busy"
                    ><el-option :label="t('只读成员')" :value="3" /><el-option
                      :label="t('会计')"
                      :value="2" /><el-option
                      v-if="currentOrg?.role === 'Owner'"
                      :label="t('管理员')"
                      :value="1" /></el-select></label
                ><el-button :loading="busy" @click="run(addMember)">{{
                  t("添加成员")
                }}</el-button>
              </div>
            </section>
            <SecurityPanel
              :key="selected"
              :api="api"
              :logged-in="true"
              :organization-id="selected"
              :role="currentOrg?.role ?? 'Viewer'"
              @changed="
                run(async () => {
                  await load();
                  if (selected) await select(selected);
                })
              "
          /></template>
          <template v-if="route.path === '/'"
            ><section class="surface guide-card" v-if="guide">
              <div class="section-heading">
                <div>
                  <span class="eyebrow">{{ t("快速开始") }}</span>
                  <h2>{{ t("首次使用指南") }}</h2>
                </div>
                <button class="text-button" @click="guide = false">
                  {{ t("暂时收起指南") }}
                </button>
              </div>
              <ol class="guide-steps">
                <li>
                  <b>01</b
                  ><span
                    >{{ t("选择企业")
                    }}<small>{{ t("创建账套或打开演示企业") }}</small></span
                  >
                </li>
                <li>
                  <b>02</b
                  ><span
                    >{{ t("登记业务")
                    }}<small>{{ t("配置账户、分类与往来") }}</small></span
                  >
                </li>
                <li>
                  <b>03</b
                  ><span
                    >{{ t("核对账务")
                    }}<small>{{ t("生成凭证并检查资金差异") }}</small></span
                  >
                </li>
                <li>
                  <b>04</b
                  ><span
                    >{{ t("查看报表")
                    }}<small>{{ t("结账前处理阻塞并备份") }}</small></span
                  >
                </li>
              </ol>
              <el-button :loading="busy" @click="run(demo)">{{
                t("生成或打开演示账套")
              }}</el-button>
            </section>
            <el-button v-else @click="guide = true">{{
              t("打开使用指南")
            }}</el-button>
            <section v-if="selected" class="surface quick-actions">
              <h2>{{ t("快捷办理") }}</h2>
              <div class="action-row">
                <el-button
                  @click="
                    router.push({ path: '/finance', query: { org: selected } })
                  "
                  >{{ t("打开核心财务") }}</el-button
                ><el-button
                  @click="
                    router.push({ path: '/business', query: { org: selected } })
                  "
                  >{{ t("打开业务扩展") }}</el-button
                ><el-button
                  @click="
                    router.push({ path: '/reports', query: { org: selected } })
                  "
                  >{{ t("打开财务报表") }}</el-button
                ><el-button
                  @click="
                    router.push({
                      path: '/accounting',
                      query: { org: selected },
                    })
                  "
                  >{{ t("打开会计引擎") }}</el-button
                >
              </div>
              <p class="muted">
                {{ t("也可以使用左侧导航进入独立业务页面。") }}
              </p>
            </section></template
          >
          <section v-if="route.path === '/help'" class="surface help-page">
            <span class="eyebrow">{{ t("使用指引") }}</span>
            <h2>{{ t("从第一笔业务开始") }}</h2>
            <div class="help-grid">
              <article>
                <h3>{{ t("资金与业务") }}</h3>
                <p>
                  {{
                    t(
                      "金额最多4位小数；转账仅支持同币种。应收应付结算会自动生成资金流水，无需重复登记。",
                    )
                  }}
                </p>
              </article>
              <article>
                <h3>{{ t("更正与冲销") }}</h3>
                <p>
                  {{
                    t(
                      "已登记交易通过撤销更正；已过账凭证只能追加冲销。历史记录始终保留供追溯。",
                    )
                  }}
                </p>
              </article>
              <article>
                <h3>{{ t("分类分摊") }}</h3>
                <p>
                  {{
                    t(
                      "普通收入支出可以分配到多个分类，明细合计须等于总额，资金仅变动一次。",
                    )
                  }}
                </p>
              </article>
              <article>
                <h3>{{ t("附件与备份") }}</h3>
                <p>
                  {{
                    t(
                      "支持5MB以内PDF、PNG和JPEG。附件只追加，随数据库备份；凭证过账后可上传。",
                    )
                  }}
                </p>
              </article>
            </div>
            <p class="note">
              {{
                t(
                  "遇到请求错误，请保留提示中的追踪号，方便管理员定位。完整中文手册随项目交付。",
                )
              }}
            </p>
            <RouterLink
              :to="{ path: '/', query: selected ? { org: selected } : {} }"
              class="button-link"
              >{{ t("返回工作空间") }}</RouterLink
            >
          </section>
        </main>
        <footer class="app-footer">
          <span>{{ t("CFIS · 企业财务信息管理系统") }}</span
          ><span>{{ t("每笔账务，都有依据。") }}</span>
        </footer>
      </div>
    </div>
  </el-config-provider>
</template>
