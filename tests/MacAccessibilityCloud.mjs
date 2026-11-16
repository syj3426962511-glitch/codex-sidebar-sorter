import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn, execFileSync} from 'node:child_process';
import {createRequire} from 'node:module';

if (process.env.GITHUB_ACTIONS !== 'true' || process.platform !== 'darwin') {
  throw new Error('Use the disposable cloud Mac.');
}
const [app] = process.argv.slice(2);
const python = process.argv[3] ?? 'python3';
const output = path.resolve(process.env.SORTER_ACCEPTANCE_OUTPUT ?? 'artifacts/mac-accessibility');
fs.mkdirSync(output, {recursive:true});
const root = path.join(process.env.RUNNER_TEMP, 'sorter-mcp-acceptance');
const codexHome = path.join(root, 'codex-home');
const profile = path.join(root, 'ax-desktop-profile');
for (const directory of [codexHome, profile]) fs.mkdirSync(directory, {recursive:true});
const helper = path.join(process.env.RUNNER_TEMP, 'sorter-ax-acceptance/SorterAXProbe');
const executableName = execFileSync('/usr/libexec/PlistBuddy', ['-c', 'Print :CFBundleExecutable', path.join(app, 'Contents/Info.plist')], {encoding:'utf8'}).trim();
const executable = path.join(app, 'Contents/MacOS', executableName);
const archive = path.join(app, 'Contents/Resources/app.asar');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const originals = {executable:hash(executable), archive:hash(archive)};
const require = createRequire(path.join(process.env.RUNNER_TEMP, 'sorter-test-tools/package.json'));
const {chromium} = require('playwright');
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const report = {route:'macOS Accessibility', fullAcceptancePassed:false};
let browser;
let client;

function inspect(label, operation = 'inspect', ...parameters) {
  const file = path.join(output, label + '.json');
  execFileSync(helper, [String(client.pid), file, operation, ...parameters], {timeout:60000});
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

async function activePage() {
  for (let attempt = 0; attempt < 60; attempt++) {
    const page = browser.contexts()[0].pages().find(candidate => !candidate.isClosed() && candidate.url() === 'app://-/index.html');
    if (page) return page;
    await delay(500);
  }
  throw new Error('The desktop page is unavailable.');
}

try {
  execFileSync(python, ['tests/PrepareMacAccessibility.py'], {timeout:120000});
  if (fs.existsSync(path.join(codexHome, 'auth.json'))) {
    const cli = path.join(app, 'Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex');
    execFileSync(python, ['tests/PrepareMacSidebarFixture.py', cli], {timeout:180000});
    report.fixtureCreated = true;
  }
  const log = fs.openSync(path.join(output, 'client.log'), 'a');
  client = spawn(executable, ['--user-data-dir=' + profile, '--remote-debugging-port=9441'], {
    env:{...process.env, CODEX_HOME:codexHome}, stdio:['ignore',log,log],
  });
  for (let attempt = 0; attempt < 60; attempt++) {
    try { browser = await chromium.connectOverCDP('http://127.0.0.1:9441'); break; }
    catch { await delay(1000); }
  }
  if (!browser) throw new Error('The cloud diagnostic port did not start.');
  await delay(8000);
  const enabled = inspect('01-accessibility-enabled', 'enable');
  report.accessibilityTrusted = enabled.trusted;
  report.manualAccessibilityStatus = enabled.manualAccessibilityStatus;
  const page = await activePage();
  await page.screenshot({path:path.join(output, '02-client-started.png')});
  report.page = await page.evaluate(() => ({
    text:document.body?.innerText?.slice(0,12000),
    buttons:[...document.querySelectorAll('button')].map(button => ({text:button.innerText, label:button.getAttribute('aria-label'), title:button.title})),
  }));
  const snapshot = inspect('03-accessibility-tree');
  report.accessibilityNodes = snapshot.nodes.length;
  report.accessibilityActions = [...new Set(snapshot.nodes.flatMap(node => node.actions ?? []))];
  report.inputState = {before:snapshot.before, after:snapshot.after};
  report.credentialsPresent = fs.existsSync(path.join(codexHome, 'auth.json'));
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
}
console.log(JSON.stringify(report));
process.exitCode = report.accessibilityTrusted && report.accessibilityNodes > 10 && report.clientFilesUnchanged ? 0 : 1;
