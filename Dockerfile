# DoveLoFanno: immagine del server per amd64 e arm64 (NAS Synology, mini-PC, Raspberry Pi 4/5).
#   docker run -d -p 8765:8765 -v ./data:/data -v ./registrazioni:/recordings ghcr.io/antoscool73/dovelofanno
# La compilazione gira sull'architettura di chi costruisce (veloce anche per arm64); solo l'immagine finale è per
# l'architettura di destinazione.

# interfaccia web (Svelte): file statici, uguali per tutte le architetture
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# numero di versione del rilascio (release.yml e ci.yml sui tag); vuoto = quello di Directory.Build.props (-dev)
ARG VERSION=
WORKDIR /src
COPY global.json Directory.Build.props DoveLoFanno.slnx ./
COPY src/DoveLoFanno.Core/DoveLoFanno.Core.csproj src/DoveLoFanno.Core/
COPY src/DoveLoFanno.Server/DoveLoFanno.Server.csproj src/DoveLoFanno.Server/
# amd64 -> linux-x64, arm64 -> linux-arm64
RUN RID="linux-$([ "$TARGETARCH" = amd64 ] && echo x64 || echo "$TARGETARCH")" \
    && echo "$RID" > /rid \
    && dotnet restore src/DoveLoFanno.Server/DoveLoFanno.Server.csproj -r "$RID"
COPY src/ src/
COPY --from=web /src/src/DoveLoFanno.Server/wwwroot src/DoveLoFanno.Server/wwwroot
RUN dotnet publish src/DoveLoFanno.Server/DoveLoFanno.Server.csproj -c Release -r "$(cat /rid)" --no-restore \
        --self-contained false -p:UseAppHost=false ${VERSION:+-p:Version=$VERSION} -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffmpeg e ffprobe della distribuzione (licenza e sorgenti: pacchetto ffmpeg di Ubuntu); tzdata per l'ora locale
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data /recordings && chown app:app /data /recordings
ENV TZ=Europe/Rome \
    ASPNETCORE_URLS=http://+:8765 \
    DoveLoFanno__DataDir=/data \
    DoveLoFanno__RecordingsDir=/recordings \
    DoveLoFanno__Ffmpeg=/usr/bin/ffmpeg
WORKDIR /app
COPY --from=build /app .
USER app
VOLUME ["/data", "/recordings"]
EXPOSE 8765
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 CMD ["dotnet", "DoveLoFanno.Server.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "DoveLoFanno.Server.dll"]
