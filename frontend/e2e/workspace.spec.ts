import { test, expect } from "@playwright/test";
test("注册、登录、创建与切换企业、退出", async ({ page }) => {
  test.setTimeout(60000);
  await page.goto("/");
  await page
    .getByLabel("邮箱", { exact: true })
    .fill("browser-" + Date.now() + "@example.test");
  await page.getByLabel("密码", { exact: true }).fill("Browser-Test!123456");
  await page.getByRole("button", { name: "注册", exact: true }).click();
  await expect(page.getByRole("status").first()).toHaveText("操作成功");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("button", { name: "退出登录" })).toBeVisible();
  for (const name of ["浏览器企业甲", "浏览器企业乙"]) {
    await page.getByPlaceholder("企业名称").fill(name);
    await page.getByRole("button", { name: "创建", exact: true }).click();
    await expect(
      page.getByRole("cell", { name, exact: true }).first(),
    ).toBeVisible();
  }
  await page.getByRole("cell", { name: "浏览器企业甲", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "当前企业：浏览器企业甲" }),
  ).toBeVisible();
  await page.getByRole("cell", { name: "浏览器企业乙", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "当前企业：浏览器企业乙" }),
  ).toBeVisible();
  const memberEmail = "member-" + Date.now() + "@example.test";
  const registered = await page.request.post("/api/v1/auth/register", {
    data: { email: memberEmail, password: "Member-Test!123456" },
  });
  expect(registered.ok()).toBeTruthy();
  await page.getByRole("link", { name: "企业设置", exact: true }).click();
  await page.getByLabel("成员邮箱", { exact: true }).fill(memberEmail);
  await page.getByRole("button", { name: "添加成员", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: memberEmail, exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "账户安全与邀请", exact: true })
    .click();
  await page.getByRole("button", { name: "加载成员权限", exact: true }).click();
  const memberControls = page
    .locator("li")
    .filter({ hasText: memberEmail })
    .locator(".control-row");
  await expect(memberControls).toBeVisible();
  const controlBoxes = await memberControls
    .locator("select,.el-button")
    .evaluateAll((nodes) =>
      nodes.map((node) => {
        const box = node.getBoundingClientRect();
        return { top: box.top, bottom: box.bottom };
      }),
    );
  expect(controlBoxes).toHaveLength(3);
  expect(
    Math.max(...controlBoxes.map((b) => b.top)) -
      Math.min(...controlBoxes.map((b) => b.top)),
  ).toBeLessThanOrEqual(1);
  expect(
    Math.max(...controlBoxes.map((b) => b.bottom)) -
      Math.min(...controlBoxes.map((b) => b.bottom)),
  ).toBeLessThanOrEqual(1);

  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  for (const name of ["银行测试", "现金测试"]) {
    await page.getByLabel("账户名称", { exact: true }).fill(name);
    await page.getByRole("button", { name: "新增账户", exact: true }).click();
    await expect(
      page.getByRole("cell", { name, exact: true }).first(),
    ).toBeVisible();
  }
  await page.getByLabel("分类名称", { exact: true }).fill("销售");
  await page.getByRole("button", { name: "新增分类", exact: true }).click();
  await page
    .getByRole("combobox", { name: "资金账户", exact: true })
    .selectOption({ label: "银行测试 (CNY)" });

  await page
    .getByRole("combobox", { name: "交易分类", exact: true })
    .selectOption({ label: "销售" });

  await page.getByLabel("交易金额", { exact: true }).fill("100.0001");
  await page.getByLabel("交易说明", { exact: true }).fill("浏览器收入");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "100.0001", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("combobox", { name: "交易类型", exact: true })
    .selectOption("Transfer");

  await page
    .getByRole("combobox", { name: "转入账户", exact: true })
    .selectOption({ label: "现金测试" });

  await page.getByLabel("交易金额", { exact: true }).fill("25.0001");
  await page.getByLabel("交易说明", { exact: true }).fill("浏览器转账");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "75.0000", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("cell", { name: "25.0001", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "查看流水 浏览器转账", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "流水详情", exact: true }),
  ).toBeVisible();
  await page.getByLabel("撤销原因", { exact: true }).fill("浏览器撤销转账");
  await page.getByRole("button", { name: "撤销交易", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "撤销交易", exact: true }),
  ).not.toBeVisible();
  await expect(
    page.getByRole("cell", { name: "0.0000", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "查看流水 浏览器收入", exact: true })
    .click();
  await page.getByLabel("撤销原因", { exact: true }).fill("收入金额更正");
  await page
    .getByRole("button", { name: "撤销并准备更正", exact: true })
    .click();
  await expect(page.getByLabel("交易金额", { exact: true })).toHaveValue(
    "100.0001",
  );
  await page.getByLabel("交易金额", { exact: true }).fill("50.0001");
  await page.getByLabel("交易说明", { exact: true }).fill("浏览器更正收入");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "50.0001", exact: true }).first(),
  ).toBeVisible();
  await page.getByLabel("账户名称", { exact: true }).fill("期初账户");
  await page.getByRole("button", { name: "新增账户", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "期初账户", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("combobox", { name: "交易类型", exact: true })
    .selectOption("Opening");
  await page
    .getByRole("combobox", { name: "资金账户", exact: true })
    .selectOption({ label: "期初账户 (CNY)" });
  await page.getByLabel("交易金额", { exact: true }).fill("42.1234");
  await page.getByLabel("交易说明", { exact: true }).fill("浏览器期初来源");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "42.1234", exact: true }).first(),
  ).toBeVisible();
  await page.getByRole("button", { name: "核对余额", exact: true }).click();
  await expect(
    page.getByRole("columnheader", { name: "差额", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("combobox", { name: "管理资源", exact: true })
    .selectOption({ label: "银行测试" });
  await page.getByLabel("资源名称", { exact: true }).fill("银行已停用");
  await page
    .getByRole("combobox", { name: "资源状态", exact: true })
    .selectOption("Inactive");
  await page.getByRole("button", { name: "保存资源", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "银行已停用", exact: true }).first(),
  ).toBeVisible();
  await expect(
    page
      .getByRole("combobox", { name: "资金账户", exact: true })
      .locator("option")
      .filter({ hasText: "银行已停用" }),
  ).toHaveCount(0);
  await page.getByRole("link", { name: "企业设置", exact: true }).click();
  await page.getByRole("button", { name: "创建", exact: true }).click();
  await expect(page.getByRole("status").first()).toContainText(
    "请填写企业名称",
  );
  await page.getByRole("button", { name: "退出登录" }).click();
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
});
