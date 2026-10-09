# The control plane's image: the WebApi, with git, which Sources runs to move code between GitHub and
# nooks. Run with the single argument `migrate`, it applies migrations and exits instead.
# Build from the repository root:
#   docker build -f src/ControlPlane/Bagatka.AiSloth.WebApi/Dockerfile -t aisloth-webapi .

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1-resolute AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json NuGet.Config Directory.Build.props Directory.Packages.props .editorconfig BannedSymbols.txt LoggerParameterTypes.txt ./
COPY analyzers/Bagatka.Analyzers/ analyzers/Bagatka.Analyzers/
COPY src/ src/
RUN dotnet restore src/ControlPlane/Bagatka.AiSloth.WebApi --locked-mode
RUN dotnet publish src/ControlPlane/Bagatka.AiSloth.WebApi -c Release --no-restore --self-contained false -o /out \
    -r linux-$(if [ "$TARGETARCH" = arm64 ]; then echo arm64; else echo x64; fi)

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute AS webapi
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
# Where a host on one server keeps checkpoints (hosting/compose.yaml), the WebApi's to write: a volume
# mounted here starts with this owner.
RUN mkdir --parents /var/lib/aisloth/objects && chown app:app /var/lib/aisloth/objects
COPY --from=build /out /app
WORKDIR /app
USER app
# Kestrel listens on the endpoints its configuration names, not on the base image's default port.
ENV ASPNETCORE_HTTP_PORTS=
ENTRYPOINT ["dotnet", "Bagatka.AiSloth.WebApi.dll"]
