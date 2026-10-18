#!/usr/bin/env python3
from pathlib import Path
import re,json,hashlib,base64
from html.parser import HTMLParser
R=Path(__file__).resolve().parents[1]
class Parser(HTMLParser):
    def __init__(self): super().__init__();self.ids=[];self.links=[];self.external=[]
    def handle_starttag(self,tag,attrs):
        a=dict(attrs)
        if 'id' in a:self.ids.append(a['id'])
        if tag=='a':self.links.append(a.get('href',''))
        if tag in ('script','link','img','iframe'):
            url=a.get('src',a.get('href',''))
            if url.startswith(('http:','https:','//')):self.external.append(url)
s=(R/'implementation-guide.html').read_text();p=Parser();p.feed(s)
assert len(p.ids)==len(set(p.ids)),'duplicate HTML ids'
assert not p.external,'guide is not offline'
for link in p.links:
    if link.startswith('#'):assert link[1:] in p.ids,link
    elif link and not link.startswith(('http:','https:')):assert (R/link).exists(),link
embedded=re.search(r'<script id="schema-data" type="text/plain">(.*?)</script>',s,re.S)[1]
assert base64.b64decode(embedded)==(R/'prisma/schema.prisma').read_bytes()
scope=json.loads((R/'scope.json').read_text());m=json.loads((R/'prisma/decisions.json').read_text())
allowed={t for g in ['businessTables','supportingTables'] for v in scope[g].values() for t in v}
selected=json.loads((R/'sources/selected-data-map.json').read_text())
assert {t['id'] for t in selected['tables']}==allowed==set(m['modelNames'])
assert len(allowed)==42 and m['sourceTables']==180 and m['columns']==537
assert not any(t.startswith(('finance.','sales.','cash_flow.','inventory.','production.')) for t in allowed)
assert m['sourceSha256']==hashlib.sha256((R/'sources/hadron-data-map.html').read_bytes()).hexdigest()
source=json.loads(re.search(r'<script id="hadron-model" type="application/json">(.*?)</script>',(R/'sources/hadron-data-map.html').read_text(),re.S)[1])
original={t['id']:t for t in source['tables']}
assert all(t==original[t['id']] for t in selected['tables']),'selected source tables were modified'
manifest=json.loads((R/'evidence/layout-before.json').read_text())
intentional={'docs/MIGRATION.md','scripts/windows/New-HadronErpMigrationBundle.ps1','scripts/windows/Restore-HadronErpMigrationBundle.ps1'}
changed=[]
for entry in manifest:
 f=R.parent/'main-erp'/entry['path'];assert f.is_file(),str(f)
 if hashlib.sha256(f.read_bytes()).hexdigest()!=entry['sha256']:changed.append(entry['path'])
assert set(changed)<=intentional,changed
assert (R.parent/'.git').exists()
print(f'PASS: offline HTML ({len(p.ids)} unique ids), embedded schema, 42-table allowlist, untouched source records, all {len(manifest)} original app paths preserved; {len(changed)} documented path-helper/doc edits.')
