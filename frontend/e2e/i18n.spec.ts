import { test, expect } from "@playwright/test";

test("双语完整覆盖、语言持久化和控件对齐", async ({ page }) => {
  test.setTimeout(150000);
  await page.setViewportSize({ width: 1440, height: 1000 });
  const email = `language-${Date.now()}@example.test`,
    password = "Language-Test!123456";
  await page.goto("/");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
  await page.getByLabel("邮箱", { exact: true }).fill(email);
  await page.getByLabel("密码", { exact: true }).fill(password);
  await page.getByRole("button", { name: "注册", exact: true }).click();
  await expect(page.getByRole("status").first()).toHaveText("操作成功");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await page.getByPlaceholder("企业名称").fill("Language QA");
  await page.getByRole("button", { name: "创建", exact: true }).click();
  await page.getByRole("cell", { name: "Language QA", exact: true }).click();

  await page.getByRole("link", { name: "会计管理", exact: true }).click();
  await page.waitForLoadState("networkidle");
  const sourceRow = page
    .getByRole("button", { name: "查找来源凭证", exact: true })
    .locator("..");
  await expect(sourceRow).toBeVisible();
  await expect(page.getByRole("button",{name:"查找来源凭证",exact:true})).toBeEnabled();
  const sourceBoxes = await sourceRow
    .locator("select,.el-input,.el-button")
    .evaluateAll((elements) =>
      elements.map((el) => {
        const r = el.getBoundingClientRect();
        return { top: r.top, bottom: r.bottom };
      }),
    );
  expect(sourceBoxes).toHaveLength(3);
  expect(
    Math.max(...sourceBoxes.map((b) => b.top)) -
      Math.min(...sourceBoxes.map((b) => b.top)),
  ).toBeLessThanOrEqual(1);
  expect(
    Math.max(...sourceBoxes.map((b) => b.bottom)) -
      Math.min(...sourceBoxes.map((b) => b.bottom)),
  ).toBeLessThanOrEqual(1);
  await sourceRow.evaluate((el) =>
    el.scrollIntoView({ behavior: "instant", block: "center" }),
  );
  await page.evaluate(
    () =>
      new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      ),
  );
  await sourceRow.screenshot({ path: "test-results/alignment-source-zh.png" });
  await page
    .getByLabel("追溯来源编号", { exact: true })
    .fill("draft-input-preserved");
  await page
    .getByRole("button", { name: "Switch to English", exact: true })
    .click();
  await expect(
    page.getByLabel("Source reference", { exact: true }),
  ).toHaveValue("draft-input-preserved");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  const routes = [
    ["Workspace", "workspace"],
    ["Cash management", "finance"],
    ["Business and budgets", "business"],
    ["Financial reports", "reports"],
    ["Accounting", "accounting"],
    ["Business attachments", "attachments"],
    ["Organization settings", "settings"],
    ["Audit logs", "audit"],
    ["Help", "help"],
  ];
  for (const [width, size] of [
    [1440, "desktop"],
    [390, "mobile"],
  ] as const) {
    await page.setViewportSize({ width, height: 1000 });
    for (const [title, key] of routes) {
      if (size === "mobile")
        await page
          .getByRole("button", { name: "Open navigation", exact: true })
          .click();
      await page
        .getByRole("navigation", { name: "Main navigation" })
        .getByRole("link", { name: title, exact: true })
        .click();
      await expect(page.locator("main h1")).toHaveText(title!);
      await page.waitForLoadState("networkidle");
      const headings: Record<string, string> = {
        workspace: "Organization overview",
        finance: "Accounts and transactions",
        business: "Business operations",
        reports: "Financial overview and reports",
        accounting: "Accounting",
        attachments: "Business attachments",
        settings: "Organization members",
        audit: "Audit logs",
        help: "Start with your first transaction",
      };
      await expect(
        page
          .getByRole("heading", { name: headings[key!]!, exact: true })
          .last(),
      ).toBeVisible();
      await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
      if (size === "mobile")
        await expect(page.locator(".sidebar")).toHaveCSS(
          "transform",
          "matrix(1, 0, 0, 1, -232, 0)",
        );
      await page.evaluate(
        () =>
          new Promise<void>((resolve) =>
            requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
          ),
      );
      if (size === "mobile") {
        const hints = await page
          .locator(".module-panel .el-table")
          .evaluateAll((tables) =>
            tables.map((table) => getComputedStyle(table, "::after").content),
          );
        for (const hint of hints)
          expect(hint).toContain("Swipe horizontally to see more");
      }
      const clippedEmptyStates=await page.locator('.el-table__empty-text').evaluateAll(elements=>elements.filter(element=>{
        const table=element.closest('.el-table')!.getBoundingClientRect(),text=element.getBoundingClientRect();
        return text.width>0&&(text.left<table.left-1||text.right>table.right+1);
      }).map(element=>element.textContent));
      expect(clippedEmptyStates,`${key} empty text is clipped`).toEqual([]);
      const chinese = await page
        .locator("body")
        .evaluate((body) =>
          body.innerText.replace("中文", "").match(/[\u3400-\u9fff]+/g),
        );
      expect(chinese, `${key} has untranslated interface text`).toBeNull();
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
        `${key} page overflow`,
      ).toBeTruthy();
      if (key === "accounting" && size === "desktop") {
        const row = page
          .getByRole("button", { name: "Find source journal", exact: true })
          .locator("..");
        const fields = row.locator("select,.el-input,.el-button");
        await expect(fields).toHaveCount(3);
        const boxes = await fields.evaluateAll((elements) =>
          elements.map((el) => {
            const r = el.getBoundingClientRect();
            return { top: r.top, bottom: r.bottom };
          }),
        );
        expect(
          Math.max(...boxes.map((b) => b.top)) -
            Math.min(...boxes.map((b) => b.top)),
        ).toBeLessThanOrEqual(1);
        expect(
          Math.max(...boxes.map((b) => b.bottom)) -
            Math.min(...boxes.map((b) => b.bottom)),
        ).toBeLessThanOrEqual(1);
        await row.evaluate((el) =>
          el.scrollIntoView({ behavior: "instant", block: "center" }),
        );
        await page.evaluate(
          () =>
            new Promise<void>((resolve) =>
              requestAnimationFrame(() =>
                requestAnimationFrame(() => resolve()),
              ),
            ),
        );
        await row.screenshot({ path: "test-results/alignment-source-en.png" });
      }
      if (size === "desktop") {
        // Each visual row must align the bottoms of its fields and actions.
        const issues = await page.locator(".control-row, .form-grid").evaluateAll((rows) =>
          rows.flatMap((row, index) => {
            const controls = Array.from(row.children)
              .flatMap((child) => {
                if (child.tagName === "LABEL")
                  return Array.from(child.children).filter((el) =>
                    el.matches(
                      "select,input:not([type=checkbox]):not([type=file]),.el-input,.el-select,.file-picker",
                    ),
                  );
                return child.matches(".el-button") ? [child] : [];
              })
              .map((el) => el.getBoundingClientRect())
              .filter((r) => r.width > 0 && r.height > 0);
            return controls.slice(1).flatMap((box, i) => {
              const previous = controls[i]!;
              return box.left > previous.left &&
                Math.abs(box.top - previous.top) < 28 &&
                Math.abs(box.bottom - previous.bottom) > 1
                ? [`row ${index}: ${Math.abs(box.bottom - previous.bottom)}`]
                : [];
            });
          }),
        );
        expect(issues, `${key} misaligned form controls`).toEqual([]);
      }
      await page.screenshot({ path: `test-results/en-${key}-${size}.png` });
    }
  }
  // Locale must survive refresh, authentication and all subsequent pages.
  await page.reload();
  await expect(
    page.getByRole("button", { name: "Sign in", exact: true }),
  ).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page
    .getByRole("button", { name: "Open navigation", exact: true })
    .click();
  await page
    .getByRole("link", { name: "Organization settings", exact: true })
    .click();
  await page
    .getByRole("button", {
      name: "Account security and invitations",
      exact: true,
    })
    .click();
  await page.waitForLoadState("networkidle");
  for (const label of [
    "Accept organization invitation",
    "Recover password",
    "Change password",
    "Invitations and permissions",
  ]) {
    await page.getByRole("button", { name: label, exact: true }).click();
    const text = await page
      .locator("body")
      .evaluate((body) =>
        body.innerText.replace("中文", "").match(/[\u3400-\u9fff]+/g),
      );
    expect(text).toBeNull();
  }

  // A server-side validation error also uses the selected language.
  await page
    .getByRole("button", { name: "Open navigation", exact: true })
    .click();
  await page
    .getByRole("link", { name: "Financial reports", exact: true })
    .click();
  await expect(
    page.getByRole("img", { name: "Income and expense trend chart" }),
  ).toBeVisible();
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await page
    .getByLabel("Report start date", { exact: true })
    .fill("2026-12-31");
  await page.getByLabel("Report end date", { exact: true }).fill("2026-01-01");
  await page.getByRole("button", { name: "Run report", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("731");
  expect(await page.getByRole("alert").innerText()).not.toMatch(
    /[\u3400-\u9fff]/,
  );
  await page
    .getByRole("button", { name: "Open navigation", exact: true })
    .click();
  await page
    .getByRole("link", { name: "Organization settings", exact: true })
    .click();
  // Business data stays in its original language even when its name matches a UI phrase.
  await page
    .getByPlaceholder("Organization name", { exact: true })
    .fill("财务报表");
  await page.getByRole("button", { name: "Create", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "财务报表", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Sign in", exact: true }),
  ).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");

  const secondTab = await page.context().newPage();
  await secondTab.goto("/");
  await expect(secondTab.locator("html")).toHaveAttribute("lang", "en");
  await page
    .getByRole("button", { name: "Switch to Chinese", exact: true })
    .click();
  await expect(secondTab.locator("html")).toHaveAttribute("lang", "zh-CN");
  await secondTab.close();

  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
});
