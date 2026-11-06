<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import { ref, onMounted } from "vue";
import { auditActions } from "./ui";
import type { AxiosInstance } from "axios";
type Audit = {
  id: string;
  actorId: string;
  action: string;
  resourceId: string;
  detail: string;
  createdAt: string;
};
const props = defineProps<{
  api: AxiosInstance;
  organizationId: string;
  role: string;
}>();
const rows = ref<Audit[]>([]),
  page = ref(1),
  total = ref(0),
  action = ref(""),
  busy = ref(false),
  message = ref("");
async function load() {
  if (busy.value) return;
  busy.value = true;
  try {
    const r = (
      await props.api.get(
        "/organizations/" + props.organizationId + "/audit-logs",
        { params: { page: page.value, action: action.value || undefined } },
      )
    ).data;
    rows.value = r.items;
    total.value = r.total;
    message.value = "";
  } catch (e: any) {
    rows.value = [];
    total.value = 0;
    message.value = e.response?.data?.title ?? e.message;
  } finally {
    busy.value = false;
  }
}
onMounted(() => {
  if (["Owner", "Admin"].includes(props.role)) load();
});
</script>
<template>
  <section class="module-panel" :aria-busy="busy" :aria-label="t('操作审计')">
    <h2>{{ t("操作审计") }}</h2>
    <p>
      {{
        t(
          "记录企业内的操作人、业务对象与变更内容。展开详情可查看完整审计数据。",
        )
      }}
    </p>
    <template v-if="['Owner', 'Admin'].includes(role)"
      ><div class="control-row">
        <label class="control-field"
          ><span>{{ t("审计动作") }}</span
          ><select
            v-model="action"
            :aria-label="t('审计动作')"
            :disabled="busy"
          >
            <option value="">{{ t("全部操作类型") }}</option>
            <option
              v-for="(title, key) in auditActions"
              :key="key"
              :value="key"
            >
              {{ t(title) }}
            </option>
          </select></label
        ><el-button
          :disabled="busy"
          @click="
            page = 1;
            load();
          "
          >{{ t("查询审计") }}</el-button
        >
      </div>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="rows"
        ><el-table-column :label="t('时间')" min-width="170"
          ><template #default="scope"
            ><TimeText
              :value="scope.row.createdAt" /></template></el-table-column
        ><el-table-column :label="t('操作')" min-width="150"
          ><template #default="scope">{{
            t(auditActions[scope.row.action] ?? scope.row.action)
          }}</template></el-table-column
        ><el-table-column
          prop="actorId"
          :label="t('操作人编号')"
          min-width="165"
        /><el-table-column
          prop="resourceId"
          :label="t('业务编号')"
          min-width="165"
        /><el-table-column :label="t('详情')" min-width="160"
          ><template #default="scope"
            ><details>
              <summary>{{ t("查看变更详情") }}</summary>
              <pre>{{ scope.row.detail }}</pre>
            </details></template
          ></el-table-column
        ></el-table
      ><el-button
        :disabled="busy || page === 1"
        @click="
          page--;
          load();
        "
        >{{ t("上一页") }}</el-button
      ><el-button
        :disabled="busy || page * 20 >= total"
        @click="
          page++;
          load();
        "
        >{{ t("下一页") }}</el-button
      ><span class="pagination-summary"
        >{{ t("第") }} {{ page }} {{ t("页 · 共") }} {{ total }}
        {{ t("条") }}</span
      ></template
    >
    <p v-else>{{ t("仅企业所有者和管理员可查看审计。") }}</p>
    <p v-if="message" role="status" class="feedback error">
      {{ localizeMessage(message) }}
    </p>
  </section>
</template>
