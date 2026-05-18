const crypto = require("crypto");
const fs = require("fs");

const repository = process.env.GITHUB_REPO || "jellystrm/source-manager";
const version = process.env.VERSION || "1.0.0.0";
const file = process.env.FILE || `source-manager-${version}.zip`;
const targetAbi = process.env.TARGET_ABI || "10.11.0.0";
const manifestPath = "./manifest.json";
const zipPath = `./dist/${file}`;

if (!fs.existsSync(manifestPath)) {
  throw new Error("manifest.json file not found");
}

if (!fs.existsSync(zipPath)) {
  throw new Error(`${zipPath} file not found. Run make zip first.`);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
const checksum = crypto
  .createHash("md5")
  .update(fs.readFileSync(zipPath))
  .digest("hex");

const newVersion = {
  version,
  changelog: `- See the full changelog at [GitHub](https://github.com/${repository}/releases/tag/${version})\n`,
  targetAbi,
  sourceUrl: `https://github.com/${repository}/releases/download/${version}/${file}`,
  checksum,
  timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, "Z"),
};

manifest[0].versions = [
  newVersion,
  ...manifest[0].versions.filter((entry) => entry.version !== version),
];

fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
console.log(`Updated ${manifestPath} with ${version}.`);
