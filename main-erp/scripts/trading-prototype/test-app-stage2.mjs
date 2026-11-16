// Real built React app with explicitly mocked APIs; this is NOT a database integration test.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..'),dist=path.join(root,'src/frontend/dist');
const server=http.createServer(async(req,res)=>{try{const u=new URL(req.url,'http://test'),f=path.resolve(dist,'.'+(u.pathname.startsWith('/assets/')?u.pathname:'/index.html'));if(!f.startsWith(dist+path.sep))throw Error('path');res.writeHead(200,{'Content-Type':{'.js':'application/javascript','.css':'text/css','.svg':'image/svg+xml','.webp':'image/webp'}[path.extname(f)]||'text/html'});res.end(await fs.readFile(f));}catch{res.writeHead(404);res.end();}});
await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
const uid=n=>`00000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const projects=Array.from({length:26},(_,i)=>({id:uid(i+1),projectNumber:`TRD-${String(i+1).padStart(6,'0')}`,titleFa:`پروژه ${i+1}`,status:i===1?'CLOSED':'DRAFT'}));
const currencies=Array.from({length:26},(_,i)=>({id:uid(100+i),code:i===0?'USD':i===25?'EUR':`CUR${i}`,nameFa:i===0?'دلار':i===25?'یورو':`ارز ${i}`,isActive:true}));
let orders=Array.from({length:26},(_,i)=>({id:uid(200+i),projectId:uid(1),registrationNumber:`REG-${String(i+1).padStart(3,'0')}`,status:i===1?'SUBMITTED':i===2?'CANCELLED':'DRAFT',registrationDate:'2026-10-18',registeredAmount:'9007199254740993.123456',currencyId:uid(100),currencyCode:'USD',currencyNameFa:'دلار',notes:`یادداشت ${i+1}`,version:1}));
let permissions=['trading.project.view','trading.order_registration.view','trading.order_registration.manage'];
const errors=[],unknown=[],creates=[],updates=[],statuses=[],reads=[];let failList=true,failCurrency=true,failCreate=true,failEdit=true;
let browser;
try{
 browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH,headless:true,args:['--no-sandbox','--no-zygote','--disable-dev-shm-usage']});
 const ctx=await browser.newContext({viewport:{width:1440,height:1100}});
 await ctx.route('**/api/**',async route=>{
  const req=route.request(),u=new URL(req.url()),p=u.pathname,method=req.method();
  const send=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
  if(p==='/api/auth/bootstrap-status')return send({initialized:true});
  if(p==='/api/auth/csrf')return send({token:'stage2-csrf'});
  if(p==='/api/auth/me')return send({id:uid(900),userName:'order-user',firstName:'کاربر',lastName:'سفارش',fullName:'کاربر سفارش',cellphoneNumber:'09120000000',emailAddress:'test@test.invalid',organizationalUnit:{id:uid(99),nameFa:'بازرگانی'},mustChangePassword:false,dataScopes:[],isActive:true,profileComplete:true,hasPhoto:true,photoUrl:'/api/auth/me/photo',isSystemAdministrator:false,roles:[],permissions});
  if(p==='/api/auth/me/photo')return route.fulfill({status:200,contentType:'image/gif',body:Buffer.from('R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==','base64')});
  if(p==='/api/notifications')return send({items:[],totalCount:0,unreadCount:0,page:1,pageSize:20});
  if(p==='/api/notifications/unread-count')return send({count:0});
  if(p==='/api/collaboration/presence')return send([]);
  if(method==='POST'||method==='PUT')assert.equal(req.headers()['x-csrf-token'],'stage2-csrf');
  if(p==='/api/trading/order-registrations/resolve-jalali-date'){assert.deepEqual(req.postDataJSON(),{jalaliDate:'14050726'});return send({jalaliDate:'14050726',gregorianDate:'2026-10-18'});}
  const page=Number(u.searchParams.get('page')||1),pageSize=Number(u.searchParams.get('pageSize')||25),search=u.searchParams.get('search')||'';
  const paged=items=>send({items:items.slice((page-1)*pageSize,page*pageSize),page,pageSize,totalCount:items.length});
  if(p==='/api/trading/projects')return paged(projects.filter(x=>`${x.projectNumber} ${x.titleFa}`.includes(search)));
  if(p==='/api/trading/order-registrations/currencies'){
   reads.push('currencies');if(failCurrency){failCurrency=false;return send({detail:'خطای نمونهٔ ارز؛ تلاش مجدد'},503);}
   return paged(currencies.filter(x=>`${x.code} ${x.nameFa}`.includes(search)));
  }
  const projectDetail=p.match(/^\/api\/trading\/projects\/([^/]+)$/);
  if(projectDetail)return send({project:{...projects.find(x=>x.id===projectDetail[1]),priority:'NORMAL',version:1,updatedAtUtc:'2026-10-18T00:00:00Z'},orderRegistrations:orders.filter(x=>x.projectId===projectDetail[1]),fxAllocations:[],shipments:[],costs:[],audit:[],capabilities:{canViewOrders:true,canViewFx:false,canViewShipments:false,canViewCustoms:false,canViewCosts:false,canViewAudit:false}});
  if(p==='/api/trading/lookups')return send({currencies:[],suppliers:[],references:[],referenceTypes:[]});
  const list=p.match(/^\/api\/trading\/projects\/([^/]+)\/order-registrations$/);
  if(list){reads.push(list[1]);if(failList){failList=false;return send({detail:'خطای نمونهٔ فهرست'},503);}let items=list[1]===uid(2)?[{...orders[0],projectId:uid(2),registrationNumber:'ARCHIVE'}]:orders.filter(x=>x.projectId===list[1]);items=items.filter(x=>x.registrationNumber.includes(search)&&(!u.searchParams.get('status')||x.status===u.searchParams.get('status')));return paged(items);}
  if(p==='/api/trading/order-registrations'&&method==='POST'){
   const body=req.postDataJSON();creates.push(body);if(failCreate){failCreate=false;return send({errors:{registrationNumber:['شمارهٔ تکراری نمونه؛ ورودی‌ها حفظ شوند']}},400);}
   const currency=currencies.find(x=>x.id===body.currencyId);const row={...body,id:uid(999),version:1,currencyCode:currency.code,currencyNameFa:currency.nameFa};orders.unshift(row);return send(row,201);
  }
  const edit=p.match(/^\/api\/trading\/order-registrations\/([^/]+)$/);
  if(edit&&method==='PUT'){
   const body=req.postDataJSON();updates.push(body);const row=orders.find(x=>x.id===edit[1]);
   if(failEdit){failEdit=false;row.version++;row.notes='اصلاح هم‌زمان کاربر دیگر';return send({detail:'نسخهٔ قدیمی نمونه؛ ویرایش شما حفظ شد'},409);}
   assert.equal(body.expectedVersion,row.version);Object.assign(row,body,{version:row.version+1});return send(row);
  }
  const status=p.match(/^\/api\/trading\/order-registrations\/([^/]+)\/status$/);
  if(status&&method==='POST'){const body=req.postDataJSON();statuses.push(body);const row=orders.find(x=>x.id===status[1]);assert.equal(body.expectedVersion,row.version);row.status=body.status;row.version++;return send(row);}
  unknown.push(`${method} ${p}`);return send({detail:'Unexpected API in test'},500);
 });
 const page=await ctx.newPage();page.on('pageerror',e=>errors.push(e.message));
 const stage=page.locator('.trade-orders');
 await page.goto(base+'/trading');try{await page.locator('.trading-flow-step').nth(1).click({timeout:10000});}catch(e){console.log({errors,unknown,body:await page.locator('body').innerText()});throw e;}
 await page.locator('[name=selectedProjectId] option[value="'+uid(1)+'"]').waitFor({state:'attached'});await page.locator('[name=selectedProjectId]').selectOption(uid(1));
 await stage.getByRole('alert').filter({hasText:'خطای نمونهٔ فهرست'}).waitFor();await stage.getByRole('button',{name:'تلاش دوباره',exact:true}).click();await stage.getByText('REG-001',{exact:true}).waitFor();
 const pager=stage.locator('.ui-pagination');
 await pager.getByRole('button',{name:'بعدی',exact:true}).click();await stage.getByText('REG-026',{exact:true}).waitFor();
 await stage.getByRole('searchbox',{name:'جست‌وجوی شمارهٔ ثبت سفارش'}).fill('REG-002');await stage.getByText('REG-002',{exact:true}).waitFor();assert.equal(await stage.locator('tbody tr').count(),1);
 await stage.getByRole('searchbox',{name:'جست‌وجوی شمارهٔ ثبت سفارش'}).fill('');await stage.getByText('REG-001',{exact:true}).waitFor();
 console.log('PASS: stage two selects real project IDs, retries list failures and uses server-side search/paging');
 await stage.getByRole('button',{name:'ثبت سفارش جدید',exact:true}).click();await page.locator('[data-order-picker=currency]').getByText('خطای نمونهٔ ارز؛ تلاش مجدد').waitFor();assert.equal(await page.locator('.trade-order-form button[type=submit]').isDisabled(),true);
 await page.locator('[data-order-picker=currency]').getByRole('button',{name:'تلاش دوباره',exact:true}).click();await page.locator('[name=currencyId] option[value="'+uid(100)+'"]').waitFor({state:'attached'});
 await page.locator('[name=currencyId]').selectOption(uid(100));await page.locator('[data-order-picker=currency]').getByRole('button',{name:'بعدی',exact:true}).click();await page.locator('[name=currencyId] option[value="'+uid(125)+'"]').waitFor({state:'attached'});assert.equal(await page.locator('[name=currencyId]').inputValue(),uid(100));await page.locator('[name=currencyId]').selectOption(uid(125));
 await page.locator('[name=registrationNumber]').fill('  REG-NEW  ');await page.locator('[name=registrationDate]').fill('۱۴۰۵۰۷۲۶');await page.locator('[name=registeredAmount]').fill('۹۰۰۷۱۹۹۲۵۴۷۴۰۹۹۳٫۱۲۳۴۵۶');await page.locator('[name=notes]').fill('یادداشت جدید');
 await page.locator('.trading-flow-step').nth(0).click();await page.locator('.trading-flow-step').nth(1).click();assert.equal(await page.locator('[name=registrationNumber]').inputValue(),'  REG-NEW  ');
 page.once('dialog',d=>d.dismiss());await page.locator('[name=selectedProjectId]').selectOption(uid(2));assert.equal(await page.locator('[name=selectedProjectId]').inputValue(),uid(1));assert.equal(await page.locator('[name=registrationNumber]').inputValue(),'  REG-NEW  ');
 await page.locator('.trade-order-form button[type=submit]').click();await stage.getByRole('alert').filter({hasText:'شمارهٔ تکراری نمونه'}).waitFor();assert.equal(await page.locator('[name=registeredAmount]').inputValue(),'۹۰۰۷۱۹۹۲۵۴۷۴۰۹۹۳٫۱۲۳۴۵۶');
 assert.deepEqual(creates[0],{registrationNumber:'REG-NEW',registrationDate:'2026-10-18',registeredAmount:'9007199254740993.123456',currencyId:uid(125),notes:'یادداشت جدید',projectId:uid(1),status:'DRAFT'});
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.setViewportSize({width:1440,height:1100});
 assert.equal(await page.locator('.trade-setup-binding').count(),0);assert.equal(await stage.locator('[name=bankId],[name=orderRegistrationId],[name=allocationDate]').count(),0);assert.equal(await page.locator('.is-done').count(),0);
 await page.locator('.trade-order-form button[type=submit]').click();let row=stage.getByRole('row').filter({hasText:'REG-NEW'});await row.waitFor();await row.getByText('۹٬۰۰۷٬۱۹۹٬۲۵۴٬۷۴۰٬۹۹۳٫۱۲۳۴۵۶',{exact:true}).waitFor();assert.equal(creates.length,2);
 console.log('PASS: independent paged currency lookup, draft preservation, discard cancellation, exact decimal strings, CSRF and validation-error preservation');
 await row.getByRole('button',{name:'ویرایش',exact:true}).click();await page.locator('[name=currencyId] option[value="'+uid(125)+'"]').waitFor({state:'attached'});assert.equal(await page.locator('[name=currencyId]').inputValue(),uid(125));
 await page.locator('[name=registeredAmount]').fill('123.654321');await page.locator('[name=reason]').fill('اصلاح مبلغ پیش‌نویس');await page.locator('.trade-order-form button[type=submit]').click();await stage.getByRole('alert').filter({hasText:'نسخهٔ قدیمی نمونه'}).waitFor();assert.equal(await page.locator('[name=registeredAmount]').inputValue(),'123.654321');assert.equal(updates[0].expectedVersion,1);assert.equal('projectId' in updates[0],false);assert.equal('status' in updates[0],false);
 page.once('dialog',d=>d.accept());await page.locator('.trade-order-form').getByRole('button',{name:'انصراف',exact:true}).click();await stage.getByRole('button',{name:'به‌روزرسانی',exact:true}).click();await stage.getByText('اصلاح هم‌زمان کاربر دیگر',{exact:true}).waitFor();
 row=stage.getByRole('row').filter({hasText:'REG-NEW'});await row.getByRole('button',{name:'ویرایش',exact:true}).click();await page.locator('[name=registeredAmount]').fill('123.654321');await page.locator('[name=reason]').fill('اصلاح تاییدشده');await page.locator('.trade-order-form button[type=submit]').click();await row.getByText('۱۲۳٫۶۵۴۳۲۱',{exact:true}).waitFor();assert.equal(updates.at(-1).expectedVersion,2);
 await row.getByRole('button',{name:'تغییر وضعیت عملیاتی',exact:true}).click();assert.equal(await page.locator('[name=targetStatus] option[value=ACTIVE]').count(),0);await page.locator('[name=targetStatus]').selectOption('SUBMITTED');await page.locator('[name=statusReason]').fill('ارسال برای بررسی');await page.locator('.trade-order-status button[type=submit],.trade-order-status button:not([type])').first().click();await row.getByText('ارسال‌شده',{exact:true}).waitFor();assert.equal(await row.getByRole('button',{name:'ویرایش',exact:true}).count(),0);assert.deepEqual(statuses[0],{status:'SUBMITTED',expectedVersion:3,reason:'ارسال برای بررسی',isCorrection:false});
 console.log('PASS: versioned draft editing, stale conflict retention, fresh reopen and separate reasoned lifecycle command');
 await page.locator('[name=selectedProjectId]').selectOption(uid(2));await stage.getByText('ARCHIVE',{exact:true}).waitFor();assert.equal(await stage.getByRole('button',{name:'ثبت سفارش جدید',exact:true}).count(),0);await stage.getByText('پروژه بسته یا لغو شده است؛ سفارش‌ها فقط قابل مشاهده‌اند.',{exact:true}).waitFor();
 const object=await ctx.newPage();object.on('pageerror',e=>errors.push(e.message));await object.goto(base+'/trading/projects/'+uid(1));await object.locator('.trade-orders').getByText('REG-NEW',{exact:true}).waitFor();assert.equal(await object.getByText('REG-NEW',{exact:true}).count(),1);await object.locator('.trade-orders').getByRole('button',{name:'ثبت سفارش جدید',exact:true}).click();assert.deepEqual(await object.locator('[data-order-field]').evaluateAll(es=>es.map(e=>e.dataset.orderField)),['projectId','registrationNumber','registrationDate','currencyId','registeredAmount','status','notes']);assert.equal(await object.locator('[name=selectedProjectId]').count(),0);await object.setViewportSize({width:390,height:844});assert.equal(await object.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await object.screenshot({path:'/tmp/trading-stage2-app.png',fullPage:true});
 console.log('PASS: the same paged manager/form on the real project object page, without duplicate legacy order UI');
 await object.locator('[name=currencyId] option[value="'+uid(100)+'"]').waitFor({state:'attached'});
 const beforeCurrency=reads.filter(x=>x==='currencies').length;
 permissions=['trading.project.view','trading.order_registration.view'];const readonly=await ctx.newPage();readonly.on('pageerror',e=>errors.push(e.message));await readonly.goto(base+'/trading');await readonly.locator('.trading-flow-step').nth(1).click();await readonly.locator('[name=selectedProjectId] option[value="'+uid(1)+'"]').waitFor({state:'attached'});await readonly.locator('[name=selectedProjectId]').selectOption(uid(1));await readonly.getByText('REG-NEW',{exact:true}).waitFor();assert.equal(await readonly.locator('.trade-orders').getByRole('button',{name:'ثبت سفارش جدید',exact:true}).count(),0);assert.equal(reads.filter(x=>x==='currencies').length,beforeCurrency);
 const readCountBeforeNoOrders=reads.length;permissions=['trading.project.view'];const noOrders=await ctx.newPage();await noOrders.goto(base+'/trading');await noOrders.locator('.trading-flow-step').nth(1).click();await noOrders.getByText('برای مشاهدهٔ سفارش‌ها به مجوز مشاهدهٔ ثبت سفارش بازرگانی نیاز دارید.',{exact:true}).waitFor();assert.equal(await noOrders.locator('[name=selectedProjectId]').count(),0);assert.equal(reads.length,readCountBeforeNoOrders);
 console.log('PASS: closed-project and read-only controls, and no unauthorized order/currency lookups');
 for(const file of ['stage-2.html','stage-2-annotated.html']){
  const offline=await ctx.newPage();offline.on('pageerror',e=>errors.push(e.message));await offline.goto('file://'+path.join(root,'docs/ui/trading-stage-2',file));assert.deepEqual(await offline.locator('[data-order-field]').evaluateAll(es=>es.map(e=>e.dataset.orderField)),['projectId','registrationNumber','registrationDate','currencyId','registeredAmount','status','notes']);assert.equal(await offline.locator('.trade-setup-binding').count(),file.includes('annotated')?7:0);await offline.setViewportSize({width:390,height:844});assert.equal(await offline.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 }
 permissions=['trading.project.view','trading.order_registration.view','trading.order_registration.manage'];
 const english=await ctx.newPage();english.on('pageerror',e=>errors.push(e.message));await english.goto(base+'/trading');await english.locator('.language-switcher select').selectOption('en');await english.locator('.trading-flow-step').nth(1).click();await english.locator('[name=selectedProjectId] option[value="'+uid(1)+'"]').waitFor({state:'attached'});await english.locator('[name=selectedProjectId]').selectOption(uid(1));await english.locator('.trade-orders').getByRole('button',{name:'Register new order',exact:true}).click();assert.equal(await english.locator('[name=registrationDate]').getAttribute('type'),'date');await english.locator('[name=registrationDate]').fill('2026-10-18');await english.locator('.language-switcher select').selectOption('fa-IR');assert.equal(await english.locator('[name=registrationDate]').getAttribute('type'),'date');assert.equal(await english.locator('[name=registrationDate]').inputValue(),'2026-10-18');
 console.log('PASS: English ISO date entry and stable date-calendar semantics across a mid-draft language switch');
 assert.deepEqual(errors,[]);assert.deepEqual(unknown,[]);
 console.log('PASS: both actual-view HTML exports have identical fields, annotation isolation and mobile layout');
 console.log('NOTE: APIs are mocked. Backend integration tests and .NET compilation must run separately.');
}finally{await browser?.close();await new Promise(r=>server.close(r));}
