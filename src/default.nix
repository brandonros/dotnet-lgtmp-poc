# Build dotnet-lgtmp-poc OCI image with nix2container
#
# Usage (on optiplex / x86_64-linux):
#   nix build .#image                          # build OCI image
#   nix run .#image.copyToRegistry             # push to localhost:5000
#
# Generate NuGet dependency lock (works on macOS too):
#   nix build .#app.fetch-deps
#   ./result deps.json
#
{ pkgs, nix2container ? null }:

let
  dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
  dotnet-aspnetcore = pkgs.dotnetCorePackages.aspnetcore_10_0;

  app = pkgs.buildDotnetModule {
    pname = "dotnet-lgtmp-poc";
    version = "1.0.0";
    src = ./DotnetLgtmpPoc;

    projectFile = "DotnetLgtmpPoc.csproj";
    dotnet-sdk = dotnet-sdk;
    dotnet-runtime = dotnet-aspnetcore;

    # Generated with: nix build .#app.fetch-deps && ./result deps.json
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

  image = nix2container.buildImage {
    name = "localhost:5000/dotnet-lgtmp-poc";
    tag = "latest";

    copyToRoot = pkgs.buildEnv {
      name = "image-root";
      paths = [
        pkgs.cacert         # TLS certificates (/etc/ssl/certs)
        pkgs.icu            # .NET globalization
        pkgs.tzdata         # timezone data
        pyroscope-libs      # Pyroscope native profiler .so files
      ];
      pathsToLink = [ "/etc" "/share" "/lib" "/pyroscope" ];
    };

    # All env vars are set in the k8s module (dotnet-lgtmp-poc.nix) — not baked into the image
    config = {
      entrypoint = [ "${dotnet-aspnetcore}/bin/dotnet" "${app}/lib/dotnet-lgtmp-poc/DotnetLgtmpPoc.dll" ];
      exposedPorts = { "8080/tcp" = {}; };
    };
  };

in
{
  inherit app;
  image = if nix2container != null then image else null;
}
