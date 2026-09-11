import { access } from "node:fs/promises";

const requiredAssets = [
  "src/Odca.Web/wwwroot/css/site.css",
  "src/Odca.Web/wwwroot/js/site.js",
  "src/Odca.Web/wwwroot/js/navigation.js",
  "src/Odca.Web/wwwroot/js/forms.js",
  "src/Odca.Web/wwwroot/js/dialogs.js",
  "src/Odca.Web/wwwroot/js/team.js",
  "src/Odca.Web/wwwroot/js/contracts.js"
];

await Promise.all(requiredAssets.map((path) => access(path)));
console.log(`Validated ${requiredAssets.length} web asset(s).`);
