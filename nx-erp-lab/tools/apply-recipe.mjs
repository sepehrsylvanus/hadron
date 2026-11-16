import { resolve, relative, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync, readdirSync, mkdirSync, copyFileSync, appendFileSync } from 'node:fs';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const target = process.cwd();
if (!existsSync(join(target,'nx.json')) || !process.argv.includes('--replace-scaffold')) {
  throw new Error('Run from a FRESH generated Nx workspace with --replace-scaffold; this replaces only the listed tutorial files.');
}
function copy(source,dest) {
  for(const entry of readdirSync(source,{withFileTypes:true})) {
    const from=join(source,entry.name), to=join(dest,entry.name);
    if(entry.isDirectory()){mkdirSync(to,{recursive:true});copy(from,to);}
    else {mkdirSync(dirname(to),{recursive:true});copyFileSync(from,to);console.log(relative(target,to));}
  }
}
copy(join(root,'recipe'), target);
mkdirSync(join(target,'prisma'),{recursive:true});
for (const file of ['schema.prisma','decisions.json','legacy-checks.sql','legacy-indexes.sql']) copyFileSync(join(root,'prisma',file),join(target,'prisma',file));
appendFileSync(join(target,'.gitignore'),'\n.env\n.env.*\n!.env.example\n.nx/\n**/generated/prisma/\n');
console.log('Scaffold overlay copied. No database commands were run.');
