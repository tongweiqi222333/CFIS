<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import AttachmentPanel from "./AttachmentPanel.vue";
import { ref, onMounted, computed } from "vue";
import { errorText, units, unitText } from "./ui";
import type { AxiosInstance } from "axios";
const failed = ref(false);
const props = defineProps<{
  api: AxiosInstance;
  organizationId: string;
  role: string;
}>();
const root = "/organizations/" + props.organizationId,
  base = root + "/accounting";
const setup = ref<any>(null),
  batchResults = ref<any[]>([]),
  traceKind = ref("Transaction"),
  traceId = ref(""),
  blockerPeriod = ref(""),
  blockerKind = ref("Transaction"),
  blockers = ref<any>(null),
  blockerPage = ref(1);
const kindLabels: Record<string, string> = {
  Asset: "资产",
  Liability: "负债",
  Equity: "权益",
  Revenue: "收入",
  Expense: "费用",
};
const mappingLabels: Record<string, string> = {
  FinancialAccount: "资金账户",
  Category: "收支分类",
  Opening: "期初权益",
  Receivable: "应收控制",
  Payable: "应付控制",
};
const canWrite = ["Owner", "Accountant"].includes(props.role),
  busy = ref(false),
  message = ref("");
const accounts = ref<any[]>([]),
  accountTotal = ref(0),
  accountPage = ref(1),
  search = ref(""),
  periods = ref<any[]>([]),
  periodTotal = ref(0),
  periodPage = ref(1),
  journals = ref<any[]>([]),
  journalPage = ref(1),
  journalTotal = ref(0),
  status = ref(""),
  selected = ref<any>(null);
const code = ref(""),
  name = ref(""),
  kind = ref("Asset"),
  currency = ref("CNY"),
  parentId = ref(""),
  periodCode = ref(""),
  start = ref(""),
  end = ref(""),
  date = ref(""),
  description = ref("");
const lines = ref([
    { ledgerAccountId: "", debit: "", credit: "0" },
    { ledgerAccountId: "", debit: "0", credit: "" },
  ]),
  editing = ref(false);
const draftDebit = computed(() =>
  lines.value.reduce(
    (total, line) => total + (units(line.debit || "0") ?? 0n),
    0n,
  ),
);
const draftCredit = computed(() =>
  lines.value.reduce(
    (total, line) => total + (units(line.credit || "0") ?? 0n),
    0n,
  ),
);
const draftValid = computed(() =>
  lines.value.every(
    (line) =>
      units(line.debit || "0") !== null && units(line.credit || "0") !== null,
  ),
);
const mapKind = ref("FinancialAccount"),
  mapResource = ref(""),
  mapLedger = ref(""),
  resourceSearch = ref(""),
  resources = ref<any[]>([]),
  mappings = ref<any[]>([]);
const sourceKind = ref("Transaction"),
  sources = ref<any[]>([]),
  sourcePage = ref(1),
  sourceTotal = ref(0),
  reason = ref(""),
  reverseDate = ref(""),
  trial = ref<any>(null),
  reconciliation = ref<any>(null),
  ledger = ref<any>(null),
  ledgerId = ref(""),
  ledgerPage = ref(1);
let pending: { payload: string; id: string } | null = null;
async function load() {
  setup.value = (await props.api.get(base + "/setup-check")).data;
  const a = (
    await props.api.get(base + "/accounts", {
      params: {
        page: accountPage.value,
        pageSize: 100,
        search: search.value || undefined,
      },
    })
  ).data;
  accounts.value = a.items;
  accountTotal.value = a.total;
  const p = (
    await props.api.get(base + "/periods", {
      params: { page: periodPage.value },
    })
  ).data;
  periods.value = p.items;
  periodTotal.value = p.total;
  const j = (
    await props.api.get(base + "/journals", {
      params: { page: journalPage.value, status: status.value || undefined },
    })
  ).data;
  journals.value = j.items;
  journalTotal.value = j.total;
  const s = (
    await props.api.get(base + "/sources", {
      params: { kind: sourceKind.value, page: sourcePage.value },
    })
  ).data;
  sources.value = s.items;
  sourceTotal.value = s.total;
  mappings.value = (
    await props.api.get(base + "/mappings", { params: { pageSize: 100 } })
  ).data.items;
  if (selected.value) await detail(selected.value.entry.id);
}
async function run(fn: () => Promise<unknown>) {
  if (busy.value) return;
  message.value = "";
  failed.value = false;
  busy.value = true;
  try {
    await fn();
    await load();
    message.value = "操作成功";
  } catch (e: any) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function loadResources() {
  if (["FinancialAccount", "Category"].includes(mapKind.value))
    resources.value = (
      await props.api.get(
        root +
          (mapKind.value === "FinancialAccount" ? "/accounts" : "/categories"),
        {
          params: { pageSize: 100, search: resourceSearch.value || undefined },
        },
      )
    ).data.items;
  else resources.value = [];
}
async function createAccount() {
  await props.api.post(base + "/accounts", {
    code: code.value,
    name: name.value,
    kind: kind.value,
    currency: currency.value,
    parentId: parentId.value || null,
  });
  code.value = "";
  name.value = "";
}
async function createPeriod() {
  await props.api.post(base + "/periods", {
    code: periodCode.value,
    startDate: start.value,
    endDate: end.value,
  });
}
async function closePeriod(p: any) {
  const check = (await props.api.get(base + "/periods/" + p.id + "/precheck"))
    .data;
  if (!check.canClose) {
    throw new Error(
      "结账检查未通过：草稿" +
        check.drafts +
        "、待入账资金" +
        check.missingCash +
        "、应收" +
        check.missingReceivables +
        "、应付" +
        check.missingPayables +
        "、未映射账户" +
        check.unmappedAccounts +
        "、对账差异" +
        check.differences,
    );
  }
  if (
    !window.confirm(
      t("关闭期间后禁止该期间新增业务；不提供重新开放接口。确认关闭 ") +
        p.code +
        "？",
    )
  )
    return;
  await props.api.post(base + "/periods/" + p.id + "/close");
}
async function setMapping() {
  await props.api.put(base + "/mappings", {
    kind: mapKind.value,
    resourceId: ["FinancialAccount", "Category"].includes(mapKind.value)
      ? mapResource.value
      : "00000000-0000-0000-0000-000000000000",
    currency: currency.value,
    ledgerAccountId: mapLedger.value,
  });
}
async function detail(id: string) {
  selected.value = (await props.api.get(base + "/journals/" + id)).data;
}
async function saveDraft() {
  const body = {
    businessDate: date.value,
    currency: currency.value,
    description: description.value,
    lines: lines.value,
  };
  const payload = JSON.stringify(body);
  if (editing.value) {
    await props.api.put(base + "/journals/" + selected.value.entry.id, {
      ...body,
      version: selected.value.entry.version,
    });
    editing.value = false;
  } else {
    if (pending?.payload !== payload)
      pending = { payload, id: crypto.randomUUID() };
    const r = await props.api.post(base + "/journals", {
      ...body,
      requestId: pending.id,
    });
    pending = null;
    await detail(r.data.id);
  }
}
function editDraft() {
  editing.value = true;
  date.value = selected.value.entry.businessDate;
  description.value = selected.value.entry.description;
  currency.value = selected.value.entry.currency;
  lines.value = selected.value.lines.map((x: any) => ({
    ledgerAccountId: x.ledgerAccountId,
    debit: x.debit,
    credit: x.credit,
  }));
}
async function post() {
  await props.api.post(
    base + "/journals/" + selected.value.entry.id + "/post",
    { version: selected.value.entry.version },
  );
}
async function removeDraft() {
  if (!window.confirm(t("删除未过账草稿？审计记录会保留。"))) return;
  await props.api.delete(base + "/journals/" + selected.value.entry.id);
  selected.value = null;
  editing.value = false;
}
let reverseRequest: { payload: string; id: string } | null = null;
async function reverse() {
  const body = { businessDate: reverseDate.value, reason: reason.value };
  const payload = selected.value.entry.id + JSON.stringify(body);
  if (reverseRequest?.payload !== payload)
    reverseRequest = { payload, id: crypto.randomUUID() };
  await props.api.post(
    base + "/journals/" + selected.value.entry.id + "/reverse",
    { ...body, requestId: reverseRequest.id },
  );
  reverseRequest = null;
}
async function loadBlockers() {
  blockers.value = (
    await props.api.get(
      base + "/periods/" + blockerPeriod.value + "/blockers",
      { params: { kind: blockerKind.value, page: blockerPage.value } },
    )
  ).data;
}
async function trace() {
  const r = await props.api.get(base + "/source-journal", {
    params: { kind: traceKind.value, sourceId: traceId.value },
  });
  await detail(r.data.id);
}
async function batchImport() {
  if (
    !sources.value.length ||
    !window.confirm(
      t("为当前页 ") +
        sources.value.length +
        t(" 项来源生成草稿？不会自动过账。"),
    )
  )
    return;
  batchResults.value = (
    await props.api.post(
      base + "/batch-import",
      sources.value.map((x) => ({
        requestId: crypto.randomUUID(),
        sourceKind: sourceKind.value,
        sourceId: x.id,
      })),
    )
  ).data.items;
}
async function batchPost() {
  const drafts = journals.value.filter((x) => x.status === "Draft");
  if (
    !drafts.length ||
    !window.confirm(
      t("过账当前页 ") + drafts.length + t(" 张草稿？已过账凭证只能冲销。"),
    )
  )
    return;
  batchResults.value = (
    await props.api.post(
      base + "/batch-post",
      drafts.map((x) => ({ id: x.id, version: x.version })),
    )
  ).data.items;
}
async function importSource(row: any) {
  const r = await props.api.post(base + "/import", {
    requestId: crypto.randomUUID(),
    sourceKind: sourceKind.value,
    sourceId: row.id,
  });
  await detail(r.data.id);
}
async function showTrial() {
  trial.value = (
    await props.api.get(base + "/trial-balance", {
      params: {
        dateFrom: start.value,
        dateTo: end.value,
        currency: currency.value,
      },
    })
  ).data;
}
async function reconcile() {
  reconciliation.value = (
    await props.api.get(base + "/reconciliation", {
      params: { dateTo: end.value, currency: currency.value },
    })
  ).data;
}
async function showLedger(id: string) {
  ledgerId.value = id;
  ledger.value = (
    await props.api.get(base + "/ledger/" + id, {
      params: {
        dateFrom: start.value,
        dateTo: end.value,
        page: ledgerPage.value,
      },
    })
  ).data;
}
onMounted(() =>
  run(async () => {
    const s = (await props.api.get(root + "/finance-settings")).data;
    date.value = s.today;
    reverseDate.value = s.today;
    start.value = s.today.slice(0, 8) + "01";
    const d = new Date(s.today + "T00:00:00Z");
    d.setUTCMonth(d.getUTCMonth() + 1, 0);
    end.value = d.toISOString().slice(0, 10);
    periodCode.value = s.today.slice(0, 7);
    await loadResources();
  }),
);

function jump(id: string) {
  const target = document.getElementById(id);
  target?.scrollIntoView({ behavior: "smooth", block: "start" });
  target?.setAttribute("tabindex", "-1");
  target?.focus({ preventScroll: true });
}
</script>
<template>
  <section class="module-panel" :aria-label="t('会计引擎')" :aria-busy="busy">
    <h2>{{ t("会计引擎") }}</h2>
    <nav class="section-nav" :aria-label="t('本页导航')">
      <a href="#accountingpanel-0" @click.prevent="jump('accountingpanel-0')">{{
        t("会计启用与待处理事项")
      }}</a
      ><a
        href="#accountingpanel-1"
        @click.prevent="jump('accountingpanel-1')"
        >{{ t("会计期间") }}</a
      ><a
        href="#accountingpanel-2"
        @click.prevent="jump('accountingpanel-2')"
        >{{ t("会计科目") }}</a
      ><a
        v-if="canWrite"
        href="#accountingpanel-3"
        @click.prevent="jump('accountingpanel-3')"
        >{{ t("来源科目映射") }}</a
      ><a
        href="#accountingpanel-4"
        @click.prevent="jump('accountingpanel-4')"
        >{{ t("来源待生成凭证") }}</a
      ><a
        v-if="canWrite"
        href="#accounting-draft"
        @click.prevent="jump('accounting-draft')"
        >{{ t("手工凭证") }}</a
      ><a
        href="#accountingpanel-5"
        @click.prevent="jump('accountingpanel-5')"
        >{{ t("凭证列表") }}</a
      ><a
        href="#accountingpanel-6"
        @click.prevent="jump('accountingpanel-6')"
        >{{ t("试算与资金对账") }}</a
      >
    </nav>
    <p>
      {{
        t(
          "先建立期间和科目，再设置来源映射。资金流水不会因凭证重复收付款；先生成草稿，再过账。已过账只能冲销。存在会计期间后，新增业务日期必须属于开放期间。",
        )
      }}
    </p>
    <div class="control-row">
      <label
        >{{ t("工作币种")
        }}<el-input
          :disabled="busy"
          v-model="currency"
          :aria-label="t('会计币种')" /></label
      ><label
        >{{ t("期间开始")
        }}<input
          :disabled="busy"
          type="date"
          v-model="start"
          :aria-label="t('会计期间开始')" /></label
      ><label
        >{{ t("期间截止")
        }}<input
          :disabled="busy"
          type="date"
          v-model="end"
          :aria-label="t('会计期间截止')"
      /></label>
    </div>
    <h3 class="section-heading" id="accountingpanel-0">
      {{ t("会计启用与待处理事项") }}
    </h3>
    <template v-if="setup"
      ><p>{{ localizeMessage(setup.guidance) }}</p>
      <p>
        {{ t("最早业务日期") }} {{ setup.earliestBusinessDate ?? t("无") }}
        {{ t("· 未覆盖期间资金") }} {{ setup.uncoveredCash }}
        {{ t("· 待入账资金") }} {{ setup.pendingCash }} {{ t("· 待确认应收") }}
        {{ setup.pendingReceivables }} {{ t("· 待确认应付") }}
        {{ setup.pendingPayables }} {{ t("· 草稿") }} {{ setup.drafts }}
        {{ t("· 未映射账户") }} {{ setup.missingAccountCount }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="setup.missingAccounts"
        ><el-table-column
          prop="name"
          :label="t('待映射账户')"
          min-width="115" /><el-table-column
          prop="currency"
          :label="t('币种')"
          min-width="115" /></el-table
    ></template>
    <p>
      {{
        t(
          "按下方来源类型逐页处理历史数据；历史业务全部接入，不重复建立期初。现金流反映收付款，试算收入费用反映会计确认，二者口径不同。",
        )
      }}
    </p>
    <template v-if="canWrite"
      ><el-button :disabled="busy" @click="run(batchImport)">{{
        t("生成当前页来源草稿")
      }}</el-button
      ><el-button :disabled="busy" @click="run(batchPost)">{{
        t("过账当前页草稿")
      }}</el-button></template
    ><el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      v-if="batchResults.length"
      :data="batchResults"
      ><el-table-column
        prop="sourceId"
        :label="t('来源')"
        min-width="115"
      /><el-table-column
        prop="id"
        :label="t('凭证')"
        min-width="115"
      /><el-table-column
        prop="success"
        :label="t('成功')"
        min-width="115"
      /><el-table-column
        prop="number"
        :label="t('凭证号')"
        min-width="115"
      /><el-table-column :label="t('失败原因（处理后可重试）')" min-width="180"
        ><template #default="scope">{{
          localizeMessage(scope.row.error)
        }}</template></el-table-column
      ></el-table
    >
    <h3>{{ t("来源追溯") }}</h3>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("追溯来源类型") }}</span
        ><select
          v-model="traceKind"
          :disabled="busy"
          :aria-label="t('追溯来源类型')"
        >
          <option value="Transaction">{{ t("资金流水") }}</option>
          <option value="Receivable">{{ t("应收单") }}</option>
          <option value="Payable">{{ t("应付单") }}</option>
        </select></label
      ><label class="control-field"
        ><span>{{ t("追溯来源编号") }}</span
        ><el-input
          v-model="traceId"
          :disabled="busy"
          :aria-label="t('追溯来源编号')"
          :placeholder="t('业务单据或流水编号')" /></label
      ><el-button :disabled="busy" @click="run(trace)">{{
        t("查找来源凭证")
      }}</el-button>
    </div>
    <h3 class="section-heading" id="accountingpanel-1">{{ t("会计期间") }}</h3>
    <template v-if="canWrite"
      ><div class="control-row">
        <label class="control-field"
          ><span>{{ t("会计期间代码") }}</span
          ><el-input
            :disabled="busy"
            v-model="periodCode"
            :aria-label="t('会计期间代码')"
            :placeholder="t('期间代码，例如2026-11')" /></label
        ><el-button
          :loading="busy"
          :disabled="busy"
          @click="run(createPeriod)"
          >{{ t("建立会计期间") }}</el-button
        >
      </div></template
    >
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="periods"
      row-key="id"
      ><el-table-column
        prop="code"
        :label="t('期间')"
        min-width="115"
      /><el-table-column
        prop="startDate"
        :label="t('开始')"
        min-width="115"
      /><el-table-column
        prop="endDate"
        :label="t('结束')"
        min-width="115"
      /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
      ><el-table-column :label="t('操作')" min-width="115"
        ><template #default="s"
          ><button
            v-if="canWrite && s.row.status === 'Open'"
            :disabled="busy"
            :aria-label="t('关闭期间 ') + s.row.code"
            @click="run(() => closePeriod(s.row))"
          >
            {{ t("关闭") }}
          </button></template
        ></el-table-column
      ></el-table
    >
    <el-button
      :disabled="busy || periodPage === 1"
      @click="
        periodPage--;
        run(async () => {});
      "
      >{{ t("期间上一页") }}</el-button
    ><el-button
      :disabled="busy || periodPage * 20 >= periodTotal"
      @click="
        periodPage++;
        run(async () => {});
      "
      >{{ t("期间下一页") }}</el-button
    >
    <h4>{{ t("结账阻塞明细") }}</h4>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("检查期间") }}</span
        ><select
          v-model="blockerPeriod"
          :aria-label="t('检查期间')"
          :disabled="busy"
        >
          <option value="">{{ t("选择期间") }}</option>
          <option v-for="p in periods" :key="p.id" :value="p.id">
            {{ p.code }}
          </option>
        </select></label
      ><label class="control-field"
        ><span>{{ t("阻塞类型") }}</span
        ><select
          v-model="blockerKind"
          :aria-label="t('阻塞类型')"
          :disabled="busy"
        >
          <option value="Transaction">{{ t("资金流水") }}</option>
          <option value="Receivable">{{ t("应收确认") }}</option>
          <option value="Payable">{{ t("应付确认") }}</option>
          <option value="Draft">{{ t("草稿凭证") }}</option>
          <option value="Unmapped">{{ t("未映射账户") }}</option>
        </select></label
      ><el-button
        :disabled="busy || !blockerPeriod"
        @click="
          blockerPage = 1;
          run(loadBlockers);
        "
        >{{ t("查看阻塞单据") }}</el-button
      >
    </div>
    <template v-if="blockers"
      ><el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="blockers.items"
        ><el-table-column
          prop="id"
          :label="t('业务编号')"
          min-width="165"
        /><el-table-column
          prop="date"
          :label="t('业务日期')"
          min-width="115"
        /><el-table-column
          prop="description"
          :label="t('说明')"
          min-width="115"
        /><el-table-column :label="t('处理')" min-width="115"
          ><template #default="s"
            ><el-button
              v-if="blockerKind === 'Draft'"
              :disabled="busy"
              @click="run(() => detail(s.row.id))"
              >{{ t("打开草稿") }}</el-button
            ><el-button
              v-if="
                canWrite &&
                ['Transaction', 'Receivable', 'Payable'].includes(blockerKind)
              "
              :disabled="busy"
              @click="
                run(async () => {
                  sourceKind = blockerKind;
                  await importSource(s.row);
                })
              "
              >{{ t("生成或打开凭证") }}</el-button
            ></template
          ></el-table-column
        ></el-table
      ><el-button
        :disabled="busy || blockerPage === 1"
        @click="
          blockerPage--;
          run(loadBlockers);
        "
        >{{ t("阻塞上一页") }}</el-button
      ><el-button
        :disabled="busy || blockerPage * 20 >= blockers.total"
        @click="
          blockerPage++;
          run(loadBlockers);
        "
        >{{ t("阻塞下一页") }}</el-button
      ></template
    >
    <h3 class="section-heading" id="accountingpanel-2">{{ t("会计科目") }}</h3>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("科目搜索") }}</span
        ><el-input
          :disabled="busy"
          v-model="search"
          :aria-label="t('科目搜索')"
          :placeholder="t('代码或名称')" /></label
      ><el-button
        :disabled="busy"
        @click="
          accountPage = 1;
          run(async () => {});
        "
        >{{ t("搜索科目") }}</el-button
      >
    </div>
    <template v-if="canWrite"
      ><div class="control-row">
        <label class="control-field"
          ><span>{{ t("科目代码") }}</span
          ><el-input
            :disabled="busy"
            v-model="code"
            :aria-label="t('科目代码')"
            :placeholder="t('科目代码，例如1002')" /></label
        ><label class="control-field"
          ><span>{{ t("科目名称") }}</span
          ><el-input
            :disabled="busy"
            v-model="name"
            :aria-label="t('科目名称')"
            :placeholder="t('科目名称')" /></label
        ><label class="control-field"
          ><span>{{ t("科目类别") }}</span
          ><select :disabled="busy" v-model="kind" :aria-label="t('科目类别')">
            <option
              v-for="k in [
                'Asset',
                'Liability',
                'Equity',
                'Revenue',
                'Expense',
              ]"
              :key="k"
              :value="k"
            >
              {{ t(kindLabels[k]) }}
            </option>
          </select></label
        ><label class="control-field"
          ><span>{{ t("上级科目") }}</span
          ><select
            :disabled="busy"
            v-model="parentId"
            :aria-label="t('上级科目')"
          >
            <option value="">{{ t("顶级科目") }}</option>
            <option
              v-for="a in accounts.filter(
                (x) => x.active && x.currency === currency && x.kind === kind,
              )"
              :key="a.id"
              :value="a.id"
            >
              {{ a.code }} {{ a.name }}
            </option>
          </select></label
        ><el-button
          :loading="busy"
          :disabled="busy"
          @click="run(createAccount)"
          >{{ t("新增会计科目") }}</el-button
        >
      </div></template
    >
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="accounts"
      row-key="id"
      ><el-table-column
        prop="code"
        :label="t('代码')"
        min-width="115"
      /><el-table-column
        prop="name"
        :label="t('名称')"
        min-width="115"
      /><el-table-column :label="t('类别')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.kind" /></template></el-table-column
      ><el-table-column
        prop="currency"
        :label="t('币种')"
        min-width="115"
      /><el-table-column
        prop="active"
        :label="t('启用')"
        min-width="115"
      /><el-table-column :label="t('操作')" min-width="115"
        ><template #default="s"
          ><button
            :disabled="busy"
            :aria-label="t('科目明细 ') + s.row.code"
            @click="
              ledgerPage = 1;
              run(() => showLedger(s.row.id));
            "
          >
            {{ t("明细") }}</button
          ><button
            v-if="canWrite"
            :disabled="busy"
            :aria-label="t('切换科目 ') + s.row.code"
            @click="
              run(() =>
                api.patch(base + '/accounts/' + s.row.id + '/status', {
                  active: !s.row.active,
                }),
              )
            "
          >
            {{ s.row.active ? t("停用") : t("启用") }}
          </button></template
        ></el-table-column
      ></el-table
    >
    <el-button
      :disabled="busy || accountPage === 1"
      @click="
        accountPage--;
        run(async () => {});
      "
      >{{ t("科目上一页") }}</el-button
    ><el-button
      :disabled="busy || accountPage * 100 >= accountTotal"
      @click="
        accountPage++;
        run(async () => {});
      "
      >{{ t("科目下一页") }}</el-button
    >
    <p>
      {{
        t(
          "选择器使用当前科目搜索结果，最多100项；请搜索后选择。已有明细用途的科目不能再增加子级。",
        )
      }}
    </p>
    <template v-if="canWrite"
      ><h3 class="section-heading" id="accountingpanel-3">
        {{ t("来源科目映射") }}
      </h3>
      <div class="control-row">
        <label class="control-field"
          ><span>{{ t("映射用途") }}</span
          ><select
            :disabled="busy"
            v-model="mapKind"
            :aria-label="t('映射用途')"
            @change="
              mapResource = '';
              run(loadResources);
            "
          >
            <option
              v-for="k in [
                'FinancialAccount',
                'Category',
                'Opening',
                'Receivable',
                'Payable',
              ]"
              :key="k"
              :value="k"
            >
              {{ t(mappingLabels[k]) }}
            </option>
          </select></label
        ><label class="control-field"
          ><span>{{ t("映射资源搜索") }}</span
          ><el-input
            :disabled="busy"
            v-model="resourceSearch"
            :aria-label="t('映射资源搜索')"
            :placeholder="t('资金账户或分类名称')" /></label
        ><el-button :disabled="busy" @click="run(loadResources)">{{
          t("搜索映射资源")
        }}</el-button
        ><label
          class="control-field"
          v-if="['FinancialAccount', 'Category'].includes(mapKind)"
          ><span>{{ t("映射资源") }}</span
          ><select
            :disabled="busy"
            v-model="mapResource"
            :aria-label="t('映射资源')"
          >
            <option value="">{{ t("选择资金账户或分类") }}</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">
              {{ r.name }}
            </option>
          </select></label
        ><label class="control-field"
          ><span>{{ t("映射科目") }}</span
          ><select
            :disabled="busy"
            v-model="mapLedger"
            :aria-label="t('映射科目')"
          >
            <option value="">{{ t("选择科目") }}</option>
            <option
              v-for="a in accounts.filter(
                (x) => x.active && x.currency === currency,
              )"
              :key="a.id"
              :value="a.id"
            >
              {{ a.code }} {{ a.name }}
            </option>
          </select></label
        ><el-button :loading="busy" :disabled="busy" @click="run(setMapping)">{{
          t("保存科目映射")
        }}</el-button>
      </div>
      <details>
        <summary>{{ t("查看映射（最近100项）") }}</summary>
        <el-table :data="mappings" :empty-text="t('暂无科目映射')"
          ><el-table-column :label="t('用途')" min-width="120"
            ><template #default="scope">{{
              t(mappingLabels[scope.row.kind] ?? scope.row.kind)
            }}</template></el-table-column
          ><el-table-column
            prop="resourceId"
            :label="t('业务资源编号')"
            min-width="180" /><el-table-column
            prop="ledgerAccountId"
            :label="t('会计科目编号')"
            min-width="180"
        /></el-table></details
    ></template>
    <h3 class="section-heading" id="accountingpanel-4">
      {{ t("来源待生成凭证") }}
    </h3>
    <label class="control-field"
      ><span>{{ t("凭证来源类型") }}</span
      ><select
        :disabled="busy"
        v-model="sourceKind"
        :aria-label="t('凭证来源类型')"
        @change="
          sourcePage = 1;
          run(async () => {});
        "
      >
        <option value="Transaction">{{ t("资金流水") }}</option>
        <option value="Receivable">{{ t("应收确认") }}</option>
        <option value="Payable">{{ t("应付确认") }}</option>
      </select></label
    ><el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="sources"
      row-key="id"
      ><el-table-column
        prop="businessDate"
        :label="t('日期')"
        min-width="115"
      /><el-table-column
        prop="description"
        :label="t('来源')"
        min-width="115"
      /><el-table-column
        prop="amount"
        :label="t('金额')"
        min-width="165"
      /><el-table-column
        prop="currency"
        :label="t('币种')"
        min-width="115"
      /><el-table-column :label="t('操作')" min-width="115"
        ><template #default="s"
          ><button
            v-if="canWrite"
            :disabled="busy"
            :aria-label="t('生成来源凭证 ') + s.row.description"
            @click="run(() => importSource(s.row))"
          >
            {{ t("生成草稿") }}
          </button></template
        ></el-table-column
      ></el-table
    ><el-button
      :disabled="busy || sourcePage === 1"
      @click="
        sourcePage--;
        run(async () => {});
      "
      >{{ t("来源上一页") }}</el-button
    ><el-button
      :disabled="busy || sourcePage * 20 >= sourceTotal"
      @click="
        sourcePage++;
        run(async () => {});
      "
      >{{ t("来源下一页") }}</el-button
    >
    <template v-if="canWrite"
      ><h3 class="section-heading" id="accounting-draft">
        {{ editing ? t("编辑手工草稿") : t("手工凭证草稿") }}
      </h3>
      <div class="control-row">
        <label class="control-field"
          ><span>{{ t("凭证业务日期") }}</span
          ><input
            :disabled="busy"
            type="date"
            v-model="date"
            :aria-label="t('凭证业务日期')" /></label
        ><label class="control-field"
          ><span>{{ t("凭证摘要") }}</span
          ><el-input
            :disabled="busy"
            v-model="description"
            :aria-label="t('凭证摘要')"
            :placeholder="t('凭证摘要')"
            maxlength="500"
        /></label>
      </div>
      <div v-for="(line, i) in lines" :key="i" class="line">
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("分录科目") }}</span
            ><select
              :disabled="busy"
              v-model="line.ledgerAccountId"
              :aria-label="t('分录科目 ') + i"
            >
              <option value="">{{ t("明细科目") }}</option>
              <option
                v-for="a in accounts.filter(
                  (x) => x.active && x.currency === currency,
                )"
                :key="a.id"
                :value="a.id"
              >
                {{ a.code }} {{ a.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("借方") }}</span
            ><el-input
              :disabled="busy"
              v-model="line.debit"
              :aria-label="t('借方 ') + i"
              :placeholder="t('借方')" /></label
          ><label class="control-field"
            ><span>{{ t("贷方") }}</span
            ><el-input
              :disabled="busy"
              v-model="line.credit"
              :aria-label="t('贷方 ') + i"
              :placeholder="t('贷方')" /></label
          ><button
            :disabled="busy || lines.length <= 2"
            @click="lines.splice(i, 1)"
          >
            {{ t("移除") }}
          </button>
        </div>
      </div>
      <el-button
        :disabled="busy || lines.length >= 200"
        @click="lines.push({ ledgerAccountId: '', debit: '0', credit: '0' })"
        >{{ t("添加分录") }}</el-button
      >
      <p class="note">
        {{ t("草稿预览：借方") }} {{ unitText(draftDebit) }} {{ t("· 贷方") }}
        {{ unitText(draftCredit) }} ·
        {{
          !draftValid
            ? t("请检查金额格式")
            : draftDebit === draftCredit && draftDebit > 0n
              ? t("借贷平衡")
              : t("借贷尚未平衡")
        }}{{ t("。保存后仍可编辑，过账前请核对科目与日期。") }}
      </p>
      <el-button
        type="primary"
        :loading="busy"
        :disabled="busy"
        @click="run(saveDraft)"
        >{{ t("保存凭证草稿") }}</el-button
      ><el-button v-if="editing" :disabled="busy" @click="editing = false">{{
        t("结束编辑")
      }}</el-button></template
    >
    <h3 class="section-heading" id="accountingpanel-5">{{ t("凭证列表") }}</h3>
    <label class="control-field"
      ><span>{{ t("凭证状态筛选") }}</span
      ><select
        :disabled="busy"
        v-model="status"
        :aria-label="t('凭证状态筛选')"
        @change="
          journalPage = 1;
          run(async () => {});
        "
      >
        <option value="">{{ t("全部状态") }}</option>
        <option value="Draft">{{ t("草稿") }}</option>
        <option>Posted</option>
      </select></label
    ><el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="journals"
      row-key="id"
      ><el-table-column
        prop="number"
        :label="t('凭证号')"
        min-width="115"
      /><el-table-column
        prop="businessDate"
        :label="t('日期')"
        min-width="115"
      /><el-table-column
        prop="description"
        :label="t('摘要')"
        min-width="115"
      /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
      ><el-table-column
        prop="debitTotal"
        :label="t('过账借方')"
        min-width="115"
      /><el-table-column :label="t('操作')" min-width="115"
        ><template #default="s"
          ><button
            :disabled="busy"
            :aria-label="t('查看凭证 ') + s.row.description"
            @click="run(() => detail(s.row.id))"
          >
            {{ t("查看") }}
          </button></template
        ></el-table-column
      ></el-table
    ><el-button
      :disabled="busy || journalPage === 1"
      @click="
        journalPage--;
        run(async () => {});
      "
      >{{ t("凭证上一页") }}</el-button
    ><el-button
      :disabled="busy || journalPage * 20 >= journalTotal"
      @click="
        journalPage++;
        run(async () => {});
      "
      >{{ t("凭证下一页") }}</el-button
    >
    <el-card v-if="selected"
      ><p v-if="selected.entry.status !== 'Posted'">
        {{ t("凭证过账后可上传附件，避免删除草稿后失去业务关联。") }}
      </p>
      <AttachmentPanel
        v-if="selected.entry.status === 'Posted'"
        :api="api"
        :organization-id="organizationId"
        :role="role"
        resource-kind="Journal"
        :resource-id="selected.entry.id"
      />
      <h4>
        {{ t("凭证详情") }} {{ selected.entry.number ?? selected.entry.id }}
      </h4>
      <p>
        {{ t("状态") }} <StatusBadge :value="selected.entry.status" /> ·
        {{ selected.entry.currency }} · {{ selected.entry.description }}
        {{ t("· 冲销原凭证") }} {{ selected.entry.reversalOfId ?? t("无") }}
        {{ t("· 冲销凭证") }} {{ selected.reversedBy ?? t("无") }}
      </p>
      <p>
        {{ t("来源：")
        }}{{
          selected.entry.sourceTransactionId ??
          selected.entry.sourceReceivableId ??
          selected.entry.sourcePayableId ??
          t("手工凭证")
        }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="selected.lines"
        ><el-table-column
          prop="code"
          :label="t('科目')"
          min-width="115"
        /><el-table-column
          prop="name"
          :label="t('名称')"
          min-width="115"
        /><el-table-column
          prop="debit"
          :label="t('借方')"
          min-width="115"
        /><el-table-column
          prop="credit"
          :label="t('贷方')"
          min-width="115"
        /><el-table-column :label="t('追溯')" min-width="115"
          ><template #default="s"
            ><el-button
              :disabled="busy"
              @click="
                ledgerPage = 1;
                run(() => showLedger(s.row.ledgerAccountId));
              "
              >{{ t("打开科目明细") }}</el-button
            ></template
          ></el-table-column
        ></el-table
      ><template v-if="canWrite"
        ><template v-if="selected.entry.status === 'Draft'"
          ><el-button :loading="busy" :disabled="busy" @click="run(post)">{{
            t("过账所选凭证")
          }}</el-button
          ><el-button
            v-if="
              !selected.entry.sourceTransactionId &&
              !selected.entry.sourceReceivableId &&
              !selected.entry.sourcePayableId &&
              !selected.entry.reversalOfId
            "
            :disabled="busy"
            @click="editDraft"
            >{{ t("编辑所选草稿") }}</el-button
          ><el-button
            :loading="busy"
            :disabled="busy"
            @click="run(removeDraft)"
            >{{ t("删除所选草稿") }}</el-button
          ></template
        ><template
          v-if="
            selected.entry.status === 'Posted' &&
            !selected.entry.reversalOfId &&
            !selected.reversedBy &&
            !selected.entry.sourceTransactionId
          "
          ><div class="control-row">
            <label class="control-field"
              ><span>{{ t("凭证冲销日期") }}</span
              ><input
                :disabled="busy"
                type="date"
                v-model="reverseDate"
                :aria-label="t('凭证冲销日期')" /></label
            ><label class="control-field"
              ><span>{{ t("凭证冲销原因") }}</span
              ><el-input
                :disabled="busy"
                v-model="reason"
                :aria-label="t('凭证冲销原因')"
                :placeholder="t('填写冲销原因')"
                maxlength="400" /></label
            ><el-button
              :loading="busy"
              :disabled="busy"
              @click="run(reverse)"
              >{{ t("冲销所选凭证") }}</el-button
            >
          </div></template
        ></template
      ></el-card
    >
    <h3 class="section-heading" id="accountingpanel-6">
      {{ t("试算与资金对账") }}
    </h3>
    <el-button :loading="busy" :disabled="busy" @click="run(showTrial)">{{
      t("查询试算平衡")
    }}</el-button
    ><el-button :loading="busy" :disabled="busy" @click="run(reconcile)">{{
      t("核对资金与总账")
    }}</el-button
    ><template v-if="trial"
      ><p>
        {{ t("借方") }} {{ trial.debit }} {{ t("· 贷方") }} {{ trial.credit }}
        {{ t("· 差额") }} {{ trial.difference }} {{ t("· 收入") }}
        {{ trial.revenue }} {{ t("· 费用") }} {{ trial.expense }}
        {{ t("· 会计收支差额") }} {{ trial.profitBeforeClosing
        }}{{ t("（未自动结转，不替代税务报表）") }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="trial.items"
        ><el-table-column
          prop="code"
          :label="t('科目')"
          min-width="115" /><el-table-column
          prop="name"
          :label="t('名称')"
          min-width="115" /><el-table-column
          prop="openingNetDebit"
          :label="t('期初借方净额')"
          min-width="115" /><el-table-column
          prop="debit"
          :label="t('本期借方')"
          min-width="115" /><el-table-column
          prop="credit"
          :label="t('本期贷方')"
          min-width="115" /><el-table-column
          prop="normalBalance"
          :label="t('正常方向余额')"
          min-width="165" /></el-table></template
    ><template v-if="reconciliation"
      ><p>
        {{ t("待过账资金流水") }} {{ reconciliation.pendingTransactions }}
        {{ t("· 未映射资金账户") }} {{ reconciliation.unmappedAccounts
        }}{{ t("。只报告差异，不自动修改账务。") }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="reconciliation.items"
        ><el-table-column
          prop="ledgerAccountId"
          :label="t('映射科目')"
          min-width="115" /><el-table-column
          prop="cash"
          :label="t('资金余额')"
          min-width="165" /><el-table-column
          prop="ledger"
          :label="t('总账余额')"
          min-width="165" /><el-table-column
          prop="difference"
          :label="t('差额')"
          min-width="115" /></el-table
    ></template>
    <template v-if="ledger"
      ><h4>{{ ledger.code }} {{ ledger.name }} {{ t("· 科目明细") }}</h4>
      <p>
        {{ t("期初借方净额") }} {{ ledger.openingNetDebit }}
        {{ t("· 期末借方净额") }} {{ ledger.closingNetDebit }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="ledger.items"
        ><el-table-column :label="t('凭证')" min-width="115"
          ><template #default="s"
            ><el-button :disabled="busy" @click="run(() => detail(s.row.id))">{{
              s.row.number
            }}</el-button></template
          ></el-table-column
        ><el-table-column
          prop="businessDate"
          :label="t('日期')"
          min-width="115" /><el-table-column
          prop="description"
          :label="t('摘要')"
          min-width="115" /><el-table-column
          prop="debit"
          :label="t('借方')"
          min-width="115" /><el-table-column
          prop="credit"
          :label="t('贷方')"
          min-width="115" /></el-table
      ><el-button
        :disabled="busy || ledgerPage === 1"
        @click="
          ledgerPage--;
          run(() => showLedger(ledgerId));
        "
        >{{ t("科目明细上一页") }}</el-button
      ><el-button
        :disabled="busy || ledgerPage * 20 >= ledger.total"
        @click="
          ledgerPage++;
          run(() => showLedger(ledgerId));
        "
        >{{ t("科目明细下一页") }}</el-button
      ></template
    >
    <p v-if="message" role="status" class="feedback" :class="{ error: failed }">
      {{ localizeMessage(message) }}
    </p>
  </section>
</template>
