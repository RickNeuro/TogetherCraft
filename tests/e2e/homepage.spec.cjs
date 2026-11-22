const { test, expect } = require('@playwright/test');

test.describe.configure({ mode: 'serial' });

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

test('world cover cards use neutral placeholders until the owner supplies real screenshots', async ({ page }) => {
  await page.goto('/Home/Worlds');
  await expect(page.locator('.world-card')).toHaveCount(3);

  await expect(page.locator('.world-art-placeholder')).toHaveCount(3);
  await expect(page.locator('.world-art img')).toHaveCount(3);
  await expect(page.locator('.world-symbol-block img')).toHaveAttribute('src', '/img/game-assets/mossy-cobblestone.png');
  await expect(page.locator('.world-symbol-gear img')).toHaveAttribute('src', '/img/game-assets/create-large-cogwheel.webp');
  await expect(page.locator('.world-symbol-biter img')).toHaveAttribute('src', '/img/game-assets/big-biter.png');
  await expect(page.locator('.world-art-placeholder strong').nth(0)).toHaveText('VANILLA');
  await expect(page.locator('.world-art-placeholder strong').nth(1)).toHaveText('CREATE');
  await expect(page.locator('.world-art-placeholder strong').nth(2)).toHaveText('FACTORIO');
  await expect(page.locator('.world-art-placeholder-note')).toHaveCount(3);

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
  for (const width of [2048, 1440, 1280, 1024, 900, 760, 390, 360]) {
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

test('TogetherCraft uses the owner logo and world covers remain neutral until screenshots arrive', async ({ page }) => {
  await page.goto('/');
  const hero = page.locator('.hero-image-light');
  await expect(hero).toHaveAttribute('src', '/img/hero-fox.jpg');
  await expect.poll(() => hero.evaluate(element => element.complete && element.naturalWidth > 0)).toBe(true);
  await expect(page.locator('header.topbar .brand img')).toHaveAttribute('src', '/img/togethercraft-mark.webp?v=3');
  await expect.poll(() => page.locator('header.topbar .brand img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await expect(page.locator('link[rel="icon"]')).toHaveAttribute('href', '/img/togethercraft-mark.webp?v=3');
  await expect(page.locator('.home-world-icon img')).toHaveCount(3);
  await expect(page.locator('.home-world-icon img').nth(0)).toHaveAttribute('src', '/img/game-assets/mossy-cobblestone.png');
  await expect(page.locator('.home-world-icon img').nth(1)).toHaveAttribute('src', '/img/game-assets/create-large-cogwheel.webp');
  await expect(page.locator('.home-world-icon img').nth(2)).toHaveAttribute('src', '/img/game-assets/big-biter.png');
  await expect(page.locator('footer .footer-brand-line img')).toHaveAttribute('src', '/img/togethercraft-mark.webp?v=3');
  await expect.poll(() => page.locator('footer .footer-brand-line img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await expect(page.locator('.footer-social img')).toHaveCount(2);

  for (const image of await page.locator('.home-world-icon img, .footer-social img').all()) {
    await expect.poll(() => image.evaluate(element => element.complete && element.naturalWidth > 0)).toBe(true);
  }

  let bodyText = await page.locator('body').innerText();
  for (const glyph of ['→', '↗', '←', '◆']) {
    expect(bodyText).not.toContain(glyph);
  }

  await page.goto('/Home/Team');
  await expect(page.locator('.team-mark img')).toHaveAttribute('src', '/img/togethercraft-mark.webp?v=3');
  await expect.poll(() => page.locator('.team-mark img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await expect(page.locator('.team-discord-link img')).toHaveAttribute('src', '/img/icon-discord.svg');
  await expect(page.locator('.team-mark span')).toHaveCount(0);

  await page.goto('/Home/Worlds');
  await expect(page.locator('.world-platform img')).toHaveAttribute('src', '/img/icon-curseforge.svg');
  await expect(page.locator('.world-art-placeholder')).toHaveCount(3);
  await expect(page.locator('.world-art img')).toHaveCount(3);
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

  await page.goto('/Home/Play');
  await expect(page.locator('.server-choice-placeholder')).toHaveCount(2);
  await expect(page.locator('.server-choice-art img')).toHaveCount(0);
  await expect(page.locator('.server-choice-placeholder strong').nth(0)).toHaveText('VANILLA');
  await expect(page.locator('.server-choice-placeholder strong').nth(1)).toHaveText('CREATE');

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


test('wide page rails are centered and use the available desktop width', async ({ page }) => {
  for (const width of [2048, 1440, 1280, 1024, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/');
    const layout = await page.evaluate(() => {
      const measure = selector => {
        const element = document.querySelector(selector);
        const rect = element.getBoundingClientRect();
        return { left: rect.left, right: rect.right, width: rect.width };
      };
      return {
        viewport: document.documentElement.clientWidth,
        home: measure('.home-page'),
        header: measure('header.topbar .wrap.nav'),
        footer: measure('footer .wrap'),
        heroColumns: getComputedStyle(document.querySelector('.home-page .hero')).gridTemplateColumns
      };
    });

    if (width > 680) {
      expect(layout.heroColumns.trim().split(' ').filter(Boolean)).toHaveLength(2);
    }

    for (const name of ['home', 'header', 'footer']) {
      const rail = layout[name];
      expect(Math.abs((rail.left + rail.right) / 2 - layout.viewport / 2),
        name + ' rail should be centered at ' + width + 'px').toBeLessThanOrEqual(2);
      expect(rail.width).toBeGreaterThan(layout.viewport * 0.6);
    }
  }
});

test('the homepage hero switches between the supplied night image and fox', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');

  const night = page.locator('.hero-image-dark');
  const fox = page.locator('.hero-image-light');
  await expect(night).toHaveAttribute('src', '/img/hero-night.webp');
  await expect(fox).toHaveAttribute('src', '/img/hero-fox.jpg');
  await expect(night).toBeVisible();
  await expect(fox).toHaveCSS('display', 'none');
  await expect.poll(() => night.evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);

  await page.getByRole('button', { name: 'Переключить на светлую тему' }).click();
  await expect(fox).toBeVisible();
  await expect(night).toHaveCSS('display', 'none');
  await expect.poll(() => fox.evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);

  await expect(page.locator('.brand img')).toHaveAttribute('src', '/img/togethercraft-mark.webp?v=3');
  await expect(page.locator('.brand span')).toContainText('TogetherCraft');
});

test('light and dark themes switch, style controls and persist between pages', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const toggle = page.getByRole('button', { name: 'Переключить на светлую тему' });
  await expect(page.locator('.theme-toggle-label')).toHaveCount(0);
  await expect(page.locator('.theme-icon-sun')).toBeVisible();
  await expect(page.locator('.theme-icon-moon')).toHaveCSS('display', 'none');
  await expect(toggle).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

  const dark = await page.evaluate(() => ({
    background: getComputedStyle(document.body).backgroundColor,
    buttonBackground: getComputedStyle(document.querySelector('.tc-btn-primary')).backgroundColor,
    buttonText: getComputedStyle(document.querySelector('.tc-btn-primary')).color
  }));
  expect(dark.background).toBe('rgb(11, 17, 26)');
  expect(dark.buttonBackground).toBe('rgb(175, 197, 222)');
  expect(dark.buttonText).toBe('rgb(16, 25, 37)');
  const darkSurface = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--panel2').trim().toLowerCase());
  expect(darkSurface).toBe('#1d2a3a');

  await toggle.click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.getByRole('button', { name: 'Переключить на тёмную тему' })).toHaveAttribute('aria-pressed', 'true');

  const light = await page.evaluate(() => ({
    background: getComputedStyle(document.body).backgroundColor,
    buttonBackground: getComputedStyle(document.querySelector('.tc-btn-primary')).backgroundColor,
    buttonText: getComputedStyle(document.querySelector('.tc-btn-primary')).color
  }));
  expect(light.background).toBe('rgb(243, 240, 230)');
  const lightAccent = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--orange').trim());
  expect(lightAccent).toBe('#B96F35');
  expect(light.buttonBackground).toBe('rgb(185, 111, 53)');
  expect(light.buttonText).toBe('rgb(255, 249, 239)');

  await page.goto('/Home/Worlds');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

  await page.getByRole('button', { name: 'Переключить на тёмную тему' }).click();
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});


test('CTA labels are vertically and horizontally centered without link underlines', async ({ page }) => {
  await page.goto('/');
  const selectors = ['.hero-actions .tc-btn-primary', '.hero-actions .tc-btn-secondary', '.home-gallery-preview .world-action'];
  for (const selector of selectors) {
    const style = await page.locator(selector).evaluate(element => {
      const css = getComputedStyle(element);
      const buttonRect = element.getBoundingClientRect();
      const textRange = document.createRange();
      textRange.selectNodeContents(element);
      const textRect = textRange.getBoundingClientRect();
      return {
        display: css.display,
        alignItems: css.alignItems,
        justifyContent: css.justifyContent,
        textAlign: css.textAlign,
        textDecoration: css.textDecorationLine,
        textCenterX: Math.abs((textRect.left + textRect.right) / 2 - (buttonRect.left + buttonRect.right) / 2),
        textCenterY: Math.abs((textRect.top + textRect.bottom) / 2 - (buttonRect.top + buttonRect.bottom) / 2)
      };
    });
    expect(['flex', 'inline-flex']).toContain(style.display);
    expect(style.alignItems).toBe('center');
    expect(style.justifyContent).toBe('center');
    expect(style.textAlign).toBe('center');
    expect(style.textDecoration).toBe('none');
    expect(style.textCenterX).toBeLessThan(4);
    expect(style.textCenterY).toBeLessThan(4);
  }

  await page.goto('/Home/Worlds');
  const worldAction = await page.locator('.world-action').first().evaluate(element => {
    const css = getComputedStyle(element);
    const buttonRect = element.getBoundingClientRect();
    const textRange = document.createRange();
    textRange.selectNodeContents(element);
    const textRect = textRange.getBoundingClientRect();
    return {
      display: css.display,
      alignItems: css.alignItems,
      justifyContent: css.justifyContent,
      textAlign: css.textAlign,
      textDecoration: css.textDecorationLine,
      textCenterX: Math.abs((textRect.left + textRect.right) / 2 - (buttonRect.left + buttonRect.right) / 2),
      textCenterY: Math.abs((textRect.top + textRect.bottom) / 2 - (buttonRect.top + buttonRect.bottom) / 2)
    };
  });
  expect(['flex', 'inline-flex']).toContain(worldAction.display);
  expect(worldAction.alignItems).toBe('center');
  expect(worldAction.justifyContent).toBe('center');
  expect(worldAction.textAlign).toBe('center');
  expect(worldAction.textDecoration).toBe('none');
  expect(worldAction.textCenterX).toBeLessThan(4);
  expect(worldAction.textCenterY).toBeLessThan(4);
});


test('brand covers in world cards and CTA controls use the warm theme colors', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/Home/Worlds');
  const darkColors = await page.locator('.world-art').evaluateAll(items =>
    items.map(item => getComputedStyle(item).backgroundColor));
  expect(darkColors).toEqual([
    'rgb(29, 42, 58)',
    'rgb(34, 50, 71)',
    'rgb(20, 31, 45)'
  ]);

  const worldButton = page.locator('.world-card .world-action').first();
  const worldButtonStyle = await worldButton.evaluate(item => {
    const s = getComputedStyle(item);
    return { background: s.backgroundColor, color: s.color, align: s.alignItems, justify: s.justifyContent, decoration: s.textDecorationLine };
  });
  expect(worldButtonStyle.background).toBe('rgb(175, 197, 222)');
  expect(worldButtonStyle.color).toBe('rgb(16, 25, 37)');
  expect(worldButtonStyle.align).toBe('center');
  expect(worldButtonStyle.justify).toBe('center');
  expect(worldButtonStyle.decoration).toBe('none');

  await page.getByRole('button', { name: 'Переключить на светлую тему' }).click();
  const lightColors = await page.locator('.world-art').evaluateAll(items =>
    items.map(item => getComputedStyle(item).backgroundColor));
  expect(lightColors).toEqual([
    'rgb(222, 226, 209)',
    'rgb(231, 232, 218)',
    'rgb(237, 235, 221)'
  ]);
});
