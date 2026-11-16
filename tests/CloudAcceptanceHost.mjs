import fs from 'node:fs';
import path from 'node:path';
import {execFileSync, spawn} from 'node:child_process';
import {createRequire} from 'node:module';

if (process.env.GITHUB_ACTIONS !== 'true') throw new Error('Use the disposable cloud test host.');
const [app, python] = process.argv.slice(2);
const require = createRequire(path.join(process.env.RUNNER_TEMP, 'sorter-test-tools/package.json'));
const {DefaultArtifactClient} = require('@actions/artifact');
const artifacts = new DefaultArtifactClient();
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const git = args => execFileSync('git', args, {encoding:'utf8'}).trim();
let handledCommit;
let attempt = 0;
const deadline = Date.now() + 60 * 60 * 1000;

function filesIn(directory) {
  return fs.readdirSync(directory, {withFileTypes:true}).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? filesIn(file) : [file];
  });
}

while (Date.now() < deadline) {
  git(['fetch', '--quiet', 'origin', 'main']);
  const commit = git(['rev-parse', 'origin/main']);
  if (commit === handledCommit) {
    await delay(30000);
    continue;
  }
  git(['checkout', '--quiet', '--detach', commit]);
  handledCommit = commit;
  const label = 'mcp-live-' + String(++attempt).padStart(2, '0');
  const output = path.resolve('artifacts', label);
  fs.mkdirSync(output, {recursive:true});
  console.log('Testing cloud diagnostic revision ' + commit);
  const exitCode = await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['tests/McpCloudAcceptance.mjs', app, python], {
      env:{...process.env, SORTER_ACCEPTANCE_OUTPUT:output}, stdio:'inherit',
    });
    child.once('error', reject);
    child.once('exit', resolve);
  });
  const file = path.join(output, 'results.json');
  const result = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {error:'The diagnostic exited before writing a result.'};
  fs.writeFileSync(path.join(output, 'cloud-revision.json'), JSON.stringify({commit, exitCode}, null, 2));
  await artifacts.uploadArtifact(label, filesIn(output), output, {retentionDays:2});
  if (result.fullAcceptancePassed === true) process.exit(0);
  console.log('Cloud credentials remain available for the next diagnostic revision.');
}
throw new Error('The retained cloud acceptance session reached its one-hour limit.');

