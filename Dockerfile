FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TogetherCraft.csproj ./
RUN dotnet restore TogetherCraft.csproj
COPY . ./
RUN dotnet publish TogetherCraft.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
# Run the web process without root privileges. Docker copies this directory's
# ownership into the initially empty named volume used for Data Protection keys.
RUN mkdir -p /var/dpkeys && chown -R $APP_UID:$APP_UID /app /var/dpkeys
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=4s --start-period=25s --retries=5 CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "TogetherCraft.dll"]
