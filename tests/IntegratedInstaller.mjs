import assert from "node:assert/strict";
import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const [payload, reportFolder, toolFolder] = process.argv.slice(2).map(path => resolve(path));
const { chromium } = createRequire(resolve(toolFolder, "package.json"))("playwright");
const manifest = JSON.parse(await readFile(resolve(payload, "manifest.json"), "utf8"));
const routes = {
    Windows: ["win-x64"],
    macOS: ["osx-arm64", "osx-x64"],
    Linux: ["linux-x64"],
};
const nativePlatforms = { Windows: "Win32", macOS: "MacIntel", Linux: "Linux x86_64" };
await mkdir(reportFolder, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
    const setupUrl = pathToFileURL(resolve(payload, manifest.entry)).href;
    const externalRequests = [];
    let page;
    for (const [label, platform] of Object.entries(nativePlatforms)) {
        if (page) await page.close();
        page = await browser.newPage({ viewport: { width: 1150, height: 780 } });
        page.on("request", request => {
            if (!request.url().startsWith("file:")) externalRequests.push(request.url());
        });
        await page.addInitScript(value => {
            Object.defineProperty(navigator, "userAgentData", { value: null });
            Object.defineProperty(navigator, "platform", { value });
        }, platform);
        await page.goto(setupUrl);
        assert.equal(await page.getByRole("button", { name: label, exact: true }).getAttribute("aria-pressed"), "true");
    }
    for (const [label, architectures] of Object.entries(routes)) {
        const target = page.getByRole("button", { name: label, exact: true });
        await target.click();
        assert.equal(await target.getAttribute("aria-pressed"), "true");
        assert.equal(await page.locator("section:visible").count(), 1);
        const links = await page.locator("section:visible a").evaluateAll(items => items.map(item => item.getAttribute("href")));
        assert.deepEqual(links, architectures.map(architecture => manifest.installers[architecture].path));
        for (const architecture of architectures) {
            const { path, size } = manifest.installers[architecture];
            assert.equal((await stat(resolve(payload, path))).size, size);
        }
        await page.screenshot({ path: resolve(reportFolder, `installer-${label.toLowerCase()}.png`) });
    }
    assert.deepEqual(externalRequests, []);
    await writeFile(resolve(reportFolder, "installer-switch.json"), JSON.stringify({
        version: manifest.version,
        automaticDetection: Object.keys(nativePlatforms),
        switched: Object.keys(routes),
        payloadCount: Object.keys(manifest.installers).length,
        offlinePage: true,
        success: true,
    }, null, 2));
} finally {
    await browser.close();
}
