import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn, execFileSync} from 'node:child_process';
import {createRequire} from 'node:module';
import net from 'node:net';
import os from 'node:os';
import {desktopRequest} from './DesktopRequest.mjs';

if (process.env.GITHUB_ACTIONS !== 'true' || process.platform !== 'linux') {
  throw new Error('Use a disposable cloud Linux desktop.');
}
const [executable] = process.argv.slice(2);
const root = path.join(process.env.RUNNER_TEMP, 'sorter-mcp-acceptance');
const codexHome = path.join(root, 'codex-home');
const profile = path.join(root, 'linux-desktop-profile');
const output = path.resolve(process.env.SORTER_ACCEPTANCE_OUTPUT);
for (const directory of [profile, output]) fs.mkdirSync(directory, {recursive:true});
const require = createRequire(path.join(process.env.RUNNER_TEMP, 'sorter-test-tools/package.json'));
const {chromium} = require('playwright');
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const executablePath = fs.realpathSync(executable);
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const clientFiles = {executable:executablePath, archive:path.join(path.dirname(process.env.SORTER_OFFICIAL_CLI),'app.asar')};
const originalHashes = Object.fromEntries(Object.entries(clientFiles).map(([name,file]) => [name,hash(file)]));
const report = {fullAcceptancePassed:false, route:'Linux official local tools', dataKind:'isolated cloud-local projects and chats'};
let client;
let browser;
let sorterLauncher;
const clientArguments = ['--user-data-dir=' + profile, '--remote-debugging-port=0', '--disable-dev-shm-usage'];
const childEnvironment = {...process.env, CODEX_HOME:codexHome};

async function startOfficialClient() {
  const logFile = path.join(output, 'client.log');
  const log = fs.openSync(logFile, 'a');
  let readOffset = fs.fstatSync(log).size;
  let startupText = '';
  client = spawn(executable, clientArguments, {env:childEnvironment, stdio:['ignore',log,log]});
  fs.closeSync(log);
  for (let attempt = 0; attempt < 60; attempt++) {
    const size = fs.statSync(logFile).size;
    if (size > readOffset) {
      const input = fs.openSync(logFile,'r');
      const added = Buffer.alloc(size-readOffset);
      fs.readSync(input,added,0,added.length,readOffset);
      fs.closeSync(input);
      readOffset = size;
      startupText += added.toString();
    }
    const endpoint = startupText.match(/DevTools listening on (ws:\/\/[^\s]+)/)?.[1];
    if (endpoint) {
      try { browser = await chromium.connectOverCDP(endpoint); break; }
      catch { }
    }
    if (client.exitCode !== null || client.signalCode !== null) throw new Error('The official client exited during startup.');
    await delay(1000);
  }
  if (!browser) throw new Error('The official Linux desktop diagnostic port did not open.');
  await delay(6000);
}

async function stopOfficialClient() {
  if (browser) { await browser.close(); browser = null; }
  if (client && client.exitCode === null && client.signalCode === null) {
    client.kill('SIGTERM');
    await Promise.race([new Promise(resolve => client.once('exit', resolve)), delay(5000)]);
  }
  if (client && client.exitCode === null && client.signalCode === null) throw new Error('The official client has not finished exiting.');
}

async function backgroundStatus() {
  const identity = 'sst-' + crypto.createHash('sha256').update(path.resolve(codexHome)).digest('hex').slice(0,12).toUpperCase();
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(path.join(os.tmpdir(), 'CoreFxPipe_' + identity + '-bg'));
    let text = '';
    socket.setTimeout(20000, () => socket.destroy(new Error('Companion status timed out.')));
    socket.on('error', reject);
    socket.on('connect', () => socket.write('status\n'));
    socket.on('data', data => {
      text += data.toString();
      if (text.includes('\n')) { socket.end(); resolve(JSON.parse(text.split('\n')[0])); }
    });
  });
}

async function verifyInstalledCompanion(fixture) {
  const installer = path.join(root, 'official-client-sorter.run');
  if (!fs.existsSync(installer)) {
    execFileSync('curl', ['--fail','--location','--retry','2','--max-time','240',
      'https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/download/v1.4.2-unix-candidate/CodexSidebarSorter-1.4.2-Linux-x64-Candidate.run',
      '-o', installer], {stdio:'inherit',timeout:750000});
  }
  const prefix = path.join(root, 'Installed companion');
  sorterLauncher = path.join(prefix, 'Start Sidebar Sorter');
  execFileSync('bash', [installer, '--prefix', prefix, '--no-launch'], {env:childEnvironment,stdio:'inherit',timeout:120000});
  execFileSync(sorterLauncher, ['--capture',path.join(output,'04-published-companion-with-official-client.png')], {env:childEnvironment,stdio:'inherit',timeout:120000});
  await delay(1500);
  const firstWorker = await backgroundStatus();
  report.installedCompanionWorker = firstWorker;
  if (!firstWorker.Automatic) throw new Error('The published companion did not enable automatic name ordering.');
  const fixtureFile = path.join(root,'linux-visible-fixture.json');
  const thread = fixture.threads[0];
  async function renameAndCheck(title, label) {
    await desktopRequest(await activePage(), 'thread/name/set', {threadId:thread.id, name:title});
    thread.title = title;
    fs.writeFileSync(fixtureFile, JSON.stringify(fixture,null,2));
    const resultFile = path.join(output,label+'.json');
    execFileSync(path.resolve('artifacts/linux-native-probe/NativeEndpointProbe'), [resultFile,'--existing-profile','--verify-name-order',fixtureFile], {
      env:{...childEnvironment,CODEX_THREAD_ID:thread.id},stdio:'inherit',timeout:120000,
    });
    const status = await backgroundStatus();
    if (status.ProcessId !== firstWorker.ProcessId || status.Error) throw new Error('The installed worker did not retain a healthy automatic session.');
  }
  await renameAndCheck('0000 Linux live renamed chat','05-automatic-with-window-closed');
  report.automaticRenameWithCompanionWindowClosed = true;
  await stopOfficialClient();
  const closedStatus = await backgroundStatus();
  if (closedStatus.ProcessId !== firstWorker.ProcessId) throw new Error('The companion exited with the official client.');
  report.workerContinuesAfterOfficialClientExit = true;
  await startOfficialClient();
  await renameAndCheck('ZZZZ Linux live renamed chat','06-automatic-after-client-reopen');
  report.automaticRecoveryAfterOfficialClientReopen = true;
  await (await activePage()).screenshot({path:path.join(output,'07-official-client-reopened.png')});
  execFileSync('bash',[installer,'--uninstall','--prefix',prefix,'--no-launch'],{env:childEnvironment,stdio:'inherit',timeout:120000});
  sorterLauncher = null;
  if ((await activePage()).isClosed() || client.exitCode !== null || client.signalCode !== null) throw new Error('Companion removal interrupted the official client.');
  report.companionRemovalKeepsOfficialClientRunning = true;
  report.publishedInstallerVerifiedWithOfficialClient = true;
}

async function activePage() {
  for (let attempt = 0; attempt < 60; attempt++) {
    const page = browser.contexts()[0].pages().find(candidate => !candidate.isClosed() && candidate.url() === 'app://-/index.html');
    if (page) return page;
    await delay(500);
  }
  throw new Error('The official Linux desktop page is unavailable.');
}

async function createFixture() {
  const names = ['Zulu Linux background project', 'Alpha Linux background project'];
  const receipt = path.join(root, 'linux-visible-fixture.json');
  if (fs.existsSync(receipt)) return JSON.parse(fs.readFileSync(receipt, 'utf8'));
  for (const name of names) {
    const page = await activePage();
    const text = await page.locator('body').innerText();
    if (!text.includes(name)) {
      await page.getByRole('button', {name:'Add new project', exact:true}).press('Enter');
      await page.getByRole('textbox', {name:'Project name', exact:true}).fill(name);
      await page.getByRole('button', {name:'Create project', exact:true}).click();
      await delay(1000);
      const trust = page.getByRole('button', {name:'Trust folder', exact:true});
      if (await trust.count()) await trust.click();
      await delay(1000);
    }
  }
  const projects = await desktopRequest(await activePage(), 'project/list', {limit:100});
  const fixture = {projectNames:names, threads:[]};
  for (const name of names) {
    const project = projects.data.find(candidate => candidate.name === name);
    if (!project) throw new Error('The Linux test project has no backend identity for ' + name);
    const directory = project.roots[0].path;
    for (const title of ['Zulu Linux visible chat', 'Middle Linux visible chat', 'Alpha Linux visible chat']) {
      const {thread} = await desktopRequest(await activePage(), 'thread/start', {cwd:directory, ephemeral:false, approvalPolicy:'never', sandbox:'read-only'});
      await desktopRequest(await activePage(), 'thread/name/set', {threadId:thread.id, name:title});
      await desktopRequest(await activePage(), 'thread/metadata/update', {threadId:thread.id, projectId:project.id});
      fixture.threads.push({id:thread.id, title});
    }
  }
  fs.writeFileSync(receipt, JSON.stringify(fixture, null, 2));
  return fixture;
}

try {
  await startOfficialClient();
  let page = await activePage();
  if ((await page.locator('body').innerText()).includes('Sign in to ChatGPT')) {
    const continued = page.getByRole('button', {name:'Continue to sign in', exact:true});
    if (await continued.count()) await continued.click();
    await delay(3000);
    page = await activePage();
  }
  report.desktopSignInRequired = (await page.locator('body').innerText()).includes('Sign in to ChatGPT');
  await page.screenshot({path:path.join(output, '01-official-linux-started.png')});
  if (report.desktopSignInRequired) throw new Error('The desktop requires interactive sign-in after CLI authorization.');
  const fixture = await createFixture();
  report.fixtureProjectCount = fixture.projectNames.length;
  report.fixtureChatCount = fixture.threads.length;
  await (await activePage()).reload();
  await delay(4000);
  await (await activePage()).screenshot({path:path.join(output, '02-official-linux-fixture.png')});
  execFileSync('dotnet', ['publish', 'tests/NativeEndpointProbe.csproj', '-c', 'Release', '-r', 'linux-x64', '--self-contained', 'true', '-o', 'artifacts/linux-native-probe'], {stdio:'inherit',timeout:300000});
  const fixtureFile = path.join(root, 'linux-visible-fixture.json');
  const resultFile = path.join(output, 'native-ordering-results.json');
  try {
    execFileSync(path.resolve('artifacts/linux-native-probe/NativeEndpointProbe'), [resultFile, '--existing-profile', '--read-lists', '--fixture', fixtureFile], {
      env:{...process.env, CODEX_HOME:codexHome, CODEX_THREAD_ID:fixture.threads.at(-1).id}, stdio:'inherit',timeout:300000,
    });
  } finally {
    if (fs.existsSync(resultFile)) report.nativeOrdering = JSON.parse(fs.readFileSync(resultFile, 'utf8'));
  }
  await (await activePage()).screenshot({path:path.join(output, '03-official-linux-sorted.png')});
  report.authenticatedSidebarOrderingPassed = report.nativeOrdering?.savedOrderModesVerified?.length === 7;
  if (report.authenticatedSidebarOrderingPassed) await verifyInstalledCompanion(fixture);
} catch (error) {
  report.error = String(error.stack ?? error);
} finally {
  if (sorterLauncher && fs.existsSync(sorterLauncher)) {
    try { execFileSync(sorterLauncher,['--exit'],{env:childEnvironment,stdio:'inherit',timeout:90000}); }
    catch (error) { report.companionCleanupError = String(error.message); }
  }
  await stopOfficialClient();
  report.clientFilesChecked = Object.keys(clientFiles);
  report.clientFilesUnchanged = Object.entries(clientFiles).every(([name,file]) => hash(file) === originalHashes[name]);
  report.fullAcceptancePassed = report.publishedInstallerVerifiedWithOfficialClient === true && report.clientFilesUnchanged;
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
}
console.log(JSON.stringify({authenticatedSidebarOrderingPassed:report.authenticatedSidebarOrderingPassed, clientFilesUnchanged:report.clientFilesUnchanged, error:report.error}));
process.exitCode = report.fullAcceptancePassed ? 0 : 1;
