import { test, expect } from "@playwright/test";
test("v1演示、导航、精确分摊、附件上传下载和刷新直达", async ({ page }) => {
  test.setTimeout(120000);
  const email = "v1-" + Date.now() + "@example.test",
    password = "V1-Browser!123456";
  await page.goto("/");
  await page.getByLabel("邮箱", { exact: true }).fill(email);
  await page.getByLabel("密码", { exact: true }).fill(password);
  await page.getByRole("button", { name: "注册", exact: true }).click();
  await expect(page.getByRole("status").first()).toHaveText("操作成功");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  const demoResponse = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" &&
      response.url().endsWith("/api/v1/demo"),
    { timeout: 45000 },
  );
  await page
    .getByRole("button", { name: "生成或打开演示账套", exact: true })
    .click();
  expect((await demoResponse).ok()).toBeTruthy();
  await expect(
    page.getByRole("heading", {
      name: "当前企业：CFIS v1.0 演示账套（合成数据）",
    }),
  ).toBeVisible();
  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  await expect(page).toHaveURL(/\/finance\?org=/);
  await page
    .getByLabel("资金账户", { exact: true })
    .selectOption({ label: "演示银行 (CNY)" });
  await page.getByLabel("交易类型", { exact: true }).selectOption("Income");
  // Add a second income category, then split a single cash receipt.
  await page.getByLabel("分类名称", { exact: true }).fill("演示其他收入");
  await page.getByLabel("分类类型", { exact: true }).selectOption("Income");
  await page.getByRole("button", { name: "新增分类", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "演示其他收入", exact: true }).first(),
  ).toBeVisible();
  await page.getByLabel("启用分类分摊", { exact: true }).check();
  await page
    .getByLabel("分摊分类 0", { exact: true })
    .selectOption({ label: "演示销售" });
  await page.getByLabel("分摊金额 0", { exact: true }).fill("1");
  await page
    .getByLabel("分摊分类 1", { exact: true })
    .selectOption({ label: "演示其他收入" });
  await page.getByLabel("分摊金额 1", { exact: true }).fill("1.0001");
  await page.getByLabel("交易金额", { exact: true }).fill("2.0001");
  await page.getByLabel("交易说明", { exact: true }).fill("v1浏览器精确分摊");
  await page.getByRole("button", { name: "提交交易", exact: true }).click();
  await page
    .getByRole("button", { name: "查看流水 v1浏览器精确分摊", exact: true })
    .click();
  await expect(
    page.getByRole("cell", { name: "1.0001", exact: true }),
  ).toBeVisible();
  const attachments = page.getByRole("region", { name: "业务附件" });
  await attachments
    .getByLabel("选择附件", { exact: true })
    .setInputFiles({
      name: "v1-receipt.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("%PDF-1.4\nv1 test receipt\n%%EOF"),
    });
  await attachments
    .getByRole("button", { name: "上传附件", exact: true })
    .click();
  await expect(
    attachments.getByRole("button", { name: "v1-receipt.pdf", exact: true }),
  ).toBeVisible();
  const download = page.waitForEvent("download");
  await attachments
    .getByRole("button", { name: "v1-receipt.pdf", exact: true })
    .click();
  expect((await download).suggestedFilename()).toBe("v1-receipt.pdf");
  await page.getByRole("link", { name: "财务报表", exact: true }).click();
  await expect(
    page.getByRole("heading", {
      name: "分类收支（含分摊及冲销）",
      exact: true,
    }),
  ).toBeVisible();
  const url = page.url();
  await page.reload();
  await page.getByLabel("邮箱", { exact: true }).fill(email);
  await page.getByLabel("密码", { exact: true }).fill(password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page).toHaveURL(url);
  await expect(
    page.getByRole("heading", {
      name: "分类收支（含分摊及冲销）",
      exact: true,
    }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/v1-workspace.png",
    fullPage: true,
  });
});
