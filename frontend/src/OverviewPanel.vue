<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import { ref, onMounted } from "vue";
import { formatAmount } from "./ui";
import type { AxiosInstance } from "axios";
const props = defineProps<{ api: AxiosInstance; organizationId: string }>();
const currency = ref("CNY"),
  summary = ref<Record<string, string> | null>(null),
  pending = ref<{ pendingCash: number; drafts: number } | null>(null),
  error = ref(""),
  busy = ref(false);
async function load() {
  if (busy.value) return;
  summary.value = null;
  pending.value = null;
  busy.value = true;
  try {
    const root = "/organizations/" + props.organizationId;
    const day = (await props.api.get(root + "/finance-settings")).data.today;
    summary.value = (
      await props.api.get(root + "/reports/summary", {
        params: {
          currency: currency.value,
          dateFrom: day.slice(0, 8) + "01",
          dateTo: day,
        },
      })
    ).data.summary;
    pending.value = (
      await props.api.get(root + "/accounting/setup-check")
    ).data;
    error.value = "";
  } catch (e: any) {
    error.value = e.response?.data?.title ?? e.message;
  } finally {
    busy.value = false;
  }
}
onMounted(load);
</script>
<template>
  <section class="module-panel" :aria-label="t('企业概览')" :aria-busy="busy">
    <h2>{{ t("企业概览") }}</h2>
    <div class="control-row">
      <label
        >{{ t("统计币种")
        }}<el-input v-model="currency" :aria-label="t('概览币种')" /></label
      ><el-button :disabled="busy" @click="load">{{ t("刷新概览") }}</el-button>
    </div>
    <div v-if="summary" class="overview">
      <article>
        <small>{{ t("截至今天资金") }}</small
        ><strong>{{ formatAmount(summary.closing) }}</strong>
      </article>
      <article>
        <small>{{ t("本月净收入") }}</small
        ><strong>{{ formatAmount(summary.income) }}</strong>
      </article>
      <article>
        <small>{{ t("本月净支出") }}</small
        ><strong>{{ formatAmount(summary.expense) }}</strong>
      </article>
      <article v-if="pending">
        <small
          ><RouterLink
            :to="{ path: '/accounting', query: { org: organizationId } }"
            >{{ t("待入账资金 / 草稿凭证 →") }}</RouterLink
          ></small
        ><strong>{{ pending.pendingCash }} / {{ pending.drafts }}</strong>
      </article>
    </div>
    <p>
      {{ t("仅统计所选币种的本月数据。")
      }}<RouterLink
        :to="{ path: '/reports', query: { org: organizationId } }"
        >{{ t("查看完整财务报表 →") }}</RouterLink
      >
    </p>
    <p v-if="error" role="alert" class="feedback error">
      {{ localizeMessage(error) }}
    </p>
  </section>
</template>
<style scoped>
.overview {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 12px;
  margin: 20px 0;
}
.overview article {
  background: white;
  border: 1px solid #dce4ee;
  padding: 20px;
  border-radius: 12px;
}
.overview strong {
  display: block;
  font-size: 23px;
  margin-top: 12px;
  font-variant-numeric: tabular-nums;
}
</style>
