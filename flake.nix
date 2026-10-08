{
  description = "AI Sloth development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs =
    { nixpkgs, ... }:
    let
      supportedSystems = [
        "aarch64-darwin"
        "aarch64-linux"
        "x86_64-linux"
      ];

      forEachSystem = nixpkgs.lib.genAttrs supportedSystems;
    in
    {
      devShells = forEachSystem (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          dotnetRoot = "${pkgs.dotnet-sdk_11}/share/dotnet";
        in
        {
          default = pkgs.mkShell {
            packages = [
              # The Docker client only: the daemon is a system service (Docker Desktop with WSL
              # integration, a system dockerd, OrbStack, or Colima) that a dev shell can't provide.
              pkgs.docker-client
              pkgs.dotnet-sdk_11
              pkgs.git
              pkgs.nushell

              # Native AOT publishing (slothd) compiles and links with clang.
              pkgs.clang

              # `dotnet aspire deploy` signs in to Azure and compiles Bicep with the Azure CLI.
              pkgs.azure-cli

              # The landing page (site/) builds with Astro.
              pkgs.nodejs_24

              # The scripts the control plane runs in nooks are checked like code.
              pkgs.shellcheck
            ];

            # Native AOT links against zlib.
            buildInputs = [ pkgs.zlib ];

            # The compiler's apphost reads DOTNET_ROOT_<ARCH> before DOTNET_ROOT, so pin all of
            # them; otherwise a host install (dnvm, Microsoft's installer) leaks into the shell.
            DOTNET_ROOT = dotnetRoot;
            DOTNET_ROOT_X64 = dotnetRoot;
            DOTNET_ROOT_ARM64 = dotnetRoot;
          };
        }
      );

      formatter = forEachSystem (system: nixpkgs.legacyPackages.${system}.nixfmt);
    };
}
