// Builds the README badges as SVG files so they are served from GitHub
// instead of a third-party badge service.
//
// Usage: node badges.mjs <output-dir>
// Reads RELEASE, LAST_COMMIT (ISO date), LICENSE and DOTNET from the environment.

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { makeBadge } from "badge-maker";
import { siDotnet, siGit, siGithub, siOpensourceinitiative } from "simple-icons";

const outDir = process.argv[2];
if (!outDir) {
  console.error("Usage: node badges.mjs <output-dir>");
  process.exit(1);
}

const { RELEASE, LAST_COMMIT, LICENSE, DOTNET } = process.env;
for (const [name, value] of Object.entries({ RELEASE, LAST_COMMIT, LICENSE, DOTNET })) {
  if (!value) {
    console.error(`Missing ${name}`);
    process.exit(1);
  }
}

const labelColor = "#24292e";

function logo(icon) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="#fff" d="${icon.path}"/></svg>`;
  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

// Same colour steps shields.io uses for commit age.
function ageColor(date) {
  const days = (Date.now() - new Date(date).getTime()) / 86_400_000;
  if (days <= 7) return "#44bb00";
  if (days <= 30) return "#97ca00";
  if (days <= 180) return "#dfb317";
  if (days <= 365) return "#fe7d37";
  return "#e05d44";
}

const badges = {
  "release.svg": { label: "release", message: RELEASE, color: "#007ec6", logo: siGithub },
  "last-commit.svg": {
    label: "last commit",
    message: LAST_COMMIT.slice(0, 10),
    color: ageColor(LAST_COMMIT),
    logo: siGit,
  },
  "license.svg": { label: "license", message: LICENSE, color: "#67ac09", logo: siOpensourceinitiative },
  "dotnet-framework.svg": { label: ".NET Framework", message: DOTNET, color: "#512bd4", logo: siDotnet },
};

mkdirSync(outDir, { recursive: true });
for (const [file, badge] of Object.entries(badges)) {
  const svg = makeBadge({
    label: badge.label,
    message: badge.message,
    color: badge.color,
    labelColor,
    logoBase64: logo(badge.logo),
    style: "flat",
  });
  writeFileSync(join(outDir, file), svg);
  console.log(`${file}: ${badge.label} | ${badge.message}`);
}
