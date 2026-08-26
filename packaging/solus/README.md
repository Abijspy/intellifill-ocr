# Solus eopkg packaging

Solus packages must be built with the native `solbuild`/`ypkg` toolchain. After
the release tarball is available, generate the pinned recipe and build it:

```bash
bash scripts/prepare-solus-package.sh <version> release/linux/IntelliFillOCR-<version>-linux-x64.tar.gz
sudo solbuild build packaging/solus/package.yml
sudo eopkg it ./intellifill-ocr-*.eopkg
```

The generated `package.yml` pins the GitHub release asset with its SHA-256 hash.
Submit the tested recipe to the Solus package repository to make updates flow
through normal `sudo eopkg upgrade` operations.
