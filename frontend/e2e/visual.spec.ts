import { test, expect } from "@playwright/test";

test("所有页面桌面与手机布局、导航和企业切换", async ({ page }) => {
  test.setTimeout(150000);
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/");
  await page.screenshot({ path: "test-results/ui-login-desktop.png" });
  await page.setViewportSize({ width: 390, height: 900 });
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth + 1,
    ),
  ).toBeTruthy();
  await page.screenshot({ path: "test-results/ui-login-mobile.png" });
  await page.setViewportSize({ width: 1440, height: 900 });

  await page
    .getByLabel("邮箱", { exact: true })
    .fill(`visual-${Date.now()}@example.test`);
  await page.getByLabel("密码", { exact: true }).fill("Visual-Test!123456");
  await page.getByRole("button", { name: "注册", exact: true }).click();
  await expect(page.getByRole("status").first()).toHaveText("操作成功");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "先选择一家企业" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "前往工作空间" }).click();
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
  const routes = [
    ["工作空间", "workspace"],
    ["资金管理", "finance"],
    ["往来与预算", "business"],
    ["财务报表", "reports"],
    ["会计管理", "accounting"],
    ["业务附件", "attachments"],
    ["企业设置", "settings"],
    ["操作审计", "audit"],
    ["使用帮助", "help"],
  ];
  for (const [size, width] of [
    ["desktop", 1440],
    ["mobile", 390],
  ] as const) {
    await page.setViewportSize({ width, height: 900 });
    for (const [name, key] of routes) {
      if (size === "mobile")
        await page
          .getByRole("button", { name: "打开导航", exact: true })
          .click();
      await page
        .getByRole("navigation", { name: "主导航" })
        .getByRole("link", { name, exact: true })
        .click();
      await expect(page.locator("main h1")).toHaveText(name!);
      const titles: Record<string, string> = {
        workspace: "企业概览",
        finance: "账户与流水",
        business: "业务扩展",
        reports: "财务总览与报表",
        accounting: "会计引擎",
        attachments: "业务附件",
        settings: "企业成员",
        audit: "操作审计",
        help: "从第一笔业务开始",
      };
      await expect(
        page.getByRole("heading", { name: titles[key!]!, exact: true }).last(),
      ).toBeVisible();
      if (size === "mobile") {
        await expect(page.locator(".nav-scrim")).toHaveCount(0);
        await expect(page.locator(".sidebar")).toHaveCSS(
          "transform",
          "matrix(1, 0, 0, 1, -232, 0)",
        );
      }

      await page.waitForLoadState("networkidle");
      await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
      if (key === "reports")
        await expect(
          page.getByRole("img", { name: "收支趋势图" }),
        ).toBeVisible();
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
        `${size}: ${name} 页面溢出`,
      ).toBeTruthy();
      await page.screenshot({ path: `test-results/ui-${key}-${size}.png` });
      if (["finance", "business", "reports", "accounting"].includes(key!)) {
        const links = page
          .getByRole("navigation", { name: "本页导航" })
          .getByRole("link");
        for (let i = 0; i < (await links.count()); i++) {
          await links.nth(i).click();
          const anchor = await links.nth(i).getAttribute("href");
          await expect(page.locator(anchor!)).toBeInViewport();
          await page
            .locator(anchor!)
            .evaluate((el) =>
              el.scrollIntoView({ behavior: "instant", block: "start" }),
            );
          await page.screenshot({
            path: `test-results/ui-${key}-${size}-section-${i}.png`,
          });
        }
      }
    }
  }
  await page.getByRole("button", { name: "打开导航", exact: true }).click();
  await page.getByRole("link", { name: "工作空间", exact: true }).click();
  await page.getByPlaceholder("企业名称").fill("空白视觉验收企业");
  await page.getByRole("button", { name: "创建", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "空白视觉验收企业", exact: true }),
  ).toBeVisible();
  await page
    .getByLabel("切换企业", { exact: true })
    .selectOption({ label: "空白视觉验收企业" });
  await expect(
    page.getByRole("heading", { name: "当前企业：空白视觉验收企业" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "打开导航", exact: true }).click();
  await page.getByRole("link", { name: "资金管理", exact: true }).click();
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await expect(
    page.getByRole("cell", { name: "演示银行", exact: true }),
  ).toHaveCount(0);
  // Changing to a type without allocations must clear the hidden split state.
  await page.getByLabel("启用分类分摊", { exact: true }).check();
  await page.getByLabel("交易类型", { exact: true }).selectOption("Transfer");
  await expect(page.getByLabel("启用分类分摊", { exact: true })).toHaveCount(0);
  await page.getByLabel("交易类型", { exact: true }).selectOption("Income");
  await expect(
    page.getByLabel("启用分类分摊", { exact: true }),
  ).not.toBeChecked();
  // Real validation failure must remove the previous report and export actions.
  await page.getByRole("button", { name: "打开导航", exact: true }).click();
  await page.getByRole("link", { name: "财务报表", exact: true }).click();
  await expect(page.getByRole("img", { name: "收支趋势图" })).toBeVisible();
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await page.getByLabel("报表开始日期", { exact: true }).fill("2026-12-31");
  await page.getByLabel("报表截止日期", { exact: true }).fill("2026-01-01");
  await page.getByRole("button", { name: "查询报表", exact: true }).click();
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page.getByRole("img", { name: "收支趋势图" })).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "导出财务报表 PDF", exact: true }),
  ).toHaveCount(0);
  expect(errors).toEqual([]);
});
