<script setup lang="ts">
import { t, localizeMessage, locale } from "./i18n";
import { ref, watch, onMounted } from "vue";
import { errorText } from "./ui";
import type { AxiosInstance } from "axios";
type Item = {
  id: string;
  name: string;
  size: number;
  sha256: string;
  createdAt: string;
};
const failed = ref(false);
const props = defineProps<{
  api: AxiosInstance;
  organizationId: string;
  role: string;
  resourceKind?: string;
  resourceId?: string;
}>();
const kind = ref(props.resourceKind ?? "Transaction"),
  id = ref(props.resourceId ?? ""),
  items = ref<Item[]>([]),
  file = ref<File | null>(null),
  busy = ref(false),
  message = ref("");
const fileInput = ref<HTMLInputElement>();
const queried = ref(false);
const base = () => "/organizations/" + props.organizationId + "/attachments";
async function run(fn: () => Promise<void>) {
  if (busy.value) return;
  message.value = "";
  failed.value = false;
  busy.value = true;
  try {
    await fn();
    message.value = "操作成功";
  } catch (e: any) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function load() {
  items.value = [];
  queried.value = false;
  const key = kind.value + id.value;
  const result = (
    await props.api.get(base(), {
      params: { resourceKind: kind.value, resourceId: id.value },
    })
  ).data;
  if (key !== kind.value + id.value) return;
  items.value = result;
  queried.value = true;
}
async function upload() {
  if (!file.value) throw new Error("请选择文件");
  if (file.value.size > 5 * 1024 * 1024)
    throw new Error("文件超过 5MB，请压缩后重新选择");
  if (!/\.(pdf|png|jpe?g)$/i.test(file.value.name))
    throw new Error("请选择 PDF、PNG 或 JPEG 文件");
  const body = new FormData();
  body.append("resourceKind", kind.value);
  body.append("resourceId", id.value);
  body.append("file", file.value);
  await props.api.post(base(), body);
  file.value = null;
  if (fileInput.value) fileInput.value.value = "";
  await load();
}
async function download(item: Item) {
  const response = await props.api.get(base() + "/" + item.id, {
    responseType: "blob",
  });
  const url = URL.createObjectURL(response.data);
  const a = document.createElement("a");
  a.href = url;
  a.download = item.name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
watch(
  () => [props.resourceId, props.resourceKind],
  () => {
    id.value = props.resourceId ?? "";
    kind.value = props.resourceKind ?? "Transaction";
  },
);
watch([kind, id], () => {
  items.value = [];
  queried.value = false;
  message.value = "";
  if (props.resourceId)
    void load().catch((e) => {
      message.value = e.response?.data?.title ?? e.message;
    });
});
onMounted(() => {
  if (props.resourceId) void run(load);
});
</script>
<template>
  <section
    class="module-panel attachments"
    :aria-busy="busy"
    :aria-label="t('业务附件')"
  >
    <h3>{{ t("业务附件") }}</h3>
    <p>
      {{
        t(
          "每个文件最多5MB，支持PDF、PNG、JPEG。附件追加保存，不能覆盖或删除历史文件；更正时上传新文件。",
        )
      }}
    </p>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("附件业务类型") }}</span
        ><select
          v-model="kind"
          :aria-label="t('附件业务类型')"
          :disabled="busy || !!resourceKind"
        >
          <option value="Transaction">{{ t("资金流水") }}</option>
          <option value="Receivable">{{ t("应收单") }}</option>
          <option value="Payable">{{ t("应付单") }}</option>
          <option value="Journal">{{ t("会计凭证") }}</option>
        </select></label
      ><label class="control-field"
        ><span>{{ t("附件业务编号") }}</span
        ><el-input
          v-model="id"
          :aria-label="t('附件业务编号')"
          :placeholder="t('业务记录编号')"
          :disabled="busy || !!resourceId" /></label
      ><el-button :disabled="busy || !id" @click="run(load)">{{
        t("查询附件")
      }}</el-button>
    </div>
    <template v-if="['Owner', 'Accountant'].includes(role)"
      ><div class="control-row">
        <div class="control-field">
          <span>{{ t("选择附件") }}</span
          ><input
            ref="fileInput"
            type="file"
            class="visually-hidden-file"
            accept=".pdf,.png,.jpg,.jpeg"
            :aria-label="t('选择附件')"
            :disabled="busy"
            @change="
              file = ($event.target as HTMLInputElement).files?.[0] ?? null
            "
          /><button
            type="button"
            class="file-picker"
            :aria-label="t('选择文件')"
            :disabled="busy"
            @click="fileInput?.click()"
          >
            {{ file?.name || t("请选择文件") }}
          </button>
        </div>
        <el-button
          type="primary"
          :disabled="busy || !id || !file"
          @click="run(upload)"
          >{{ t("上传附件") }}</el-button
        >
      </div></template
    >
    <p v-if="queried && !items.length" class="empty-note">
      {{ t("该业务暂无附件，可上传原始凭据。") }}
    </p>
    <ul>
      <li v-for="item in items" :key="item.id">
        <button :disabled="busy" @click="run(() => download(item))">
          {{ item.name }}
        </button>
        · {{ (item.size / 1024).toFixed(1) }} KB ·
        {{
          new Date(item.createdAt).toLocaleString(
            locale === "en" ? "en-GB" : "zh-CN",
          )
        }}
      </li>
    </ul>
    <p v-if="message" role="status" class="feedback" :class="{ error: failed }">
      {{ localizeMessage(message) }}
    </p>
  </section>
</template>
