import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { chromium } from 'playwright-core';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../docs/ui/trading-prototype');
const specContext={window:{}};
vm.runInNewContext(await fs.readFile(path.join(root,'assets/data.js'),'utf8'),specContext);
const spec=specContext.window.TRADING_SPEC;
const manifest=JSON.parse(await fs.readFile(path.join(root,'field-map.json'),'utf8'));
assert.equal(Object.keys(spec.models).length,33);
assert.equal(spec.pages.length,9);
for(const row of manifest){assert.ok(spec.models[row.model].fields[row.field]);assert.equal(row.table,spec.models[row.model].table);}
console.log(`PASS: ${manifest.length} controls map to existing scalar fields in 33 Prisma models`);

const server=http.createServer(async(req,res)=>{
 try{
  const url=new URL(req.url,'http://test');
  const f=path.resolve(root,'.'+decodeURIComponent(url.pathname==='/'?'/index.html':url.pathname));
  if(!f.startsWith(root+path.sep))throw Error('invalid path');
  const bytes=await fs.readFile(f);
  const type={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.svg':'image/svg+xml','.json':'application/json'}[path.extname(f)]||'application/octet-stream';
  res.writeHead(200,{'content-type':type});res.end(bytes);
 }catch{res.writeHead(404);res.end('not found');}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const base=`http://127.0.0.1:${server.address().port}`;
let browser;
try{
 browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH,headless:true,args:['--no-sandbox','--disable-dev-shm-usage','--no-zygote']});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});
 const p=await context.newPage();
 const errors=[],failedRequests=[];
 p.on('pageerror',e=>errors.push(e.message));
 p.on('requestfailed',r=>failedRequests.push(r.url()));
 p.on('dialog',d=>d.accept());
 const go=file=>p.goto(base+'/'+file);
 const fill=async(model,key,value)=>{const l=p.locator(`[data-model="${model}"][data-field="${key}"]`).first();await l.fill(value);};
 const select=async(model,key,index=1)=>{const l=p.locator(`[data-model="${model}"][data-field="${key}"]`).first();const v=await l.locator('option').nth(index).getAttribute('value');await l.selectOption(v);};
 const choose=async(model,key)=>{const l=p.locator(`[data-model="${model}"][data-field="${key}"]`).first();const list=await l.getAttribute('list');const v=await p.locator(`#${list} option`).first().getAttribute('value');await l.fill(v);};
 const saved=()=>p.evaluate(()=>JSON.parse(localStorage.getItem('hadron.trading.annotated.v1')));
 // Every route loads without script/runtime errors, including the schema inspector.
 for(const f of ['index.html',...spec.pages.map(p=>p.file),'data-sources.html']){
  await go(f);assert.equal(await p.locator('h1').count(),1,f);
  if(f!=='index.html'&&f!=='data-sources.html'){
   const missing=await p.locator('#form-fields input,#form-fields select,#form-fields textarea').evaluateAll(es=>es.filter(e=>!e.closest('.field')?.querySelector('.binding code')).map(e=>e.id));
   assert.deepEqual(missing,[],f);
  }
 }
 console.log('PASS: 11 routes render; all form controls carry field provenance');
 await go('data-sources.html');assert.equal(await p.locator('.schema-model').count(),33);await p.locator('#schema-search').fill('eightDigitCode');assert.ok(await p.locator('.schema-model:visible').count()>0);
 console.log('PASS: searchable reference exposes all 33 models');
 await go('01-project.html');
 assert.equal(await p.locator('[data-field="priorityCode"]').count(),0);
 await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/نیاز به اصلاح/);
 const title='<img src=x onerror="window.injected=1"> تأمین قطعه';
 await fill('TradeProject','titleFa',title);
 await select('TradeProject','responsibleUserId');
 await fill('ProjectStage','startedAt','2026-10-18');
 await choose('TradeProject','customerPartyId');
 await choose('TradeProject','businessCardId');
 assert.match(await p.locator('[id$="startedAt-jalali"]').innerText(),/۱۴۰۵/);
 await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/ذخیره شد/);
 let data=await saved();assert.match(data.project.projectNumber,/^DEMO-/);assert.equal(data.project.status,'DRAFT');assert.equal(data.pages.project[0][0].titleFa,title);assert.match(data.pages.project[2][0].customerPartyId,/^00000000-/);
 assert.equal(await p.evaluate(()=>window.injected),undefined);
 await p.reload();assert.equal(await p.locator('[data-field="titleFa"]').inputValue(),title);
 console.log('PASS: required fields, Jalali readout, FK selection, local persistence, XSS escaping');
 // Gate is not confused with viewing the next step.
 await go('02-orders.html');assert.equal(await p.locator('#form-fields').evaluate(e=>e.disabled),true);await p.locator('#enable-preview').click();assert.equal(await p.locator('#form-fields').evaluate(e=>e.disabled),false);
 await p.locator('[data-add="1"]').click();await fill('OrderRegistrationLine','quantity','۰.۱');await fill('OrderRegistrationLine','unitPrice','۰.۲');
 assert.equal(await p.locator('[data-field="lineTotal"]').inputValue(),'0.02');
 await p.locator('[data-add="1"]').click();assert.equal(await p.locator('[data-rows="1"] .repeat-row').count(),2);await p.locator('[data-remove="1:1"]').click();assert.equal(await p.locator('[data-rows="1"] .repeat-row').count(),1);
 await p.locator('#save-draft').click();
 console.log('PASS: pre-code stage gate, explicit preview, repeat add/remove, exact decimal multiplication');
 await go('01-project.html');await p.locator('#simulate').click();await p.locator('#code-value').fill('12345678');await p.locator('#code-issued').fill('2026-10-18');await p.locator('#code-expiry').fill('2027-01-18');await p.locator('#code-form button[type="submit"]').click();
 assert.equal(await p.locator('#form-fields').evaluate(e=>e.disabled),true);assert.equal((await saved()).project.eightDigitCode,'12345678');
 await go('03-fx.html');assert.equal(await p.locator('#form-fields').evaluate(e=>e.disabled),false);
 await fill('FxFile','paidRialAmount','100');await p.locator('[data-add="1"]').click();await fill('FxAllocation','allocatedAmount','2');await p.locator('[data-add="2"]').click();await fill('FxReceipt','receivedAmount','1');
 assert.equal(await p.locator('[data-model="FxFile"][data-field="grossRate"]').inputValue(),'50');assert.equal(await p.locator('[data-model="FxFile"][data-field="netRate"]').inputValue(),'100');assert.equal(await p.locator('[data-field="receiptStatus"]').inputValue(),'دریافت بخشی');
 await fill('FxAllocation','allocatedAmount','0');assert.equal(await p.locator('[data-model="FxFile"][data-field="grossRate"]').inputValue(),'');
 await p.locator('#save-draft').click();
 console.log('PASS: code issuance simulation, first-stage lock, FX rates and zero denominator');
 await go('08-commitments.html');await select('Commitment','currencyId',1);await fill('Commitment','committedAmount','10');await fill('Commitment','deadlineDate','2027-01-18');await p.locator('[data-add="1"]').click();await fill('CommitmentDischarge','amount','2');await select('CommitmentDischarge','currencyId',2);await fill('CommitmentDischarge','dischargeDate','2026-10-18');await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/ارز تسویه/);await p.locator('#save-draft').click();
 console.log('PASS: mixed currencies never enter commitment total');
 // Cost percentages, allocation amounts, precision limits, and currency checks.
 await go('09-costs.html');await select('TradeCost','costTypeId');await select('TradeCost','currencyId');await fill('TradeCost','description','هزینهٔ آزمایشی');await fill('TradeCost','amount','10');await fill('TradeCost','effectiveDate','2026-10-18');
 await p.locator('[data-add="1"]').click();await select('CostAllocation','scopeType');await select('CostAllocation','currencyId');await fill('CostAllocation','allocationPercent','50');await fill('CostAllocation','allocatedAmount','10');
 await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/دقیقاً ۱۰۰/);
 await fill('CostAllocation','allocationPercent','100');await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/ذخیره شد/);
 await fill('CostAllocation','allocationPercent','99.12345');await p.locator('#save-validate').click();assert.match(await p.locator('#validation').innerText(),/precision/);await p.locator('#save-draft').click();
 console.log('PASS: cost percentages, same-currency allocations and schema numeric precision');
 // Export/import round trip; malformed input must not overwrite the existing draft.
 const downloadPromise=p.waitForEvent('download');await p.locator('#export-top').click();const download=await downloadPromise;const backup=await fs.readFile(await download.path());assert.equal(JSON.parse(backup).format,'hadron-trading-review-v1');
 await go('index.html');await p.locator('#import-file').setInputFiles({name:'review.json',mimeType:'application/json',buffer:backup});await p.waitForFunction(()=>document.querySelector('#toast').textContent.includes('وارد شد'));assert.equal((await saved()).project.eightDigitCode,'12345678');
 await p.locator('#import-file').setInputFiles({name:'bad.json',mimeType:'application/json',buffer:Buffer.from('{"format":"invalid"}')});await p.waitForFunction(()=>document.querySelector('#toast').textContent.includes('ناموفق'));assert.equal((await saved()).project.eightDigitCode,'12345678');
 console.log('PASS: JSON download/import round trip; invalid backup is rejected without data loss');
 // All pages must fit a narrow viewport (local horizontal scrollers are allowed).
 await p.setViewportSize({width:390,height:844});
 for(const f of ['index.html',...spec.pages.map(p=>p.file),'data-sources.html']){await go(f);assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,f);}

 // No fake completion indicators just from navigation.
 await go('01-project.html');assert.equal(await p.locator('.rail .active').count(),1);
 await p.setViewportSize({width:390,height:844});assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await p.screenshot({path:'/tmp/trading-mobile.png',fullPage:true});
 await p.setViewportSize({width:1440,height:1000});
 await p.screenshot({path:'/tmp/trading-project-tested.png',fullPage:true});
 console.log('PASS: RTL mobile layout without horizontal page overflow');
 // Offline file:// smoke test. No fetching JSON or third-party CDNs.
 const offline=await context.newPage();offline.on('pageerror',e=>errors.push(e.message));await offline.goto('file://'+path.join(root,'01-project.html'));assert.equal(await offline.locator('h1').count(),1);assert.ok(await offline.locator('[data-field="titleFa"]').count());
 console.log('PASS: offline file:// rendering');
 // Storage unavailable must be explicit, never a false persisted success.
 const blocked=await browser.newContext();await blocked.addInitScript(()=>{Object.defineProperty(window,'localStorage',{get(){throw new DOMException('denied','SecurityError');}});});const bp=await blocked.newPage();bp.on('pageerror',e=>errors.push(e.message));await bp.goto(base+'/01-project.html');assert.match(await bp.locator('.notice.error').innerText(),/مسدود/);await bp.locator('#save-draft').click();assert.match(await bp.locator('#toast').innerText(),/در دسترس نیست/);
 console.log('PASS: denied storage reports failure with JSON fallback');
 assert.deepEqual(errors,[]);assert.deepEqual(failedRequests,[]);
 console.log('PASS: zero page errors and zero failed resource requests');
} finally {await browser?.close();await new Promise(resolve=>server.close(resolve));}
