(() => {
  const api = "https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest";
  const fallback = "https://github.com/Abijspy/intellifill-ocr/releases/latest";
  const buttons = document.querySelectorAll("[data-windows-download]");
  const linuxButtons = document.querySelectorAll("[data-linux-download]");
  const macButtons = document.querySelectorAll("[data-macos-download]");
  const macArmButtons = document.querySelectorAll("[data-macos-arm-download]");
  const macX64Buttons = document.querySelectorAll("[data-macos-x64-download]");
  const status = document.querySelector("[data-release-status]");

  const detectedPlatform = String(
    navigator.userAgentData?.platform || navigator.platform || navigator.userAgent || ""
  ).toLowerCase();
  const prefersLinux = detectedPlatform.includes("linux") && !detectedPlatform.includes("android");
  const prefersMac = detectedPlatform.includes("mac");

  buttons.forEach((button) => {
    button.classList.toggle("primary", !prefersLinux && !prefersMac);
    button.toggleAttribute("data-recommended", !prefersLinux && !prefersMac);
  });
  linuxButtons.forEach((button) => {
    button.classList.toggle("primary", prefersLinux);
    button.toggleAttribute("data-recommended", prefersLinux);
  });
  macButtons.forEach((button) => {
    button.classList.toggle("primary", prefersMac);
    button.toggleAttribute("data-recommended", prefersMac);
  });
  document.querySelectorAll(".actions [data-recommended]").forEach((button) => {
    button.parentElement?.prepend(button);
  });

  fetch(api, { headers: { Accept: "application/vnd.github+json" } })
    .then((response) => {
      if (!response.ok) throw new Error(String(response.status));
      return response.json();
    })
    .then((release) => {
      const installer = release.assets?.find((asset) => /setup-win-x64\.exe$/i.test(asset.name));
      const macArm = release.assets?.find((asset) => /osx-arm64\.dmg$/i.test(asset.name));
      const macX64 = release.assets?.find((asset) => /osx-x64\.dmg$/i.test(asset.name));
      if (!installer) throw new Error("No Windows installer");
      buttons.forEach((button) => { button.href = installer.browser_download_url; });
      if (macArm) macArmButtons.forEach((button) => { button.href = macArm.browser_download_url; });
      if (macX64) macX64Buttons.forEach((button) => { button.href = macX64.browser_download_url; });
      if (status) status.textContent = release.tag_name;
    })
    .catch(() => {
      buttons.forEach((button) => { button.href = fallback; });
      if (status) status.textContent = "Latest release";
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
