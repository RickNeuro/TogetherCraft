(() => {
    const statusElement = document.getElementById("minecraft-server-status");
    if (!statusElement) return;

    const addressElement = document.getElementById("minecraft-server-address");
    const versionElement = document.getElementById("minecraft-server-version");
    const playersElement = document.getElementById("minecraft-server-players");
    const tpsElement = document.getElementById("minecraft-server-tps");
    const msptElement = document.getElementById("minecraft-server-mspt");
    const pingElement = document.getElementById("minecraft-server-ping");
    const noteElement = document.getElementById("minecraft-server-note");
    const updatedElement = document.getElementById("minecraft-server-updated");
    const copyButton = document.getElementById("minecraft-copy-address");

    function showValue(element, value) {
        if (element) element.textContent = value;
    }

    function formatNumber(value, digits = 1) {
        return typeof value === "number" && Number.isFinite(value)
            ? value.toFixed(digits)
            : "—";
    }

    async function updateMinecraftStatus() {
        showValue(statusElement, "Проверяем…");
        statusElement.className = "badge text-bg-secondary";

        try {
            const response = await fetch("/api/minecraft/status", { cache: "no-store" });
            if (!response.ok) {
                throw new Error("Не удалось получить статус Minecraft.");
            }

            const data = await response.json();

            showValue(addressElement, data.address || "—");
            showValue(versionElement, data.version || "—");
            showValue(playersElement,
                Number.isInteger(data.onlinePlayers) && Number.isInteger(data.maxPlayers)
                    ? data.onlinePlayers + " / " + data.maxPlayers
                    : "—");
            showValue(tpsElement,
                typeof data.tps === "number" ? formatNumber(data.tps) + " (1 мин.)" : "—");
            showValue(msptElement,
                typeof data.msptP95 === "number" ? formatNumber(data.msptP95) + " ms" : "—");
            showValue(pingElement,
                typeof data.pingMs === "number" ? Math.round(data.pingMs) + " ms" : "—");

            if (data.isOnline) {
                showValue(statusElement, "Онлайн");
                statusElement.className = "badge text-bg-success";
                showValue(noteElement,
                    data.sparkAvailable
                        ? "Minecraft отвечает. Показатели Spark обновлены."
                        : "Minecraft отвечает. TPS/MSPT пока недоступны: проверь RCON и Spark.");
            } else {
                showValue(statusElement, "Оффлайн");
                statusElement.className = "badge text-bg-danger";
                showValue(noteElement, data.message || "Minecraft не отвечает.");
            }

            const updated = data.updatedAtUtc ? new Date(data.updatedAtUtc) : new Date();
            showValue(updatedElement, updated.toLocaleTimeString("ru-RU"));
        } catch (error) {
            showValue(statusElement, "Нет связи");
            statusElement.className = "badge text-bg-secondary";
            showValue(noteElement, error.message || "Не удалось обновить данные Minecraft.");
            showValue(tpsElement, "—");
            showValue(msptElement, "—");
        }
    }

    if (copyButton && addressElement) {
        copyButton.addEventListener("click", async () => {
            const address = addressElement.textContent.trim();
            if (!address || address === "—") return;

            try {
                await navigator.clipboard.writeText(address);
            } catch {
                const temporaryInput = document.createElement("textarea");
                temporaryInput.value = address;
                temporaryInput.style.position = "fixed";
                temporaryInput.style.opacity = "0";
                document.body.appendChild(temporaryInput);
                temporaryInput.select();
                document.execCommand("copy");
                temporaryInput.remove();
            }

            const oldText = copyButton.textContent;
            copyButton.textContent = "Скопировано";
            window.setTimeout(() => {
                copyButton.textContent = oldText;
            }, 1500);
        });
    }

    updateMinecraftStatus();
    window.setInterval(updateMinecraftStatus, 15000);
})();
