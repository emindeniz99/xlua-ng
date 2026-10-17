# Linux image for the native analysis, coverage, fuzz and sanitizer scripts
# (Test/README.md, "Linux container"). The base is pinned by digest; the
# packages are Ubuntu 26.04's at build time.
FROM ubuntu:26.04@sha256:c098726dd9b9733f3e885f8de0ef375206a311b0a317eadc40d347b1115fb944
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang clang-tidy clang-tools llvm lld libclang-rt-dev cmake make gcc g++ \
    mono-devel python3 ca-certificates git file && rm -rf /var/lib/apt/lists/*
