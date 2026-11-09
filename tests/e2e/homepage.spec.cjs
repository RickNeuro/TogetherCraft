const { test, expect } = require('@playwright/test');

test('homepage renders the site header, both game worlds and footer', async ({ page }) => {
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));

  const response = await page.goto('/');
  expect(response && response.status()).toBe(200);

  await expect(page.locator('.brand')).toContainText('TogetherCraft');
  await expect(page.locator('.hero h1')).toBeVisible();
  await expect(page.locator('.server-card')).toHaveCount(2);
  await expect(page.locator('.footer')).toBeVisible();

  expect(pageErrors).toEqual([]);
});

test('server navigation moves to the game worlds section', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'Серверы' }).click();
  await expect(page).toHaveURL(/#servers$/);
  await expect(page.locator('#servers')).toBeVisible();
});

test('homepage has no horizontal overflow on desktop and mobile widths', async ({ page }) => {
  for (const width of [1440, 390, 360]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/');

    const dimensions = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth
    }));

    expect(
      dimensions.content,
      `Horizontal overflow at viewport width ${width}px: content is ${dimensions.content}px`
    ).toBeLessThanOrEqual(dimensions.viewport);
  }
});

test('login and registration pages render forms', async ({ page }) => {
  for (const route of ['/Account/Login', '/Account/Register']) {
    const response = await page.goto(route);
    expect(response && response.status(), `${route} should return HTTP 200`).toBe(200);
    await expect(page.locator('form').first()).toBeVisible();
  }
});


test('Minecraft status is public while host metrics require approval', async ({ request }) => {
  const statusResponse = await request.get('/api/minecraft/servers');
  expect(statusResponse.status()).toBe(200);
  const status = await statusResponse.json();
  expect(status.fabric).toBeTruthy();
  expect(status.create).toBeTruthy();
  expect(status.fabric).toHaveProperty('isOnline');
  expect(status.create).toHaveProperty('isOnline');

  const metricsResponse = await request.get('/api/metrics', { maxRedirects: 0 });
  expect(metricsResponse.status()).toBe(302);
});
