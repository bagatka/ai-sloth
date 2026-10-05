#!/bin/sh
# A nook's entry point, under tini: becomes slothd, with the Docker engine for the project's own
# containers starting on first use. Docker's socket listens from the start, and the first connection
# starts dockerd on it, which answers once ready, in about 0.4 seconds; a nook that never uses Docker
# pays nothing. The activator's parent exits at once, so tini adopts and reaps the engine.
# /var/log/dockerd.log says what it did.
sh -c 'systemd-socket-activate --listen=/run/docker.sock dockerd --host=fd:// >/var/log/dockerd.log 2>&1 &'

exec /usr/local/bin/slothd
