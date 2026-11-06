<script setup lang="ts">
import { t, localizeMessage } from "./i18n";
import { ref, onMounted, watch, computed } from "vue";
import { units, unitText } from "./ui";
import AttachmentPanel from "./AttachmentPanel.vue";
import { errorText } from "./ui";
import type { AxiosInstance } from "axios";
const failed = ref(false);
const props = defineProps<{
  api: AxiosInstance;
  organizationId: string;
  role: string;
}>();
type Resource = {
  id: string;
  name: string;
  status: string;
  kind: string;
  currency?: string;
  balance?: string;
};
const accounts = ref<Resource[]>([]),
  categories = ref<Resource[]>([]),
  transactions = ref<any[]>([]);
const accountName = ref(""),
  currency = ref("CNY"),
  accountKind = ref("Bank"),
  categoryName = ref(""),
  categoryKind = ref("Income");
const kind = ref("Income"),
  accountId = ref(""),
  destinationId = ref(""),
  categoryId = ref(""),
  amount = ref(""),
  description = ref(""),
  message = ref(""),
  busy = ref(false);
const page = ref(1),
  total = ref(0),
  filterKind = ref(""),
  accountPage = ref(1),
  categoryPage = ref(1),
  accountTotal = ref(0),
  categoryTotal = ref(0);
const localToday = ref(""),
  businessDate = ref(""),
  timeZone = ref(""),
  filterAccount = ref(""),
  dateFrom = ref(""),
  dateTo = ref("");
const resourceType = ref("accounts"),
  resourceId = ref(""),
  resourceName = ref(""),
  resourceStatus = ref("Active");
const split = ref(false),
  allocations = ref([
    { categoryId: "", amount: "" },
    { categoryId: "", amount: "" },
  ]);
watch(
  kind,
  (value) => {
    if (!["Income", "Expense"].includes(value)) split.value = false;
    if (
      !categories.value.some(
        (c) => c.id === categoryId.value && c.kind === value,
      )
    )
      categoryId.value = "";
    if (value !== "Transfer") destinationId.value = "";
  },
  { flush: "sync" },
);
const splitTotal = computed(() =>
  allocations.value.reduce((sum, line) => sum + (units(line.amount) ?? 0n), 0n),
);
const splitMatches = computed(
  () =>
    units(amount.value) !== null &&
    splitTotal.value === units(amount.value) &&
    allocations.value.every((line) => units(line.amount) !== null),
);
const historicalWarnings = ref<any[]>([]);
async function checkHistory() {
  historicalWarnings.value = (
    await props.api.get(base() + "/historical-balance-warnings")
  ).data.items;
}
const correctionOf = ref<string | null>(null),
  receivableId = ref<string | null>(null),
  payableId = ref<string | null>(null);
const detail = ref<any>(null),
  reversalReason = ref(""),
  reconciliation = ref<any[]>([]);
let pendingRequest: { body: string; id: string } | null = null;
let reverseRequest: { body: string; id: string } | null = null;
const resourceSearch = ref("");
const base = () => "/organizations/" + props.organizationId;
const canWrite = () => ["Owner", "Accountant"].includes(props.role);
const empty = async () => {};
function validAmount(value: string) {
  return /^[0-9]{1,15}(\.[0-9]{1,4})?$/.test(value) && /[1-9]/.test(value);
}
async function load() {
  const ar = (
    await props.api.get(base() + "/accounts", {
      params: {
        page: accountPage.value,
        pageSize: 20,
        search: resourceSearch.value || undefined,
      },
    })
  ).data;
  accounts.value = ar.items;
  accountTotal.value = ar.total;
  const cr = (
    await props.api.get(base() + "/categories", {
      params: {
        page: categoryPage.value,
        pageSize: 20,
        search: resourceSearch.value || undefined,
      },
    })
  ).data;
  categories.value = cr.items;
  categoryTotal.value = cr.total;
  const result = (
    await props.api.get(base() + "/transactions", {
      params: {
        page: page.value,
        pageSize: 20,
        kind: filterKind.value || undefined,
        accountId: filterAccount.value || undefined,
        dateFrom: dateFrom.value || undefined,
        dateTo: dateTo.value || undefined,
      },
    })
  ).data;
  transactions.value = result.items;
  total.value = result.total;
}
async function run(fn: () => Promise<unknown>) {
  if (busy.value) return;
  message.value = "";
  failed.value = false;
  busy.value = true;
  try {
    await fn();
    message.value = "操作成功";
    await load();
  } catch (e: any) {
    failed.value = true;
    message.value = errorText(e);
  } finally {
    busy.value = false;
  }
}
async function addAccount() {
  if (!accountName.value.trim() || !/^[A-Z]{3}$/.test(currency.value))
    throw new Error("填写账户名称和三位大写币种");
  await props.api.post(base() + "/accounts", {
    name: accountName.value,
    currency: currency.value,
    kind: accountKind.value,
  });
  accountName.value = "";
}
async function addCategory() {
  if (!categoryName.value.trim()) throw new Error("填写分类名称");
  await props.api.post(base() + "/categories", {
    name: categoryName.value,
    kind: categoryKind.value,
  });
  categoryName.value = "";
}
async function post() {
  if (
    !validAmount(amount.value) ||
    !accountId.value ||
    !businessDate.value ||
    (kind.value === "Transfer"
      ? !destinationId.value || destinationId.value === accountId.value
      : kind.value !== "Opening" && !split.value && !categoryId.value)
  )
    throw new Error("请检查金额、业务日期、账户与分类");
  if (businessDate.value > localToday.value) {
    if (!window.confirm(t("未来日期交易将立即改变全部已登记余额。确认登记？")))
      return;
  }
  if (split.value) {
    if (
      !["Income", "Expense"].includes(kind.value) ||
      receivableId.value ||
      payableId.value
    )
      throw new Error("仅普通收支支持分摊");
    categoryId.value = allocations.value[0]?.categoryId ?? "";
  }
  const body = {
    allocations: split.value ? allocations.value : undefined,
    receivableId: receivableId.value,
    payableId: payableId.value,
    correctionOfId: correctionOf.value,
    kind: kind.value,
    accountId: accountId.value,
    destinationAccountId:
      kind.value === "Transfer" ? destinationId.value : null,
    categoryId: ["Transfer", "Opening"].includes(kind.value)
      ? null
      : categoryId.value,
    amount: amount.value,
    description: description.value,
    businessDate: businessDate.value,
  };
  const serialized = JSON.stringify(body);
  if (pendingRequest?.body !== serialized)
    pendingRequest = { body: serialized, id: crypto.randomUUID() };
  await props.api.post(
    base() + (kind.value === "Transfer" ? "/transfers" : "/transactions"),
    { ...body, requestId: pendingRequest.id },
  );
  pendingRequest = null;
  receivableId.value = null;
  payableId.value = null;
  correctionOf.value = null;
  amount.value = "";
  description.value = "";
}
function pickResource() {
  const item = (
    resourceType.value === "accounts" ? accounts.value : categories.value
  ).find((x) => x.id === resourceId.value);
  if (item) {
    resourceName.value = item.name;
    resourceStatus.value = item.status;
  }
}
async function patchResource() {
  if (!resourceId.value || !resourceName.value.trim())
    throw new Error("请选择资源并填写名称");
  await props.api.patch(
    base() + "/" + resourceType.value + "/" + resourceId.value,
    { name: resourceName.value, status: resourceStatus.value },
  );
}
async function showDetail(row: { id: string }) {
  detail.value = (await props.api.get(base() + "/transactions/" + row.id)).data;
}
async function reverse(correct = false) {
  if (!detail.value || !reversalReason.value.trim() || !businessDate.value)
    throw new Error("选择流水并填写撤销原因与业务日期");
  const original = detail.value.transaction;
  const body = {
    reason: reversalReason.value.trim(),
    businessDate: businessDate.value,
  };
  const serialized = original.id + JSON.stringify(body);
  if (reverseRequest?.body !== serialized)
    reverseRequest = { body: serialized, id: crypto.randomUUID() };
  await props.api.post(base() + "/transactions/" + original.id + "/reverse", {
    ...body,
    requestId: reverseRequest.id,
  });
  await showDetail(original);
  if (correct) {
    split.value = (detail.value.allocations?.length ?? 0) > 0;
    if (split.value)
      allocations.value = detail.value.allocations.map((x: any) => ({
        categoryId: x.categoryId,
        amount: x.amount,
      }));
    receivableId.value = original.receivableId;
    payableId.value = original.payableId;
    correctionOf.value = original.id;
    kind.value = original.kind;
    accountId.value = original.accountId;
    destinationId.value = original.destinationAccountId ?? "";
    categoryId.value = original.categoryId ?? "";
    amount.value = original.amount;
    description.value = (
      "更正原交易 " +
      original.id +
      "：" +
      original.description
    ).slice(0, 500);
  }
}
async function reconcile() {
  reconciliation.value = (await props.api.get(base() + "/reconciliation")).data;
}
onMounted(() =>
  run(async () => {
    const settings = (await props.api.get(base() + "/finance-settings")).data;
    timeZone.value = settings.timeZone;
    localToday.value = settings.today;
    businessDate.value = settings.today;
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
    <h2>{{ t("账户与流水") }}</h2>
    <p v-if="!canWrite()" class="note">
      {{
        t("当前角色可查看资金数据；登记和更正业务需要企业所有者或会计权限。")
      }}
    </p>
    <nav class="section-nav" :aria-label="t('本页导航')">
      <a href="#financepanel-0" @click.prevent="jump('financepanel-0')">{{
        t("账户与分类")
      }}</a
      ><a
        v-if="canWrite()"
        href="#financepanel-1"
        @click.prevent="jump('financepanel-1')"
        >{{ t("登记收支 / 转账") }}</a
      ><a href="#financepanel-2" @click.prevent="jump('financepanel-2')">{{
        t("交易流水")
      }}</a>
    </nav>
    <h3 class="section-heading" id="financepanel-0">{{ t("账户与分类") }}</h3>
    <template v-if="canWrite()"
      ><el-button @click="run(checkHistory)">{{
        t("检查历史负余额")
      }}</el-button>
      <p v-for="w in historicalWarnings" :key="w.accountId + w.businessDate">
        {{ t("历史负余额：")
        }}{{
          accounts.find((a) => a.id === w.accountId)?.name ?? w.accountId
        }}
        · {{ w.businessDate }} · {{ w.balance }}
      </p></template
    >
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("账户分类搜索") }}</span
        ><el-input
          :disabled="busy"
          v-model="resourceSearch"
          :aria-label="t('账户分类搜索')"
          :placeholder="t('搜索账户和分类')" /></label
      ><el-button
        @click="
          accountPage = 1;
          categoryPage = 1;
          run(empty);
        "
        >{{ t("搜索账户分类") }}</el-button
      >
    </div>
    <p>
      {{ t("企业时区：") }}{{ timeZone
      }}{{
        t(
          "。账户余额为全部已登记余额，包含未来业务日期；截至日期余额由报表单独统计。金额最多4位小数；不透支；仅同币种转账。更正须先撤销原交易。",
        )
      }}
    </p>

    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="accounts"
      ><el-table-column
        prop="name"
        :label="t('账户')"
        min-width="115" /><el-table-column
        prop="currency"
        :label="t('币种')"
        min-width="115" /><el-table-column
        prop="balance"
        :label="t('余额')"
        min-width="165" /><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
    ></el-table>
    <el-button
      :disabled="accountPage === 1 || busy"
      @click="
        accountPage--;
        run(empty);
      "
      >{{ t("账户上一页") }}</el-button
    ><el-button
      :disabled="accountPage * 20 >= accountTotal || busy"
      @click="
        accountPage++;
        run(empty);
      "
      >{{ t("账户下一页") }}</el-button
    >
    <template v-if="canWrite()">
      <el-card
        ><h3>{{ t("新增账户") }}</h3>
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("账户名称") }}</span
            ><el-input
              :disabled="busy"
              v-model="accountName"
              :aria-label="t('账户名称')"
              :placeholder="t('账户名称')" /></label
          ><label class="control-field"
            ><span>{{ t("币种") }}</span
            ><el-input
              :disabled="busy"
              v-model="currency"
              :aria-label="t('币种')"
              placeholder="CNY" /></label
          ><label class="control-field"
            ><span>{{ t("账户类型") }}</span
            ><select
              :disabled="busy"
              v-model="accountKind"
              :aria-label="t('账户类型')"
            >
              <option value="Bank">{{ t("银行") }}</option>
              <option value="Cash">{{ t("现金") }}</option>
            </select></label
          ><el-button type="primary" :loading="busy" @click="run(addAccount)">{{
            t("新增账户")
          }}</el-button>
        </div></el-card
      >
      <el-card
        ><h3>{{ t("新增分类") }}</h3>
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("分类名称") }}</span
            ><el-input
              :disabled="busy"
              v-model="categoryName"
              :aria-label="t('分类名称')"
              :placeholder="t('分类名称')" /></label
          ><label class="control-field"
            ><span>{{ t("分类类型") }}</span
            ><select
              :disabled="busy"
              v-model="categoryKind"
              :aria-label="t('分类类型')"
            >
              <option value="Income">{{ t("收入") }}</option>
              <option value="Expense">{{ t("支出") }}</option>
            </select></label
          ><el-button
            type="primary"
            :loading="busy"
            @click="run(addCategory)"
            >{{ t("新增分类") }}</el-button
          >
        </div></el-card
      >
    </template>
    <el-table
      :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
      :data="categories"
      ><el-table-column
        prop="name"
        :label="t('分类')"
        min-width="115" /><el-table-column :label="t('类型')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.kind" /></template></el-table-column
      ><el-table-column :label="t('状态')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.status" /></template></el-table-column
    ></el-table>
    <el-button
      :disabled="categoryPage === 1 || busy"
      @click="
        categoryPage--;
        run(empty);
      "
      >{{ t("分类上一页") }}</el-button
    ><el-button
      :disabled="categoryPage * 20 >= categoryTotal || busy"
      @click="
        categoryPage++;
        run(empty);
      "
      >{{ t("分类下一页") }}</el-button
    >
    <template v-if="canWrite()">
      <el-card
        ><h3>{{ t("账户 / 分类管理") }}</h3>
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("资源类型") }}</span
            ><select
              :disabled="busy"
              v-model="resourceType"
              :aria-label="t('资源类型')"
              @change="resourceId = ''"
            >
              <option value="accounts">{{ t("账户") }}</option>
              <option value="categories">{{ t("分类") }}</option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("管理资源") }}</span
            ><select
              :disabled="busy"
              v-model="resourceId"
              :aria-label="t('管理资源')"
              @change="pickResource"
            >
              <option value="">{{ t("请选择") }}</option>
              <option
                v-for="r in resourceType === 'accounts' ? accounts : categories"
                :key="r.id"
                :value="r.id"
              >
                {{ r.name }}
              </option>
            </select></label
          ><label class="control-field"
            ><span>{{ t("资源名称") }}</span
            ><el-input
              :disabled="busy"
              v-model="resourceName"
              :aria-label="t('资源名称')" /></label
          ><label class="control-field"
            ><span>{{ t("资源状态") }}</span
            ><select
              :disabled="busy"
              v-model="resourceStatus"
              :aria-label="t('资源状态')"
            >
              <option value="Active">{{ t("已启用") }}</option>
              <option value="Inactive">{{ t("已停用") }}</option>
              <option value="Archived">{{ t("已归档") }}</option>
            </select></label
          ><el-button :loading="busy" @click="run(patchResource)">{{
            t("保存资源")
          }}</el-button>
        </div></el-card
      >
      <el-card
        ><h3 class="section-heading" id="financepanel-1">
          {{ t("登记收支 / 转账") }}
        </h3>
        <div class="control-row">
          <label
            >{{ t("业务日期")
            }}<input
              v-model="businessDate"
              type="date"
              :aria-label="t('业务日期')"
          /></label>
          <label class="control-field"
            ><span>{{ t("交易类型") }}</span
            ><select
              :disabled="busy"
              v-model="kind"
              :aria-label="t('交易类型')"
            >
              <option value="Income">{{ t("收入") }}</option>
              <option value="Expense">{{ t("支出") }}</option>
              <option value="Transfer">{{ t("转账") }}</option>
              <option value="Opening">{{ t("期初余额") }}</option>
            </select></label
          >
          <label class="control-field"
            ><span>{{ t("资金账户") }}</span
            ><select
              :disabled="busy"
              v-model="accountId"
              :aria-label="t('资金账户')"
            >
              <option value="">{{ t("请选择") }}</option>
              <option
                v-for="a in accounts.filter((x) => x.status === 'Active')"
                :key="a.id"
                :value="a.id"
              >
                {{ a.name }} ({{ a.currency }})
              </option>
            </select></label
          >
          <label class="control-field" v-if="kind === 'Transfer'"
            ><span>{{ t("转入账户") }}</span
            ><select v-model="destinationId" :aria-label="t('转入账户')">
              <option value="">{{ t("请选择") }}</option>
              <option
                v-for="a in accounts.filter(
                  (x) =>
                    x.id !== accountId &&
                    x.status === 'Active' &&
                    x.currency ===
                      accounts.find((a) => a.id === accountId)?.currency,
                )"
                :key="a.id"
                :value="a.id"
              >
                {{ a.name }}
              </option>
            </select></label
          >
          <label
            class="control-field"
            v-if="kind !== 'Transfer' && kind !== 'Opening' && !split"
            ><span>{{ t("交易分类") }}</span
            ><select v-model="categoryId" :aria-label="t('交易分类')">
              <option value="">{{ t("请选择") }}</option>
              <option
                v-for="c in categories.filter(
                  (x) => x.kind === kind && x.status === 'Active',
                )"
                :key="c.id"
                :value="c.id"
              >
                {{ c.name }}
              </option>
            </select></label
          >
        </div>
        <div class="control-row">
          <label class="control-field"
            ><span>{{ t("交易金额") }}</span
            ><el-input
              :disabled="busy"
              v-model="amount"
              :aria-label="t('交易金额')"
              :placeholder="t('金额，例如100.0001')" /></label
          ><label class="control-field"
            ><span>{{ t("交易说明") }}</span
            ><el-input
              :disabled="busy"
              v-model="description"
              :aria-label="t('交易说明')"
              maxlength="500"
              :placeholder="t('说明 / 期初来源')"
          /></label>
        </div>
        <template v-if="canWrite() && ['Income', 'Expense'].includes(kind)"
          ><label
            ><input
              type="checkbox"
              v-model="split"
              :disabled="busy"
              :aria-label="t('启用分类分摊')"
            />{{ t("按多个分类分摊") }}</label
          >
          <div v-if="split">
            <p>
              {{ t("明细合计须等于交易总额，仅普通收支可用。") }}<br />{{
                t("当前合计")
              }}
              {{ unitText(splitTotal) }} ·
              {{
                splitMatches
                  ? t("已与交易金额一致")
                  : t("请核对交易总额与分摊明细")
              }}
            </p>
            <div v-for="(line, index) in allocations" :key="index">
              <div class="control-row">
                <label class="control-field"
                  ><span>{{ t("分摊分类") }}</span
                  ><select
                    v-model="line.categoryId"
                    :disabled="busy"
                    :aria-label="t('分摊分类 ') + index"
                  >
                    <option value="">{{ t("选择分类") }}</option>
                    <option
                      v-for="c in categories.filter(
                        (x) => x.kind === kind && x.status === 'Active',
                      )"
                      :key="c.id"
                      :value="c.id"
                    >
                      {{ c.name }}
                    </option>
                  </select></label
                ><label class="control-field"
                  ><span>{{ t("分摊金额") }}</span
                  ><el-input
                    v-model="line.amount"
                    :disabled="busy"
                    :aria-label="t('分摊金额 ') + index" /></label
                ><button
                  :disabled="busy || allocations.length <= 2"
                  @click="allocations.splice(index, 1)"
                >
                  {{ t("移除分摊") }}
                </button>
              </div>
            </div>
            <el-button
              :disabled="busy || allocations.length >= 100"
              @click="allocations.push({ categoryId: '', amount: '' })"
              >{{ t("增加分摊分类") }}</el-button
            >
          </div></template
        ><el-button type="primary" :loading="busy" @click="run(post)">{{
          t("提交交易")
        }}</el-button></el-card
      >
      <el-button :loading="busy" @click="run(reconcile)">{{
        t("核对余额")
      }}</el-button
      ><el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        v-if="reconciliation.length"
        :data="reconciliation"
        ><el-table-column
          prop="name"
          :label="t('账户')"
          min-width="115" /><el-table-column
          prop="currency"
          :label="t('币种')"
          min-width="115" /><el-table-column
          prop="actual"
          :label="t('实际余额')"
          min-width="165" /><el-table-column
          prop="expected"
          :label="t('流水余额')"
          min-width="165" /><el-table-column
          prop="difference"
          :label="t('差额')"
          min-width="115"
      /></el-table>
    </template>
    <h3 class="section-heading" id="financepanel-2">
      {{ t("交易流水（点击查看详情）") }}
    </h3>
    <div class="control-row">
      <label class="control-field"
        ><span>{{ t("交易筛选") }}</span
        ><select
          :disabled="busy"
          v-model="filterKind"
          :aria-label="t('交易筛选')"
        >
          <option value="">{{ t("全部") }}</option>
          <option value="Income">{{ t("收入") }}</option>
          <option value="Expense">{{ t("支出") }}</option>
          <option value="Transfer">{{ t("转账") }}</option>
          <option value="Opening">{{ t("期初余额") }}</option>
        </select></label
      ><label class="control-field"
        ><span>{{ t("账户筛选") }}</span
        ><select
          :disabled="busy"
          v-model="filterAccount"
          :aria-label="t('账户筛选')"
        >
          <option value="">{{ t("全部账户") }}</option>
          <option v-for="a in accounts" :key="a.id" :value="a.id">
            {{ a.name }}
          </option>
        </select></label
      >
      <label
        >{{ t("起始日期")
        }}<input
          v-model="dateFrom"
          type="date"
          :aria-label="t('起始日期')" /></label
      ><label
        >{{ t("结束日期")
        }}<input
          v-model="dateTo"
          type="date"
          :aria-label="t('结束日期')" /></label
      ><el-button
        @click="
          page = 1;
          run(empty);
        "
        >{{ t("查询流水") }}</el-button
      >
    </div>
    <el-table
      row-key="id"
      :data="transactions"
      @row-click="(row: { id: string }) => run(() => showDetail(row))"
      ><el-table-column
        prop="businessDate"
        :label="t('业务日期')"
        min-width="115"
      /><el-table-column :label="t('记账时间')" min-width="170"
        ><template #default="scope"
          ><TimeText :value="scope.row.createdAt" /></template></el-table-column
      ><el-table-column :label="t('类型')" min-width="115"
        ><template #default="scope"
          ><StatusBadge :value="scope.row.kind" /></template></el-table-column
      ><el-table-column
        prop="amount"
        :label="t('金额')"
        min-width="165"
      /><el-table-column
        prop="description"
        :label="t('说明')"
        min-width="115"
      /><el-table-column :label="t('详情')" min-width="115"
        ><template #default="scope"
          ><button
            :disabled="busy"
            :aria-label="t('查看流水 ') + scope.row.description"
            @click.stop="run(() => showDetail(scope.row))"
          >
            {{ t("查看") }}
          </button></template
        ></el-table-column
      ></el-table
    >
    <el-button
      :disabled="page === 1 || busy"
      @click="
        page--;
        run(empty);
      "
      >{{ t("上一页") }}</el-button
    ><span
      >{{ t("第") }}{{ page }}{{ t("页 / 共") }}{{ total }}{{ t("条") }}</span
    ><el-button
      :disabled="page * 20 >= total || busy"
      @click="
        page++;
        run(empty);
      "
      >{{ t("下一页") }}</el-button
    >
    <el-card v-if="detail"
      ><h3>{{ t("流水详情") }}</h3>
      <p>
        {{ t("账户：") }}{{ detail.accountName }} →
        {{ detail.destinationName ?? "—" }}{{ t("；币种：")
        }}{{ detail.currency }}{{ t("；分类：") }}{{ detail.categoryName ?? "—"
        }}{{ t("；操作人：") }}{{ detail.actor }}
      </p>
      <p>
        {{ t("原交易：") }}{{ detail.transaction.reversalOfId ?? "—"
        }}{{ t("；撤销流水：") }}{{ detail.reversedBy ?? "—"
        }}{{ t("；更正流水：") }}{{ detail.correctedBy ?? "—" }}
      </p>
      <dl class="detail-grid">
        <div>
          <dt>{{ t("业务日期") }}</dt>
          <dd>{{ detail.transaction.businessDate }}</dd>
        </div>
        <div>
          <dt>{{ t("交易类型") }}</dt>
          <dd><StatusBadge :value="detail.transaction.kind" /></dd>
        </div>
        <div>
          <dt>{{ t("金额") }}</dt>
          <dd>{{ detail.transaction.amount }} {{ detail.currency }}</dd>
        </div>
        <div>
          <dt>{{ t("交易说明") }}</dt>
          <dd>{{ detail.transaction.description || "—" }}</dd>
        </div>
        <div>
          <dt>{{ t("流水编号") }}</dt>
          <dd class="identifier">{{ detail.transaction.id }}</dd>
        </div>
      </dl>
      <template
        v-if="
          canWrite() && !detail.transaction.reversalOfId && !detail.reversedBy
        "
        ><div class="control-row">
          <label class="control-field"
            ><span>{{ t("撤销原因") }}</span
            ><el-input
              :disabled="busy"
              v-model="reversalReason"
              :aria-label="t('撤销原因')"
              :placeholder="t('撤销原因')"
              maxlength="500" /></label
          ><el-button :loading="busy" @click="run(() => reverse())">{{
            t("撤销交易")
          }}</el-button
          ><el-button :loading="busy" @click="run(() => reverse(true))">{{
            t("撤销并准备更正")
          }}</el-button>
        </div>
        <p>
          {{
            t(
              "准备更正后，请检查上方交易表单并重新提交；已撤销的原记录会保留。",
            )
          }}
        </p></template
      ></el-card
    >
    <template v-if="detail"
      ><h3>{{ t("分类分摊明细") }}</h3>
      <el-table
        :empty-text="t('暂无记录，请调整筛选条件或新增业务')"
        :data="detail.allocations"
        ><el-table-column
          prop="categoryId"
          :label="t('分类编号')"
          min-width="165" /><el-table-column
          prop="amount"
          :label="t('分摊金额')"
          min-width="165" /></el-table
      ><AttachmentPanel
        :key="detail.transaction.id"
        :api="api"
        :organization-id="organizationId"
        :role="role"
        resource-kind="Transaction"
        :resource-id="detail.transaction.id"
    /></template>
    <p v-if="message" role="status" class="feedback" :class="{ error: failed }">
      {{ localizeMessage(message) }}
    </p>
  </section>
</template>
