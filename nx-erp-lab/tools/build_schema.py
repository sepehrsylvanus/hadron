#!/usr/bin/env python3
"""Reproducible, lab-only projection of the supplied Data Map; never connects to a database."""
from pathlib import Path
import re, json, hashlib, collections
ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'sources/hadron-data-map.html'
raw = SOURCE.read_text()
model = json.loads(re.search(r'<script id="hadron-model" type="application/json">(.*?)</script>', raw, re.S).group(1))
scope = json.loads((ROOT / 'scope.json').read_text())
allowed = {tid for group in ('businessTables', 'supportingTables') for ids in scope[group].values() for tid in ids}
tables = [t for t in model['tables'] if t['id'] in allowed]
assert {t['id'] for t in tables} == allowed, 'The scope allowlist contains unknown source tables'
by = {t['id']:t for t in tables}
# Supplement incomplete legacy column metadata, never add a table/column from EF.
snapshot = (ROOT.parent/'main-erp/src/backend/Hadron.Erp.Api/Infrastructure/Migrations/HadronDbContextModelSnapshot.cs').read_text()
meta = {}; indexes = {}; checks = []; partial_indexes = []; decisions = []
for block in re.findall(r'modelBuilder\.Entity\("[^\"]+", b =>\s*\{(.*?)\n                \}\);', snapshot, re.S):
    table = re.search(r'b\.ToTable\("([^\"]+)", "([^\"]+)"', block)
    if not table: continue
    tid = table[2]+'.'+table[1]
    if tid not in by or table[2]=='trading': continue
    for p in re.finditer(r'b\.Property<([^>]+)>\("([^\"]+)"\)(.*?);', block, re.S):
        typ, name, tail = p.groups()
        meta[(tid,name)] = {'nullable': '?' in typ or (typ=='string' and '.IsRequired()' not in tail), 'tail':tail}
    indexes[tid] = []
    for idx in re.finditer(r'b\.HasIndex\((.*?)\)(.*?);',block,re.S):
        names = re.findall(r'"([^\"]+)"',idx[1]); tail=idx[2]
        if names and all(any(c['name']==n for c in by[tid]['columns']) for n in names):
            indexes[tid].append((names,'.IsUnique()' in tail,tail))
    for c in re.finditer(r'\.HasCheckConstraint\("((?:\\.|[^"\\])*)",\s*"((?:\\.|[^"\\])*)"\)', block):
        name, expr = [json.loads('"'+x+'"') for x in c.groups()]
        cols = set(re.findall(r'"([A-Za-z_][A-Za-z0-9_]*)"',expr))
        if cols <= {x['name'] for x in by[tid]['columns']}:
            checks.append(f'ALTER TABLE "{table[2]}"."{table[1]}" ADD CONSTRAINT "{name}" CHECK ({expr});')

def pascal(s): return ''.join(x[0].upper()+x[1:] for x in s.split('_'))
def name(t): return pascal(t['schema'])+pascal(t['name'])
def short(s): return s[:40]+'_'+hashlib.sha256(s.encode()).hexdigest()[:10]
def note(t,c,kind,value): decisions.append({'table':t['id'],'column':c['name'],'decision':kind,'value':value})
fields={}; enums=[]
for t in tables:
    for c in t['columns']:
        if c['isPk'] and c.get('unique') and sum(x['isPk'] for x in t['columns'])>1:
            note(t,c,'composite-PK-not-individually-unique','@@id covers the complete key; no per-column unique')
        tid=t['id']; col=c['name']; ef=meta.get((tid,col),{}); tail=ef.get('tail','')
        nullable=c.get('nullable')=='yes'
        if not c.get('nullable'):
            nullable=ef.get('nullable',True) and not c['isPk']
            note(t,c,'nullability-from-EF' if ef else 'UNKNOWN-nullability-nullable-in-lab',nullable)
        typ=c['type']; native=''; enum=None
        if typ=='enum':
            enum=name(t)+pascal(col)+'Enum'; vals=c.get('enumValues',[])
            if tid=='trading.order_registrations' and col=='status':
                vals=['DRAFT','SUBMITTED','ACTIVE','SUSPENDED','CANCELLED']
                note(t,c,'current-domain-overlay-not-source-enum',vals)
            assert vals,(tid,col)
            enums.append('enum '+enum+' {\n'+'\n'.join('  '+v for v in vals)+'\n  @@schema("'+t['schema']+'")\n}')
            scalar=enum
        elif typ=='uuid': scalar='String'; native='@db.Uuid'
        elif typ in ('character_varying','text','character'):
            scalar='String'; length=re.search(r'HasMaxLength\((\d+)\)',tail)
            native='@db.VarChar('+length[1]+')' if length else '@db.Text'
            if col.lower()=='id' and t['schema']=='trading':
                native='@db.Uuid'; note(t,c,'lab-UUID-normalization','Data Map varchar UUID identifiers → PostgreSQL uuid')
        elif typ=='numeric':
            scalar='Decimal'; precision=re.search(r'HasPrecision\((\d+), (\d+)\)',tail)
            p,s=precision.groups() if precision else ('24','8' if 'rate' in col.lower() else '6')
            native=f'@db.Decimal({p}, {s})'
            note(t,c,'precision-from-EF' if precision else 'lab-explicit-precision-not-in-map',f'{p},{s}')
        elif typ=='date': scalar='DateTime'; native='@db.Date'
        elif typ=='timestamp_with_time_zone': scalar='DateTime'; native='@db.Timestamptz(6)'
        elif typ in ('integer','bigint'): scalar='Int' if typ=='integer' else 'BigInt'
        elif typ=='boolean': scalar='Boolean'
        elif typ=='jsonb': scalar='Json'; native='@db.JsonB'
        else: raise ValueError((tid,col,typ))
        default=''; d=str(c.get('default',''))
        if d.startswith('uuid(') or (c['isPk'] and not c['isFk'] and native=='@db.Uuid' and sum(x['isPk'] for x in t['columns'])==1):default='@default(uuid())'
        elif d.startswith('now(') or 'HasDefaultValueSql("CURRENT_TIMESTAMP")' in tail:default='@default(now())'
        elif d in ('true','false'):default='@default('+d+')'
        elif re.fullmatch(r'-?\d+',d) and scalar in ('Int','BigInt','Decimal'):default='@default('+d+')'
        elif enum and d in c.get('enumValues',[]):default='@default('+d+')'
        elif c['isPk'] and scalar=='Int' and 'ValueGeneratedOnAdd' in tail:default='@default(autoincrement())'
        if col.lower()=='version' and not default:
            default='@default(1)'; note(t,c,'lab-domain-default','1')
        fields[(tid,col)]={'c':c,'type':scalar,'native':native,'optional':nullable and not c['isPk'],'default':default}
# Reviewed overlays: textual source references and the known Identity role join.
# Do NOT infer relations merely because a name ends with Id.
for t in tables:
    for c in t['columns']:
        overlay=None
        if t['id']=='identity.user_roles' and c['name']=='RoleId':
            overlay=('identity','roles','Id')
        match=re.search(r'FK\s*→\s*(identity|reference_data)\.([a-z_]+)\.([A-Za-z_]+)',c.get('descriptionFa',''))
        if match: overlay=match.groups()
        if overlay and not c['isFk']:
            target_id=overlay[0]+'.'+overlay[1]
            if target_id in by and (target_id,overlay[2]) in fields:
                # Keep selected-data-map.json as the original map, overlay only projection metadata.
                fields[(t['id'],c['name'])]['overlay']={'schema':overlay[0],'table':overlay[1],'column':overlay[2]}
                note(t,c,'reviewed-explicit-textual-FK-overlay',target_id+'.'+overlay[2])
# Explicit FK declarations plus the logged overlays above.

rels=collections.defaultdict(list); backs=collections.defaultdict(list)
for t in tables:
    for c in t['columns']:
        target=fields[(t['id'],c['name'])].get('overlay') or c.get('target')
        if not target or (not c['isFk'] and not fields[(t['id'],c['name'])].get('overlay')):continue
        dest=target['schema']+'.'+target['table']; ref=target['column']
        if dest in by and (dest,ref) not in fields:
            dest_pks=[x['name'] for x in by[dest]['columns'] if x['isPk']]
            assert len(dest_pks)==1,(t['id'],c['name'],dest,ref)
            note(t,c,'repair-invalid-target-column-using-DataMap-PK',dest+'.'+ref+' → '+dest_pks[0])
            ref=dest_pks[0]
        assert dest in by and (dest,ref) in fields,(t['id'],c['name'],dest,ref)
        field=fields[(t['id'],c['name'])]; parent=fields[(dest,ref)]
        if (field['type'],field['native']) != (parent['type'],parent['native']):
            note(t,c,'FK-native-type-aligned-to-target',dest+'.'+ref)
            field['type']=parent['type'];field['native']=parent['native']
        assert parent['c']['isPk'] or parent['c'].get('unique'),(dest,ref)
        rn=short('rel_'+t['id'].replace('.','_')+'_'+c['name']); fn='link_'+c['name']
        rels[t['id']].append(f'  {fn} {name(by[dest])}{"?" if field["optional"] else ""} @relation("{rn}", fields: [{c["name"]}], references: [{ref}], onDelete: Restrict, onUpdate: NoAction, map: "{short("fk_"+rn)}")')
        one=(c.get('unique') and not c['isPk']) or (c['isPk'] and sum(x['isPk'] for x in t['columns'])==1)
        backs[dest].append(f'  back_{hashlib.sha256(rn.encode()).hexdigest()[:12]} {name(t)}'+('?' if one else '[]')+f' @relation("{rn}")')

out=['// LAB-ONLY projection. Read decisions.json before migration. Never use the main ERP database.','generator client {\n  provider = "prisma-client"\n  output = "../apps/api/src/generated/prisma"\n  moduleFormat = "cjs"\n  importFileExtension = ""\n}', 'datasource db {\n  provider = "postgresql"\n  schemas = '+json.dumps(sorted({t['schema'] for t in tables}))+'\n}']+enums
for t in tables:
    lines=['/// Source Data Map: '+t['id']+'; '+t.get('descriptionFa','').replace('\n',' '), 'model '+name(t)+' {']
    pk=[c['name'] for c in t['columns'] if c['isPk']]; assert pk,t['id']
    unique_sets=[]
    for c in t['columns']:
        f=fields[(t['id'],c['name'])]; flags=[]
        if len(pk)==1 and c['isPk']:flags+=['@id']
        if c.get('unique') and not c['isPk']:unique_sets.append([c['name']])
        lines.append('  '+c['name']+' '+f['type']+('?' if f['optional'] else '')+' '+' '.join(flags+[x for x in [f['native'],f['default']] if x]))
    lines+=rels[t['id']]+backs[t['id']]
    if len(pk)>1:lines.append('  @@id(['+', '.join(pk)+'])')
    for names,unique,tail in indexes.get(t['id'],[]):
        if '.HasFilter(' in tail:
            filt = re.search(r'HasFilter\("((?:\\.|[^"\\])*)"\)',tail)
            assert filt,(t['id'],names,tail)
            sql_filter=json.loads('"'+filt[1]+'"')
            quoted=', '.join('"'+n+'"' for n in names)
            ddl=f'CREATE {"UNIQUE " if unique else ""}INDEX "{short("partial_"+t["id"]+"_"+"_".join(names))}" ON "{t["schema"]}"."{t["name"]}" ({quoted}) WHERE {sql_filter};'
            partial_indexes.append(ddl)
            note(t,{'name':','.join(names)},'filtered-index-manual-SQL',ddl);continue
        if unique:unique_sets.append(names)
        else:lines.append('  @@index(['+', '.join(names)+'], map: "'+short('ix_'+t['id']+'_'+'_'.join(names))+'")')
    for names in sorted({tuple(x) for x in unique_sets}):
        if list(names)!=pk:lines.append('  @@unique(['+', '.join(names)+'], map: "'+short('uq_'+t['id']+'_'+'_'.join(names))+'")')
    lines+=['  @@map("'+t['name']+'")','  @@schema("'+t['schema']+'")','}'];out.append('\n'.join(lines))
(ROOT/'prisma/schema.prisma').write_text('\n'.join(line.rstrip() for line in '\n\n'.join(out).splitlines())+'\n')
(ROOT/'prisma/legacy-checks.sql').write_text('-- Lab-only reviewed legacy checks. Append to the initial migration AFTER inspecting SQL.\n'+'\n'.join(checks)+'\n')
(ROOT/'prisma/legacy-indexes.sql').write_text('-- Append after Prisma-generated table DDL, after review.\n'+'\n'.join(partial_indexes)+'\n')
manifest={'secondarySource':{'path':'main-erp/src/backend/Hadron.Erp.Api/Infrastructure/Migrations/HadronDbContextModelSnapshot.cs','sha256':hashlib.sha256(snapshot.encode()).hexdigest(),'purpose':'Only supplement metadata of source-map columns, never add EF-only fields/tables'},'scopeFile':'scope.json','selectionPolicy':scope['selectionPolicy'],'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'sourceTables':len(model['tables']),'selectedTables':len(tables),'columns':sum(len(t['columns']) for t in tables),'schemas':dict(collections.Counter(t['schema'] for t in tables)),'modelNames':{t['id']:name(t) for t in tables},'excludedTables':[t['id'] for t in model['tables'] if t['id'] not in by],'decisions':decisions,'limitations':['Data Map does not specify all nullability, precision, indexes, triggers, tenant policy or Nest authentication semantics.','EF snapshot supplements only missing legacy field metadata; no EF-only columns or tables are introduced.','UUID normalization for new Trading IDs is a lab decision, not an in-place migration plan.','Filtered indexes, application triggers, row-level security and external textual FK comments require explicit review.','Authentication hashes from ASP.NET Identity are NOT Nest/Argon2 password hashes. Use fresh lab-only accounts.']}
(ROOT/'prisma/decisions.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
(ROOT/'sources/selected-data-map.json').write_text(json.dumps({'sourceSha256':manifest['sourceSha256'],'tables':tables},ensure_ascii=False,indent=2)+'\n')
print('Generated',len(tables),'models,',manifest['columns'],'columns,',len(enums),'enums;',len(decisions),'documented decisions.')
