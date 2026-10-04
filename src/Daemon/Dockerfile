# The image every nook starts from: slothd under tini, on Ubuntu with git, and the harnesses coding
# agents run in: Claude Code, through its Agent Client Protocol adapter.
# Build it from the repository root:
#   docker build -f src/Daemon/Dockerfile -t aisloth-nook .

# slothd is published as Native AOT inside the SDK that global.json pins.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1-resolute-aot AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json NuGet.Config Directory.Build.props Directory.Packages.props .editorconfig BannedSymbols.txt LoggerParameterTypes.txt ./
COPY src/Daemon/Bagatka.AiSloth.DaemonProtocol/ src/Daemon/Bagatka.AiSloth.DaemonProtocol/
COPY src/Daemon/Bagatka.AiSloth.Daemon/ src/Daemon/Bagatka.AiSloth.Daemon/
RUN dotnet restore src/Daemon/Bagatka.AiSloth.Daemon --locked-mode
RUN dotnet publish src/Daemon/Bagatka.AiSloth.Daemon -c Release --no-restore -o /out \
    -r linux-$(if [ "$TARGETARCH" = arm64 ]; then echo arm64; else echo x64; fi)

# Harnesses are installed in a stage of their own, so npm never reaches the nook. Their versions are
# pinned here; Chats starts them by command name.
FROM node:22-slim AS harnesses
RUN npm install --global --prefix /opt/harnesses @agentclientprotocol/claude-agent-acp@0.85.1

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-rc.1-resolute
# tini runs as PID 1 and reaps the orphaned processes agents leave behind; harnesses run on Node.js.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini git ca-certificates curl nodejs \
    && rm -rf /var/lib/apt/lists/*
COPY --from=harnesses /opt/harnesses /opt/harnesses
ENV PATH=/opt/harnesses/bin:$PATH
COPY --from=build /out/Bagatka.AiSloth.Daemon /usr/local/bin/slothd
RUN mkdir -p /work /var/lib/slothd
WORKDIR /work
ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/slothd"]
