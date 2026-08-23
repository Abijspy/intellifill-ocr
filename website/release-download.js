(() => {
  const api = "https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest";
  const fallback = "https://github.com/Abijspy/intellifill-ocr/releases/latest";
  const buttons = document.querySelectorAll("[data-windows-download]");
  const status = document.querySelector("[data-release-status]");

  fetch(api, { headers: { Accept: "application/vnd.github+json" } })
    .then((response) => {
      if (!response.ok) throw new Error(String(response.status));
      return response.json();
    })
    .then((release) => {
      const installer = release.assets?.find((asset) => /setup-win-x64\.exe$/i.test(asset.name));
      if (!installer) throw new Error("No Windows installer");
      buttons.forEach((button) => { button.href = installer.browser_download_url; });
      if (status) status.textContent = `${release.tag_name} · Windows x64 installer · ${(installer.size / 1048576).toFixed(1)} MB`;
    })
    .catch(() => {
      buttons.forEach((button) => { button.href = fallback; });
      if (status) status.textContent = "Latest Windows installer on GitHub Releases";
    });
})();
