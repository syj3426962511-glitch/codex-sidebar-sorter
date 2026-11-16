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

async function readCurrentPage(action) {
  for (let attempt = 0; attempt < 10; attempt++) {
    try { return await action(await activePage()); }
    catch (error) {
      if (!String(error).includes('closed') || !browser.isConnected() || attempt === 9) throw error;
      await delay(500);
    }
  }
}

function activateAccessibilityButton(node, label) {
  if (node.actions?.includes('AXPress')) return inspect(label, 'action', node.path, 'AXPress');
  const focused = inspect(label + '-focused', 'set', node.path, 'AXFocused', 'true');
  if (focused.setStatus !== 0) throw new Error('The action did not accept Accessibility focus.');
  return inspect(label, 'key', '49');
}

async function trustCreatedFixtureFolder() {
  const state = inspect('fixture-trust-before');
  const trust = state.nodes.find(node => node.AXRole === 'AXButton' && node.AXTitle === 'Trust folder');
  if (!trust) return;
  const allowed = ['Zulu AX background project', 'Alpha AX background project'].map(name => path.join(process.env.HOME, 'Documents', 'ChatGPT', name));
  const directory = allowed.find(candidate => state.nodes.some(node => node.AXValue === candidate));
  if (!directory) throw new Error('The folder trust prompt belongs to another directory.');
  const files = fs.readdirSync(directory);
  if (files.some(name => name !== '.git')) throw new Error('The new fixture folder contains files that require inspection.');
  const result = activateAccessibilityButton(trust, 'fixture-trust-submitted');
  report.fixtureFolderTrust = {directory,files,before:state.before,after:result.after};
  await delay(1000);
}

async function createProjectWithAccessibility(name, sequence) {
  const label = 'fixture-' + sequence;
  const before = inspect(label + '-before');
  const menu = before.nodes.find(node => node.AXRole === 'AXMenuItem' && node.AXTitle === 'New Project…' && node.actions?.includes('AXPress'));
  if (!menu) throw new Error('The native New Project command is unavailable.');
  const opened = inspect(label + '-dialog', 'action', menu.path, 'AXPress');
  const field = opened.nodes.find(node => node.AXRole === 'AXTextField' && node.AXTitle === 'Project name' && node.writable?.includes('AXValue'));
  if (!field) throw new Error('The native project dialog has no editable project name.');
  const filled = inspect(label + '-named', 'set', field.path, 'AXValue', name);
  const button = filled.nodes.find(node => node.AXRole === 'AXButton' && node.AXTitle === 'Create project');
  if (!button || filled.setStatus !== 0) throw new Error('The project name was not accepted through Accessibility.');
  const submitted = activateAccessibilityButton(button, label + '-submitted');
  await trustCreatedFixtureFolder();
  await delay(1000);
  const text = await readCurrentPage(page => page.evaluate(() => document.body?.innerText ?? ''));
  const result = {
    name, nameAccepted:filled.setStatus === 0, activationMethod:button.actions?.includes('AXPress') ? 'AXPress' : 'targeted-key',
    dialogClosed:!submitted.nodes.some(node => node.AXRole === 'AXTextField' && node.AXTitle === 'Project name'),
    visible:text.includes(name), before:opened.before, after:submitted.after,
  };
  report.accessibilityProjectSetup ??= [];
  report.accessibilityProjectSetup.push(result);
  await readCurrentPage(page => page.screenshot({path:path.join(output, label + '-result.png')}));
  if (!result.dialogClosed || !result.visible) throw new Error('The Accessibility project action requires adaptation.');
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
  report.enhancedAccessibilityStatus = enabled.enhancedAccessibilityStatus;
  await readCurrentPage(page => page.screenshot({path:path.join(output, '02-client-started.png')}));
  report.page = await readCurrentPage(page => page.evaluate(() => ({
    text:document.body?.innerText?.slice(0,12000),
    buttons:[...document.querySelectorAll('button')].map(button => ({text:button.innerText, label:button.getAttribute('aria-label'), title:button.title})),
  })));
  report.desktopSignInRequired = report.page.text?.includes('Sign in to ChatGPT') === true;
  if (report.desktopSignInRequired && fs.existsSync(path.join(codexHome, 'auth.json'))) {
    await readCurrentPage(page => page.getByRole('button', {name:'Continue to sign in', exact:true}).click());
    await delay(3000);
    await readCurrentPage(page => page.screenshot({path:path.join(output, '02b-desktop-sign-in-continued.png')}));
    report.desktopAfterContinue = await readCurrentPage(page => page.evaluate(() => ({
      text:document.body?.innerText?.slice(0,12000),
      buttons:[...document.querySelectorAll('button')].map(button => ({text:button.innerText, label:button.getAttribute('aria-label')})),
    })));
    report.desktopSignInRequired = report.desktopAfterContinue.text?.includes('Sign in to ChatGPT') === true;
  } else if (report.desktopSignInRequired) {
    await readCurrentPage(async page => {
      await page.getByRole('button', {name:'Sign in another way', exact:true}).click();
    });
    await delay(1000);
    await readCurrentPage(page => page.screenshot({path:path.join(output, '02b-desktop-sign-in-options.png')}));
    report.desktopSignInOptions = await readCurrentPage(page => page.evaluate(() => ({
      text:document.body?.innerText?.slice(0,12000),
      buttons:[...document.querySelectorAll('button')].map(button => ({text:button.innerText, label:button.getAttribute('aria-label')})),
    })));
  }
  const snapshot = inspect('03-accessibility-tree');
  report.accessibilityNodes = snapshot.nodes.length;
  report.accessibilityActions = [...new Set(snapshot.nodes.flatMap(node => node.actions ?? []))];
  report.inputState = {before:snapshot.before, after:snapshot.after};
  report.credentialsPresent = fs.existsSync(path.join(codexHome, 'auth.json'));
  report.dataLocations = [codexHome, path.join(process.env.HOME, '.codex')].map(directory => ({
    fixtureHome:directory === codexHome,
    exists:fs.existsSync(directory),
    databases:fs.existsSync(directory) ? fs.readdirSync(directory).filter(name => /^state_\d+\.sqlite$/.test(name)).map(name => {
      const file = path.join(directory, name);
      try { return {name, threads:Number(execFileSync('/usr/bin/sqlite3', ['-readonly', file, 'SELECT count(*) FROM threads;'], {encoding:'utf8',timeout:5000}).trim())}; }
      catch (error) { return {name,error:String(error.message)}; }
    }) : [],
  }));
  if (report.credentialsPresent && !report.desktopSignInRequired) {
    await trustCreatedFixtureFolder();
    for (const [index, name] of ['Zulu AX background project', 'Alpha AX background project'].entries()) {
      const text = await readCurrentPage(page => page.evaluate(() => document.body?.innerText ?? ''));
      if (!text.includes(name)) await createProjectWithAccessibility(name, index + 1);
    }
  }
} catch (error) {
  report.error = String(error.stack ?? error);
} finally {
  report.clientProcess = client ? {pid:client.pid, exitCode:client.exitCode} : null;
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
