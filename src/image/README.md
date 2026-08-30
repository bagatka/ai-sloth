# Sandbox image

This directory owns the disposable container image: its base image, pinned tools,
Pi SDK runner, and runtime settings. It does not own session coordination,
persistence, API behavior, or Cloudflare Durable Object lifecycle.

The runner uses the pinned `@earendil-works/pi-coding-agent` package and emits a
small project-owned JSONL timeline protocol for assistant text and thinking
blocks, tool activity and output, edit diffs, retries, and compaction. `src/pi`
owns validation and bounds at the process boundary.

`Dockerfile` is the deployment entry point.
