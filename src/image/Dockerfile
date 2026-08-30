FROM docker.io/node:22-bookworm-slim AS pi-runner-build

WORKDIR /build
COPY package.json package-lock.json tsconfig.json runner.ts tool-diff.ts ./
RUN npm ci --ignore-scripts --workspaces=false \
    && npm run build

FROM docker.io/cloudflare/sandbox:0.13.0-next.751.1

RUN apt-get update \
    && apt-get install -y --no-install-recommends fd-find ripgrep \
    && ln -s /usr/bin/fdfind /usr/local/bin/fd \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /bin/sh agent \
    && install -d -m 0755 -o agent -g agent /home/agent/.pi/agent

WORKDIR /opt/ai-sloth/pi-runner
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts --workspaces=false \
    && npm cache clean --force
COPY --from=pi-runner-build /build/dist/runner.js /build/dist/tool-diff.js ./

COPY --chown=agent:agent pi-settings.json /home/agent/.pi/agent/settings.json
