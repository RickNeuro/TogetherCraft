(() => {
    const root = document.querySelector("[data-server-status]");

    // The homepage shows both game worlds.
    if (root) {
        function setText(id, value) {
            const element = document.getElementById(id);
            if (element) element.textContent = value;
        }

        function updateServer(prefix, server) {
            const status = document.getElementById(prefix + "-status");
            if (!status) return;

            status.classList.remove("is-online", "is-offline");
            if (server.isOnline) {
                status.textContent = "Онлайн";
                status.classList.add("is-online");
            } else {
                status.textContent = "Оффлайн";
                status.classList.add("is-offline");
            }

            setText(prefix + "-version", server.version || (prefix === "fabric" ? "26.2" : "1.21.1"));
            setText(prefix + "-players",
                Number.isInteger(server.onlinePlayers) && Number.isInteger(server.maxPlayers)
                    ? server.onlinePlayers + " / " + server.maxPlayers
                    : "—");
            setText(prefix + "-ping", typeof server.pingMs === "number" ? Math.round(server.pingMs) + " ms" : "—");
            setText(prefix + "-note", server.message || "Статус обновляется автоматически.");
            const address = server.address || (prefix === "fabric" ? "fried-newer.tun.ply.gg" : "shrimp-crafts.tun.ply.gg");
            setText(prefix + "-address", address);
            const copyButton = document.getElementById("copy-" + prefix + "-address");
            if (copyButton) copyButton.dataset.copyAddress = address;
            setText(prefix + "-updated", server.updatedAtUtc
                ? new Date(server.updatedAtUtc).toLocaleTimeString("ru-RU")
                : "—");
        }

        async function updateStatuses() {
            try {
                const response = await fetch("/api/minecraft/servers", {
                    cache: "no-store",
                    headers: { "Accept": "application/json" }
                });
                if (!response.ok) throw new Error("Статус временно недоступен.");

                const data = await response.json();
                if (data.fabric) updateServer("fabric", data.fabric);
                if (data.create) updateServer("create", data.create);
            } catch {
                ["fabric", "create"].forEach(prefix => {
                    const status = document.getElementById(prefix + "-status");
                    if (status) {
                        status.textContent = "Нет связи";
                        status.classList.remove("is-online", "is-offline");
                    }
                    setText(prefix + "-note", "Не удалось получить статус. Повторим проверку автоматически.");
                });
            }
        }

        root.querySelectorAll("[data-copy-address]").forEach(button => {
            button.addEventListener("click", async () => {
                const address = button.dataset.copyAddress;
                try {
                    await navigator.clipboard.writeText(address);
                } catch {
                    const input = document.createElement("textarea");
                    input.value = address;
                    input.style.position = "fixed";
                    input.style.opacity = "0";
                    document.body.appendChild(input);
                    input.select();
                    document.execCommand("copy");
                    input.remove();
                }

                const oldText = button.textContent;
                button.textContent = "Скопировано";
                window.setTimeout(() => button.textContent = oldText, 1400);
            });
        });

        if (root.dataset.serverStatus !== "true") return;
        updateStatuses();
        window.setInterval(updateStatuses, 15000);
        return;
    }

    // Keep the existing dedicated monitoring page compatible with its original Fabric status card.
    const statusElement = document.getElementById("minecraft-server-status");
    if (!statusElement) return;

    const addressElement = document.getElementById("minecraft-server-address");
    const versionElement = document.getElementById("minecraft-server-version");
    const playersElement = document.getElementById("minecraft-server-players");
    const pingElement = document.getElementById("minecraft-server-ping");
    const noteElement = document.getElementById("minecraft-server-note");
    const updatedElement = document.getElementById("minecraft-server-updated");
    const copyButton = document.getElementById("minecraft-copy-address");

    function showValue(element, value) {
        if (element) element.textContent = value;
    }

    async function updateMinecraftStatus() {
        try {
            const response = await fetch("/api/minecraft/status", { cache: "no-store" });
            if (!response.ok) throw new Error("Не удалось получить статус Minecraft.");

            const data = await response.json();
            showValue(addressElement, data.address || "—");
            showValue(versionElement, data.version || "—");
            showValue(playersElement,
                Number.isInteger(data.onlinePlayers) && Number.isInteger(data.maxPlayers)
                    ? data.onlinePlayers + " / " + data.maxPlayers
                    : "—");
            showValue(pingElement,
                typeof data.pingMs === "number" ? Math.round(data.pingMs) + " ms" : "—");

            statusElement.className = data.isOnline ? "badge text-bg-success" : "badge text-bg-danger";
            showValue(statusElement, data.isOnline ? "Онлайн" : "Оффлайн");
            showValue(noteElement, data.message || "Minecraft не отвечает.");
            const updated = data.updatedAtUtc ? new Date(data.updatedAtUtc) : new Date();
            showValue(updatedElement, updated.toLocaleTimeString("ru-RU"));
        } catch (error) {
            statusElement.className = "badge text-bg-secondary";
            showValue(statusElement, "Нет связи");
            showValue(noteElement, error.message || "Не удалось обновить данные Minecraft.");
            showValue(pingElement, "—");
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
            window.setTimeout(() => copyButton.textContent = oldText, 1500);
        });
    }

    updateMinecraftStatus();
    window.setInterval(updateMinecraftStatus, 15000);
})();