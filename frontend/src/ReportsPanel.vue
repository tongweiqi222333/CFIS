<script setup lang="ts">
import { t, localizeMessage, locale } from "./i18n";
import { ref, onMounted, onBeforeUnmount, nextTick, watch } from "vue";
import { formatAmount } from "./ui";
import type { AxiosInstance } from "axios";
import { use, init, type EChartsType } from "echarts/core";
import { LineChart, BarChart } from "echarts/charts";
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
} from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
use([
  LineChart,
  BarChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  CanvasRenderer,
]);
const props = defineProps<{ api: AxiosInstance; organizationId: string }>();
const from = ref(""),
  to = ref(""),
  currency = ref("CNY"),
  account = ref(""),
  known = ref(""),
  busy = ref(false),
  error = ref(""),
  report = ref<any>(null);
const accounts = ref<any[]>([]),
  search = ref(""),
  trendElement = ref<HTMLElement>(),
  budgetElement = ref<HTMLElement>();
let charts: EChartsType[] = [],
  applied: any = null,
  observer: ResizeObserver | undefined;
const root = "/organizations/" + props.organizationId;
async function lookup() {
  accounts.value = (
    await props.api.get(root + "/accounts", {
      params: { search: search.value || undefined, pageSize: 100 },
    })
  ).data.items;
}
async function load() {
  if (busy.value) return;
  busy.value = true;
  error.value = "";
  try {
    const params = {
      dateFrom: from.value,
      dateTo: to.value,
      currency: currency.value,
      accountId: account.value || undefined,
      knowledgeAt: known.value
        ? new Date(known.value).toISOString()
        : undefined,
    };
    const response = await props.api.get(root + "/reports/summary", { params });
    report.value = response.data;
    applied = params;
    await nextTick();
    draw();
    observer?.disconnect();
    observer ??= new ResizeObserver(() => charts.forEach((c) => c.resize()));
    if (trendElement.value) observer.observe(trendElement.value);
    if (budgetElement.value) observer.observe(budgetElement.value);
  } catch (e: any) {
    report.value = null;
    applied = null;
    charts.forEach((c) => c.dispose());
    charts = [];
    error.value = e.response?.data?.title ?? e.message;
  } finally {
    busy.value = false;
  }
}
function draw() {
  charts.forEach((c) => c.dispose());
  charts = [];
  if (!report.value) return;
  const r = report.value;
  const tooltip = (params: any) =>
    params.map((p: any) => p.seriesName + ": " + p.data.exact).join("<br>");
  if (trendElement.value) {
    const chart = init(trendElement.value);
    chart.setOption({
      tooltip: { trigger: "axis", formatter: tooltip },
      legend: { data: ["净收入", "净支出", "收支净额", "资金净变动"].map(t) },
      grid: { left: 80, right: 30, bottom: 50 },
      xAxis: { type: "category", data: r.trend.map((x: any) => x.date) },
      yAxis: { type: "value" },
      series: [
        ["净收入", "income"],
        ["净支出", "expense"],
        ["收支净额", "net"],
        ["资金净变动", "cashNet"],
      ].map(([name, key]) => ({
        name: t(name),
        type: "line",
        showSymbol: r.trend.length === 1,
        symbolSize: 8,
        data: r.trend.map((x: any) => ({
          value: Number(x[key!]),
          exact: x[key!],
        })),
      })),
    });
    charts.push(chart);
  }
  if (budgetElement.value) {
    const chart = init(budgetElement.value);
    chart.setOption({
      tooltip: { trigger: "axis", formatter: tooltip },
      legend: { data: ["完整预算", "期间实际"].map(t) },
      grid: { left: 80, right: 30, bottom: 60 },
      xAxis: {
        type: "category",
        data: r.budgets.map((x: any) => x.name),
      },
      yAxis: { type: "value" },
      series: [
        ["完整预算", "planned"],
        ["期间实际", "actual"],
      ].map(([name, key]) => ({
        name: t(name),
        type: "bar",
        data: r.budgets.map((x: any) => ({
          value: Number(x[key!]),
          exact: x[key!],
        })),
      })),
    });
    charts.push(chart);
  }
}
async function download(format: string) {
  if (!applied) return;
  busy.value = true;
  error.value = "";
  try {
    const response = await props.api.get(root + "/reports/export." + format, {
      params: applied,
      responseType: "blob",
    });
    const url = URL.createObjectURL(response.data),
      link = document.createElement("a");
    link.href = url;
    link.download = "CFIS-report." + format;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (e: any) {
    if (e.response?.data instanceof Blob) {
      try {
        error.value = JSON.parse(await e.response.data.text()).title;
      } catch {
        error.value = "导出失败";
      }
    } else error.value = e.message;
  } finally {
    busy.value = false;
  }
}
onMounted(async () => {
  try {
    const s = (await props.api.get(root + "/finance-settings")).data;
    to.value = s.today;
    from.value = s.today.slice(0, 8) + "01";
    await lookup();
    await load();
  } catch (e: any) {
    error.value = e.message;
  }
});
watch(locale, async () => {
  await nextTick();
  draw();
});
onBeforeUnmount(() => {
  observer?.disconnect();
  charts.forEach((c) => c.dispose());
});
const labels: Record<string, string> = {
  opening: "期初余额",
  periodOpening: "期间期初登记",
  income: "净收入",
  expense: "净支出",
  transferIn: "转入",
  transferOut: "转出",
  closing: "截至日期余额",
  allRegistered: "全部已登记余额（含未来日期）",
};

function jump(id: string) {
  const target = document.getElementById(id);
  target?.scrollIntoView({ behavior: "smooth", block: "start" });
  target?.setAttribute("tabindex", "-1");
  target?.focus({ preventScroll: true });
}
</script>
<template>
  <section class="module-panel" :aria-label="t('财务报表')" :aria-busy="busy">
    <h2>{{ t("财务总览与报表") }}</h2>
    <nav class="section-nav" :aria-label="t('本页导航')">
      <a href="#reportspanel-0" @click.prevent="jump('reportspanel-0')">{{
        t("收支趋势")
      }}</a
      ><a href="#reportspanel-1" @click.prevent="jump('reportspanel-1')">{{
        t("分类收支（含分摊及冲销）")
      }}</a
      ><a href="#reportspanel-2" @click.prevent="jump('reportspanel-2')">{{
        t("企业应收应付账龄")
      }}</a
      ><a href="#reportspanel-3" @click.prevent="jump('reportspanel-3')">{{
        t("启用预算执行")
      }}</a>
    </nav>
    <p>
      {{
        t(
          "资金流量口径。不同币种分别查看；收支净额表示资金收支差额。日期最多731天。",
        )
      }}
    </p>
    <div class="control-row">
      <label
        >{{ t("开始")
        }}<input
          v-model="from"
          type="date"
          :aria-label="t('报表开始日期')" /></label
      ><label
        >{{ t("截止")
        }}<input
          v-model="to"
          type="date"
          :aria-label="t('报表截止日期')" /></label
      ><label class="control-field"
        ><span>{{ t("报表币种") }}</span
        ><el-input v-model="currency" :aria-label="t('报表币种')" maxlength="3"
      /></label>
      <label class="control-field"
        ><span>{{ t("报表账户搜索") }}</span
        ><el-input v-model="search" :aria-label="t('报表账户搜索')" /></label
      ><el-button :disabled="busy" @click="lookup">{{
        t("搜索报表账户")
      }}</el-button
      ><label class="control-field"
        ><span>{{ t("报表账户") }}</span
        ><select v-model="account" :aria-label="t('报表账户')">
          <option value="">{{ t("企业全部账户") }}</option>
          <option
            v-for="a in accounts.filter((x) => x.currency === currency)"
            :key="a.id"
            :value="a.id"
          >
            {{ a.name }}
          </option>
        </select></label
      >
      <label
        >{{ t("当时已知记录截止（留空按当前记录重算）")
        }}<input
          v-model="known"
          type="datetime-local"
          :aria-label="t('报表记录截止')" /></label
      ><el-button type="primary" :loading="busy" @click="load">{{
        t("查询报表")
      }}</el-button>
    </div>
    <p v-if="error" role="alert" class="feedback error">
      {{ localizeMessage(error) }}
    </p>
    <template v-if="report"
      ><p>
        {{ t("结果：") }}{{ report.organization }} · {{ report.currency }} ·
        {{ report.dateFrom }} {{ t("至") }} {{ report.dateTo
        }}{{ t("。记录截止") }} <TimeText :value="report.knowledgeAt" />{{
          t("；生成")
        }}
        <TimeText :value="report.generatedAt" />{{
          t("。导出使用本次已查询条件。")
        }}
      </p>
      <dl class="metrics">
        <template v-for="(value, key) in report.summary" :key="key"
          ><div>
            <dt>{{ t(labels[key] ?? key) }}</dt>
            <dd>{{ formatAmount(String(value)) }}</dd>
          </div></template
        >
      </dl>
      <p>
        {{
          t(
            "期初＋期间期初登记＋净收入－净支出＋转入－转出＝期末。企业内部转账在企业整体汇总中抵消。",
          )
        }}
      </p>
      <h3 class="section-heading" id="reportspanel-0">{{ t("收支趋势") }}</h3>
      <div
        ref="trendElement"
        class="chart"
        role="img"
        :aria-label="t('收支趋势图')"
      ></div>
      <details>
        <summary>{{ t("查看精确趋势数据") }}</summary>
        <el-table
          :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
          :data="report.trend"
          ><el-table-column
            prop="date"
            :label="t('日期')"
            min-width="115" /><el-table-column
            prop="income"
            :label="t('净收入')"
            min-width="115" /><el-table-column
            prop="expense"
            :label="t('净支出')"
            min-width="115" /><el-table-column
            prop="net"
            :label="t('收支净额')"
            min-width="115" /><el-table-column
            prop="cashNet"
            :label="t('资金净变动')"
            min-width="115"
        /></el-table>
      </details>
      <h3 class="section-heading" id="reportspanel-1">
        {{ t("分类收支（含分摊及冲销）") }}
      </h3>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="report.categories"
        ><el-table-column
          prop="name"
          :label="t('分类')"
          min-width="115" /><el-table-column :label="t('方向')" min-width="115"
          ><template #default="scope"
            ><StatusBadge :value="scope.row.kind" /></template></el-table-column
        ><el-table-column prop="amount" :label="t('精确净额')" min-width="115"
      /></el-table>
      <h3 class="section-heading" id="reportspanel-2">
        {{ t("企业应收应付账龄") }}
      </h3>
      <p v-if="!report.aging.length">
        {{ t("账户筛选时不展示企业单据账龄。") }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="report.aging"
        ><el-table-column :label="t('单据类型')" min-width="115"
          ><template #default="scope"
            ><StatusBadge :value="scope.row.type" /></template></el-table-column
        ><el-table-column :label="t('逾期区间')" min-width="115"
          ><template #default="scope"
            ><StatusBadge
              :value="scope.row.bucket" /></template></el-table-column
        ><el-table-column prop="amount" :label="t('未结金额')" min-width="165"
      /></el-table>
      <h3 class="section-heading" id="reportspanel-3">
        {{ t("启用预算执行") }}
      </h3>
      <p>
        {{
          t(
            "只展示记录截止时启用的版本。计划为完整预算，实际为预算期间与报表期间交集。草稿预览和关闭版本请在业务扩展查看。",
          )
        }}
      </p>
      <div
        ref="budgetElement"
        class="chart"
        role="img"
        :aria-label="t('预算执行图')"
      ></div>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="report.budgets"
        ><el-table-column
          prop="name"
          :label="t('预算名称')"
          min-width="115" /><el-table-column
          prop="id"
          :label="t('版本ID')"
          min-width="165" /><el-table-column
          prop="previousVersionId"
          :label="t('前版本')"
          min-width="115" /><el-table-column
          :label="t('生效时间')"
          min-width="170"
          ><template #default="scope"
            ><TimeText
              :value="scope.row.activatedAt" /></template></el-table-column
        ><el-table-column
          prop="planned"
          :label="t('完整预算')"
          min-width="115" /><el-table-column
          prop="actual"
          :label="t('期间实际')"
          min-width="165"
      /></el-table>
      <el-button :disabled="busy" @click="download('xlsx')">{{
        t("导出财务报表 Excel")
      }}</el-button
      ><el-button :disabled="busy" @click="download('pdf')">{{
        t("导出财务报表 PDF")
      }}</el-button></template
    >
  </section>
</template>
<style scoped>
.chart {
  height: 330px;
  width: 100%;
  min-width: 0;
}
.metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}
.metrics div {
  background: #fff;
  padding: 16px;
  border: 1px solid #dce4ee;
  border-radius: 8px;
}
dd {
  margin: 10px 0 0;
  font-size: 20px;
  font-variant-numeric: tabular-nums;
}
input,
select {
  margin: 8px;
  padding: 8px;
}
</style>
