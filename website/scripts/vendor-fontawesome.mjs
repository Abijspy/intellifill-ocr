import { copyFile, mkdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const websiteRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const packageRoot = join(websiteRoot, "node_modules", "@fortawesome", "fontawesome-free");
const files = [
  ["css/fontawesome.min.css", "vendor/fontawesome/css/fontawesome.min.css"],
  ["css/solid.min.css", "vendor/fontawesome/css/solid.min.css"],
  ["webfonts/fa-solid-900.woff2", "vendor/fontawesome/webfonts/fa-solid-900.woff2"],
  ["LICENSE.txt", "vendor/fontawesome/LICENSE.txt"]
];

for (const [source, destination] of files) {
  const destinationPath = join(websiteRoot, destination);
  await mkdir(dirname(destinationPath), { recursive: true });
  await copyFile(join(packageRoot, source), destinationPath);
}

console.log("Vendored the Font Awesome solid icon subset for static deployment.");
