TogetherCraft: fix CPU temperature query

The current Prometheus sample labels the AMD k10temp hwmon sensor with a PCI-address chip label, not a label containing "k10temp". The old query therefore returned no result.

Apply this patch from the app directory (the directory containing compose.yaml):
  unzip -o TogetherCraft-temp-fix.zip
  sudo docker compose up -d --build togethercraft-web

This only replaces Services/PrometheusMetricsService.cs and rebuilds the web container. It does not change PostgreSQL data or the Minecraft server.
