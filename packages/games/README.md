# Bootstrapped game package feed

This directory contains immutable packages produced by the separate `FortuneForge.Games` repository. It allows a standalone FortuneForge application checkout and its deployment build to restore approved game versions before a remote private registry is connected.

Do not edit package contents. Publish a new semantic version from `FortuneForge.Games`, copy the resulting packages here, then update explicit application package references.
