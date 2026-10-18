// Standalone review exports of the actual production React components, not a second form implementation.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import i18next from 'i18next';
import { I18nextProvider } from 'react-i18next';
import { createServer } from 'vite';
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(frontend, '../..');
const out = path.join(root, 'docs/ui/trading-stage-2');
await fs.mkdir(out, { recursive: true });
const vite = await createServer({ root: frontend, server: { middlewareMode: true }, appType: 'custom' });
try {
  const { TradingOrderView } = await vite.ssrLoadModule('/src/TradingOrderView.tsx');
  const { TradingOrdersListView } = await vite.ssrLoadModule('/src/TradingOrdersListView.tsx');
  const { TradingOrderPickerView } = await vite.ssrLoadModule('/src/TradingOrderPickerView.tsx');
  const { default: fa } = await vite.ssrLoadModule('/src/i18n/locales/fa.ts');
  const i18n = i18next.createInstance();
  await i18n.init({ lng: 'fa', resources: { fa: { translation: fa } }, interpolation: { escapeValue: false } });
  const noop = () => {};
  const project = { id: '00000000-0000-4000-8000-000000000001', projectNumber: 'TRD-DEMO', titleFa: 'پروژهٔ نمونه — فقط HTML', status: 'DRAFT' };
  const currencyId = '00000000-0000-4000-8000-000000000002';
  const items = [
    { id: 'demo-order-1', projectId: project.id, registrationNumber: 'REG-DEMO-01', registrationDate: '2026-10-18', registeredAmount: '123456.123456', currencyId, currencyCode: 'USD', currencyNameFa: 'دلار نمونه', status: 'DRAFT', notes: 'ردیف نمونهٔ آفلاین؛ در دیتابیس نیست.', version: 1 },
    { id: 'demo-order-2', projectId: project.id, registrationNumber: 'REG-DEMO-02', registrationDate: '2026-10-17', registeredAmount: '9007199254740993.123456', currencyId, currencyCode: 'USD', currencyNameFa: 'دلار نمونه', status: 'SUBMITTED', notes: 'مبلغ دقیق، بدون گردکردن در نمایش.', version: 2 },
  ];
  const css = (await Promise.all(['styles.css', 'tradingProjectSetup.css', 'tradingOrders.css'].map(f => fs.readFile(path.join(frontend, 'src', f), 'utf8')))).join('\n');
  await fs.copyFile(path.join(frontend, 'src/assets/brand/hadron-logo-red.svg'), path.join(out, 'hadron-logo-red.svg'));
  const localScript = `
const form=document.querySelector('.trade-order-form'),message=document.getElementById('offline-status');
const key='hadron.trading.stage2.offline.v1';
try{const data=JSON.parse(localStorage.getItem(key)||'{}');for(const e of form.elements)if(e.name&&!e.readOnly&&typeof data[e.name]==='string')e.value=data[e.name];}catch{}
form.addEventListener('submit',e=>{e.preventDefault();const data={};for(const input of form.elements)if(input.name&&!input.readOnly)data[input.name]=input.value;try{localStorage.setItem(key,JSON.stringify(data));message.textContent='فقط پیش‌نویس آفلاین در مرورگر ذخیره شد؛ هیچ سفارش واقعی ثبت نشده است.';}catch{message.textContent='ذخیرهٔ مرورگر در دسترس نیست؛ اطلاعاتی به سرور ارسال نشده است.';}});
for(const picker of form.querySelectorAll('[data-order-picker]')){const search=picker.querySelector('input[type="search"]'),select=picker.querySelector('select');search.addEventListener('input',()=>{for(const option of select.options)option.hidden=!!option.value&&!option.textContent.toLowerCase().includes(search.value.toLowerCase());});}
form.querySelector('.trade-setup-actions button[type="button"]').addEventListener('click',()=>{if(confirm('پیش‌نویس آفلاین پاک شود؟')){form.reset();try{localStorage.removeItem(key);}catch{}message.textContent='ورودی‌های آفلاین پاک شدند.';}});
`;
  for (const annotated of [false, true]) {
    const view = React.createElement(TradingOrderView, { project, values: { registrationNumber: '', registrationDate: '', registeredAmount: '', currencyId: '', notes: '', reason: '' }, language: 'fa', annotated, saving: false, ready: true, onChange: noop, onSubmit: noop, onCancel: noop,
      currencyPicker: React.createElement(TradingOrderPickerView, { mode: 'currency', value: '', query: '', items: [{ id: currencyId, code: 'USD', nameFa: 'دلار نمونه — فقط HTML' }], page: 1, totalCount: 1, loading: false, error: '', onSelect: noop, onSearch: noop, onPage: noop, onRetry: noop }) });
    const table = React.createElement(TradingOrdersListView, { items, language: 'fa', loading: false, filtered: false, canManage: true, busy: true, onEdit: noop, onStatus: noop });
    const markup = renderToStaticMarkup(React.createElement(I18nextProvider, { i18n }, React.createElement(React.Fragment, null, view, React.createElement('h2', null, 'نمونهٔ فهرست سفارش‌های پروژه'), table)));
    const file = annotated ? 'stage-2-annotated.html' : 'stage-2.html';
    const html = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ثبت و مدیریت سفارش‌ها | Hadron ERP System</title><style>${css}\nbody{margin:0;background:var(--surface-page)}.offline-shell{max-width:1184px;margin:auto;padding:24px;min-width:0}.offline-brand{display:flex;align-items:center;gap:24px}.offline-brand img{width:100px;height:44px;object-fit:contain}.offline-banner{padding:16px;border:1px solid var(--border-default);background:white;margin:16px 0;font-size:12px}.offline-shell .trade-setup{max-width:none}.offline-shell h2{font-size:18px;margin-block:24px 16px}#offline-status{font-size:12px;overflow-wrap:anywhere}</style></head><body><main class="offline-shell"><header class="offline-brand"><img src="hadron-logo-red.svg" alt="لوگوی رسمی هادرون"><div><strong dir="ltr">Hadron ERP System</strong><h1>ثبت و مدیریت سفارش‌ها — مرحلهٔ دوم</h1></div></header><nav><a href="stage-2.html">نسخهٔ ساده</a> · <a href="stage-2-annotated.html">نسخهٔ توضیح‌دار</a></nav><div class="offline-banner">خروجی همان فرم و جدول اپ، برای بررسی آفلاین. پروژه، ارز و ردیف‌ها نمونه‌اند. دکمهٔ ثبت فقط پیش‌نویس مرورگر را نگه می‌دارد؛ عملیات ویرایش و تغییر وضعیت جدول در HTML غیرفعال‌اند و فقط در اپ به API وصل‌اند.${annotated ? '<br>«ذخیرهٔ فعلی» و «مدل پیشنهادی» عمداً جدا شده‌اند؛ مدل کامل پیشنهادی به دیتابیس فعلی تحمیل نشده است.' : ''}</div>${markup}<p id="offline-status" role="status" aria-live="polite"></p></main><script>${localScript}</script></body></html>`;
    await fs.writeFile(path.join(out, file), html);
    console.log('Exported actual React order view:', file);
  }
  execFileSync('python3', ['-c', `from pathlib import Path\nimport zipfile\nr=Path(${JSON.stringify(root)})\np=r/'docs/ui/trading-stage-2'\nwith zipfile.ZipFile(r/'output/trading-stage-2.zip','w',zipfile.ZIP_DEFLATED) as z:\n for f in sorted(p.rglob('*')):\n  if f.is_file(): z.write(f,'trading-stage-2/'+str(f.relative_to(p)))`]);
} finally { await vite.close(); }
