const { test, expect } = require('@playwright/test');

test('homepage shows the agreed public introduction and keeps private addresses hidden', async ({ page }) => {
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  const response = await page.goto('/');
  expect(response && response.status()).toBe(200);

  await expect(page.locator('.brand')).toContainText('TogetherCraft');
  await expect(page.locator('.hero h1')).toContainText('Выбирай своё');
  await expect(page.locator('.hero-visual')).toBeVisible();
  await expect(page.locator('.home-worlds-strip > div')).toHaveCount(3);
  await expect(page.getByRole('link', { name: /Все игровые миры/ })).toHaveAttribute('href', '/Home/Worlds');
  await expect(page.getByRole('link', { name: /Открыть галерею/ })).toHaveAttribute('href', '/Home/Gallery');
  await expect(page.locator('.footer')).toBeVisible();

  const homepageText = await page.locator('body').innerText();
  for (const address of ['127.0.0.1', 'fried-newer.tun.ply.gg', 'shrimp-crafts.tun.ply.gg', '25565', '25566']) {
    expect(homepageText, 'Homepage must not expose ' + address).not.toContain(address);
  }
  expect(pageErrors).toEqual([]);
});

test('main navigation opens dedicated pages instead of jumping to homepage sections', async ({ page }) => {
  await page.goto('/');
  const destinations = [
    ['Игровые миры', '/Home/Worlds', 'Игровые миры'],
    ['Галерея', '/Home/Gallery', 'Галерея'],
    ['Команда', '/Home/Team', 'Команда TogetherCraft'],
    ['FAQ', '/Home/Faq', 'Частые вопросы']
  ];

  for (const [label, url, heading] of destinations) {
    await page.getByRole('link', { name: label, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(url.replaceAll('/', '\\/') + '$'));
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
  }
});

test('world pages load the original game artwork and keep full-size action buttons', async ({ page }) => {
  await page.goto('/Home/Worlds');
  await expect(page.locator('.world-card')).toHaveCount(3);

  const artwork = [
    ['.world-symbol-block img', '/img/game-assets/mossy-cobblestone.png'],
    ['.world-symbol-gear img', '/img/game-assets/create-large-cogwheel.png'],
    ['.world-symbol-factory img', '/img/game-assets/factorio-logo.png']
  ];

  for (const [selector, source] of artwork) {
    const image = page.locator(selector);
    await expect(image).toHaveAttribute('src', source);
    await expect.poll(() => image.evaluate(element => element.complete && element.naturalWidth > 0)).toBe(true);
  }

  const buttonHeight = await page.locator('.world-action').first().evaluate(element => element.getBoundingClientRect().height);
  expect(buttonHeight).toBeGreaterThanOrEqual(48);
  await expect(page.getByRole('link', { name: /Исследовать мир/ })).toHaveAttribute('href', /returnUrl=.*Home%2FVanilla/);
  await expect(page.getByRole('link', { name: /Изучить механизмы/ })).toHaveAttribute('href', /returnUrl=.*Home%2FCreate/);
});

test('gallery has a real empty state when no images have been uploaded', async ({ page }) => {
  await page.goto('/Home/Gallery');
  await expect(page.getByRole('heading', { name: 'Галерея', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Галерея пока наполняется' })).toBeVisible();
  await expect(page.locator('[data-gallery-open]')).toHaveCount(0);
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
  const routes = ['/Home/Play', '/Home/Vanilla', '/Home/Create', '/Home/Server', '/Admin', '/Admin/Users', '/Admin/Gallery', '/api/minecraft/servers', '/api/minecraft/status', '/api/metrics'];
  for (const route of routes) {
    const response = await request.get(route, { maxRedirects: 0 });
    expect(response.status(), route + ' must not be public').toBe(302);
  }
});

test('shared header is a transparent square bar and footer has no decorative cube', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');

  const header = page.locator('header.topbar');
  await expect(header).toBeVisible();
  await expect(header.locator('.navlinks')).toBeVisible();
  await expect(page.locator('.mobile-menu')).toHaveCount(0);
  await expect(page.locator('.footer-cube')).toHaveCount(0);
  await expect(page.locator('footer .footer-main')).toBeVisible();

  const headerStyle = await header.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      position: style.position,
      background: style.backgroundColor,
      radius: style.borderRadius
    };
  });
  expect(headerStyle.position).toBe('sticky');
  expect(headerStyle.background).toMatch(/^rgba\(/);
  expect(headerStyle.radius).toBe('0px');

  const footerStyle = await page.locator('footer').evaluate(element => ({
    height: element.getBoundingClientRect().height,
    width: element.getBoundingClientRect().width
  }));
  expect(footerStyle.height).toBeLessThan(180);
});

test('header keeps a full row on desktop and wraps links below the brand on mobile', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const nav = page.locator('header.topbar .wrap.nav');

  await expect.poll(() => nav.evaluate(el => getComputedStyle(el).flexWrap)).toBe('nowrap');

  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => nav.evaluate(el => getComputedStyle(el).flexWrap)).toBe('wrap');
  await expect(page.locator('header.topbar .navlinks')).toBeVisible();
});

test('the internal application shell fills the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');

  const body = await page.locator('body').evaluate(el => ({
    minHeight: getComputedStyle(el).minHeight,
    display: getComputedStyle(el).display
  }));
  expect(body.minHeight).toBe('900px');
  expect(body.display).toBe('flex');
});

test('final TogetherCraft artwork and game icons are local and replace placeholder glyphs', async ({ page }) => {
  await page.goto('/');
  const hero = page.locator('.hero-image');
  await expect(hero).toHaveAttribute('src', '/img/hero-fox.jpg');
  await expect.poll(() => hero.evaluate(element => element.complete && element.naturalWidth > 0)).toBe(true);
  await expect(page.locator('header.topbar .brand img')).toHaveAttribute('src', '/img/icon-meteor.svg');
  await expect(page.locator('.home-world-icon img')).toHaveCount(3);
  await expect(page.locator('.footer-social img')).toHaveCount(2);

  for (const image of await page.locator('.home-world-icon img, .footer-social img').all()) {
    await expect.poll(() => image.evaluate(element => element.complete && element.naturalWidth > 0)).toBe(true);
  }

  let bodyText = await page.locator('body').innerText();
  for (const glyph of ['→', '↗', '←', '◆']) {
    expect(bodyText).not.toContain(glyph);
  }

  await page.goto('/Home/Team');
  await expect(page.locator('.team-mark img')).toHaveAttribute('src', '/img/icon-meteor.svg');
  await expect(page.locator('.team-discord-link img')).toHaveAttribute('src', '/img/icon-discord.svg');
  await expect(page.locator('.team-mark span')).toHaveCount(0);

  await page.goto('/Home/Worlds');
  await expect(page.locator('.world-platform img')).toHaveAttribute('src', '/img/icon-curseforge.svg');
  await expect(page.locator('.world-symbol-block img')).toHaveAttribute('src', '/img/game-assets/mossy-cobblestone.png');
  bodyText = await page.locator('body').innerText();
  for (const glyph of ['→', '↗', '◆']) {
    expect(bodyText).not.toContain(glyph);
  }
});


test('administrator can upload, publish and delete a gallery image', async ({ page }) => {
  const username = process.env.CI_ADMIN_USERNAME;
  const password = process.env.CI_ADMIN_PASSWORD;
  expect(username).toBeTruthy();
  expect(password).toBeTruthy();

  await page.goto('/Account/Login');
  await page.locator('input[name="Username"]').fill(username);
  await page.locator('input[name="Password"]').fill(password);
  await page.locator('form').first().getByRole('button', { name: 'Войти' }).click();
  await expect(page).toHaveURL(/Admin/);

  await page.goto('/Admin/Gallery');
  await expect(page.getByRole('heading', { name: 'Галерея сообщества' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Пока нет скриншотов' })).toBeVisible();

  await page.locator('input[name="title"]').fill('Invalid upload');
  await page.locator('input[name="image"]').setInputFiles({
    name: 'not-really-a-png.png',
    mimeType: 'image/png',
    buffer: Buffer.from('This is not a PNG image')
  });
  await page.getByRole('button', { name: 'Загрузить в галерею' }).click();
  await expect(page.getByRole('alert')).toContainText('Содержимое файла не соответствует формату');
  await expect(page.getByRole('heading', { name: 'Пока нет скриншотов' })).toBeVisible();

  const gif = Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64');
  await page.locator('input[name="title"]').fill('CI Gallery Test');
  await page.locator('input[name="image"]').setInputFiles({
    name: 'ci-gallery.gif',
    mimeType: 'image/gif',
    buffer: gif
  });
  await page.getByRole('button', { name: 'Загрузить в галерею' }).click();
  await expect(page.getByText('ci gallery test', { exact: true })).toBeVisible();

  await page.goto('/Home/Gallery');
  await expect(page.locator('.gallery-photo')).toHaveCount(1);
  const uploadedImage = page.locator('.gallery-photo img').first();
  await expect(uploadedImage).toHaveAttribute('alt', 'ci gallery test');
  await expect.poll(() => uploadedImage.evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);

  await page.goto('/Admin/Gallery');
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Удалить' }).click();
  await expect(page.getByRole('heading', { name: 'Пока нет скриншотов' })).toBeVisible();

  await page.goto('/Home/Gallery');
  await expect(page.locator('.gallery-photo')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Галерея пока наполняется' })).toBeVisible();
});
