import { spawnSync } from 'node:child_process';
const u = new URL(process.env.DATABASE_URL ?? '');
const allowed = ['127.0.0.1','localhost'].includes(u.hostname) &&
  ((u.port === '5544' && u.pathname === '/hadron_nx_lab') ||
   (u.port === '5545' && u.pathname === '/hadron_nx_test')) &&
  ['postgres:','postgresql:'].includes(u.protocol) && u.password.length >= 32 &&
  u.search === '';
if (!allowed) throw new Error('Refusing non-isolated database URL; do NOT change this to reach the main ERP.');
const commands = {
  validate: ['validate'], generate: ['generate'], status: ['migrate','status'],
  'migrate-create': ['migrate','dev','--name','initial_scoped_lab','--create-only'],
  'migrate-deploy': ['migrate','deploy'],
};
const args = commands[process.argv[2]];
if (!args) throw new Error('Allowed: validate, generate, status, migrate-create, migrate-deploy. No reset/db push.');
// Direct Node invocation also works on Windows, without shell interpolation.
const cli = new URL('../node_modules/prisma/build/index.js', import.meta.url);
const result = spawnSync(process.execPath, [decodeURIComponent(cli.pathname).replace(/^\/(?=[A-Za-z]:)/,''), ...args], { stdio:'inherit', env:process.env });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
