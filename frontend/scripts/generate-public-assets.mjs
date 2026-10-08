// Original geometric illustrations under the root MIT license.
// Run from frontend after npm ci. Sharp is already supplied by Next.js.
import fs from "node:fs/promises";
import sharp from "sharp";

const book =
  '<path d="M12 14H26L32 20L38 14H52V48H38L32 54L26 48H12Z" fill="none" stroke="#4dc9b8" stroke-width="4" stroke-linejoin="round"/><path d="M32 20V54" stroke="#e2e8f0" stroke-width="3"/>';
const icon = `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 64 64"><rect width="64" height="64" rx="12" fill="#15252b"/>${book}</svg>`;
await fs.mkdir("public/icons", { recursive: true });
await fs.mkdir("public/landing", { recursive: true });
await fs.writeFile("public/icon.svg", `${icon}\n`);
for (const size of [192, 512]) {
  const png = await sharp(Buffer.from(icon)).resize(size, size).png().toBuffer();
  for (const name of [`icon-${size}`, `icon-maskable-${size}`]) {
    await fs.writeFile(`public/icons/${name}.png`, png);
  }
}
await sharp(Buffer.from(icon)).resize(180, 180).png().toFile("public/apple-touch-icon.png");
const favicon = await sharp(Buffer.from(icon)).resize(32, 32).png().toBuffer();
const header = Buffer.alloc(22);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(1, 4);
header[6] = header[7] = 32;
header.writeUInt16LE(1, 10);
header.writeUInt16LE(32, 12);
header.writeUInt32LE(favicon.length, 14);
header.writeUInt32LE(22, 18);
await fs.writeFile("public/favicon.ico", Buffer.concat([header, favicon]));
const og = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630"><rect width="1200" height="630" fill="#15252b"/><g transform="translate(376 91) scale(7)">${book}</g></svg>`;
await sharp(Buffer.from(og)).png().toFile("public/og-image.png");
const avatar =
  '<svg xmlns="http://www.w3.org/2000/svg" width="480" height="576" viewBox="0 0 480 576"><rect width="480" height="576" fill="#15252b"/><circle cx="240" cy="192" r="76" fill="#89a9af"/><path d="M88 576V420C88 334 156 292 240 292S392 334 392 420V576Z" fill="#89a9af"/></svg>';
await sharp(Buffer.from(avatar)).png().toFile("public/landing/author.png");
