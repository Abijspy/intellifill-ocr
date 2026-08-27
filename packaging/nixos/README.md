# NixOS package

Each GitHub release includes `intellifill-ocr-<version>.nix`, generated with
fixed hashes for the x86_64 and ARM64 portable archives.

```bash
nix-build intellifill-ocr-6.1.0.nix -E \
  'with import <nixpkgs> {}; callPackage ./intellifill-ocr-6.1.0.nix {}'
./result/bin/intellifill-ocr
```

Install it into the current user profile with:

```bash
nix-env -f '<nixpkgs>' -iE \
  'pkgs: pkgs.callPackage ./intellifill-ocr-6.1.0.nix {}'
```

The derivation chooses `x86_64-linux` or `aarch64-linux` automatically, patches
the self-contained binaries for NixOS, installs the desktop entry and icon, and
places both `intellifill-ocr` and `intellifill` on PATH.
