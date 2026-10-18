#!/usr/bin/env python3
"""Build the offline, annotated Trading UI from the proposed Prisma schema."""
from pathlib import Path
import json
import re
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/ui/trading-prototype'
SCHEMA = ROOT / 'docs/modules/trading/data-map/trading.prisma'
text = SCHEMA.read_text()
enums = {n: re.findall(r'^\s{2}(\w+)\s*(?://.*)?$', b, re.M) for n,b in re.findall(r'enum (\w+) \{(.*?)\n\}', text, re.S)}
models = {}
for name, body in re.findall(r'model (\w+) \{(.*?)\n\}', text, re.S):
    fields = {}
    for line in body.splitlines():
        m = re.match(r'\s+(\w+)\s+(\w+)(\??)(\[\])?\s*(.*)', line)
        if not m or m[4] or (m[2] not in enums and m[2] not in ['String','Int','Decimal','DateTime','Boolean','Json']):
            continue
        tail, _, comment = m[5].partition('//')
        length = re.search(r'@db.VarChar\((\d+)\)', tail)
        numeric = re.search(r'@db.Decimal\((\d+),\s*(\d+)\)', tail)
        fields[m[1]] = dict(type=m[2], optional=bool(m[3]), label=comment.strip(), enum=enums.get(m[2]), maxLength=int(length[1]) if length else None, date='@db.Date' in tail and '@db.Timestamptz' not in tail, precision=int(numeric[1]) if numeric else None, scale=int(numeric[2]) if numeric else None)
    models[name] = dict(table=re.search(r'@@map\("(.*?)"\)', body)[1], fields=fields)

# Compact declarative UI specification. Every editable field is checked against Prisma.
def field(key, label=None, **kw):
    return dict(key=key, label=label, **kw)
def group(model, title, fields, **kw):
    return dict(model=model, title=title, fields=[field(x) if isinstance(x,str) else x for x in fields], **kw)

pages = [
 dict(id='project', file='01-project.html', title='ایجاد پروژه جدید', short='پرونده', number='۱', stage='PROJECT_SETUP', subtitle='یک بار ثبت اطلاعات پایه؛ یک پرونده برای تمام مسیر بازرگانی.', groups=[
  group('TradeProject','هویت پرونده', [field('titleFa','موضوع پروژه', required=True, wide=True, placeholder='مثلاً تأمین قطعات شارژر — سفارش پاییز ۱۴۰۵'), field('goodsSummary','خلاصهٔ کالا',wide=True), field('responsibleUserId','مسئول پیگیری',required=True), field('targetDate','مهلت هدف')], description='شمارهٔ پرونده پس از ثبت توسط سرور ساخته می‌شود؛ با کد هشت‌رقمی متفاوت است.'),
  group('ProjectStage','شروع و ارجاع', [field('startedAt','تاریخ شروع / ارجاع',required=True), field('dueDate','مهلت مرحله')], description='تاریخ کاری قابل ورود است؛ تاریخ ثبت سیستمی جدا نگه داشته می‌شود.'),
  group('TradeProject','طرفین تجاری', ['customerPartyId','businessCardId',field('supplierPartyId','تأمین‌کننده (اختیاری)')], description='مشتری و تأمین‌کننده از طرفین مشترک انتخاب می‌شوند؛ کارت بازرگانی موجودیت مستقلی است.'),
  group('TradeProject','یادداشت پرونده', [field('notes','توضیحات کلی',wide=True)], description='اطلاعات تکمیلی، نکات مهم و دستور پیگیری؛ بدون تکرار عنوان پروژه.'),
  group('ProjectStage','جزئیات مرحلهٔ اول', [field('notes','توضیحات مخصوص این مرحله',wide=True)], advanced=True),
  group('TradeProject','عنوان انگلیسی', ['titleEn'], advanced=True),
 ]),
 dict(id='orders',file='02-orders.html',title='ثبت سفارش',short='سفارش',number='۲',stage='ORDER',subtitle='اطلاعات سند از اقلام و مجوزها جداست؛ یک سند، چند قلم کالا.',groups=[
  group('OrderRegistration','مشخصات ثبت سفارش',['orderNumber','registrationDate','currencyId','declaredValueAmount','currencyAmount','incotermCode','paymentMethodCode','notes'],description='بانک و عملیات ارزی در بخش ارز مدیریت می‌شوند. فیلد bankPartyId مدل قدیمی در این فرم تکرار نشده است.'),
  group('OrderRegistrationLine','اقلام سفارش',['goodsTitle','hsCode','quantity','unitId','unitPrice',field('lineTotal',readonly=True,computed=True)],repeat=True),
  group('OrderPermit','مجوزهای سفارش',['permitTypeCode','permitNumber','issuedBy','issueDate','expiryDate'],repeat=True),
 ]),
 dict(id='fx',file='03-fx.html',title='عملیات ارزی',short='ارز و بانک',number='۳',stage='FX',subtitle='پروندهٔ ارزی، تخصیص و دریافت مستقل؛ نرخ‌ها حاصل داده‌اند، نه ورودی دستی.',groups=[
  group('FxFile','پروندهٔ ارزی',['bankPartyId','fxSourceCode','currencyId','requestDate','allocationNumber','allocationNumberDate','paidRialAmount',field('allocatedAmount',readonly=True,computed=True),field('receivedAmount',readonly=True,computed=True),field('grossRate',readonly=True,computed=True),field('netRate',readonly=True,computed=True),field('receiptStatus',readonly=True),'notes']),
  group('FxAllocation','تخصیص‌های ارز',['allocatedAmount','allocationDate','bankReferenceNumber'],repeat=True),
  group('FxReceipt','دریافت‌های ارز',['receivedAmount','receivedAt','bankReferenceNumber'],repeat=True),
  group('Collateral','وثایق و چک‌های بانک',['bankPartyId','collateralTypeCode','instrumentNumber','amount','currencyId','dueDate','heldByBank','heldAt'],repeat=True),
 ]),
 dict(id='shipments',file='04-shipments.html',title='ثبت و مدیریت محموله‌ها',short='محموله',number='۴',stage='SHIPPING',subtitle='محتویات محموله در سطح قلم؛ اتصال کالا به سفارش و پرونده روشن می‌ماند.',groups=[
  group('Shipment','زمان‌بندی محموله',['estimatedDeparture','estimatedArrival','actualDeparture','actualArrival'],description='ذخیرهٔ میلادی؛ نمایش هم‌زمان شمسی. تاریخ‌ها در ابتدای صفحه قرار دارند.'),
  group('Shipment','مشخصات حمل',['transportMode','forwarderPartyId','carrierPartyId','originPortCode','destinationPortCode','packageTypeCode','packageCount','grossWeight','weightUnitId','volume','volumeUnitId','hasInsurance','billOfLadingNumber','notes']),
  group('ShipmentItem','محتویات و کالاها',['goodsTitle','orderRegistrationLineId','quantity','unitId','grossWeight','volume','packageCount','packageTypeCode'],repeat=True),
  group('ShipmentParty','طرف‌های دیگر حمل',['partyId','roleCode','contactName','phone'],repeat=True),
 ]),
 dict(id='legs',file='05-transport.html',title='مراحل حمل',short='مسیر حمل',number='۵',stage='SHIPPING',subtitle='هر مسیر یک ردیف مستقل است؛ انتخاب مرحلهٔ ناوبری به معنی تکمیل عملیات نیست.',groups=[
  group('ShipmentLeg','مسیرهای محموله',['sequence','fromPortCode','toPortCode','transportMode','carrierPartyId','vehicleOrVessel','trackingNumber','estimatedDeparture','estimatedArrival','actualDeparture','actualArrival','notes'],repeat=True,description='این صفحه و محموله، هر دو متعلق به مرحلهٔ دامنهٔ SHIPPING هستند؛ مرحلهٔ دامنهٔ جدیدی اختراع نشده است.'),
 ]),
 dict(id='customs',file='06-customs.html',title='گمرک و ترخیص',short='گمرک',number='۶',stage='CUSTOMS',subtitle='ردیابی اظهار، تکمیل مدارک و ترخیص بدون مخلوط شدن با حمل.',groups=[
  group('ShipmentCustoms','پروندهٔ گمرکی',['clearanceNumber','clearanceType','brokerPartyId','customsOfficeCode','declarationDate','documentsCompletedAt','releaseDate','dutyAmount','dutyCurrencyId','responsibleUserId','notes']),
 ]),
 dict(id='insurance',file='07-insurance.html',title='بیمه و بازرسی',short='بیمه و بازرسی',number='↗',stage='INSURANCE',subtitle='بخش مرتبط با حمل؛ وضعیت و نتیجهٔ بازرسی از دیکشنری خوانده می‌شود، نه enum.',groups=[
  group('InsurancePolicy','بیمه‌نامه',['insurerPartyId','policyNumber','coverageTypeCode','premiumAmount','currencyId','issueDate','startDate','expiryDate','notes']),
  group('Inspection','بازرسی',['inspectorPartyId','requestDate','inspectionDate','certificateNumber','statusCode','resultCode','resultNotes']),
 ]),
 dict(id='commitments',file='08-commitments.html',title='رفع تعهد ارزی',short='رفع تعهد',number='↗',stage='COMMITMENT',subtitle='اصل تعهد، مهلت و تسویه‌ها مستقل‌اند؛ جمع فقط در یک ارز انجام می‌شود.',groups=[
  group('Commitment','تعهد',['currencyId','committedAmount','deadlineDate','extendedToDate',field('dischargedAmount',readonly=True,computed=True),'responsibleUserId','notes']),
  group('CommitmentDischarge','تسویه‌های تعهد',['dischargeDate','amount','currencyId','methodCode','documentNumber'],repeat=True),
 ]),
 dict(id='costs',file='09-costs.html',title='هزینه‌های بازرگانی',short='هزینه‌ها',number='↗',stage='COSTS',subtitle='ثبت هزینه، تفکیک سهم بخش‌ها و پرداخت مستقل؛ جمع ارزهای متفاوت ممنوع.',groups=[
  group('TradeCost','هزینه',['costTypeId','stage','description','amount','currencyId','effectiveDate','payeePartyId','paymentMethodCode','referenceNumber','notes']),
  group('CostAllocation','تفکیک بخش‌ها',['scopeType','scopeId','stage','allocationPercent','allocatedAmount','currencyId'],repeat=True,description='درصد سهم‌ها باید در مجموع ۱۰۰ باشد. scopeId باید به موجودیت مشخص‌شده در scopeType اشاره کند.'),
  group('CostPayment','پرداخت‌ها و برگشت وجه',['paidAt','amount','currencyId','methodCode','isReturn','referenceNumber'],repeat=True),
 ]),
]
# Standard per-stage metadata is present once, not copied into every group.
for p in pages[1:]:
    p['groups'].append(group('ProjectStage','مسئول و یادداشت مرحله', ['responsibleUserId','startedAt','dueDate','notes'],advanced=True))
for p in pages:
    for g in p['groups']:
        for f in g['fields']:
            assert f['key'] in models[g['model']]['fields'], (g['model'],f['key'])

OUT.mkdir(parents=True,exist_ok=True)
(OUT/'assets').mkdir(exist_ok=True)
shutil.copyfile(ROOT/'src/frontend/src/assets/brand/hadron-logo-red.svg',OUT/'assets/hadron-logo-red.svg')
data=dict(models=models,enums=enums,pages=pages,referenceCommit='620c750919584a37ea5f23d06d28bbdc750ee810')
(OUT/'assets/data.js').write_text('window.TRADING_SPEC = '+json.dumps(data,ensure_ascii=False,indent=2)+';\n')
for p in [dict(id='overview',file='index.html',title='مسیر بازرگانی'), *pages, dict(id='mapping',file='data-sources.html',title='راهنمای منابع داده')]:
    html=f'''<!doctype html>
<html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>{p['title']} | Hadron ERP System</title><link rel="stylesheet" href="assets/style.css"><script defer src="assets/data.js"></script><script defer src="assets/app.js"></script></head>
<body data-page="{p['id']}"><a class="skip" href="#main">رفتن به محتوا</a><noscript>برای فرم تعاملی، JavaScript را فعال کنید. فایل README.md روش استفاده و نگاشت داده‌ها را توضیح می‌دهد.</noscript><div id="app"></div><div id="toast" role="status" aria-live="polite"></div></body></html>'''
    (OUT/p['file']).write_text(html)
manifest=[]
for p in pages:
    for g in p['groups']:
        for f in g['fields']:
            manifest.append(dict(page=p['file'],model=g['model'],table=models[g['model']]['table'],field=f['key'],mode='computed' if f.get('computed') else 'readonly' if f.get('readonly') else 'input',repeat=bool(g.get('repeat'))))
(OUT/'field-map.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print(f'Built {len(pages)+2} HTML pages; {len(manifest)} mapped controls; {len(models)} schema models.')


def archive():
    target=ROOT/'output/trading-prototype.zip'
    target.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
        for f in sorted(OUT.rglob('*')):
            if f.is_file():
                z.write(f,'trading-prototype/'+str(f.relative_to(OUT)))
    print(f'ZIP: {target} ({target.stat().st_size:,} bytes)')

if __name__=='__main__':
    archive()
