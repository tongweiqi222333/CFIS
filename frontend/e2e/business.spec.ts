import { test, expect } from "@playwright/test";
test("往来方、应收应付、预算及Excel真实流程", async ({ page }) => {
  test.setTimeout(90000);
  await page.goto("/");
  await page
    .getByLabel("邮箱", { exact: true })
    .fill("business-" + Date.now() + "@example.test");
  await page.getByLabel("密码", { exact: true }).fill("Business-Test!123456");
  await page.getByRole("button", { name: "注册", exact: true }).click();
  await expect(page.getByRole("status").first()).toHaveText("操作成功");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await page.getByPlaceholder("企业名称").fill("业务测试企业");
  await page.getByRole("button", { name: "创建", exact: true }).click();
  await page.getByRole("cell", { name: "业务测试企业", exact: true }).click();
  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  await page.getByLabel("账户名称", { exact: true }).fill("业务银行");
  await page.getByRole("button", { name: "新增账户", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "业务银行", exact: true }).first(),
  ).toBeVisible();
  for (const [name, type] of [
    ["业务收入", "Income"],
    ["业务费用", "Expense"],
  ]) {
    await page.getByLabel("分类名称", { exact: true }).fill(name);
    await page
      .getByRole("combobox", { name: "分类类型", exact: true })
      .selectOption(type);
    await page.getByRole("button", { name: "新增分类", exact: true }).click();
    await expect(
      page.getByRole("cell", { name, exact: true }).first(),
    ).toBeVisible();
  }
  await page
    .getByRole("combobox", { name: "交易类型", exact: true })
    .selectOption("Opening");
  await page
    .getByRole("combobox", { name: "资金账户", exact: true })
    .selectOption({ label: "业务银行 (CNY)" });
  await page.getByLabel("交易金额", { exact: true }).fill("1000");
  await page.getByLabel("交易说明", { exact: true }).fill("业务测试期初");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "1000.0000", exact: true }).first(),
  ).toBeVisible();
  await page.getByRole("link", { name: "往来与预算", exact: true }).click();
  await page.getByLabel("往来方名称", { exact: true }).fill("业务客户供应商");
  await page
    .getByRole("combobox", { name: "往来方类型", exact: true })
    .selectOption("Both");
  await page.getByRole("button", { name: "新增往来方", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "业务客户供应商", exact: true }),
  ).toBeVisible();
  for (const [type, number, category, amount] of [
    ["receivables", "AR-E2E", "业务收入", "120"],
    ["payables", "AP-E2E", "业务费用", "80"],
  ]) {
    await page
      .getByRole("combobox", { name: "单据类型", exact: true })
      .selectOption(type);
    await page.getByLabel("单据编号", { exact: true }).fill(number);
    await page
      .getByRole("combobox", { name: "单据往来方", exact: true })
      .selectOption({ label: "业务客户供应商" });
    await page
      .getByRole("combobox", { name: "单据分类", exact: true })
      .selectOption({ label: category });
    await page.getByLabel("单据金额", { exact: true }).fill(amount);
    await page.getByRole("button", { name: "新增单据", exact: true }).click();
    await page
      .getByRole("button", { name: "选择单据 " + number, exact: true })
      .click();
    await page
      .getByRole("combobox", { name: "结算账户", exact: true })
      .selectOption({ label: "业务银行" });
    await page
      .getByLabel("结算金额", { exact: true })
      .fill(type === "receivables" ? "40" : "80");
    await page.getByRole("button", { name: "登记结算", exact: true }).click();
    if (type === "receivables")
      await expect(
        page.getByRole("heading", { name: /结算 AR-E2E · 未结 80.0000/ }),
      ).toBeVisible();
    else
      await expect(
        page.getByRole("cell", { name: "已结清", exact: true }),
      ).toBeVisible();
  }
  await page.getByLabel("预算名称", { exact: true }).fill("业务费用预算");
  await page
    .getByRole("combobox", { name: "预算分类 0", exact: true })
    .selectOption({ label: "业务费用" });
  await page.getByLabel("预算金额 0", { exact: true }).fill("70");
  await page.getByRole("button", { name: "制定预算", exact: true }).click();
  await page.getByRole("button", { name: "启用", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "已启用", exact: true }).last(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "执行分析 业务费用预算", exact: true })
    .click();
  await expect(page.getByText("超支", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "调整版本", exact: true }).click();
  await expect(page.getByLabel("预算名称", { exact: true })).toHaveValue(
    "业务费用预算 调整",
  );
  await page.getByLabel("预算金额 0", { exact: true }).fill("90");
  await page.getByRole("button", { name: "制定预算", exact: true }).click();
  await page.getByRole("button", { name: "启用", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "已关闭", exact: true }),
  ).toBeVisible();
  await page.getByLabel("单据状态筛选", { exact: true }).selectOption("Paid");
  await page.getByRole("button", { name: "筛选单据", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "选择单据 AP-E2E", exact: true }),
  ).toBeVisible();
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "导出 Excel", exact: true }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("CFIS-transactions.xlsx");
  expect(await download.failure()).toBeNull();

  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  await page
    .getByRole("button", { name: "查看流水 结算单据 AP-E2E", exact: true })
    .click();
  await page.getByLabel("撤销原因", { exact: true }).fill("更正应付结算");
  const reversalResponse = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" &&
      response.url().endsWith("/reverse"),
  );
  await page
    .getByRole("button", { name: "撤销并准备更正", exact: true })
    .click();
  const reversed = await reversalResponse;
  expect(reversed.ok()).toBeTruthy();
  await reversed.finished();
  await expect(page.getByLabel("交易金额", { exact: true })).toHaveValue(
    "80.0000",
  );
  await page.getByLabel("交易金额", { exact: true }).fill("75");
  await page.getByLabel("交易说明", { exact: true }).fill("更正应付结算测试");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("button", {
      name: "查看流水 更正应付结算测试",
      exact: true,
    }),
  ).toBeVisible();
  await page.getByRole("link", { name: "往来与预算", exact: true }).click();
  await page.getByLabel("单据类型", { exact: true }).selectOption("payables");
  await page.getByLabel("单据状态筛选", { exact: true }).selectOption("");
  await page.getByRole("button", { name: "筛选单据", exact: true }).click();
  await page
    .getByRole("button", { name: "选择单据 AP-E2E", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: /结算 AP-E2E · 未结 5.0000/ }),
  ).toBeVisible();
  await page.getByRole("link", { name: "财务报表", exact: true }).click();
  const reports = page.getByRole("region", { name: "财务报表" });
  await expect(
    reports.getByText("截至日期余额", { exact: true }),
  ).toBeVisible();
  await expect(reports.getByRole("img", { name: "收支趋势图" })).toBeVisible();
  await expect(
    reports.getByRole("cell", { name: "90.0000", exact: true }),
  ).toBeVisible();
  await reports.screenshot({ path: "test-results/reports-validation.png" });
  for (const format of ["PDF", "Excel"]) {
    const next = page.waitForEvent("download");
    await reports
      .getByRole("button", { name: "导出财务报表 " + format, exact: true })
      .click();
    const file = await next;
    expect(await file.failure()).toBeNull();
  }
});
