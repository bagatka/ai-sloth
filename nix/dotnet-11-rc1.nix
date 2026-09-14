final: prev:

let
  version = "11.0.100-rc.1.26425.128";

  sources = {
    aarch64-darwin = {
      rid = "osx-arm64";
      hash = "sha512-lptvOKDr6FPdYy3J7wfdBpDMlr/lHWN50J16QF9HFgKrXl4JxQX5N4Nvqe6XF6CAeoyChZnIeGVSj9lSyMaViw==";
    };

    aarch64-linux = {
      rid = "linux-arm64";
      hash = "sha512-WePIwZN/EiGfQ3oF/65v661+iBVerIPCvJe9tZCnCSnf3QtYWJjgI804g/9Ehlv7jN6enSZdPk8bpJ23cgyV5w==";
    };

    x86_64-linux = {
      rid = "linux-x64";
      hash = "sha512-YIUYV3qrnbM9uS69dPD8m/vMez1HNhn0cxrKLtuoCvbFcaONW+aZSGL8Rnn289E9t2dxL8tvcKdoOXShiCSgzA==";
    };
  };

  source = sources.${prev.stdenv.hostPlatform.system};

  sdk = prev.dotnetCorePackages.sdk_11_0-bin;

  unwrapped = sdk.unwrapped.overrideAttrs {
    inherit version;

    src = prev.fetchurl {
      url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${version}/dotnet-sdk-${version}-${source.rid}.tar.gz";
      inherit (source) hash;
    };

    # The native CLI loads ICU and OpenSSL dynamically.
    appendRunpaths = prev.lib.optionals prev.stdenv.hostPlatform.isLinux [
      "${prev.lib.getLib final.icu}/lib"
      "${prev.lib.getLib final.openssl}/lib"
    ];
  };
in
{
  dotnet-sdk_11 = sdk.overrideAttrs (old: {
    inherit version;
    src = unwrapped;
    passthru = old.passthru // {
      inherit unwrapped;
    };
  });
}
