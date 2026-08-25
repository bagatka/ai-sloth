FROM docker.io/cloudflare/sandbox:0.13.0-next.751.1

ARG PI_VERSION=0.84.3

RUN apt-get update \
    && apt-get install -y --no-install-recommends fd-find ripgrep \
    && ln -s /usr/bin/fdfind /usr/local/bin/fd \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g --ignore-scripts "@earendil-works/pi-coding-agent@${PI_VERSION}" \
    && npm cache clean --force \
    && pi --version

COPY pi-settings.json /root/.pi/agent/settings.json
