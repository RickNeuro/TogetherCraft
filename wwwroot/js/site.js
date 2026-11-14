(() => {
    const toast = document.querySelector('[data-toast]');
    let toastTimer;

    function showToast(message) {
        if (!toast) return;
        toast.textContent = message;
        toast.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.remove('show'), 4200);
    }

    document.querySelectorAll('[data-copy]').forEach(button => {
        button.addEventListener('click', async () => {
            const text = button.dataset.copy || '';
            try {
                await navigator.clipboard.writeText(text);
                showToast('Адрес скопирован: ' + text);
            } catch {
                showToast('Адрес сервера: ' + text + '. Скопируй его вручную.');
            }
        });
    });

    document.querySelectorAll('[data-demo-auth]').forEach(form => {
        form.addEventListener('submit', event => {
            event.preventDefault();
            showToast('Это пока макет интерфейса. Аккаунты и авторизация ещё не подключены — данные формы никуда не отправлены.');
        });
    });

    const menuButton = document.querySelector('[data-menu-toggle]');
    const nav = document.querySelector('.main-nav');
    if (menuButton && nav) {
        menuButton.addEventListener('click', () => {
            const open = nav.classList.toggle('open');
            menuButton.setAttribute('aria-expanded', String(open));
            menuButton.setAttribute('aria-label', open ? 'Закрыть меню' : 'Открыть меню');
            menuButton.textContent = open ? '×' : '☰';
        });
    }

    // Gallery lightbox: each image button opens its full-size source.
    const galleryDialog = document.querySelector('#gallery-lightbox');
    const galleryImage = document.querySelector('#gallery-lightbox-image');
    const galleryCaption = document.querySelector('#gallery-lightbox-caption');

    document.querySelectorAll('[data-gallery-open]').forEach(button => {
        button.addEventListener('click', () => {
            if (!galleryDialog || !galleryImage) return;
            galleryImage.src = button.dataset.gallerySrc || '';
            galleryImage.alt = button.dataset.galleryTitle || '';
            if (galleryCaption) galleryCaption.textContent = button.dataset.galleryTitle || '';
            if (typeof galleryDialog.showModal === 'function') galleryDialog.showModal();
            else galleryDialog.setAttribute('open', '');
        });
    });

    if (galleryDialog) {
        galleryDialog.addEventListener('click', event => {
            if (event.target === galleryDialog) galleryDialog.close();
        });
    }

    const rangeLabels = {
        '1ч': ['10:00', '10:15', '10:30', '10:45', '11:00', '11:15'],
        '6ч': ['05:00', '06:00', '07:00', '08:00', '09:00', '10:00'],
        '24ч': ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00']
    };
    document.querySelectorAll('[data-range]').forEach(button => {
        button.addEventListener('click', () => {
            document.querySelectorAll('[data-range]').forEach(item => item.classList.remove('selected'));
            button.classList.add('selected');
            const labels = rangeLabels[button.dataset.range];
            const chartLabels = document.querySelectorAll('.chart-x span');
            chartLabels.forEach((label, index) => { label.textContent = labels[index] || ''; });
            showToast('Выбран период ' + button.dataset.range + '. График пока демонстрационный.');
        });
    });
})();
