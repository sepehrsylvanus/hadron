'use strict';
(() => {
const {models,pages,referenceCommit} = window.TRADING_SPEC;
const id = document.body.dataset.page;
const page = pages.find(p=>p.id===id);
const KEY='hadron.trading.annotated.v1';
const $=(s,r=document)=>r.querySelector(s), $$=(s,r=document)=>[...r.querySelectorAll(s)];
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fa=n=>new Intl.NumberFormat('fa-IR').format(n);
const uid=n=>`00000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const nowDate=()=>new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
const fresh=()=>({format:'hadron-trading-review-v1',pages:{},project:{},savedAt:null});
let state=fresh(),storageAvailable=true,dirty=false;
function validState(raw){
 if(!raw || raw.format!=='hadron-trading-review-v1' || !raw.pages || typeof raw.pages!=='object')throw Error('ساختار فایل پشتیبان معتبر نیست.');
 const clean=fresh();
 for(const p of pages){
  const data=raw.pages[p.id];if(!Array.isArray(data))continue;
  clean.pages[p.id]=p.groups.map((g,i)=>{
   const rows=Array.isArray(data[i])?data[i]:[];
   if(rows.length>200)throw Error('حداکثر ۲۰۰ ردیف برای هر بخش در این نمونه مجاز است.');
   return rows.map(row=>{
    const o={};for(const f of g.fields){
     const v=row?.[f.key],meta=models[g.model].fields[f.key];
     if(v===undefined)continue;
     if(meta.type==='Boolean'?typeof v!=='boolean':typeof v!=='string')throw Error('نوع دادهٔ ورودی نامعتبر است.');
     if(String(v).length>(meta.maxLength||5000))throw Error('طول فیلد بیش از حد مجاز است.');
     o[f.key]=v;
    }return o;
   });
  });
 }
 for(const k of ['id','projectNumber','eightDigitCode','codeIssuedAt','codeExpiresAt','isLocked','createdAtUtc','registrationDate','createdByUserId','status']){
  const v=raw.project?.[k];if(typeof v==='string'&&v.length<=200||typeof v==='boolean')clean.project[k]=v;
 }
 const c=clean.project.eightDigitCode;
 if(c&&!/^\d{8}$/.test(c))throw Error('کد هشت‌رقمی نامعتبر است.');
 clean.project.isLocked=!!c;
 clean.savedAt=typeof raw.savedAt==='string'&&!isNaN(Date.parse(raw.savedAt))?raw.savedAt:null;
 return clean;
}
try{const raw=localStorage.getItem(KEY);if(raw)state=validState(JSON.parse(raw));}catch(e){storageAvailable=false;}
const labels={projectNumber:'شمارهٔ داخلی پروژه',titleFa:'موضوع پروژه',titleEn:'عنوان انگلیسی',goodsSummary:'خلاصهٔ کالا',customerPartyId:'مشتری',supplierPartyId:'تأمین‌کننده',businessCardId:'کارت بازرگانی',responsibleUserId:'مسئول پیگیری',targetDate:'مهلت هدف',notes:'توضیحات',startedAt:'تاریخ شروع',dueDate:'مهلت',orderNumber:'شمارهٔ ثبت سفارش',registrationDate:'تاریخ ثبت سفارش',currencyId:'ارز',declaredValueAmount:'ارزش اظهارشدهٔ کالا',currencyAmount:'مقدار ارز سفارش',incotermCode:'اینکوترم',paymentMethodCode:'روش پرداخت',goodsTitle:'عنوان کالا / محتویات',hsCode:'کد تعرفه (HS)',quantity:'تعداد',unitId:'واحد سنجش',unitPrice:'قیمت واحد',lineTotal:'جمع ردیف',permitTypeCode:'نوع مجوز',permitNumber:'شمارهٔ مجوز',issuedBy:'مرجع صدور',issueDate:'تاریخ صدور',expiryDate:'تاریخ انقضا',bankPartyId:'بانک عامل',fxSourceCode:'منبع تأمین ارز',requestDate:'تاریخ شروع درخواست',allocationNumber:'شمارهٔ تخصیص',allocationNumberDate:'تاریخ شمارهٔ تخصیص',paidRialAmount:'ریال پرداختی',allocatedAmount:'مبلغ تخصیص',receivedAmount:'مبلغ دریافت',grossRate:'نرخ ناخالص (ریال / واحد ارز)',netRate:'نرخ خالص (ریال / واحد ارز)',receiptStatus:'وضعیت دریافت',allocationDate:'تاریخ تخصیص',bankReferenceNumber:'شمارهٔ مرجع بانک',receivedAt:'تاریخ دریافت',collateralTypeCode:'نوع وثیقه',instrumentNumber:'شمارهٔ چک / سند',amount:'مبلغ',heldByBank:'وثیقه در اختیار بانک است',heldAt:'تاریخ تحویل به بانک',estimatedDeparture:'حرکت تخمینی',estimatedArrival:'رسیدن تخمینی',actualDeparture:'حرکت واقعی',actualArrival:'رسیدن واقعی',transportMode:'روش حمل',forwarderPartyId:'فورواردر',carrierPartyId:'حامل',originPortCode:'بندر مبدأ',destinationPortCode:'بندر مقصد',packageTypeCode:'نوع بسته‌بندی',packageCount:'تعداد بسته',grossWeight:'وزن ناخالص',weightUnitId:'واحد وزن',volume:'حجم',volumeUnitId:'واحد حجم',hasInsurance:'محموله بیمه دارد',billOfLadingNumber:'شمارهٔ بارنامه (اختیاری)',orderRegistrationLineId:'قلم سفارش مبدأ',partyId:'طرف تجاری',roleCode:'نقش طرف',contactName:'نام رابط',phone:'شمارهٔ تماس',sequence:'ترتیب مسیر',fromPortCode:'بندر حرکت',toPortCode:'بندر رسیدن',vehicleOrVessel:'نام کشتی / وسیله',trackingNumber:'شمارهٔ رهگیری',clearanceNumber:'شمارهٔ اظهارنامه / ترخیص',clearanceType:'نوع ترخیص',brokerPartyId:'ترخیص‌کار',customsOfficeCode:'ادارهٔ گمرک',declarationDate:'تاریخ اظهار',documentsCompletedAt:'تاریخ تکمیل مدارک',releaseDate:'تاریخ ترخیص',dutyAmount:'حقوق و عوارض',dutyCurrencyId:'ارز حقوق و عوارض',insurerPartyId:'شرکت بیمه',policyNumber:'شمارهٔ بیمه‌نامه',coverageTypeCode:'نوع پوشش',premiumAmount:'حق بیمه',startDate:'شروع پوشش',inspectorPartyId:'شرکت بازرسی',inspectionDate:'تاریخ بازرسی',certificateNumber:'شمارهٔ گواهی',statusCode:'وضعیت بازرسی',resultCode:'نتیجهٔ بازرسی',resultNotes:'شرح نتیجه',committedAmount:'مبلغ تعهد',deadlineDate:'مهلت رفع تعهد',extendedToDate:'مهلت تمدیدشده',dischargedAmount:'مبلغ تسویه‌شده',dischargeDate:'تاریخ تسویه',methodCode:'روش',documentNumber:'شمارهٔ سند',costTypeId:'نوع هزینه',stage:'مرحلهٔ مرتبط',description:'شرح هزینه',effectiveDate:'تاریخ اثر هزینه',payeePartyId:'دریافت‌کنندهٔ وجه',referenceNumber:'شمارهٔ پیگیری',scopeType:'نوع بخش',scopeId:'شناسهٔ بخش',allocationPercent:'درصد تخصیص',paymentMethodCode:'روش پرداخت',paidAt:'تاریخ پرداخت',isReturn:'برگشت وجه است'};
const names={TradeReferenceValue:'دیکشنری بازرگانی',TradeParty:'طرف تجاری',TradePartyRole:'نقش طرف',TradePartyLicense:'مجوز طرف',BusinessCard:'کارت بازرگانی',TradePartyAllocationRule:'قواعد تخصیص طرف',BankCollateralTerm:'شرایط وثیقهٔ بانک',TradeProject:'پروندهٔ بازرگانی',ProjectStage:'مراحل پرونده',ProjectParty:'طرف‌های پرونده',ProjectKeyDate:'تاریخ‌های کلیدی',ProjectDocument:'مدارک پرونده',OrderRegistration:'ثبت سفارش',OrderRegistrationLine:'اقلام سفارش',OrderPermit:'مجوز ثبت سفارش',FxFile:'پروندهٔ ارزی',FxAllocation:'تخصیص ارز',FxReceipt:'دریافت ارز',Collateral:'وثیقه',InsurancePolicy:'بیمه‌نامه',Inspection:'بازرسی',Shipment:'محموله',ShipmentItem:'اقلام محموله',ShipmentParty:'طرف‌های حمل',ShipmentLeg:'مسیر حمل',ShipmentCustoms:'ترخیص گمرکی',Commitment:'تعهد',CommitmentDischarge:'رفع تعهد',CostType:'نوع هزینه',TradeCost:'هزینه',CostAllocation:'تخصیص هزینه',CostPayment:'پرداخت هزینه',TradeAuditEvent:'ممیزی'};
const enumFa={DRAFT:'پیش‌نویس',NOT_RECEIVED:'دریافت نشده',PARTIAL:'دریافت بخشی',RECEIVED:'دریافت شده',SEA:'دریایی',AIR:'هوایی',ROAD:'جاده‌ای',RAIL:'ریلی',MULTIMODAL:'چندوجهی',DEFINITIVE:'قطعی',TEMPORARY:'موقت',TRANSIT:'ترانزیت',PROJECT_SETUP:'ایجاد پرونده',ORDER:'ثبت سفارش',FX:'ارز',INSURANCE:'بیمه / بازرسی',SHIPPING:'حمل',CUSTOMS:'گمرک',COMMITMENT:'رفع تعهد',COSTS:'هزینه',PROJECT:'پرونده',SHIPMENT:'محموله',SHIPMENT_ITEM:'قلم محموله',ORDER_LINE:'قلم سفارش',LEG:'مسیر',STAGE:'مرحله'};
const opts=(items)=>items.map(([value,label])=>({value,label}));
const parties=opts([[uid(1),'شرکت نمونه آلفا — مشتری'],[uid(2),'شرکت نمونه تأمین — تأمین‌کننده'],[uid(3),'بانک نمونه'],[uid(4),'شرکت نمونه حمل'],[uid(5),'بیمه‌گر نمونه'],[uid(6),'بازرسی نمونه'],[uid(7),'ترخیص‌کار نمونه']]);
const dictionaries={incotermCode:[['FOB','FOB'],['CIF','CIF'],['EXW','EXW']],paymentMethodCode:[['TRANSFER','حواله'],['CASH','نقدی']],fxSourceCode:[['BANKING','بانکی'],['EXPORT','صادراتی'],['FREE','آزاد']],permitTypeCode:[['IMPORT','مجوز ورود'],['STANDARD','استاندارد']],collateralTypeCode:[['CHEQUE','چک'],['DEPOSIT','سپرده']],packageTypeCode:[['CARTON','کارتن'],['PALLET','پالت'],['BOX','جعبه']],originPortCode:[['SHA','شانگهای'],['BND','بندرعباس']],destinationPortCode:[['BND','بندرعباس'],['IST','استانبول']],fromPortCode:[['SHA','شانگهای'],['BND','بندرعباس']],toPortCode:[['BND','بندرعباس'],['IST','استانبول']],customsOfficeCode:[['BND','شهید رجایی — نمونه']],roleCode:[['FORWARDER','فورواردر'],['CARRIER','حامل'],['BROKER','ترخیص‌کار']],statusCode:[['REQUESTED','درخواست شده'],['IN_PROGRESS','در حال انجام'],['DONE','انجام شده']],resultCode:[['PASS','قبول'],['FAIL','رد'],['CONDITIONAL','مشروط']]};
const dictTypes={incotermCode:'INCOTERM',paymentMethodCode:'COST_PAYMENT_METHOD',fxSourceCode:'FX_SOURCE',permitTypeCode:'PERMIT_TYPE',collateralTypeCode:'COLLATERAL_TYPE',packageTypeCode:'PACKAGE_TYPE',originPortCode:'PORT',destinationPortCode:'PORT',fromPortCode:'PORT',toPortCode:'PORT',customsOfficeCode:'CUSTOMS_OFFICE',roleCode:'PARTY_ROLE',statusCode:'INSPECTION_STATUS',resultCode:'INSPECTION_RESULT'};
function lookup(model,key){
 const meta=models[model].fields[key];
 if(meta.enum)return {values:opts(meta.enum.map(x=>[x,enumFa[x]||x])),source:`enum: ${meta.type} (مقدار قراردادی؛ نه جدول)`};
 if(/currencyId$/i.test(key))return {values:opts([[uid(20),'USD — دلار آمریکا'],[uid(21),'EUR — یورو'],[uid(22),'IRR — ریال']]),source:'reference_data.currencies · FK → id؛ نمایش code (مرجع مشترک)'};
 if(/unitId$/.test(key))return {values:opts([[uid(30),'عدد'],[uid(31),'کیلوگرم'],[uid(32),'مترمکعب']]),source:'reference_data.units · FK → id (مرجع مشترک)'};
 if(key==='responsibleUserId')return {values:opts([[uid(40),'کارشناس بازرگانی — کاربر نمونه'],[uid(41),'مدیر بازرگانی — کاربر نمونه']]),source:'identity.users · شناسه از مرجع هویت؛ نام از API پروفایل (خارج از این اسکیما)'};
 if(key==='businessCardId')return {values:opts([[uid(50),'کارت نمونه ۱۲۳۴۵۶ — شرکت نمونه آلفا']]),source:'business_cards.id / cardNumber → trade_parties.legalNameFa؛ کنترل status و expiryDate در سرویس'};
 if(/PartyId$/.test(key)||key==='partyId'){
  let values=parties,role='نقش مرتبط';
  const filter={customerPartyId:[1,'CUSTOMER'],supplierPartyId:[2,'SUPPLIER'],bankPartyId:[3,'BANK'],forwarderPartyId:[4,'FORWARDER'],carrierPartyId:[4,'CARRIER'],insurerPartyId:[5,'INSURER'],inspectorPartyId:[6,'INSPECTOR'],brokerPartyId:[7,'BROKER']}[key];
  if(filter){values=parties.filter(x=>x.value===uid(filter[0]));role=filter[1];}
  return {values,source:`trade_parties.id / legalNameFa + trade_party_roles.roleCode (${role})؛ فقط isActive و غیر isBlocked`};
 }
 if(dictionaries[key])return {values:opts(dictionaries[key]),source:`trade_reference_values.code / nameFa؛ referenceType = ${model==='OrderRegistration'&&key==='paymentMethodCode'?'PAYMENT_METHOD':dictTypes[key]}`};
 if(key==='costTypeId')return {values:opts([[uid(60),'هزینهٔ حمل — نمونه'],[uid(61),'هزینهٔ ثبت سفارش — نمونه']]),source:'cost_types.id / nameFa / stage / isMandatory؛ اجباری بودن طبق تنظیمات سرور'};
 if(key==='orderRegistrationLineId'){
  const rows=state.pages.orders?.[1]||[];
  return {values:opts(rows.map((r,i)=>[`demo-order-line-${i+1}`,r.goodsTitle||`قلم ${fa(i+1)}`])),source:'order_registration_lines.id / goodsTitle؛ فقط اقلام سفارش همین پرونده (شناسهٔ محلی نمونه)'};
 }
 return null;
}
function binding(model,key,source=''){
 const table=models[model].table;
 return `<span class="binding">محل داده: <code dir="ltr">${table}.${key}</code>${source?`<span class="source">${esc(source)}</span>`:''}</span>`;
}
function toast(s){$('#toast').textContent=s;setTimeout(()=>{$('#toast').textContent='';},5500);}
function persist(){const time=new Date().toISOString();try{localStorage.setItem(KEY,JSON.stringify({...state,savedAt:time}));state.savedAt=time;storageAvailable=true;return true;}catch(e){storageAvailable=false;toast('ذخیرهٔ مرورگر در دسترس نیست؛ خروجی JSON بگیرید.');return false;}}
function jalali(value){if(!value||!/^\d{4}-\d{2}-\d{2}$/.test(value))return '';const d=new Date(value+'T12:00:00Z');return isNaN(d)?'':new Intl.DateTimeFormat('fa-IR-u-ca-persian',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).format(d);}
function normalizeDigits(s){return String(s).replace(/[۰-۹٠-٩]/g,c=>'۰۱۲۳۴۵۶۷۸۹'.includes(c)?'۰۱۲۳۴۵۶۷۸۹'.indexOf(c):'٠١٢٣٤٥٦٧٨٩'.indexOf(c));}
const SCALE=1000000n;
function decimal(s){s=normalizeDigits(s).trim();if(!/^-?\d+(\.\d{0,6})?$/.test(s))return null;const neg=s.startsWith('-');const [a,b='']=s.replace('-','').split('.');return (BigInt(a)*SCALE+BigInt(b.padEnd(6,'0')))*(neg?-1n:1n);}
function formatted(n,precision=6){const neg=n<0n;if(neg)n=-n;const scale=10n**BigInt(precision);let frac=(n%scale).toString().padStart(precision,'0').replace(/0+$/,'');return (neg?'-':'')+(n/scale).toString()+(frac?'.'+frac:'');}
function sum(rows,key){let n=0n;for(const r of rows){const v=decimal(r[key]||'0');if(v!==null)n+=v;}return n;}
function valuesOf(model){if(!page)return [];const data=state.pages[page.id]||[];return page.groups.flatMap((g,i)=>g.model===model?(data[i]||[]):[]);}
function rowTemplate(g,gi,ri,value={}){
 const controls=g.fields.map(f=>control(g.model,f,gi,ri,value[f.key])).join('');
 return `<div class="repeat-row" data-row="${ri}"><div class="row-head"><span>ردیف ${fa(ri+1)} <code>${models[g.model].table}</code></span><button type="button" class="button quiet small" data-remove="${gi}:${ri}" aria-label="حذف ردیف ${fa(ri+1)} از ${esc(g.title)}">حذف ردیف آزمایشی</button></div><div class="fields">${controls}</div></div>`;
}
function control(model,f,gi,ri,value){
 const meta=models[model].fields[f.key],key=f.key,label=f.label||labels[key]||meta.label.split('—').pop().trim(),source=lookup(model,key);
 const fieldId=`f-${gi}-${ri}-${key}`;
 const defaultRequired=!meta.optional&&!['notes','stage','currencyAmount'].includes(key)&&!f.readonly;
 const required=f.required??defaultRequired;
 const attrs=`id="${fieldId}" data-group="${gi}" data-row="${ri}" data-field="${key}" data-model="${model}" ${required&&!f.readonly?'required':''} ${f.readonly?'readonly tabindex="-1"':''}`;
 let input='',help='';
 if(value===undefined)value=meta.type==='Boolean'?false:'';
 if(f.readonly){
  if(key==='receiptStatus'&&!value)value='NOT_RECEIVED';
  input=`<input ${attrs} value="${esc(enumFa[value]||value)}" aria-describedby="${fieldId}-binding" placeholder="محاسبه در سرور">`;
  help=f.computed?'پیش‌نمایش محاسبه در مرورگر؛ نتیجهٔ معتبر و نهایی فقط در سرور.':'سیستمی؛ تغییر وضعیت در محصول نهایی فقط با فرمان مجاز.';
 }else if(source){
  const searchable=/PartyId$/.test(key)||['businessCardId','partyId','packageTypeCode'].includes(key);
  if(searchable){
   const selected=source.values.find(x=>x.value===String(value));
   input=`<input type="text" ${attrs} list="${fieldId}-options" data-lookup="true" autocomplete="off" value="${esc(selected?.label||value)}" placeholder="جست‌وجو و انتخاب از فهرست…" aria-describedby="${fieldId}-binding"><datalist id="${fieldId}-options">${source.values.map(x=>`<option value="${esc(x.label)}"></option>`).join('')}</datalist>`;
  }else{
   input=`<select ${attrs} aria-describedby="${fieldId}-binding"><option value="">انتخاب کنید${source.values.length?'':' — هنوز داده‌ای ثبت نشده'}</option>${source.values.map(x=>`<option value="${esc(x.value)}"${String(value)===x.value?' selected':''}>${esc(x.label)}</option>`).join('')}</select>`;
  }
  help='گزینه‌های نمایش‌داده‌شده آزمایشی هستند؛ منبع واقعی در زیر مشخص است.';
 }else if(meta.type==='Boolean'){
  input=`<div class="check-wrap"><input type="checkbox" ${attrs} ${value?'checked':''} aria-describedby="${fieldId}-binding"><span class="muted">بله</span></div>`;
 }else if(key==='notes'||key==='resultNotes'||key==='description'){
  input=`<textarea ${attrs} maxlength="${meta.maxLength||4000}" aria-describedby="${fieldId}-binding">${esc(value)}</textarea>`;
 }else if(meta.date){
  input=`<input type="date" ${attrs} value="${esc(value)}" dir="ltr" aria-describedby="${fieldId}-binding ${fieldId}-jalali"><output class="date-preview" id="${fieldId}-jalali">${value?'شمسی: '+esc(jalali(value)):'معادل شمسی پس از انتخاب تاریخ'}</output>`;
  help='ورود میلادی با تقویم مرورگر؛ ذخیرهٔ date میلادی، نمایش شمسی با Intl.';
 }else{
  const numeric=meta.type==='Decimal'||meta.type==='Int';
  input=`<input type="text" ${attrs} value="${esc(value)}" ${numeric?'inputmode="decimal" data-numeric="'+meta.type+'" data-scale="'+(meta.scale??0)+'" data-precision="'+(meta.precision??10)+'" dir="ltr"':''} ${meta.maxLength?'maxlength="'+meta.maxLength+'"':''} placeholder="${esc(f.placeholder|| (key==='scopeId'?'UUID موجودیت هدف':numeric?'۰':'وارد کنید'))}" aria-describedby="${fieldId}-binding">`;
  if(numeric)help=meta.type==='Decimal'?`عدد دقیق، حداکثر ${fa(meta.scale||6)} رقم اعشار؛ مبلغ با ارز، مقدار با واحد مربوط.`:'عدد صحیح غیرمنفی.';
  if(key==='scopeId')help='پیش‌نمونه: UUID دستی؛ در محصول نهایی جست‌وجوی موجودیت متناسب با scopeType لازم است.';
 }
 const caption=binding(model,key,source?.source||(f.computed?formulas(model,key):''));
 return `<div class="field${f.wide||['notes','resultNotes','description'].includes(key)?' wide':''}"><label for="${fieldId}">${esc(label)}${required&&!f.readonly?'<span class="required" aria-label="الزامی">*</span>':''}${f.readonly?' <span class="badge">'+(f.computed?'محاسباتی':'سیستمی')+'</span>':''}</label>${input}${help?`<div class="help">${esc(help)}</div>`:''}<span id="${fieldId}-binding">${caption}</span></div>`;
}
function formulas(model,key){
 return ({lineTotal:'order_registration_lines.quantity × unitPrice',allocatedAmount:'SUM(fx_allocations.allocatedAmount)',receivedAmount:'SUM(fx_receipts.receivedAmount)',grossRate:'fx_files.paidRialAmount ÷ allocatedAmount؛ مخرج صفر → بدون نرخ',netRate:'fx_files.paidRialAmount ÷ receivedAmount؛ مخرج صفر → بدون نرخ',dischargedAmount:'SUM(commitment_discharges.amount) با کنترل برابری currencyId'})[key]||'';
}
function renderGroup(g,gi){
 const data=state.pages[page.id]?.[gi]||[];
 let content;
 if(g.repeat){content=`<div data-rows="${gi}">${data.length?data.map((v,ri)=>rowTemplate(g,gi,ri,v)).join(''):'<div class="empty">هنوز ردیفی ثبت نشده؛ برای بررسی فرم یک ردیف اضافه کنید.</div>'}</div><button type="button" class="button small" data-add="${gi}">＋ افزودن ${esc(g.title)}</button>`;}
 else content=`<div class="fields">${g.fields.map(f=>control(g.model,f,gi,0,data[0]?.[f.key])).join('')}</div>`;
 const head=`<h2>${esc(g.title)}</h2>${g.description?`<p>${esc(g.description)}</p>`:''}`;
 return g.advanced?`<details class="card"><summary>${esc(g.title)} <span class="badge">اختیاری</span></summary><div class="card-body">${content}</div></details>`:`<section class="card" aria-label="${esc(g.title)}"><header class="card-head"><div>${head}</div><span class="section-index">${String(gi+1).padStart(2,'0')}</span></header><div class="card-body">${content}</div></section>`;
}
function projectTitle(){return state.pages.project?.[0]?.[0]?.titleFa||'پروندهٔ جدید';}
function shell(content){
 const overview=id==='overview',mapping=id==='mapping';
 const nav=pages.map(p=>`<a href="${p.file}" class="nav-link${id===p.id?' active':''}" ${id===p.id?'aria-current="page"':''}><span class="nav-number">${p.number}</span>${esc(p.short)}</a>`);
 $('#app').innerHTML=`<header class="shell"><a class="brand" href="index.html"><img src="assets/hadron-logo-red.svg" alt="لوگوی رسمی هادرون"><div><strong dir="ltr">Hadron ERP System</strong><small>فضای کاری بازرگانی</small></div></a><div class="shell-actions"><small>نسخهٔ بررسی طراحی · آفلاین</small><span class="badge">دادهٔ آزمایشی</span><button class="button small" id="export-top">خروجی JSON</button></div></header><div class="layout"><aside class="sidebar"><strong class="workspace">بازرگانی</strong><a href="index.html" class="nav-link${overview?' active':''}" ${overview?'aria-current="page"':''}>نمای کلی مسیر</a><div class="nav-caption">مراحل عملیاتی</div>${nav.slice(0,6).join('')}<div class="nav-caption">بخش‌های مرتبط</div>${nav.slice(6).join('')}<a href="data-sources.html" class="nav-link${mapping?' active':''}" ${mapping?'aria-current="page"':''}>جدول‌ها و منابع داده</a><div class="side-note">این نسخه به API متصل نیست. ذخیره و قفل‌ها برای بررسی تعامل‌اند؛ کنترل مجوز و قواعد نهایی باید در سرور اجرا شود.<br><br>مرجع مراحل: <code>${referenceCommit.slice(0,7)}</code></div></aside><main class="main" id="main"><div class="breadcrumb"><a href="index.html">بازرگانی</a><span> / </span><span>${esc(page?.title||(mapping?'منابع داده':'نمای کلی'))}</span></div>${content}<footer class="footer"><span>Hadron ERP System · طراحی با ردیابی منبع داده</span><a href="data-sources.html">۳۳ مدل پیشنهادی · بانک اطلاعاتی هنوز مهاجرت نکرده</a></footer></main></div>`;
 $('#export-top').addEventListener('click',exportJSON);
}
function rail(){return `<ol class="rail" aria-label="مراحل اصلی بازرگانی">${pages.slice(0,6).map(p=>`<li><a href="${p.file}" class="${id===p.id?'active':''}" ${id===p.id?'aria-current="step"':''}><b>${p.number}</b><span>${esc(p.short)}<small>${p.id==='project'?'اطلاعات پایه':state.project.eightDigitCode?'پیش‌نمایش در دسترس':'نیازمند کد هشت‌رقمی'}</small></span></a></li>`).join('')}</ol>`;}
function summary(){
 const p=state.project;
 const info=[['شمارهٔ داخلی',p.projectNumber||'پس از ثبت، توسط سرور', 'projectNumber'],['وضعیت پرونده',p.status==='DRAFT'?'پیش‌نویس پرونده (DRAFT)':'پیش‌نویس (پیش‌فرض DRAFT)','status'],['کد هشت‌رقمی',p.eightDigitCode||'هنوز صادر نشده','eightDigitCode'],['تاریخ صدور کد',jalali(p.codeIssuedAt)||'—','codeIssuedAt'],['تاریخ انقضای کد',jalali(p.codeExpiresAt)||'از سرویس؛ بدون فرمول حدسی','codeExpiresAt'],['تاریخ ثبت سیستمی',jalali(p.registrationDate)||'پس از ثبت توسط سرور','registrationDate'],['ثبت‌کننده',p.createdByUserId?'کاربر نمونهٔ نشست':'از نشست احراز هویت','createdByUserId'],['قفل اطلاعات پایه',p.isLocked?'قفل شده':'قابل ویرایش','isLocked']];
 return `<aside class="summary"><section class="card"><header class="card-head"><h2>شناسنامهٔ پرونده</h2><span class="badge${p.eightDigitCode?' green':''}">${p.eightDigitCode?'کد صادر شده':'پیش‌نویس'}</span></header><div class="card-body"><span class="context-label">موضوع پرونده</span><div class="context-value" id="summary-title">${esc(projectTitle())}</div>${binding('TradeProject','titleFa')}<dl>${info.map(([l,v,k])=>`<dt>${l}</dt><dd>${esc(v)}${binding('TradeProject',k)}</dd>`).join('')}</dl><div class="summary-foot">تاریخ‌ها و انجام‌دهنده از سرور/نشست خوانده می‌شوند؛ شمارهٔ نمونهٔ مرورگر شمارهٔ واقعی نیست.</div></div></section><section class="card"><div class="card-body"><h3>منطق این مرحله</h3><p class="attachment-note">مرحلهٔ دامنه: <code>${page.stage}</code></p>${binding('ProjectStage','stage')}${binding('ProjectStage','status')}<p class="attachment-note">حرکت بین صفحات وضعیت پرونده را تغییر نمی‌دهد. بیمه، رفع تعهد و هزینه‌ها حذف نشده‌اند؛ از منوی مرتبط قابل دسترسی‌اند.</p></div></section>${id==='project'?`<button type="button" class="button" style="width:100%" id="simulate" ${p.isLocked?'disabled':''}>شبیه‌سازی صدور کد و قفل</button><p class="attachment-note">فقط ابزار بررسی رفتار؛ فرمان واقعی صدور کد نیست.</p>`:''}</aside>`;
}
function gate(){
 if(id==='project'&&state.project.isLocked)return `<div class="notice warn"><div><strong>اطلاعات پایه پس از صدور کد قفل است.</strong><br>این نمونه اجازهٔ ویرایش مرحلهٔ اول را نمی‌دهد. اصلاح واقعی نیازمند مجوز و ثبت ممیزی در سرور است.</div></div>`;
 if(id!=='project'&&!state.project.eightDigitCode)return `<div class="notice warn" id="gate"><div><strong>این مرحله هنوز عملیاتی نیست.</strong><br>تا پیش از کد هشت‌رقمی فقط مرحلهٔ اول باز است. برای بررسی طراحی می‌توانید پیش‌نمایش را فعال کنید؛ این کار وضعیت یا مجوز واقعی را تغییر نمی‌دهد.</div><div class="gate-actions"><button type="button" class="button small" id="enable-preview">بررسی فرم در حالت پیش‌نمایش</button></div></div>`;
 return '';
}
function renderPage(){
 shell(`<header class="page-head"><div><div class="eyebrow">TRADING / ${esc(page.stage)}</div><h1>${esc(page.title)}</h1><p>${esc(page.subtitle)}</p></div><div class="action-group"><a class="button small" href="data-sources.html">راهنمای اتصال فیلدها</a></div></header>${rail()}${!storageAvailable?'<div class="notice error">ذخیرهٔ مرورگر مسدود یا دادهٔ قبلی نامعتبر است. برای نگهداری اطلاعات خروجی JSON بگیرید.</div>':''}${gate()}<div class="content-grid"><form id="stage-form" novalidate><div id="validation" role="alert" tabindex="-1"></div><fieldset class="locked-fieldset" id="form-fields" ${id==='project'?state.project.isLocked?'disabled':'':!state.project.eightDigitCode?'disabled':''}>${page.groups.map(renderGroup).join('')}</fieldset><div id="derived-totals" aria-live="polite"></div><div class="card"><div class="card-body"><h3>هزینه و مدارک مرتبط</h3><div class="sub-links"><a href="09-costs.html" class="button small">هزینه‌های مرحله</a><a href="07-insurance.html" class="button small">بیمه و بازرسی</a><a href="data-sources.html#ProjectDocument" class="button small">قرارداد مدارک</a></div><p class="attachment-note">آپلود فایل واقعی در این نمونه انجام نمی‌شود. فایل نهایی باید با سرویس اسناد و storageKey ذخیره شود.</p>${binding('ProjectDocument','storageKey')}${binding('CostType','isMandatory')}${binding('CostType','stage')}</div></div><div class="sticky-actions"><div><span class="save-caption" id="save-state">${state.savedAt?'آخرین ذخیرهٔ نمونه: '+esc(new Intl.DateTimeFormat('fa-IR',{dateStyle:'short',timeStyle:'short'}).format(new Date(state.savedAt))):'هنوز ذخیره نشده'}</span><br><span class="save-caption">فقط در مرورگر شما؛ نه دیتابیس</span></div><div class="action-group"><button type="button" class="button" id="save-draft" >ذخیرهٔ پیش‌نویس</button><button type="submit" class="button primary" id="save-validate">${id==='project'?'بررسی و ثبت نمونهٔ پرونده':'بررسی و ذخیرهٔ نمونه'}</button></div></div></form>${summary()}</div>${id==='project'?simulationDialog():''}`);
 if(id==='project'&&state.project.isLocked){$('#save-draft').disabled=true;$('#save-validate').disabled=true;}
 if(id!=='project'&&!state.project.eightDigitCode){$('#save-draft').disabled=true;$('#save-validate').disabled=true;}
 $('#enable-preview')?.addEventListener('click',()=>{$('#form-fields').disabled=false;$('#save-draft').disabled=false;$('#save-validate').disabled=false;$('#gate').innerHTML='<div><strong>حالت بررسی طراحی فعال است — نه مجوز عملیاتی.</strong><br>ذخیره صرفاً یک پیش‌نویس آزمایشی در مرورگر است.</div>';});
 $('#stage-form').addEventListener('input',e=>{dirty=true;const el=e.target;el.setCustomValidity?.('');if(el.dataset.field==='titleFa')$('#summary-title').textContent=el.value||'پروندهٔ جدید';if(el.type==='date')$('#'+el.id+'-jalali').textContent=el.value?'شمسی: '+jalali(el.value):'معادل شمسی پس از انتخاب تاریخ';collect();calculate();});
 $('#stage-form').addEventListener('change',()=>{collect();calculate();});
 $('#stage-form').addEventListener('click',e=>{const add=e.target.closest('[data-add]'),remove=e.target.closest('[data-remove]');if(add){collect();const gi=Number(add.dataset.add),rows=state.pages[id][gi];if(rows.length>=200){toast('حداکثر ۲۰۰ ردیف در نمونه.');return;}rows.push({});refreshRows(gi);dirty=true;}if(remove){collect();const [gi,ri]=remove.dataset.remove.split(':').map(Number);state.pages[id][gi].splice(ri,1);refreshRows(gi);dirty=true;calculate();}});
 $('#save-draft').addEventListener('click',()=>{collect();calculate();const ok=persist();dirty=!ok;if(ok){$('#save-state').textContent='پیش‌نویس در مرورگر ذخیره شد';toast('پیش‌نویس آزمایشی ذخیره شد؛ هیچ داده‌ای به سرور ارسال نشد.');}});
 $('#stage-form').addEventListener('submit',e=>{e.preventDefault();submit();});
 $('#simulate')?.addEventListener('click',()=>{collect();if(!state.project.projectNumber){toast('ابتدا اطلاعات الزامی را وارد و پروندهٔ نمونه را ثبت کنید.');return;}$('#code-dialog').showModal();});
 $('#close-code')?.addEventListener('click',()=>$('#code-dialog').close());
 $('#code-form')?.addEventListener('submit',e=>{e.preventDefault();const c=normalizeDigits($('#code-value').value);$('#code-value').value=c;if(!/^\d{8}$/.test(c)){toast('کد باید دقیقاً هشت رقم باشد.');return;}if($('#code-expiry').value<$('#code-issued').value){toast('انقضا نمی‌تواند قبل از صدور باشد.');return;}state.project={...state.project,eightDigitCode:c,codeIssuedAt:$('#code-issued').value,codeExpiresAt:$('#code-expiry').value,isLocked:true};const ok=persist();dirty=!ok;renderPage();toast('صدور کد شبیه‌سازی شد؛ مرحلهٔ اول قفل و پیش‌نمایش مراحل دیگر در دسترس است.');});
 collect();calculate();
}
function collect(){
 if(!page)return;
 state.pages[id]=page.groups.map((g,gi)=>{
  const rows=g.repeat?$$(`[data-rows="${gi}"] > .repeat-row`).map(x=>Number(x.dataset.row)):[0];
  return rows.map(ri=>{
   const r={};for(const f of g.fields){const el=$(`#f-${gi}-${ri}-${f.key}`);if(!el)continue;r[f.key]=el.type==='checkbox'?el.checked:el.dataset.lookup?(lookup(g.model,f.key).values.find(x=>x.label===el.value)?.value||el.value):el.dataset.numeric?normalizeDigits(el.value).trim():el.value;}
   return r;
  });
 });
}
function refreshRows(gi){const g=page.groups[gi],rows=state.pages[id][gi];$(`[data-rows="${gi}"]`).innerHTML=rows.length?rows.map((r,ri)=>rowTemplate(g,gi,ri,r)).join(''):'<div class="empty">هیچ ردیفی اضافه نشده است.</div>';}
function setComputed(model,key,value){
 for(let gi=0;gi<page.groups.length;gi++){const g=page.groups[gi];if(g.model!==model)continue;const rows=state.pages[id][gi];rows.forEach((r,ri)=>{const el=$(`#f-${gi}-${ri}-${key}`);if(el){r[key]=value;el.value=enumFa[value]||value;}});}
}
function totalsCard(items){
 const target=$('#derived-totals');
 if(target)target.innerHTML=`<section class="card"><header class="card-head"><h2>جمع‌های زندهٔ نمونه</h2><span class="badge">محاسبهٔ دقیق</span></header><div class="card-body"><div class="fields">${items.map(([title,value,source])=>`<div class="field"><h3>${esc(title)}</h3><div dir="ltr">${esc(value)}</div><span class="binding"><code>${esc(source)}</code></span></div>`).join('')}</div><p class="attachment-note">مبلغ‌ها رشتهٔ اعشاری‌اند؛ نتیجهٔ نهایی باید با decimal در سرور بازبینی شود. تبدیل ارز خودکار نداریم.</p></div></section>`;
}
function calculate(){
 if(!page)return;
 if(id==='orders'){
  const gi=page.groups.findIndex(g=>g.model==='OrderRegistrationLine');
  for(const [ri,r] of (state.pages[id]?.[gi]||[]).entries()){
   const q=decimal(r.quantity),u=decimal(r.unitPrice),val=q!==null&&u!==null?formatted(q*u/SCALE):'';
   r.lineTotal=val;$(`#f-${gi}-${ri}-lineTotal`).value=val;
  }
  totalsCard([['جمع اقلام سفارش',formatted(sum(valuesOf('OrderRegistrationLine'),'lineTotal')),'SUM(order_registration_lines.lineTotal) → order_registrations.currencyId']]);
 }
 if(id==='fx'){
  const a=sum(valuesOf('FxAllocation'),'allocatedAmount'),r=sum(valuesOf('FxReceipt'),'receivedAmount'),paid=decimal(valuesOf('FxFile')[0]?.paidRialAmount||'0');
  setComputed('FxFile','allocatedAmount',formatted(a));setComputed('FxFile','receivedAmount',formatted(r));
  setComputed('FxFile','grossRate',a>0n&&paid!==null?formatted(paid*100000000n/a,8):'');setComputed('FxFile','netRate',r>0n&&paid!==null?formatted(paid*100000000n/r,8):'');
  setComputed('FxFile','receiptStatus',r===0n?'NOT_RECEIVED':r<a?'PARTIAL':'RECEIVED');
 }
 if(id==='costs'){
  const cost=valuesOf('TradeCost')[0]||{},allocation=valuesOf('CostAllocation'),payments=valuesOf('CostPayment');
  const mismatch=[...allocation,...payments].some(r=>r.currencyId&&r.currencyId!==cost.currencyId);
  let net=0n;for(const r of payments){const n=decimal(r.amount||'0');if(n!==null)net+=r.isReturn?-n:n;}
  totalsCard([
   ['مبلغ هزینه',cost.amount||'—','trade_costs.amount / currencyId'],
   ['جمع سهم بخش‌ها',mismatch?'ارزها یکسان نیستند':formatted(sum(allocation,'allocatedAmount')),'SUM(cost_allocations.allocatedAmount) / currencyId'],
   ['جمع درصدها',formatted(sum(allocation,'allocationPercent'))+' %','SUM(cost_allocations.allocationPercent)'],
   ['خالص پرداخت با کسر برگشت',mismatch?'ارزها یکسان نیستند':formatted(net),'SUM(cost_payments.amount با علامت isReturn) / currencyId'],
  ]);
 }
 if(id==='commitments'){
  const currency=valuesOf('Commitment')[0]?.currencyId,rows=valuesOf('CommitmentDischarge');
  const mismatch=rows.some(r=>r.currencyId&&r.currencyId!==currency);
  setComputed('Commitment','dischargedAmount',mismatch?'':formatted(sum(rows,'amount')));
 }
}
function validate(){
 const errors=[];
 for(const el of $$('input,select,textarea',$('#form-fields'))){
  if(el.readOnly||el.disabled)continue;
  if(el.required&&!el.value.trim()){errors.push([el,'این فیلد الزامی است.']);continue;}
  if(el.dataset.numeric&&el.value){const v=decimal(el.value);if(v===null||v<0n||el.dataset.numeric==='Int'&&!/^\d+$/.test(normalizeDigits(el.value)))errors.push([el,'عدد معتبر غیرمنفی وارد کنید؛ حداکثر ۶ رقم اعشار.']);}
  if(el.dataset.lookup&&el.value&&!lookup(el.dataset.model,el.dataset.field).values.some(x=>x.label===el.value))errors.push([el,'یک گزینهٔ معتبر از فهرست انتخاب کنید؛ متن آزاد شناسهٔ مرجع نیست.']);
  if(el.dataset.numeric&&el.value){
   const clean=normalizeDigits(el.value),[whole,fraction='']=clean.split('.');
   if(fraction.length>Number(el.dataset.scale)||whole.replace(/^0+/,'').length>Number(el.dataset.precision)-Number(el.dataset.scale))errors.push([el,'تعداد ارقام با precision/scale مدل داده سازگار نیست.']);
   if(el.dataset.numeric==='Int'&&/^\d+$/.test(clean)&&BigInt(clean)>2147483647n)errors.push([el,'عدد صحیح خارج از محدودهٔ Int است.']);
  }
  if(!el.checkValidity())errors.push([el,'قالب یا طول مقدار معتبر نیست.']);
 }
 // Date order is validated without inventing operational transitions.
 for(let gi=0;gi<page.groups.length;gi++){
  for(const [ri,r] of (state.pages[id][gi]||[]).entries()){
   for(const [start,end] of [['estimatedDeparture','estimatedArrival'],['actualDeparture','actualArrival'],['issueDate','expiryDate'],['startDate','expiryDate'],['startedAt','dueDate']]){
    if(r[start]&&r[end]&&r[end]<r[start])errors.push([$(`#f-${gi}-${ri}-${end}`),'تاریخ پایان نمی‌تواند قبل از شروع باشد.']);
   }
  }
 }
 if(id==='commitments'){
  const c=valuesOf('Commitment')[0]?.currencyId;
  const gi=page.groups.findIndex(g=>g.model==='CommitmentDischarge');
  valuesOf('CommitmentDischarge').forEach((r,i)=>{if(r.currencyId!==c)errors.push([$(`#f-${gi}-${i}-currencyId`),'ارز تسویه باید با ارز تعهد یکسان باشد؛ تبدیل ارز خودکار نداریم.']);});
 }
 if(id==='costs'){
  const rows=valuesOf('CostAllocation'),cost=valuesOf('TradeCost')[0]||{};
  if(rows.length&&sum(rows,'allocationPercent')!==100n*SCALE)errors.push([null,'جمع درصد تخصیص بخش‌ها باید دقیقاً ۱۰۰ باشد.']);
  if(rows.length&&sum(rows,'allocatedAmount')!==decimal(cost.amount||'0'))errors.push([null,'جمع مبلغ تخصیص باید با مبلغ هزینه برابر باشد.']);
  if([...rows,...valuesOf('CostPayment')].some(r=>r.currencyId&&r.currencyId!==cost.currencyId))errors.push([null,'ارز سهم‌ها و پرداخت‌ها باید با ارز هزینه برابر باشد؛ بدون تبدیل خودکار.']);
 }
 return errors;
}
function submit(){
 collect();calculate();const errors=validate(),v=$('#validation');
 if(errors.length){v.innerHTML=`<div class="notice error"><div><strong>اطلاعات نیاز به اصلاح دارد (${fa(errors.length)} مورد)</strong><ul>${errors.slice(0,8).map(([el,msg])=>`<li>${el?esc(labels[el.dataset.field]||el.dataset.field)+': ':''}${esc(msg)}</li>`).join('')}</ul></div></div>`;for(const [el,msg]of errors)el?.setCustomValidity(msg);for(const d of $$('details', $('#stage-form')))if(errors.some(([el])=>el&&d.contains(el)))d.open=true;v.focus();return;}
 if(id==='project'&&!state.project.projectNumber){const now=new Date().toISOString();state.project={...state.project,id:globalThis.crypto?.randomUUID?.()||'demo-project',projectNumber:'DEMO-TRD-'+Date.now().toString().slice(-6),registrationDate:nowDate(),createdAtUtc:now,createdByUserId:uid(40),status:'DRAFT',isLocked:false};}
 const ok=persist();dirty=!ok;
 if(ok){renderPage();$('#validation').innerHTML='<div class="notice success">اطلاعات نمونه اعتبارسنجی و در مرورگر ذخیره شد. ثبت واقعی، تراکنش، ممیزی و مجوز سرور هنوز اجرا نشده‌اند.</div>';toast('نمونه ذخیره شد؛ API فراخوانی نشد.');}
}
function simulationDialog(){return `<dialog id="code-dialog" aria-labelledby="code-title"><h2 id="code-title">شبیه‌سازی پاسخ سرویس صدور کد</h2><p>این ورودی‌ها فقط برای بررسی قفل هستند. در محصول نهایی، صدور، انقضا، یکتایی و مجوز باید توسط سرویس معتبر کنترل شوند. هیچ فرمولی برای انقضا فرض نشده است.</p><form id="code-form"><div class="fields"><div class="field wide"><label for="code-value">کد هشت‌رقمی آزمایشی</label><input id="code-value" required inputmode="numeric" maxlength="8" dir="ltr">${binding('TradeProject','eightDigitCode')}</div><div class="field"><label for="code-issued">تاریخ صدور فرضی</label><input type="date" id="code-issued" required value="${nowDate()}">${binding('TradeProject','codeIssuedAt')}</div><div class="field"><label for="code-expiry">تاریخ انقضای فرضی از سرویس</label><input type="date" id="code-expiry" required>${binding('TradeProject','codeExpiresAt')}</div></div><div class="action-group"><button type="submit" class="button primary">اعمال پاسخ فرضی و قفل</button><button type="button" id="close-code" class="button">انصراف</button></div></form></dialog>`;}
function exportJSON(){if(page)collect();download('trading-review.json',JSON.stringify(state,null,2),'application/json');}
function download(name,text,type){const url=URL.createObjectURL(new Blob([text],{type}));const a=document.createElement('a');a.href=url;a.download=name;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),2000);}
function overview(){
 const cards=pages.slice(0,6).map(p=>{const ms=[...new Set(p.groups.map(g=>g.model))];return `<a class="stage-card" href="${p.file}"><span class="num">${p.number.padStart(2,'۰')}</span><h2>${esc(p.title)}</h2><p>${esc(p.subtitle)}</p><span class="binding">جداول این صفحه: ${ms.map(m=>`<code>${models[m].table}</code>`).join(' ')}</span><span class="link">ورود به مرحله ←</span></a>`;}).join('');
 shell(`<header class="page-head"><div><div class="eyebrow">TRADING WORKSPACE</div><h1>از پرونده تا ترخیص، در یک مسیر روشن</h1><p>شش گام اصلی مرجع، با اتصال مشخص به مدل دادهٔ پیشنهادی بازرگانی.</p></div><span class="badge green">طرح تعاملی · بدون وابستگی به شبکه</span></header><section class="intro"><div><h2>${esc(projectTitle())}</h2><p>شروع از اطلاعات پایه؛ سپس ثبت سفارش، ارز، محموله، مسیر حمل و گمرک. بیمه و بازرسی، رفع تعهد و هزینه‌ها در دسترس‌اند و داخل هیچ مرحله‌ای گم نشده‌اند.</p>${binding('TradeProject','titleFa')}${binding('TradeProject','currentStage','ناوبری وضعیت را تغییر نمی‌دهد؛ currentStage فقط از پاسخ سرویس معتبر می‌آید.')}</div><a href="01-project.html" class="button primary">${state.project.projectNumber?'ادامهٔ بررسی پرونده':'ایجاد پروژه جدید'} ←</a></section><div class="notice"><div>این بسته یک طرح تعاملی قابل بررسی است؛ API، احراز هویت و بانک اطلاعاتی متصل نیست. صفحات بعد از مرحلهٔ اول پیشنهاد اولیه‌اند؛ تنها فیلدهای مرحلهٔ اول از تصاویر شما استخراج شده‌اند. برچسب زیر هر ورودی محل ذخیره و منبع گزینه‌های آن را مشخص می‌کند.</div></div>${rail()}<div class="stage-cards">${cards}</div><h2 style="margin-top:24px">بخش‌های مرتبط</h2><div class="sub-links">${pages.slice(6).map(p=>`<a class="button" href="${p.file}">${p.title} ↗</a>`).join('')}<a class="button" href="data-sources.html">مرجع ۳۳ جدول ↗</a></div><section class="card"><header class="card-head"><div><h2>انتقال دادهٔ آزمایشی بین فایل‌ها یا دستگاه‌ها</h2><p>اگر مرورگر برای file:// فضای ذخیرهٔ جدا بدهد، از خروجی/ورودی JSON استفاده کنید یا پوشه را با HTTP باز کنید.</p></div></header><div class="card-body"><div class="action-group"><button type="button" class="button" id="export-local">دریافت JSON</button><label class="button" for="import-file">ورود پشتیبان نمونه</label><input id="import-file" type="file" accept=".json,application/json" aria-label="فایل JSON پیش‌نویس نمونه"><button type="button" class="button quiet" id="reset-local">پاک‌کردن دادهٔ آزمایشی این بسته</button></div></div></section>`);
 $('#export-local').addEventListener('click',exportJSON);
 $('#import-file').addEventListener('change',async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>2000000)throw Error('فایل بزرگ‌تر از حد مجاز ۲ مگابایت است.');const imported=validState(JSON.parse(await f.text()));if(!confirm('دادهٔ نمونهٔ فعلی با این فایل جایگزین شود؟'))return;state=imported;const ok=persist();overview();toast(ok?'پشتیبان نمونه وارد شد.':'پشتیبان فقط در حافظه وارد شد؛ ذخیرهٔ مرورگر مسدود است.');}catch(err){toast('ورود فایل ناموفق: '+err.message);}});
 $('#reset-local').addEventListener('click',()=>{if(!confirm('فقط اطلاعات آزمایشی این بسته حذف شود؟ این عملیات هیچ جدول واقعی را تغییر نمی‌دهد.'))return;state=fresh();persist();overview();toast('دادهٔ آزمایشی پاک شد.');});
}
function mapping(){
 const notes=`<ul class="mapping-notes"><li>شمارهٔ داخلی سیستمی: <code>trade_projects.projectNumber</code>؛ با کد هشت‌رقمی یکی نیست.</li><li>شروع / ارجاع: <code>project_stages.startedAt</code>؛ تاریخ ثبت: <code>trade_projects.registrationDate</code> سیستمی و فقط‌خواندنی.</li><li>اولویت از فرم حذف شده است. <code>trade_projects.priorityCode</code> هنوز در اسکیما وجود دارد؛ حذف از مدل نیازمند تأیید تغییر اسکیماست.</li><li>وضعیت اولیهٔ مدل DRAFT است. عبارت «در انتظار ثبت سفارش» نمونه با WAITING_ORDER یکی است؛ این نمونه بدون فرمان انتقال، آن را جای DRAFT نمی‌گذارد.</li><li>بانک از فرم سفارش حذف شده، ولی <code>order_registrations.bankPartyId</code> هنوز در مدل وجود دارد؛ محل انتخاب این طرح <code>fx_files.bankPartyId</code> است.</li><li>مرحلهٔ «مسیر حمل» و «محموله» هر دو SHIPPING هستند؛ بیمه، رفع تعهد و هزینه‌ها مراحل دامنهٔ مستقل باقی می‌مانند.</li><li>درخواست/ثبت خودکار سفارش هنوز قرارداد و فرمان API ندارد؛ نمونه موفقیت صوری برای آن نمایش نمی‌دهد.</li><li>یادداشت کلی پرونده و یادداشت مرحله دو دادهٔ متفاوت‌اند. مسئول پرونده با انجام‌دهندهٔ سیستمی و مسئول مرحله یکی فرض نشده است.</li><li>فهرست مشترک کاربران، ارزها و واحدها بیرون از ۳۳ مدل است؛ ایجاد جدول تکراری پیشنهاد نشده. نام نمایشی کاربران از قرارداد مرکزی هویت خوانده خواهد شد.</li><li>فورواردر، طرفین و بسته‌بندی انتخاب‌گر جست‌وجویی آفلاین (datalist) دارند؛ در محصول نهایی همین تعامل باید به جست‌وجوی صفحه‌بندی‌شدهٔ API وصل شود.</li><li>این نمونه در هر صفحه یک رکورد اصلی انتخاب‌شده و ردیف‌های فرزند دارد؛ مدیریت چند سفارش/پروندهٔ ارزی/محموله، انتخاب محموله برای مسیر و ترخیص، و اتصال FK واقعی، کار اتصال به محصول است.</li><li>درصد و مبلغ با BigInt ثابت ۶ رقم اعشار برای پیش‌نمایش محاسبه می‌شود؛ مالیات، جمع ارزهای مختلف یا نرخ تبدیل حدسی نداریم. نرخ ارزی ۸ رقم اعشار است.</li><li>قفل و gate در مرورگر صرفاً نمایشی‌اند. سرور باید permission، optimistic concurrency، CHECK، یکتایی، تراکنش و append-only audit را کنترل کند.</li></ul>`;
 const modelCards=Object.entries(models).map(([name,m])=>`<details class="card schema-model" id="${name}" data-search="${esc(name+' '+m.table+' '+(names[name]||'')+' '+Object.keys(m.fields).join(' '))}"><summary>${esc(names[name]||name)} · <code>${m.table}</code> <span class="badge">${fa(Object.keys(m.fields).length)} فیلد</span></summary><div class="schema-wrap"><table class="schema-table"><thead><tr><th scope="col">جدول و فیلد</th><th scope="col">نوع</th><th scope="col">شرح فارسی در مدل</th><th scope="col">وضعیت در طرح</th></tr></thead><tbody>${Object.entries(m.fields).map(([key,f])=>{const used=pages.flatMap(p=>p.groups.filter(g=>g.model===name&&g.fields.some(x=>x.key===key)).map(()=>p));return `<tr><td><code>${m.table}.${key}</code></td><td><code>${f.type}${f.optional?'?':''}</code></td><td>${esc(f.label)}</td><td>${used.length?[...new Set(used.map(p=>p.id))].map(pid=>{const p=pages.find(x=>x.id===pid);return `<a href="${p.file}">${esc(p.short)}</a>`;}).join(' · '):'در مرجع مدل؛ فرم مدیریتی مستقل هنوز ساخته نشده'}</td></tr>`;}).join('')}</tbody></table></div></details>`).join('');
 shell(`<header class="page-head"><div><div class="eyebrow">DATA LINEAGE / CONTRACT</div><h1>هر ورودی، یک منبع مشخص</h1><p>نام جدول فیزیکی + نام فیلد Prisma؛ این اسکیما هنوز به دیتابیس اجرایی مهاجرت نکرده است.</p></div><a href="field-map.json" download class="button small">دریافت نگاشت فرم‌ها</a></header><section class="card"><header class="card-head"><h2>اختلاف‌ها و تصمیم‌های طراحی</h2></header><div class="card-body">${notes}</div></section><section class="card"><header class="card-head"><h2>قرارداد ذخیرهٔ مرحلهٔ اول</h2></header><div class="card-body"><p class="mapping-notes">یک تراکنش سمت سرور: TradeProject + ProjectStage با stage = PROJECT_SETUP + TradeAuditEvent. عنوان و مسئول طبق تصویر در UI الزامی‌اند، هرچند مسئول در مدل nullable است. اولین ثبت به‌خودی‌خود وضعیت WAITING_ORDER ایجاد نمی‌کند. پس از صدور کد، اطلاعات پایه قفل می‌شود؛ انقضا از پاسخ سرویس می‌آید، نه محاسبهٔ حدسی در فرم.</p>${binding('TradeProject','version')}${binding('ProjectStage','version')}${binding('TradeAuditEvent','action')}</div></section><label for="schema-search">جست‌وجو در ۳۳ جدول پیشنهادی</label><input class="search" id="schema-search" type="search" placeholder="نام جدول، مدل، فیلد یا عنوان فارسی"><p class="attachment-note">فقط فیلتر رابط کاربری؛ در جدول ذخیره نمی‌شود.</p><div id="schema-models">${modelCards}</div>`);
 $('#schema-search').addEventListener('input',e=>{const q=e.target.value.toLowerCase().trim();$$('.schema-model').forEach(d=>{d.hidden=!d.dataset.search.toLowerCase().includes(q);});});
 if(location.hash){const target=document.getElementById(decodeURIComponent(location.hash.slice(1)));if(target?.tagName==='DETAILS'){target.open=true;target.scrollIntoView();}}
}
window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
if(page)renderPage();else if(id==='mapping')mapping();else overview();
})();
