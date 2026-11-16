import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn, spawnSync, execFileSync} from 'node:child_process';
import {createRequire} from 'node:module';

if (process.env.GITHUB_ACTIONS !== 'true' || process.platform !== 'darwin') {
  throw new Error('Use the disposable cloud Mac.');
}

const [app, python] = process.argv.slice(2);
const root = path.join(process.env.RUNNER_TEMP, 'sorter-mcp-acceptance');
const output = path.resolve(process.env.SORTER_ACCEPTANCE_OUTPUT ?? 'artifacts/mcp');
const codexHome = path.join(root, 'codex-home');
const profile = path.join(root, 'desktop-profile');
const receipts = path.join(output, 'bridge');
for (const directory of [codexHome, profile, receipts]) fs.mkdirSync(directory, {recursive:true});
const require = createRequire(path.join(process.env.RUNNER_TEMP, 'sorter-test-tools/package.json'));
const {chromium} = require('playwright');
const probe = path.resolve('tests/McpBridgeProbe.py');
const signedHost = JSON.parse(execFileSync(python, ['tests/PrepareSignedRelay.py'], {encoding:'utf8', timeout:300000}));
const relay = signedHost.node;
const relayScript = path.resolve('tests/NativeRelay.mjs');
const config = `cli_auth_credentials_store = "file"\n[mcp_servers.sidebar_sorter_cloud_probe]\ncommand = ${JSON.stringify(python)}\nargs = ${JSON.stringify([probe, receipts])}\nrequired = false\nstartup_timeout_sec = 5\ntool_timeout_sec = 10\n[mcp_servers.sidebar_sorter_cloud_probe.env]\nGITHUB_ACTIONS = "true"\nSORTER_CLOUD_RELAY = ${JSON.stringify(relay)}\nSORTER_CLOUD_RELAY_SCRIPT = ${JSON.stringify(relayScript)}\n`;
fs.writeFileSync(path.join(codexHome, 'config.toml'), config);
const executableName = execFileSync('/usr/libexec/PlistBuddy', ['-c', 'Print :CFBundleExecutable', path.join(app, 'Contents/Info.plist')], {encoding:'utf8'}).trim();
const executable = path.join(app, 'Contents/MacOS', executableName);
const archive = path.join(app, 'Contents/Resources/app.asar');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const originals = {executable:hash(executable), archive:hash(archive)};
const report = {mcpConfigRegistered:true, signedDesktopLaunched:false, nativeCatalogAccessible:false};
report.signedHost = signedHost;
report.relaySignature = spawnSync('/usr/bin/codesign', ['-dv', '--verbose=4', '-r-', relay], {encoding:'utf8'}).stderr;
const authorizer = path.join(app, 'Contents/Resources/native/browser-use-peer-authorization.node');
if (fs.existsSync(authorizer)) {
  report.nativePeerRequirements = execFileSync('/usr/bin/strings', ['-a', authorizer], {encoding:'utf8'})
    .split('\n').filter(line => /anchor apple|subject\.OU|com\.openai|org\.openai|untrusted-|team-identifier/.test(line)).slice(0, 45);
}
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
let browser;
let client;

async function desktopRequest(page, method, params) {
  return page.evaluate(async ({method, params}) => {
    const id = 'sorter-cloud-' + crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const observed = [];
      const listener = event => {
        const message = event.data;
        if (message?.type?.startsWith('mcp-') && observed.length < 10) {
          observed.push({type:message.type, keys:Object.keys(message)});
        }
        const response = message?.type === 'mcp-response' ? message.message : null;
        if (response?.id !== id) return;
        clearTimeout(timer);
        window.removeEventListener('message', listener);
        response.error ? reject(new Error(JSON.stringify(response.error))) : resolve(response.result);
      };
      const timer = setTimeout(() => {
        window.removeEventListener('message', listener);
        reject(new Error(method + ' timed out. Message types ' + JSON.stringify(observed)));
      }, 30000);
      window.addEventListener('message', listener);
      window.electronBridge.sendMessageFromView({
        type:'mcp-request', hostId:'local', request:{id, method, params},
        priority:'interactive', source:'sidebar-sorter-cloud-acceptance', timeoutMs:25000,
      }).catch(error => {
        clearTimeout(timer);
        window.removeEventListener('message', listener);
        reject(error);
      });
    });
  }, {method, params});
}

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
  await delay(5000);
  let page;
  for (let attempt = 0; attempt < 60; attempt++) {
    page = browser.contexts()[0].pages().find(page => !page.isClosed() && page.url() === 'app://-/index.html');
    if (page) break;
    await delay(500);
  }
  if (!page) throw new Error('The official desktop page is unavailable.');
  await page.waitForLoadState('domcontentloaded');
  await delay(2000);
  if (page.isClosed()) {
    page = browser.contexts()[0].pages().find(page => !page.isClosed() && page.url() === 'app://-/index.html');
    if (!page) throw new Error('The signed-in desktop replacement page is unavailable.');
    await page.waitForLoadState('domcontentloaded');
  }
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
  const thread = await desktopRequest(page, 'thread/start', {cwd:root, ephemeral:true, approvalPolicy:'never', sandbox:'read-only'});
  report.desktopMcpStatus = await desktopRequest(page, 'mcpServerStatus/list', {threadId:thread.thread.id, serverName:'sidebar_sorter_cloud_probe', detail:'toolsAndAuthOnly'});
  for (let attempt = 0; attempt < 30; attempt++) {
    const files = fs.readdirSync(receipts).filter(name => name.endsWith('.json'));
    report.bridges = files.map(name => JSON.parse(fs.readFileSync(path.join(receipts, name), 'utf8')));
    if (report.bridges.some(bridge => bridge.checkedAt)) break;
    await delay(1000);
  }
  report.nativeCatalogAccessible = report.bridges?.some(bridge => bridge.nativeCatalogAccessible) ?? false;
  report.mcpInitialized = report.bridges?.some(bridge => bridge.mcpInitialized) ?? false;
  report.desktopOwnedBridgeVerified = report.bridges?.some(bridge => bridge.nativeCatalogAccessible && bridge.ancestry?.some(ancestor => ancestor.pid === client.pid)) ?? false;
  await snapshot(page, '04-bridge-result');
} catch (error) {
  report.error = String(error.stack ?? error);
} finally {
  if (browser) await browser.close();
  if (client && client.exitCode === null) {
    client.kill('SIGTERM');
    await Promise.race([new Promise(resolve => client.once('exit', resolve)), delay(5000)]);
  }
  report.clientFilesUnchanged = originals.executable === hash(executable) && originals.archive === hash(archive);
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  execFileSync(python, ['tests/read_mac_transport.py', archive, path.join(output, 'shipping-mcp-contract.json'), '--mcp']);
}
console.log(JSON.stringify({mcpInitialized:report.mcpInitialized, nativeCatalogAccessible:report.nativeCatalogAccessible, desktopOwnedBridgeVerified:report.desktopOwnedBridgeVerified, clientFilesUnchanged:report.clientFilesUnchanged, error:report.error}));
process.exitCode = report.nativeCatalogAccessible && report.clientFilesUnchanged ? 0 : 1;

