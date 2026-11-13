const { test, expect } = require('@playwright/test');

test('homepage follows the TogetherCraft concept and keeps private addresses hidden', async ({ page }) => {
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  const response = await page.goto('/');
  expect(response && response.status()).toBe(200);

  await expect(page.locator('.brand')).toContainText('TogetherCraft');
  await expect(page.locator('.nav-actions .register-link')).toHaveCount(0);
  await expect(page.locator('.hero-actions a').first()).toHaveText(/Войти/);
  await expect(page.locator('.hero h1')).toBeVisible();
  await expect(page.locator('.world-card')).toHaveCount(3);
  for (const direction of ['Minecraft · Fabric', 'Minecraft · Create', 'Factorio']) {
    await expect(page.locator('.world-card').filter({ hasText: direction })).toHaveCount(1);
  }
  await expect(page.locator('.footer')).toBeVisible();
  await expect(page.locator('#gallery .gallery-thumbs img')).toHaveCount(3);
  await expect(page.locator('#team .team-avatars img')).toHaveCount(3);
  await expect(page.locator('#faq details')).toHaveCount(4);

  const brokenImages = await page.locator('#tc-landing img').evaluateAll(images =>
    images.filter(image => !image.complete || image.naturalWidth === 0).map(image => image.src)
  );
  expect(brokenImages).toEqual([]);

  const homepageText = await page.locator('body').innerText();
  for (const address of ['127.0.0.1', 'fried-newer.tun.ply.gg', 'shrimp-crafts.tun.ply.gg', '25565', '25566']) {
    expect(homepageText, 'Homepage must not expose ' + address).not.toContain(address);
  }
  expect(pageErrors).toEqual([]);
});

test('game worlds navigation moves to the directions section', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'Игровые миры' }).click();
  await expect(page).toHaveURL(/#worlds$/);
  await expect(page.locator('#worlds')).toBeVisible();
});

test('homepage has no horizontal overflow on desktop and mobile widths', async ({ page }) => {
  for (const width of [1440, 900, 390, 360]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/');
    const dimensions = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth
    }));
    expect(dimensions.content, 'Horizontal overflow at viewport width ' + width + 'px').toBeLessThanOrEqual(dimensions.viewport);
  }
});

test('login remains available while registration is disabled without Microsoft OAuth', async ({ page }) => {
  const loginResponse = await page.goto('/Account/Login');
  expect(loginResponse && loginResponse.status()).toBe(200);
  await expect(page.locator('form').first()).toBeVisible();

  const registerResponse = await page.goto('/Account/Register');
  expect(registerResponse && registerResponse.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Регистрация временно отключена' })).toBeVisible();
  await expect(page.locator('form')).toHaveCount(0);
});

test('connection page and status endpoints are private', async ({ request }) => {
  const routes = ['/Home/Play', '/Home/Server', '/Admin', '/Admin/Users', '/api/minecraft/servers', '/api/minecraft/status', '/api/metrics'];
  for (const route of routes) {
    const response = await request.get(route, { maxRedirects: 0 });
    expect(response.status(), route + ' must not be public').toBe(302);
  }
});
