import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn, execFileSync} from 'node:child_process';
import {createRequire} from 'node:module';

if (process.env.GITHUB_ACTIONS !== 'true' || process.platform !== 'darwin') {
  throw new Error('Use the disposable cloud Mac.');
}

const [app, python] = process.argv.slice(2);
const root = path.join(process.env.RUNNER_TEMP, 'sorter-mcp-acceptance');
const output = path.resolve('artifacts/mcp');
const codexHome = path.join(root, 'codex-home');
const profile = path.join(root, 'desktop-profile');
const receipts = path.join(output, 'bridge');
for (const directory of [codexHome, profile, receipts]) fs.mkdirSync(directory, {recursive:true});
const require = createRequire(path.join(process.env.RUNNER_TEMP, 'sorter-test-tools/package.json'));
const {chromium} = require('playwright');
const probe = path.resolve('tests/McpBridgeProbe.py');
const config = `[mcp_servers.sidebar_sorter_cloud_probe]\ncommand = ${JSON.stringify(python)}\nargs = ${JSON.stringify([probe, receipts])}\nrequired = false\nstartup_timeout_sec = 5\ntool_timeout_sec = 10\n[mcp_servers.sidebar_sorter_cloud_probe.env]\nGITHUB_ACTIONS = "true"\n`;
fs.writeFileSync(path.join(codexHome, 'config.toml'), config);
const executableName = execFileSync('/usr/libexec/PlistBuddy', ['-c', 'Print :CFBundleExecutable', path.join(app, 'Contents/Info.plist')], {encoding:'utf8'}).trim();
const executable = path.join(app, 'Contents/MacOS', executableName);
const archive = path.join(app, 'Contents/Resources/app.asar');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const originals = {executable:hash(executable), archive:hash(archive)};
const report = {mcpConfigRegistered:true, signedDesktopLaunched:false, nativeCatalogAccessible:false};
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
let browser;
let client;

async function snapshot(page, label) {
  await page.screenshot({path:path.join(output, label + '.png')});
  report[label] = await page.evaluate(() => ({
    title:document.title, text:document.body?.innerText?.slice(0, 12000),
    bridgeKeys:Object.keys(globalThis).filter(key => /electron|bridge/i.test(key)),
    buttons:[...document.querySelectorAll('button')].map(button => ({text:button.innerText, label:button.getAttribute('aria-label'), title:button.title})),
  }));
}

try {
  const log = fs.openSync(path.join(output, 'client.log'), 'a');
  client = spawn(executable, ['--user-data-dir=' + profile, '--remote-debugging-port=9440'], {
    env:{...process.env, CODEX_HOME:codexHome}, stdio:['ignore', log, log],
  });
  for (let attempt = 0; attempt < 60; attempt++) {
    try { browser = await chromium.connectOverCDP('http://127.0.0.1:9440'); break; }
    catch { await delay(1000); }
  }
  if (!browser) throw new Error('The official desktop diagnostic port did not start.');
  report.signedDesktopLaunched = true;
  const page = browser.contexts()[0].pages().find(page => page.url().startsWith('app://'));
  if (!page) throw new Error('The official desktop page is unavailable.');
  await page.waitForLoadState('domcontentloaded');
  await delay(3000);
  await snapshot(page, '01-client-started');
  const settings = page.getByRole('button', {name:/settings|设置/i});
  if (await settings.count()) await settings.first().click();
  else await page.keyboard.press('Meta+,');
  await delay(2000);
  await snapshot(page, '02-settings');
  const mcp = page.getByText(/MCP servers|MCP 服务器/i, {exact:true});
  if (await mcp.count()) await mcp.first().click();
  await delay(1500);
  await snapshot(page, '03-mcp-settings');
  for (let attempt = 0; attempt < 30; attempt++) {
    const files = fs.readdirSync(receipts).filter(name => name.endsWith('.json'));
    report.bridges = files.map(name => JSON.parse(fs.readFileSync(path.join(receipts, name), 'utf8')));
    if (report.bridges.some(bridge => bridge.checkedAt)) break;
    await delay(1000);
  }
  report.nativeCatalogAccessible = report.bridges?.some(bridge => bridge.nativeCatalogAccessible) ?? false;
  report.mcpInitialized = report.bridges?.some(bridge => bridge.mcpInitialized) ?? false;
  await snapshot(page, '04-bridge-result');
} catch (error) {
  report.error = String(error.stack ?? error);
} finally {
  if (browser) await browser.close();
  if (client && client.exitCode === null) client.kill('SIGTERM');
  report.clientFilesUnchanged = originals.executable === hash(executable) && originals.archive === hash(archive);
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  execFileSync(python, ['tests/read_mac_transport.py', archive, path.join(output, 'shipping-mcp-contract.json'), '--mcp']);
}
console.log(JSON.stringify({mcpInitialized:report.mcpInitialized, nativeCatalogAccessible:report.nativeCatalogAccessible, clientFilesUnchanged:report.clientFilesUnchanged, error:report.error}));
process.exitCode = report.nativeCatalogAccessible && report.clientFilesUnchanged ? 0 : 1;

