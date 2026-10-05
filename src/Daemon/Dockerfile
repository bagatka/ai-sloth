# The images nooks start from, as targets: `nook` is slothd under tini, on Ubuntu 26.04 with git; each
# harness target adds Node.js and one harness for chats' agents. A nook carries at most one harness,
# so a host pulls only the images its nooks use, and the layers below a harness are shared.
# Build from the repository root:
#   docker build -f src/Daemon/Dockerfile --target nook -t aisloth-nook .
#   docker build -f src/Daemon/Dockerfile --target claude-code -t aisloth-nook-claude-code .
#   docker build -f src/Daemon/Dockerfile --target codex -t aisloth-nook-codex .
#   docker build -f src/Daemon/Dockerfile --target pi -t aisloth-nook-pi .
#   docker build -f src/Daemon/Dockerfile --target copilot -t aisloth-nook-copilot .

# slothd is published as Native AOT inside the SDK that global.json pins.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1-resolute-aot AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json NuGet.Config Directory.Build.props Directory.Packages.props .editorconfig BannedSymbols.txt LoggerParameterTypes.txt ./
COPY analyzers/Bagatka.Analyzers/ analyzers/Bagatka.Analyzers/
COPY src/Daemon/Bagatka.AiSloth.DaemonProtocol/ src/Daemon/Bagatka.AiSloth.DaemonProtocol/
COPY src/Daemon/Bagatka.AiSloth.Daemon/ src/Daemon/Bagatka.AiSloth.Daemon/
RUN dotnet restore src/Daemon/Bagatka.AiSloth.Daemon --locked-mode
RUN dotnet publish src/Daemon/Bagatka.AiSloth.Daemon -c Release --no-restore -o /out \
    -r linux-$(if [ "$TARGETARCH" = arm64 ]; then echo arm64; else echo x64; fi)

# Harnesses are installed in stages of their own, so npm never reaches a nook. Their versions are
# pinned here, matching their start scripts in src/Harnesses/start, which each image installs as
# `harness`: Chats starts every harness that way.
FROM node:22-slim AS claude-code-install
RUN npm install --global --prefix /opt/harness @agentclientprotocol/claude-agent-acp@0.85.1

FROM node:22-slim AS codex-install
RUN npm install --global --prefix /opt/harness @agentclientprotocol/codex-acp@2.1.1

# pi asks not to run install scripts; it needs none.
FROM node:22-slim AS pi-install
RUN npm install --global --prefix /opt/harness --ignore-scripts @earendil-works/pi-coding-agent@1.0.3 pi-acp@0.0.34

FROM node:22-slim AS copilot-install
RUN npm install --global --prefix /opt/harness @github/copilot@1.0.91

# Plain Ubuntu: slothd needs only libc, and OpenSSL with the CA certificates for TLS to the control
# plane; it runs without ICU. tini runs as PID 1 and reaps the orphaned processes agents leave behind.
FROM ubuntu:26.04 AS nook
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini git ca-certificates curl libssl3t64 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/Bagatka.AiSloth.Daemon /usr/local/bin/slothd
RUN mkdir -p /work /var/lib/slothd
WORKDIR /work
ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/slothd"]

# Harnesses run on Node.js; every harness image shares this layer.
FROM nook AS node
RUN apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*
ENV PATH=/opt/harness/bin:$PATH

FROM node AS claude-code
COPY --from=claude-code-install /opt/harness /opt/harness
COPY --chmod=755 src/Harnesses/start/claude-code.sh /usr/local/bin/harness

FROM node AS codex
COPY --from=codex-install /opt/harness /opt/harness
COPY --chmod=755 src/Harnesses/start/codex.sh /usr/local/bin/harness

FROM node AS pi
COPY --from=pi-install /opt/harness /opt/harness
COPY --chmod=755 src/Harnesses/start/pi.sh /usr/local/bin/harness

FROM node AS copilot
COPY --from=copilot-install /opt/harness /opt/harness
COPY --chmod=755 src/Harnesses/start/copilot.sh /usr/local/bin/harness

# Without --target, the base image.
FROM nook
