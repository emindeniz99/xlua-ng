# Linux image for the native analysis, coverage, fuzz and sanitizer scripts
# (Test/README.md, "Linux container"). The base is pinned by digest; the
# packages are Ubuntu 26.04's at build time.

# The .NET SDK, for the source generator passes of sanitize.sh and coverage.sh:
# the version mise.toml pins, checked against the SHA-512 of its archive that
# Tools/dependencies.py records (DOTNET_SDK_SHA512; its --check keeps these
# three values equal to the pin). Owner's decision of 2026-10-29.
FROM ubuntu:26.04@sha256:c098726dd9b9733f3e885f8de0ef375206a311b0a317eadc40d347b1115fb944 AS dotnet
ARG TARGETARCH
ARG DOTNET_SDK_VERSION=10.0.401
ARG DOTNET_SDK_SHA512_LINUX_ARM64=58ace73ced6b4360754689a686bdfb8a317f4da6cb8bb416dbc7d0ba9f47e43e3c09f5eb1f1a1cfaacbd10df9558da4882bf2a5e195d6ab56a02c1f9f76102ed
ARG DOTNET_SDK_SHA512_LINUX_X64=51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/*
RUN set -eu; \
    case "$TARGETARCH" in \
        arm64) rid=linux-arm64; sha512="$DOTNET_SDK_SHA512_LINUX_ARM64" ;; \
        amd64) rid=linux-x64; sha512="$DOTNET_SDK_SHA512_LINUX_X64" ;; \
        *) echo "no pinned .NET SDK archive for $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    archive="dotnet-sdk-$DOTNET_SDK_VERSION-$rid.tar.gz"; \
    curl -fsSL -o "/tmp/$archive" "https://builds.dotnet.microsoft.com/dotnet/Sdk/$DOTNET_SDK_VERSION/$archive"; \
    echo "$sha512  /tmp/$archive" | sha512sum -c -; \
    mkdir -p /opt/dotnet; \
    tar -xzf "/tmp/$archive" -C /opt/dotnet; \
    rm "/tmp/$archive"

FROM ubuntu:26.04@sha256:c098726dd9b9733f3e885f8de0ef375206a311b0a317eadc40d347b1115fb944
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang clang-tidy clang-tools llvm lld libclang-rt-dev cmake make gcc g++ \
    mono-devel python3 ca-certificates git file && rm -rf /var/lib/apt/lists/*
COPY --from=dotnet /opt/dotnet /opt/dotnet
# No libicu in the image: without invariant globalization the first dotnet
# restore aborts ("Couldn't find a valid ICU package").
ENV DOTNET_ROOT=/opt/dotnet \
    PATH=/opt/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
