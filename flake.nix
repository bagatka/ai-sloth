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
          pkgs = import nixpkgs {
            inherit system;
            overlays = [ (import ./nix/dotnet-11-rc1.nix) ];
          };
        in
        {
          default = pkgs.mkShell {
            packages = [
              pkgs.docker-client
              pkgs.dotnet-sdk_11
              pkgs.git
              pkgs.nushell
            ];
          };
        }
      );

      formatter = forEachSystem (system: nixpkgs.legacyPackages.${system}.nixfmt);
    };
}
