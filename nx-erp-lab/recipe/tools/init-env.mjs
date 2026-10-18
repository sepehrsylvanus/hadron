import { randomBytes } from 'node:crypto';
import { writeFileSync, existsSync } from 'node:fs';
if (!existsSync('nx.json')) throw new Error('Run inside the generated workspace');
const password = randomBytes(32).toString('hex');
writeFileSync('.env', `POSTGRES_USER=hadron_nx_lab\nPOSTGRES_PASSWORD=${password}\nPOSTGRES_DB=hadron_nx_lab\nDATABASE_URL=postgresql://hadron_nx_lab:${password}@127.0.0.1:5544/hadron_nx_lab\nLAB_API_TOKEN=${randomBytes(32).toString('hex')}\nPORT=3101\n`, { flag: 'wx', mode: 0o600 });
console.log('Created a fresh .env; secrets are not printed. Existing files are never overwritten.');
