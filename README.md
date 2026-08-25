# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development environment

With [Nix](https://nixos.org/download/) installed, enter the development environment after cloning:

```sh
./dev
```

## Cloudflare

The development environment includes [Wrangler](https://developers.cloudflare.com/workers/wrangler/). To deploy, authenticate with your Cloudflare account:

```sh
wrangler login
wrangler whoami
```
