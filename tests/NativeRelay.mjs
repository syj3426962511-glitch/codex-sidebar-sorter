import net from 'node:net';

if (process.env.GITHUB_ACTIONS !== 'true') throw new Error('Use the cloud authorization probe.');
const connection = net.createConnection(process.argv[2]);
connection.on('error', error => {console.error(error.message); process.exitCode = 1;});
process.stdin.pipe(connection);
connection.pipe(process.stdout);

