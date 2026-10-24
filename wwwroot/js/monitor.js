(() => {
    const root = document.querySelector('[data-live-monitor]');
    if (!root || root.dataset.liveMonitor === 'false') return;

    const byId = id => document.getElementById(id);
    const setText = (id, text) => {
        const element = byId(id);
        if (element) element.textContent = text;
    };
    const formatGib = bytes => bytes == null ? '—' : (bytes / 1024 ** 3).toFixed(1);
    const formatNumber = (value, digits = 1) => value == null ? '—' : Number(value).toFixed(digits);
    const setBar = (id, value) => {
        const element = byId(id);
        if (element) element.style.width = value == null ? '0%' : `${Math.max(0, Math.min(100, value))}%`;
    };

    let requestInProgress = false;

    async function refreshMetrics() {
        if (requestInProgress) return;
        requestInProgress = true;

        try {
            const response = await fetch('/api/metrics', {
                method: 'GET',
                cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            });
            if (!response.ok) {
                let message = 'Не удалось прочитать метрики. Повторяем подключение.';
                try {
                    const errorData = await response.json();
                    if (errorData.message) message = errorData.message;
                } catch { /* Ответ может быть не JSON. */ }
                setText('source-status', 'Источник метрик временно недоступен');
                setText('source-message', message);
                return;
            }

            const data = await response.json();
            if (!data.sourceAvailable) {
                setText('source-status', 'Источник метрик не подключён');
                setText('source-message', data.message || 'Prometheus ещё не получил данные Ubuntu.');
                return;
            }

            setText('source-status', 'Системные метрики подключены');
            setText('source-message', data.message || 'Данные Ubuntu обновляются автоматически.');
            setText('last-updated', data.updatedAtUtc ? new Date(data.updatedAtUtc).toLocaleTimeString() : 'сейчас');

            setText('cpu-value', formatNumber(data.cpuPercent));
            setText('cpu-note', data.cpuPercent == null ? 'Собираем данные CPU' : 'Нагрузка всех ядер CPU');
            setBar('cpu-bar', data.cpuPercent);

            setText('memory-used', formatGib(data.memoryUsedBytes));
            setText('memory-total', formatGib(data.memoryTotalBytes));
            setText('memory-note', data.memoryPercent == null ? 'Нет данных о памяти' : `${formatNumber(data.memoryPercent)}% использовано`);
            setBar('memory-bar', data.memoryPercent);

            setText('disk-used', formatGib(data.diskUsedBytes));
            setText('disk-total', formatGib(data.diskTotalBytes));
            setText('disk-note', data.diskPercent == null ? 'Нет данных о системном разделе' : `${formatNumber(data.diskPercent)}% занято`);
            setBar('disk-bar', data.diskPercent);

            setText('temperature-value', formatNumber(data.cpuTemperatureCelsius));
            setText('temperature-note', data.cpuTemperatureCelsius == null ? 'Датчик CPU недоступен' : 'Датчик процессора AMD');
            setText('fan-value', data.fan1Rpm == null ? '—' : formatNumber(data.fan1Rpm, 0));
            setText('fan3-value', data.fan3Rpm == null ? '—' : formatNumber(data.fan3Rpm, 0));

            if (data.fan1Rpm == null && data.fan3Rpm == null) {
                setText('fan-note', 'Linux не предоставляет обороты вентилятора');
            } else {
                const fanStatus = [
                    data.fan1Rpm == null ? 'FAN1: нет данных' : 'FAN1: датчик активен',
                    data.fan3Rpm == null ? 'FAN3: нет данных' : 'FAN3: датчик активен'
                ];
                setText('fan-note', fanStatus.join(' · '));
            }

            document.querySelectorAll('.fan-rotor').forEach(rotor => {
                if (data.fan1Rpm != null && data.fan1Rpm > 0) {
                    const duration = Math.max(0.3, Math.min(1.8, 1800 / data.fan1Rpm));
                    rotor.style.animationDuration = `${duration}s`;
                    rotor.style.animationPlayState = 'running';
                } else {
                    rotor.style.animationPlayState = 'paused';
                }
            });
        } catch {
            setText('source-status', 'Нет связи с источником метрик');
            setText('source-message', 'Проверяем подключение автоматически через 5 секунд.');
        } finally {
            requestInProgress = false;
        }
    }

    refreshMetrics();
    window.setInterval(refreshMetrics, 5000);
})();
