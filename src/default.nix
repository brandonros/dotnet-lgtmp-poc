# Build dotnet-lgtmp-poc OCI images with nix2container
#
# Usage (on optiplex / x86_64-linux):
#   nix build .#web-image                        # build web OCI image
#   nix run .#web-image.copyToRegistry            # push to localhost:5000
#   nix build .#console-image                     # build console OCI image
#   nix run .#console-image.copyToRegistry        # push to localhost:5000
#
# Generate NuGet dependency lock (works on macOS too):
#   nix build .#web-app.fetch-deps
#   ./result deps.json
#
{ pkgs, nix2container ? null }:

let
  dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
  dotnet-aspnetcore = pkgs.dotnetCorePackages.aspnetcore_10_0;
  dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;

  web-app = pkgs.buildDotnetModule {
    pname = "dotnet-lgtmp-poc-web";
    version = "1.0.0";
    src = ./.;

    projectFile = "DotnetLgtmpPoc.Web/DotnetLgtmpPoc.Web.csproj";
    dotnet-sdk = dotnet-sdk;
    dotnet-runtime = dotnet-aspnetcore;

    # Generated with: nix build .#web-app.fetch-deps && ./result deps.json
    nugetDeps = ./deps.json;
  };

  console-app = pkgs.buildDotnetModule {
    pname = "dotnet-lgtmp-poc-console";
    version = "1.0.0";
    src = ./.;

    projectFile = "DotnetLgtmpPoc.Console/DotnetLgtmpPoc.Console.csproj";
    dotnet-sdk = dotnet-sdk;
    dotnet-runtime = dotnet-runtime;

    nugetDeps = ./deps.json;
  };

  # Pyroscope native profiler (CLR profiler + API wrapper)
  # NOTE: Officially supports .NET 6/7/8 — .NET 10 is untested but likely works
  pyroscope-native = pkgs.fetchurl {
    url = "https://github.com/grafana/pyroscope-dotnet/releases/download/v0.14.0-rc.7-pyroscope/pyroscope.0.14.0-rc.7-glibc-x86_64.tar.gz";
    sha256 = "sha256-owMe1jLla9tUn+APTzOZTvehU65rXEmUPdPVFBpXhNY="; # nix will error with expected hash on first build — copy it here
  };

  pyroscope-libs = pkgs.stdenv.mkDerivation {
    pname = "pyroscope-native-libs";
    version = "0.14.0-rc.7";
    src = pyroscope-native;
    sourceRoot = ".";
    unpackPhase = ''
      tar xzf $src
    '';
    installPhase = ''
      mkdir -p $out/pyroscope
      cp Pyroscope.Profiler.Native.so $out/pyroscope/
      cp Pyroscope.Linux.ApiWrapper.x64.so $out/pyroscope/
    '';
  };

  imageRoot = pkgs.buildEnv {
    name = "image-root";
    paths = [
      pkgs.cacert         # TLS certificates (/etc/ssl/certs)
      pkgs.icu            # .NET globalization
      pkgs.tzdata         # timezone data
      pyroscope-libs      # Pyroscope native profiler .so files
    ];
    pathsToLink = [ "/etc" "/share" "/lib" "/pyroscope" ];
  };

  web-image = nix2container.buildImage {
    name = "localhost:5000/dotnet-lgtmp-poc";
    tag = "latest";
    copyToRoot = imageRoot;

    # All env vars are set in the k8s module (dotnet-lgtmp-poc.nix) — not baked into the image
    config = {
      entrypoint = [ "${dotnet-aspnetcore}/bin/dotnet" "${web-app}/lib/dotnet-lgtmp-poc-web/DotnetLgtmpPoc.Web.dll" ];
      exposedPorts = { "8080/tcp" = {}; };
    };
  };

  console-image = nix2container.buildImage {
    name = "localhost:5000/dotnet-lgtmp-console";
    tag = "latest";
    copyToRoot = imageRoot;

    config = {
      entrypoint = [ "${dotnet-runtime}/bin/dotnet" "${console-app}/lib/dotnet-lgtmp-poc-console/DotnetLgtmpPoc.Console.dll" ];
    };
  };

in
{
  inherit web-app console-app;
  web-image = if nix2container != null then web-image else null;
  console-image = if nix2container != null then console-image else null;
}
