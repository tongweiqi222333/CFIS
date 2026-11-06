<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import { ref, onMounted, watch } from "vue";
import { errorText } from "./ui";
import type { AxiosInstance } from "axios";
const failed = ref(false);
const props = defineProps<{
  api: AxiosInstance;
  loggedIn: boolean;
  organizationId: string;
  role: string;
}>();
const emit = defineEmits<{ changed: [] }>();
const recoveryAvailable = ref(false);
onMounted(async () => {
  try {
    recoveryAvailable.value = (
      await props.api.get("/auth/options")
    ).data.passwordRecoveryAvailable;
  } catch {
    message.value = "无法读取邮件服务状态";
  }
});
const mode = ref(props.loggedIn && props.organizationId ? "invite" : "accept");
watch(mode, () => {
  message.value = "";
  password.value = "";
  oldPassword.value = "";
  code.value = "";
  invitation.value = "";
  issued.value = "";
});
const open = ref(false),
  busy = ref(false),
  message = ref(""),
  email = ref(""),
  password = ref(""),
  oldPassword = ref(""),
  code = ref(""),
  invitation = ref(""),
  issued = ref(""),
  inviteRole = ref(3),
  members = ref<any[]>([]);
async function run(fn: () => Promise<unknown>) {
  if (busy.value) return;
  failed.value = false;
  busy.value = true;
  message.value = "";
  try {
    await fn();
    message.value ||= "操作成功";
    emit("changed");
  } catch (e: any) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function invite() {
  issued.value = "";
  const r = await props.api.post(
    "/organizations/" + props.organizationId + "/invitations",
    { email: email.value, role: inviteRole.value },
  );
  issued.value = r.data.token;
}
async function accept() {
  await props.api.post("/invitations/accept", {
    token: invitation.value,
    password: password.value || undefined,
  });
  password.value = "";
  invitation.value = "";
}
async function loadMembers() {
  members.value = (
    await props.api.get("/organizations/" + props.organizationId + "/members")
  ).data;
}
async function changeMember(m: any) {
  await props.api.patch(
    "/organizations/" + props.organizationId + "/members/" + m.userId + "/role",
    { role: Number(m.nextRole) },
  );
  await loadMembers();
}
async function remove(m: any) {
  if (!window.confirm(t("移除 ") + m.email + t(" 的本企业访问权限？"))) return;
  await props.api.delete(
    "/organizations/" + props.organizationId + "/members/" + m.userId,
  );
  await loadMembers();
}
async function reset() {
  await props.api.post("/auth/resetPassword", {
    email: email.value,
    resetCode: code.value,
    newPassword: password.value,
  });
  password.value = "";
  code.value = "";
}
async function changePassword() {
  await props.api.post("/auth/manage/info", {
    oldPassword: oldPassword.value,
    newPassword: password.value,
  });
  oldPassword.value = "";
  password.value = "";
  message.value = "密码已更新。下次登录请使用新密码。";
}
</script>
<template>
  <section class="security-panel">
    <el-button :aria-expanded="open" @click="open = !open">{{
      t("账户安全与邀请")
    }}</el-button>
    <el-card v-if="open" :aria-label="t('账户安全与邀请')" :aria-busy="busy">
      <h2>{{ t("账户安全与邀请") }}</h2>
      <div class="module-tabs" :aria-label="t('选择安全操作')">
        <button
          v-if="loggedIn && organizationId && ['Owner', 'Admin'].includes(role)"
          :aria-pressed="mode === 'invite'"
          @click="mode = 'invite'"
        >
          {{ t("邀请与权限") }}
        </button>
        <button :aria-pressed="mode === 'accept'" @click="mode = 'accept'">
          {{ t("接受企业邀请") }}
        </button>
        <button :aria-pressed="mode === 'reset'" @click="mode = 'reset'">
          {{ t("找回密码") }}
        </button>
        <button
          v-if="loggedIn"
          :aria-pressed="mode === 'change'"
          @click="mode = 'change'"
        >
          {{ t("修改密码") }}
        </button>
      </div>
      <p v-if="mode === 'invite'">
        {{
          t(
            "为指定邮箱生成邀请代码，有效期 48 小时。请通过可信渠道交付给受邀人。",
          )
        }}
      </p>
      <p v-if="mode === 'accept'">
        {{
          t(
            "已有账户请先登录再接受邀请。新账户请设置密码：至少 12 位，包含大小写字母、数字和符号。",
          )
        }}
      </p>
      <p v-if="mode === 'reset'">
        {{ t("重置后原登录会话失效。")
        }}{{
          recoveryAvailable
            ? t("重置代码将发送至账户邮箱。")
            : t("邮件服务尚未启用，请联系管理员。")
        }}
      </p>
      <p v-if="mode === 'change'">
        {{
          t(
            "请输入原密码和新密码。新密码至少 12 位，包含大小写字母、数字和符号。",
          )
        }}
      </p>
      <div class="form-grid">
        <div class="control-row">
          <label v-if="['invite', 'reset'].includes(mode)" class="field"
            >{{ t("邮箱")
            }}<el-input
              v-model="email"
              :aria-label="t('安全邮箱')"
              placeholder="name@company.com"
              :disabled="busy"
          /></label>
          <label v-if="mode === 'accept'" class="field"
            >{{ t("邀请代码")
            }}<el-input
              v-model="invitation"
              :aria-label="t('邀请代码')"
              :disabled="busy"
          /></label>
          <label v-if="mode === 'change'" class="field"
            >{{ t("原密码")
            }}<el-input
              v-model="oldPassword"
              :aria-label="t('原密码')"
              type="password"
              autocomplete="current-password"
              :disabled="busy"
          /></label>
          <label
            v-if="
              mode === 'change' ||
              mode === 'reset' ||
              (mode === 'accept' && !loggedIn)
            "
            class="field"
            >{{ t("新密码")
            }}<el-input
              v-model="password"
              :aria-label="t('安全新密码')"
              type="password"
              show-password
              autocomplete="new-password"
              :disabled="busy"
          /></label>
          <label v-if="mode === 'reset'" class="field"
            >{{ t("邮件中的重置代码")
            }}<el-input
              v-model="code"
              :aria-label="t('密码重置代码')"
              :disabled="busy"
          /></label>
          <label v-if="mode === 'invite'" class="field"
            >{{ t("成员角色")
            }}<select
              v-model="inviteRole"
              :aria-label="t('邀请角色')"
              :disabled="busy"
            >
              <option :value="3">{{ t("只读成员") }}</option>
              <option :value="2">{{ t("会计") }}</option>
              <option v-if="role === 'Owner'" :value="1">
                {{ t("管理员") }}
              </option>
            </select></label
          >
        </div>
      </div>
      <div class="action-row">
        <el-button
          v-if="mode === 'invite'"
          type="primary"
          :disabled="busy || !email.trim()"
          @click="run(invite)"
          >{{ t("生成邀请代码") }}</el-button
        >
        <el-button
          v-if="mode === 'accept'"
          type="primary"
          :disabled="busy || !invitation.trim()"
          @click="run(accept)"
          >{{ t("接受邀请") }}</el-button
        >
        <template v-if="mode === 'reset'"
          ><el-button
            :disabled="busy || !recoveryAvailable || !email.trim()"
            @click="run(() => api.post('/auth/forgotPassword', { email }))"
            >{{ t("发送重置邮件") }}</el-button
          ><el-button
            type="primary"
            :disabled="busy || !email.trim() || !code || !password"
            @click="run(reset)"
            >{{ t("重置密码") }}</el-button
          ></template
        >
        <el-button
          v-if="mode === 'change'"
          type="primary"
          :disabled="busy || !oldPassword || !password"
          @click="run(changePassword)"
          >{{ t("修改密码") }}</el-button
        >
      </div>
      <p v-if="issued">
        {{ t("请保存并交付以下代码，关闭页面后不再显示：")
        }}<code>{{ issued }}</code>
      </p>
      <template v-if="mode === 'invite'">
        <h3>{{ t("成员权限") }}</h3>
        <p>{{ t("企业所有者权限固定；其他成员的权限调整后立即生效。") }}</p>
        <el-button :disabled="busy" @click="run(loadMembers)">{{
          t("加载成员权限")
        }}</el-button>
        <ul>
          <li v-for="m in members" :key="m.userId">
            {{ m.email }} <StatusBadge :value="m.role" /><template
              v-if="m.role !== 'Owner'"
              ><div class="control-row">
                <select
                  v-model="m.nextRole"
                  :aria-label="m.email + t(' 新成员角色')"
                  :disabled="busy"
                >
                  <option :value="3">{{ t("只读成员") }}</option>
                  <option :value="2">{{ t("会计") }}</option>
                  <option v-if="role === 'Owner'" :value="1">
                    {{ t("管理员") }}
                  </option></select
                ><el-button
                  :disabled="busy || m.nextRole === undefined"
                  @click="run(() => changeMember(m))"
                  >{{ t("调整角色") }}</el-button
                ><el-button :disabled="busy" @click="run(() => remove(m))">{{
                  t("移除成员")
                }}</el-button>
              </div></template
            >
          </li>
        </ul>
      </template>
      <p
        v-if="message"
        role="status"
        class="feedback"
        :class="{ error: failed }"
      >
        {{ localizeMessage(message) }}
      </p>
    </el-card>
  </section>
</template>
