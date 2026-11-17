import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn, execFileSync} from 'node:child_process';
import {createRequire} from 'node:module';
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
const originalHash = hash(executablePath);
const report = {fullAcceptancePassed:false, route:'Linux official local tools', dataKind:'isolated cloud-local projects and chats'};
let client;
let browser;

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
  const log = fs.openSync(path.join(output, 'client.log'), 'a');
  client = spawn(executable, ['--user-data-dir=' + profile, '--remote-debugging-port=9442', '--disable-dev-shm-usage'], {
    env:{...process.env, CODEX_HOME:codexHome}, stdio:['ignore',log,log],
  });
  for (let attempt = 0; attempt < 60; attempt++) {
    try { browser = await chromium.connectOverCDP('http://127.0.0.1:9442'); break; }
    catch { await delay(1000); }
  }
  if (!browser) throw new Error('The official Linux desktop diagnostic port did not open.');
  await delay(6000);
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
} catch (error) {
  report.error = String(error.stack ?? error);
} finally {
  if (browser) await browser.close();
  if (client && client.exitCode === null) {
    client.kill('SIGTERM');
    await Promise.race([new Promise(resolve => client.once('exit', resolve)), delay(5000)]);
  }
  report.clientFilesUnchanged = hash(executablePath) === originalHash;
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
}
console.log(JSON.stringify({authenticatedSidebarOrderingPassed:report.authenticatedSidebarOrderingPassed, clientFilesUnchanged:report.clientFilesUnchanged, error:report.error}));
process.exitCode = report.authenticatedSidebarOrderingPassed && report.clientFilesUnchanged ? 0 : 1;
