{
  description = "dotnet-lgtmp-poc – .NET 10 LGTMP observability demo";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    nix2container = {
      url = "github:nlewo/nix2container";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, nix2container }:
    let
      # Image only builds on x86_64-linux (optiplex); app builds on both
      systems = [ "x86_64-linux" "aarch64-darwin" "x86_64-darwin" "aarch64-linux" ];
      eachSystem = f: nixpkgs.lib.genAttrs systems (system: f {
        pkgs = nixpkgs.legacyPackages.${system};
        n2c = nix2container.packages.${system}.nix2container;
        inherit system;
      });
    in
    {
      packages = eachSystem ({ pkgs, n2c, system }:
        let
          built = import ./src/default.nix {
            inherit pkgs;
            nix2container = if system == "x86_64-linux" then n2c else null;
          };
        in
        {
          default = built.web-app;
          web-app = built.web-app;
          console-app = built.console-app;
        } // nixpkgs.lib.optionalAttrs (system == "x86_64-linux") {
          web-image = built.web-image;
          console-image = built.console-image;
        }
      );
    };
}
