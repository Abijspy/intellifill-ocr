(() => {
  const api = "https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest";
  const statuses = document.querySelectorAll("[data-release-status]");
  const assetLinks = document.querySelectorAll("[data-release-asset]");
  const downloadGroups = document.querySelectorAll("[data-download-group]");

  const assetPatterns = {
    "windows-x64": /setup-win-x64\.exe$/i,
    "windows-arm64": /setup-win-arm64\.exe$/i,
    "macos-arm64": /osx-arm64\.dmg$/i,
    "macos-x64": /osx-x64\.dmg$/i,
    "flatpak-x64": /linux-x64\.flatpak$/i,
    "flatpak-arm64": /linux-arm64\.flatpak$/i,
    "deb-x64": /_amd64\.deb$/i,
    "deb-arm64": /_arm64\.deb$/i,
    "rpm-x64": /\.x86_64\.rpm$/i,
    "rpm-arm64": /\.aarch64\.rpm$/i,
    "arch-x64": /-x86_64\.pkg\.tar\.zst$/i,
    "arch-arm64": /-aarch64\.pkg\.tar\.zst$/i,
    "tar-x64": /-linux-x64\.tar\.gz$/i,
    "tar-arm64": /-linux-arm64\.tar\.gz$/i,
    nixos: /intellifill-ocr-[\d.]+\.nix$/i,
    solus: /-solus-package\.yml$/i,
    checksums: /^SHA256SUMS\.txt$/i
  };

  const detectedPlatform = String(
    navigator.userAgentData?.platform || navigator.platform || navigator.userAgent || ""
  ).toLowerCase();
  const prefersLinux = detectedPlatform.includes("linux") && !detectedPlatform.includes("android");
  const prefersMac = detectedPlatform.includes("mac");
  const prefersArm = detectedPlatform.includes("arm64") || detectedPlatform.includes("aarch64");

  fetch(api, { headers: { Accept: "application/vnd.github+json" } })
    .then((response) => {
      if (!response.ok) throw new Error(String(response.status));
      return response.json();
    })
    .then((release) => {
      const assets = release.assets || [];
      assetLinks.forEach((link) => {
        const pattern = assetPatterns[link.dataset.releaseAsset];
        const asset = pattern && assets.find((candidate) => pattern.test(candidate.name));
        if (!asset) return;
        link.href = asset.browser_download_url;
        link.hidden = false;
      });

      downloadGroups.forEach((group) => {
        group.hidden = !group.querySelector("[data-release-asset]:not([hidden])");
      });

      const preferredKey = prefersMac
        ? (prefersArm ? "macos-arm64" : "macos-x64")
        : prefersLinux
          ? (prefersArm ? "flatpak-arm64" : "flatpak-x64")
          : (prefersArm ? "windows-arm64" : "windows-x64");
      const recommended = document.querySelector(`[data-release-asset="${preferredKey}"]:not([hidden])`);
      if (recommended) {
        recommended.classList.add("recommended-asset");
        recommended.querySelector("b").textContent += " · Recommended";
        recommended.closest("[data-download-group]")?.parentElement?.prepend(recommended.closest("[data-download-group]"));
      }

      statuses.forEach((status) => { status.textContent = release.tag_name; });
    })
    .catch(() => {
      downloadGroups.forEach((group) => { group.hidden = true; });
      statuses.forEach((status) => { status.textContent = "View latest release"; });
    });

  document.querySelectorAll("[data-copy-target]").forEach((button) => {
    button.addEventListener("click", async () => {
      const command = document.getElementById(button.dataset.copyTarget);
      if (!command) return;

      try {
        await navigator.clipboard.writeText(command.innerText.trim());
        const originalText = button.textContent;
        button.textContent = "Copied!";
        button.classList.add("copied");
        window.setTimeout(() => {
          button.textContent = originalText;
          button.classList.remove("copied");
        }, 1600);
      } catch {
        button.textContent = "Select text";
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(command);
        selection.removeAllRanges();
        selection.addRange(range);
      }
    });
  });

  const viewer = document.querySelector("[data-image-viewer]");
  const viewerImage = document.querySelector("[data-image-viewer-image]");
  const viewerCaption = document.querySelector("[data-image-viewer-caption]");
  let viewerTrigger = null;

  if (viewer && viewerImage && typeof viewer.showModal === "function") {
    document.querySelectorAll(".gallery a").forEach((link) => {
      link.addEventListener("click", (event) => {
        const image = link.querySelector("img");
        if (!image) return;

        event.preventDefault();
        viewerTrigger = link;
        viewerImage.src = link.href;
        viewerImage.alt = image.alt;
        viewerCaption.textContent = image.alt;
        viewer.showModal();
        document.body.classList.add("viewer-open");
      });
    });

    viewer.addEventListener("click", (event) => {
      if (event.target === viewer) viewer.close();
    });

    viewer.addEventListener("close", () => {
      document.body.classList.remove("viewer-open");
      viewerImage.src = "";
      viewerTrigger?.focus();
      viewerTrigger = null;
    });
  }
})();
