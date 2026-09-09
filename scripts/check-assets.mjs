import { access } from "node:fs/promises";

const requiredAssets = [
  "src/Odca.Web/wwwroot/css/site.css"
];

await Promise.all(requiredAssets.map((path) => access(path)));
console.log(`Validated ${requiredAssets.length} web asset(s).`);
