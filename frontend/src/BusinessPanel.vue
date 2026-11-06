<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import AttachmentPanel from "./AttachmentPanel.vue";
import { ref, onMounted } from "vue";
import { errorText } from "./ui";
import type { AxiosInstance } from "axios";
const failed = ref(false);
const props = defineProps<{
  api: AxiosInstance;
  organizationId: string;
  role: string;
}>();
const emit = defineEmits<{ changed: [] }>();
const canWrite = ["Owner", "Accountant"].includes(props.role);
const root = "/organizations/" + props.organizationId;
const busy = ref(false),
  message = ref(""),
  today = ref("");
const parties = ref<any[]>([]),
  accounts = ref<any[]>([]),
  categories = ref<any[]>([]),
  documents = ref<any[]>([]),
  budgets = ref<any[]>([]);
const partyPage = ref(1),
  partyTotal = ref(0),
  docPage = ref(1),
  docTotal = ref(0),
  budgetPage = ref(1),
  budgetTotal = ref(0),
  search = ref("");
const partyName = ref(""),
  partyKind = ref("Customer"),
  contact = ref(""),
  editParty = ref(""),
  editName = ref(""),
  editStatus = ref("Active"),
  editContact = ref("");
const docKind = ref("receivables"),
  docNumber = ref(""),
  partyId = ref(""),
  categoryId = ref(""),
  currency = ref("CNY"),
  amount = ref(""),
  issue = ref(""),
  due = ref(""),
  description = ref("");
const detailPage = ref(1);
const documentDetail = ref<any>(null);
const selected = ref<any>(null),
  settlementAccount = ref(""),
  settlementAmount = ref(""),
  settlementDate = ref("");
const budgetName = ref(""),
  budgetCurrency = ref("CNY"),
  start = ref(""),
  end = ref(""),
  lines = ref([{ categoryId: "", amount: "" }]),
  execution = ref<any>(null);
const lookup = ref(""),
  partyOptions = ref<any[]>([]),
  docStatus = ref(""),
  docCurrency = ref(""),
  dueFrom = ref(""),
  dueTo = ref(""),
  docSort = ref("asc");
const revisionOf = ref<string | null>(null);
const createRequests = new Map<string, { body: string; id: string }>();
async function createOnce(path: string, body: any) {
  const serialized = JSON.stringify(body);
  let pending = createRequests.get(path);
  if (pending?.body !== serialized) {
    pending = { body: serialized, id: crypto.randomUUID() };
    createRequests.set(path, pending);
  }
  const response = await props.api.post(root + path, {
    ...body,
    requestId: pending.id,
  });
  createRequests.delete(path);
  return response;
}
async function searchOptions() {
  const params = { pageSize: 100, search: lookup.value || undefined };
  const [a, c, p] = await Promise.all(
    ["/accounts", "/categories", "/counterparties"].map((path) =>
      props.api.get(root + path, { params }),
    ),
  );
  accounts.value = a!.data.items;
  categories.value = c!.data.items;
  partyOptions.value = p!.data.items;
}
const exportFrom = ref(""),
  exportTo = ref("");
let pending: { body: string; id: string } | null = null;
function validMoney(v: string) {
  return /^[0-9]{1,15}(\.[0-9]{1,4})?$/.test(v) && /[1-9]/.test(v);
}
async function load() {
  const p = (
    await props.api.get(root + "/counterparties", {
      params: { page: partyPage.value, search: search.value || undefined },
    })
  ).data;
  parties.value = p.items;
  partyTotal.value = p.total;
  const d = (
    await props.api.get(root + "/" + docKind.value, {
      params: {
        page: docPage.value,
        status: docStatus.value || undefined,
        currency: docCurrency.value || undefined,
        dueFrom: dueFrom.value || undefined,
        dueTo: dueTo.value || undefined,
        sort: docSort.value,
      },
    })
  ).data;
  documents.value = d.items;
  docTotal.value = d.total;
  if (selected.value) {
    selected.value =
      documents.value.find((x) => x.id === selected.value.id) ?? null;
    if (selected.value) await loadDocumentDetail();
    else documentDetail.value = null;
  }
  const b = (
    await props.api.get(root + "/budgets", {
      params: { page: budgetPage.value },
    })
  ).data;
  budgets.value = b.items;
  budgetTotal.value = b.total;
}
async function run(fn: () => Promise<unknown>) {
  if (busy.value) return;
  message.value = "";
  failed.value = false;
  busy.value = true;
  try {
    await fn();
    await load();
    if (!lookup.value) await searchOptions();
    message.value = "操作成功";
  } catch (e: any) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function createParty() {
  if (!partyName.value.trim()) throw new Error("填写往来方名称");
  await createOnce("/counterparties", {
    name: partyName.value,
    kind: partyKind.value,
    contact: contact.value,
  });
  partyName.value = "";
  contact.value = "";
}
function pickParty() {
  const p = partyOptions.value.find((x) => x.id === editParty.value);
  if (p) {
    editName.value = p.name;
    editStatus.value = p.status;
    editContact.value = p.contact;
  }
}
async function changeParty() {
  if (!editParty.value || !editName.value.trim())
    throw new Error("选择往来方并填写名称");
  await props.api.patch(root + "/counterparties/" + editParty.value, {
    name: editName.value,
    status: editStatus.value,
    contact: editContact.value,
  });
}
async function createDocument() {
  if (
    !docNumber.value.trim() ||
    !partyId.value ||
    !categoryId.value ||
    !validMoney(amount.value) ||
    !issue.value ||
    due.value < issue.value
  )
    throw new Error("检查单号、往来方、分类、金额和日期");
  await createOnce("/" + docKind.value, {
    number: docNumber.value,
    counterpartyId: partyId.value,
    categoryId: categoryId.value,
    currency: currency.value,
    amount: amount.value,
    issueDate: issue.value,
    dueDate: due.value,
    description: description.value,
  });
  docNumber.value = "";
  amount.value = "";
}
async function loadDocumentDetail() {
  if (selected.value)
    documentDetail.value = (
      await props.api.get(
        root + "/" + docKind.value + "/" + selected.value.id,
        { params: { page: detailPage.value } },
      )
    ).data;
}
async function selectDocument(row: any) {
  selected.value = row;
  settlementAmount.value = row.remaining;
  detailPage.value = 1;
  await loadDocumentDetail();
}
async function settle() {
  if (
    !selected.value ||
    !settlementAccount.value ||
    !validMoney(settlementAmount.value) ||
    !settlementDate.value
  )
    throw new Error("选择单据、账户和有效金额/日期");
  const body = {
    accountId: settlementAccount.value,
    amount: settlementAmount.value,
    businessDate: settlementDate.value,
    description: "结算单据 " + selected.value.number,
  };
  const serialized = selected.value.id + JSON.stringify(body);
  if (pending?.body !== serialized)
    pending = { body: serialized, id: crypto.randomUUID() };
  await props.api.post(
    root + "/" + docKind.value + "/" + selected.value.id + "/settlements",
    { ...body, requestId: pending.id },
  );
  pending = null;
  emit("changed");
}
async function cancelDocument() {
  if (!selected.value) throw new Error("选择单据");
  await props.api.post(
    root + "/" + docKind.value + "/" + selected.value.id + "/cancel",
  );
}
async function createBudget() {
  if (
    !budgetName.value.trim() ||
    !start.value ||
    end.value < start.value ||
    !lines.value.length ||
    lines.value.some((x) => !x.categoryId || !validMoney(x.amount))
  )
    throw new Error("检查预算日期、分类与金额");
  await createOnce("/budgets", {
    previousVersionId: revisionOf.value,
    name: budgetName.value,
    currency: budgetCurrency.value,
    startDate: start.value,
    endDate: end.value,
    items: lines.value,
  });
  budgetName.value = "";
  revisionOf.value = null;
}
async function budgetStatus(row: any, status: string) {
  await props.api.post(root + "/budgets/" + row.id + "/status", { status });
}
async function revise(row: any) {
  const b = (await props.api.get(root + "/budgets/" + row.id)).data;
  revisionOf.value = b.id;
  budgetName.value = b.name + " 调整";
  budgetCurrency.value = b.currency;
  start.value = b.startDate;
  end.value = b.endDate;
  lines.value = b.items.map((x: any) => ({
    categoryId: x.categoryId,
    amount: x.amount,
  }));
}
async function showExecution(row: any) {
  execution.value = (
    await props.api.get(root + "/budgets/" + row.id + "/execution")
  ).data;
}
async function exportExcel() {
  const response = await props.api.get(root + "/exports/transactions.xlsx", {
    params: {
      dateFrom: exportFrom.value || undefined,
      dateTo: exportTo.value || undefined,
    },
    responseType: "blob",
  });
  const url = URL.createObjectURL(response.data),
    link = document.createElement("a");
  link.href = url;
  link.download = "CFIS-transactions.xlsx";
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
onMounted(() =>
  run(async () => {
    const s = (await props.api.get(root + "/finance-settings")).data;
    today.value = s.today;
    issue.value = s.today;
    due.value = s.today;
    settlementDate.value = s.today;
    start.value = s.today;
    end.value = s.today;
    await searchOptions();
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
  <section class="module-panel" :aria-busy="busy">
    <h2>{{ t("业务扩展") }}</h2>
    <nav class="section-nav" :aria-label="t('本页导航')">
      <a href="#businesspanel-0" @click.prevent="jump('businesspanel-0')">{{
        t("客户与供应商")
      }}</a
      ><a href="#businesspanel-1" @click.prevent="jump('businesspanel-1')">{{
        t("应收应付")
      }}</a
      ><a href="#businesspanel-2" @click.prevent="jump('businesspanel-2')">{{
        t("费用预算")
      }}</a
      ><a href="#businesspanel-3" @click.prevent="jump('businesspanel-3')">{{
        t("Excel 交易导出")
      }}</a>
    </nav>
    <h3 class="section-heading" id="businesspanel-0">
      {{ t("客户与供应商") }}
    </h3>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("往来方搜索") }}</span
        ><el-input
          :disabled="busy"
          v-model="search"
          :aria-label="t('往来方搜索')"
          :placeholder="t('名称搜索')" /></label
      ><el-button
        @click="
          partyPage = 1;
          run(async () => {});
        "
        >{{ t("搜索往来方") }}</el-button
      >
    </div>
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="parties"
      row-key="id"
      ><el-table-column
        prop="name"
        :label="t('往来方')"
        min-width="115" /><el-table-column :label="t('类型')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.kind" /></template></el-table-column
      ><el-table-column
        prop="contact"
        :label="t('联系方式')"
        min-width="115" /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
    ></el-table>
    <el-button
      :disabled="partyPage === 1 || busy"
      @click="
        partyPage--;
        run(async () => {});
      "
      >{{ t("往来方上一页") }}</el-button
    ><el-button
      :disabled="partyPage * 20 >= partyTotal || busy"
      @click="
        partyPage++;
        run(async () => {});
      "
      >{{ t("往来方下一页") }}</el-button
    >
    <template v-if="canWrite"
      ><el-card
        ><div class="control-row">
          <label class="control-field"
            ><span>{{ t("往来方名称") }}</span
            ><el-input
              :disabled="busy"
              v-model="partyName"
              :aria-label="t('往来方名称')"
              :placeholder="t('往来方名称')"
              maxlength="200" /></label
          ><label class="control-field"
            ><span>{{ t("往来方类型") }}</span
            ><select
              :disabled="busy"
              v-model="partyKind"
              :aria-label="t('往来方类型')"
            >
              <option value="Customer">{{ t("客户") }}</option>
              <option value="Supplier">{{ t("供应商") }}</option>
              <option value="Both">{{ t("客户及供应商") }}</option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("联系方式") }}</span
            ><el-input
              :disabled="busy"
              v-model="contact"
              :aria-label="t('联系方式')"
              :placeholder="t('联系方式')"
              maxlength="300" /></label
          ><el-button
            type="primary"
            :loading="busy"
            @click="run(createParty)"
            >{{ t("新增往来方") }}</el-button
          >
          <label class="control-field"
            ><span>{{ t("编辑往来方") }}</span
            ><select
              :disabled="busy"
              v-model="editParty"
              :aria-label="t('编辑往来方')"
              @change="pickParty"
            >
              <option value="">{{ t("选择搜索结果往来方") }}</option>
              <option v-for="p in partyOptions" :key="p.id" :value="p.id">
                {{ p.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("往来方新名称") }}</span
            ><el-input
              :disabled="busy"
              v-model="editName"
              :aria-label="t('往来方新名称')" /></label
          ><label class="control-field"
            ><span>{{ t("往来方状态") }}</span
            ><select
              :disabled="busy"
              v-model="editStatus"
              :aria-label="t('往来方状态')"
            >
              <option value="Active">{{ t("已启用") }}</option>
              <option value="Inactive">{{ t("已停用") }}</option>
              <option value="Archived">{{ t("已归档") }}</option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("往来方新联系方式") }}</span
            ><el-input
              :disabled="busy"
              v-model="editContact"
              :aria-label="t('往来方新联系方式')"
              maxlength="300" /></label
          ><el-button :loading="busy" @click="run(changeParty)">{{
            t("保存往来方")
          }}</el-button>
        </div></el-card
      ></template
    >
    <h3 class="section-heading" id="businesspanel-1">{{ t("应收应付") }}</h3>
    <p>
      {{
        t(
          "停用往来方不影响已有单据结算；账户和分类须启用。日期允许补录及未来登记，历史每日透支仅提示规则见文档。",
        )
      }}
    </p>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("资源搜索") }}</span
        ><el-input
          :disabled="busy"
          v-model="lookup"
          :aria-label="t('资源搜索')"
          :placeholder="
            t('搜索往来方、账户、分类（最多100项，请缩小关键词）')
          " /></label
      ><el-button @click="run(searchOptions)">{{ t("搜索可选资源") }}</el-button
      ><label class="control-field"
        ><span>{{ t("单据状态筛选") }}</span
        ><select
          :disabled="busy"
          v-model="docStatus"
          :aria-label="t('单据状态筛选')"
        >
          <option value="">{{ t("全部状态") }}</option>
          <option
            v-for="s in [
              'Open',
              'PartiallyPaid',
              'Overdue',
              'Paid',
              'Cancelled',
            ]"
            :key="s"
          >
            {{ s }}
          </option>
        </select></label
      ><label class="control-field"
        ><span>{{ t("单据币种筛选") }}</span
        ><el-input
          :disabled="busy"
          v-model="docCurrency"
          :aria-label="t('单据币种筛选')" /></label
      ><label class="control-field"
        ><span>{{ t("到期起日") }}</span
        ><input
          :disabled="busy"
          type="date"
          v-model="dueFrom"
          :aria-label="t('到期起日')" /></label
      ><label class="control-field"
        ><span>{{ t("到期止日") }}</span
        ><input
          :disabled="busy"
          type="date"
          v-model="dueTo"
          :aria-label="t('到期止日')" /></label
      ><label class="control-field"
        ><span>{{ t("单据排序") }}</span
        ><select :disabled="busy" v-model="docSort" :aria-label="t('单据排序')">
          <option value="asc">{{ t("到期日升序") }}</option>
          <option value="desc">{{ t("到期日降序") }}</option>
        </select></label
      ><el-button
        @click="
          docPage = 1;
          run(async () => {});
        "
        >{{ t("筛选单据") }}</el-button
      ><label class="control-field"
        ><span>{{ t("单据类型") }}</span
        ><select
          :disabled="busy"
          v-model="docKind"
          :aria-label="t('单据类型')"
          @change="
            selected = null;
            documentDetail = null;
            docPage = 1;
            run(async () => {});
          "
        >
          <option value="receivables">{{ t("应收款") }}</option>
          <option value="payables">{{ t("应付款") }}</option>
        </select></label
      >
    </div>
    <template v-if="canWrite"
      ><el-card
        ><div class="control-row">
          <label class="control-field"
            ><span>{{ t("单据编号") }}</span
            ><el-input
              :disabled="busy"
              v-model="docNumber"
              :aria-label="t('单据编号')"
              :placeholder="t('单据编号')"
              maxlength="100" /></label
          ><label class="control-field"
            ><span>{{ t("单据往来方") }}</span
            ><select
              :disabled="busy"
              v-model="partyId"
              :aria-label="t('单据往来方')"
            >
              <option value="">{{ t("选择搜索结果往来方") }}</option>
              <option
                v-for="p in partyOptions.filter(
                  (x) =>
                    x.status === 'Active' &&
                    (x.kind === 'Both' ||
                      x.kind ===
                        (docKind === 'receivables' ? 'Customer' : 'Supplier')),
                )"
                :key="p.id"
                :value="p.id"
              >
                {{ p.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("单据分类") }}</span
            ><select
              :disabled="busy"
              v-model="categoryId"
              :aria-label="t('单据分类')"
            >
              <option value="">{{ t("选择分类") }}</option>
              <option
                v-for="c in categories.filter(
                  (x) =>
                    x.status === 'Active' &&
                    x.kind ===
                      (docKind === 'receivables' ? 'Income' : 'Expense'),
                )"
                :key="c.id"
                :value="c.id"
              >
                {{ c.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("单据币种") }}</span
            ><el-input
              :disabled="busy"
              v-model="currency"
              :aria-label="t('单据币种')"
              placeholder="CNY" /></label
          ><label class="control-field"
            ><span>{{ t("单据金额") }}</span
            ><el-input
              :disabled="busy"
              v-model="amount"
              :aria-label="t('单据金额')"
              :placeholder="t('金额')" /></label
          ><label
            >{{ t("业务日期")
            }}<input
              :disabled="busy"
              type="date"
              v-model="issue"
              :aria-label="t('单据业务日期')" /></label
          ><label
            >{{ t("到期日期")
            }}<input
              :disabled="busy"
              type="date"
              v-model="due"
              :aria-label="t('单据到期日期')" /></label
          ><label class="control-field"
            ><span>{{ t("单据说明") }}</span
            ><el-input
              :disabled="busy"
              v-model="description"
              :aria-label="t('单据说明')"
              maxlength="500" /></label
          ><el-button :loading="busy" @click="run(createDocument)">{{
            t("新增单据")
          }}</el-button>
        </div></el-card
      ></template
    >
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="documents"
      row-key="id"
      ><el-table-column
        prop="number"
        :label="t('单号')"
        min-width="115"
      /><el-table-column
        prop="counterpartyName"
        :label="t('往来方')"
        min-width="115"
      /><el-table-column
        prop="currency"
        :label="t('币种')"
        min-width="115"
      /><el-table-column
        prop="amount"
        :label="t('总额')"
        min-width="115"
      /><el-table-column
        prop="settled"
        :label="t('已结')"
        min-width="115"
      /><el-table-column
        prop="remaining"
        :label="t('未结')"
        min-width="115"
      /><el-table-column
        prop="dueDate"
        :label="t('到期日')"
        min-width="115"
      /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
      ><el-table-column :label="t('操作')" min-width="115"
        ><template #default="scope"
          ><button
            :disabled="busy"
            :aria-label="t('选择单据 ') + scope.row.number"
            @click="run(() => selectDocument(scope.row))"
          >
            {{ t("选择") }}
          </button></template
        ></el-table-column
      ></el-table
    >
    <el-button
      :disabled="docPage === 1 || busy"
      @click="
        docPage--;
        run(async () => {});
      "
      >{{ t("单据上一页") }}</el-button
    ><el-button
      :disabled="docPage * 20 >= docTotal || busy"
      @click="
        docPage++;
        run(async () => {});
      "
      >{{ t("单据下一页") }}</el-button
    >
    <el-card v-if="selected && canWrite"
      ><h4>
        {{ t("结算") }} {{ selected.number }} {{ t("· 未结") }}
        {{ selected.remaining }}
        {{ selected.currency }}
      </h4>
      <div class="control-row">
        <label class="control-field"
          ><span>{{ t("结算账户") }}</span
          ><select
            :disabled="busy"
            v-model="settlementAccount"
            :aria-label="t('结算账户')"
          >
            <option value="">{{ t("选择账户") }}</option>
            <option
              v-for="a in accounts.filter(
                (x) =>
                  x.status === 'Active' && x.currency === selected.currency,
              )"
              :key="a.id"
              :value="a.id"
            >
              {{ a.name }}
            </option>
          </select></label
        ><label class="control-field"
          ><span>{{ t("结算金额") }}</span
          ><el-input
            :disabled="busy"
            v-model="settlementAmount"
            :aria-label="t('结算金额')" /></label
        ><label class="control-field"
          ><span>{{ t("结算日期") }}</span
          ><input
            :disabled="busy"
            v-model="settlementDate"
            type="date"
            :aria-label="t('结算日期')" /></label
        ><el-button :loading="busy" @click="run(settle)">{{
          t("登记结算")
        }}</el-button
        ><el-button :loading="busy" @click="run(cancelDocument)">{{
          t("取消单据")
        }}</el-button>
      </div>
      <p>{{ t("取消要求已结为零；结算撤销请在财务流水中操作。") }}</p></el-card
    >
    <el-card v-if="documentDetail"
      ><AttachmentPanel
        :api="api"
        :organization-id="organizationId"
        :role="role"
        :resource-kind="docKind === 'receivables' ? 'Receivable' : 'Payable'"
        :resource-id="documentDetail.id" /><el-button
        :disabled="detailPage === 1 || busy"
        @click="
          detailPage--;
          run(loadDocumentDetail);
        "
        >{{ t("结算历史上一页") }}</el-button
      ><el-button
        :disabled="detailPage * 20 >= documentDetail.settlements.total || busy"
        @click="
          detailPage++;
          run(loadDocumentDetail);
        "
        >{{ t("结算历史下一页") }}</el-button
      >
      <h4>{{ t("单据结算历史 ·") }} {{ documentDetail.number }}</h4>
      <p>
        {{ t("共") }} {{ documentDetail.settlements.total }} {{ t("笔，第") }}
        {{ detailPage }} {{ t("页。") }}
      </p>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="documentDetail.settlements.items"
        ><el-table-column
          prop="businessDate"
          :label="t('业务日期')"
          min-width="115" /><el-table-column
          prop="amount"
          :label="t('金额')"
          min-width="165" /><el-table-column :label="t('方向')" min-width="115"
          ><template #default="scope"
            ><StatusBadge :value="scope.row.kind" /></template></el-table-column
        ><el-table-column
          prop="reversalOfId"
          :label="t('撤销原交易')"
          min-width="115" /></el-table
    ></el-card>
    <h3 class="section-heading" id="businesspanel-2">{{ t("费用预算") }}</h3>
    <p>
      {{ t("新预算保存为草稿，启用后生效；调整通过新版本保留历史。")
      }}{{ revisionOf ? t("正在调整预算 ") + revisionOf : "" }}
    </p>
    <el-card v-if="canWrite"
      ><div class="control-row">
        <label class="control-field"
          ><span>{{ t("预算名称") }}</span
          ><el-input
            :disabled="busy"
            v-model="budgetName"
            :aria-label="t('预算名称')"
            :placeholder="t('预算名称')" /></label
        ><label class="control-field"
          ><span>{{ t("预算币种") }}</span
          ><el-input
            :disabled="busy"
            v-model="budgetCurrency"
            :aria-label="t('预算币种')" /></label
        ><label class="control-field"
          ><span>{{ t("预算开始日期") }}</span
          ><input
            :disabled="busy"
            v-model="start"
            type="date"
            :aria-label="t('预算开始日期')" /></label
        ><label class="control-field"
          ><span>{{ t("预算结束日期") }}</span
          ><input
            :disabled="busy"
            v-model="end"
            type="date"
            :aria-label="t('预算结束日期')"
        /></label>
      </div>
      <div v-for="(line, index) in lines" :key="index">
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("预算分类") }}</span
            ><select
              :disabled="busy"
              v-model="line.categoryId"
              :aria-label="t('预算分类 ') + index"
            >
              <option value="">{{ t("支出分类") }}</option>
              <option
                v-for="c in categories.filter(
                  (x) => x.kind === 'Expense' && x.status === 'Active',
                )"
                :key="c.id"
                :value="c.id"
              >
                {{ c.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("预算金额") }}</span
            ><el-input
              :disabled="busy"
              v-model="line.amount"
              :aria-label="t('预算金额 ') + index" /></label
          ><button :disabled="busy" @click="lines.splice(index, 1)">
            {{ t("移除") }}
          </button>
        </div>
      </div>
      <el-button @click="lines.push({ categoryId: '', amount: '' })">{{
        t("添加预算明细")
      }}</el-button
      ><el-button :loading="busy" @click="run(createBudget)">{{
        t("制定预算")
      }}</el-button></el-card
    >
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="budgets"
      row-key="id"
      ><el-table-column
        prop="name"
        :label="t('预算')"
        min-width="115"
      /><el-table-column
        prop="currency"
        :label="t('币种')"
        min-width="115"
      /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
      ><el-table-column
        prop="startDate"
        :label="t('开始')"
        min-width="115"
      /><el-table-column
        prop="endDate"
        :label="t('结束')"
        min-width="115"
      /><el-table-column :label="t('执行')" min-width="115"
        ><template #default="scope"
          ><button
            :disabled="busy"
            :aria-label="t('执行分析 ') + scope.row.name"
            @click="run(() => showExecution(scope.row))"
          >
            {{ t("查看") }}</button
          ><template v-if="canWrite"
            ><button
              :disabled="busy"
              v-if="scope.row.status === 'Draft'"
              @click="run(() => budgetStatus(scope.row, 'Active'))"
            >
              {{ t("启用") }}</button
            ><button
              :disabled="busy"
              v-if="scope.row.status === 'Active'"
              @click="run(() => budgetStatus(scope.row, 'Closed'))"
            >
              {{ t("关闭") }}</button
            ><button
              :disabled="busy"
              v-if="scope.row.status === 'Active'"
              @click="run(() => revise(scope.row))"
            >
              {{ t("调整版本") }}
            </button></template
          ></template
        ></el-table-column
      ></el-table
    ><el-button
      :disabled="budgetPage === 1 || busy"
      @click="
        budgetPage--;
        run(async () => {});
      "
      >{{ t("预算上一页") }}</el-button
    ><el-button
      :disabled="budgetPage * 20 >= budgetTotal || busy"
      @click="
        budgetPage++;
        run(async () => {});
      "
      >{{ t("预算下一页") }}</el-button
    >
    <template v-if="execution"
      ><h4>
        {{ execution.name }} · {{ execution.currency }} {{ t("· 执行分析") }}
      </h4>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="execution.items"
        ><el-table-column
          prop="name"
          :label="t('分类')"
          min-width="115"
        /><el-table-column
          prop="planned"
          :label="t('预算')"
          min-width="115"
        /><el-table-column
          prop="actual"
          :label="t('实际')"
          min-width="165"
        /><el-table-column
          prop="remaining"
          :label="t('剩余')"
          min-width="115"
        /><el-table-column :label="t('预警')" min-width="115"
          ><template #default="scope"
            ><strong v-if="scope.row.overBudget" style="color: #b42318">{{
              t("超支")
            }}</strong
            ><span v-else>{{ t("正常") }}</span></template
          ></el-table-column
        ></el-table
      ></template
    >
    <h3 class="section-heading" id="businesspanel-3">
      {{ t("Excel 交易导出") }}
    </h3>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("导出开始日期") }}</span
        ><input
          :disabled="busy"
          v-model="exportFrom"
          type="date"
          :aria-label="t('导出开始日期')" /></label
      ><label class="control-field"
        ><span>{{ t("导出结束日期") }}</span
        ><input
          :disabled="busy"
          v-model="exportTo"
          type="date"
          :aria-label="t('导出结束日期')" /></label
      ><el-button :loading="busy" @click="run(exportExcel)">{{
        t("导出 Excel")
      }}</el-button>
    </div>
    <p>{{ t("最多10000条，金额以精确文本保留全部有效位。") }}</p>
    <p v-if="message" role="status" class="feedback" :class="{ error: failed }">
      {{ localizeMessage(message) }}
    </p>
  </section>
</template>
