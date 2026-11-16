// Export the actual React view, not a separately maintained HTML imitation.
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
const out = path.join(root, 'docs/ui/trading-stage-1');
await fs.mkdir(out, { recursive: true });
const vite = await createServer({ root: frontend, server: { middlewareMode: true }, appType: 'custom' });
try {
  const { TradingProjectSetupView, SetupPickerView } = await vite.ssrLoadModule('/src/TradingProjectSetupView.tsx');
  const { default: fa } = await vite.ssrLoadModule('/src/i18n/locales/fa.ts');
  const i18n = i18next.createInstance();
  await i18n.init({ lng: 'fa', resources: { fa: { translation: fa } }, interpolation: { escapeValue: false } });
  const fixtures = {
    CUSTOMER: [{ id: '00000000-0000-4000-8000-000000000001', code: 'CUS-DEMO', nameFa: 'مشتری نمونه — فقط HTML' }],
    BUSINESS_CARD: [{ id: '00000000-0000-4000-8000-000000000002', code: 'BCR-DEMO', nameFa: 'کارت بازرگانی نمونه — فقط HTML' }],
    ASSIGNEE: [{ id: '00000000-0000-4000-8000-000000000003', code: '', nameFa: 'کارشناس نمونه — فقط HTML' }],
  };
  const noop = () => {};
  const baseCss = await fs.readFile(path.join(frontend, 'src/styles.css'), 'utf8');
  const setupCss = await fs.readFile(path.join(frontend, 'src/tradingProjectSetup.css'), 'utf8');
  const logo = await fs.readFile(path.join(frontend, 'src/assets/brand/hadron-logo-red.svg'), 'utf8');
  await fs.writeFile(path.join(out, 'hadron-logo-red.svg'), logo);
  const localScript = `
const form=document.querySelector('form.trade-setup'),message=document.getElementById('offline-status');
const storageKey='hadron.trading.stage1.offline.v1';
function report(text){message.textContent=text;}
try{const data=JSON.parse(localStorage.getItem(storageKey)||'{}');for(const e of form.elements){if(e.name&&!e.readOnly&&typeof data[e.name]==='string')e.value=data[e.name];}}catch{report('ذخیرهٔ مرورگر در دسترس نیست؛ فرم همچنان قابل بررسی است.');}
form.addEventListener('submit',e=>{e.preventDefault();const values={};for(const input of form.elements){if(input.name&&!input.readOnly)values[input.name]=input.value;}try{localStorage.setItem(storageKey,JSON.stringify(values));report('فقط پیش‌نویس HTML در مرورگر ذخیره شد. هیچ پرونده‌ای در اپ یا دیتابیس ایجاد نشده است.');}catch{report('ذخیرهٔ مرورگر مسدود است؛ اطلاعات به سرور ارسال نشده است.');}});
form.querySelector('.trade-setup-actions button[type="button"]').addEventListener('click',()=>{if(confirm('ورودی‌های این فایل پاک شوند؟')){form.reset();report('ورودی‌های فرم بازنشانی شد.');}});
for(const picker of document.querySelectorAll('[data-picker]')){const search=picker.querySelector('input[type="search"]'),select=picker.querySelector('select');search.addEventListener('input',()=>{for(const option of select.options)option.hidden=!!option.value&&!option.textContent.includes(search.value);});}
`;
  for (const annotated of [false, true]) {
    const content = renderToStaticMarkup(React.createElement(I18nextProvider, { i18n },
      React.createElement(TradingProjectSetupView, {
        values: { titleFa: '', startDate: '', customerId: '', businessCardId: '', responsibleUserId: '', notes: '' },
        language: 'fa-IR', saving: false, annotated, onChange: noop, onSubmit: noop, onCancel: noop,
        renderLookup: (kind, label, required, value) => React.createElement(SetupPickerView, {
          kind, label, required, value, query: '', items: fixtures[kind], page: 1, totalCount: 1, loading: false,
          error: '', onSelect: noop, onSearch: noop, onPage: noop, onRetry: noop,
        }),
      })));
    const file = annotated ? 'stage-1-annotated.html' : 'stage-1.html';
    const html = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>مرحلهٔ اول بازرگانی | Hadron ERP System</title><style>${baseCss}\n${setupCss}\nbody{margin:0;background:var(--surface-page)}.offline-shell{max-width:1184px;margin:auto;padding:24px}.offline-brand{display:flex;align-items:center;gap:24px;padding-bottom:24px}.offline-brand img{width:100px;height:44px;object-fit:contain}.offline-banner{padding:16px;border:1px solid var(--border-default);background:white;margin:16px 0;font-size:12px}.offline-status{font-size:12px;color:var(--text-secondary);margin:16px 0}.trade-setup{max-width:none}</style></head><body><main class="offline-shell"><header class="offline-brand"><img src="hadron-logo-red.svg" alt="لوگوی رسمی هادرون"><div><strong dir="ltr">Hadron ERP System</strong><h1>ایجاد پروژه جدید — مرحلهٔ اول</h1></div></header><nav><a href="stage-1.html">نسخهٔ ساده</a> · <a href="stage-1-annotated.html">نسخهٔ توضیح‌دار</a></nav><div class="offline-banner">این فایل خروجی همان کامپوننت فرم اپ است؛ فقط برای بررسی آفلاین. گزینه‌ها نمونه‌اند و دکمهٔ ثبت فقط پیش‌نویس مرورگر را ذخیره می‌کند. اتصال واقعی به API در خود اپ انجام می‌شود.${annotated ? '<br>«ذخیرهٔ فعلی» مسیر پیاده‌سازی محدود فعلی است؛ «مدل پیشنهادی» طرح ۳۳جدولی آینده را نشان می‌دهد. این دو با هم اشتباه نشوند.' : ''}</div>${content}<p class="offline-status" id="offline-status" role="status" aria-live="polite"></p></main><script>${localScript}</script></body></html>`;
    await fs.writeFile(path.join(out, file), html);
    console.log('Exported actual React view:', file);
  }
  execFileSync('python3', ['-c', `from pathlib import Path\nimport zipfile\nr=Path(${JSON.stringify(root)})\np=r/'docs/ui/trading-stage-1'\nwith zipfile.ZipFile(r/'output/trading-stage-1.zip','w',zipfile.ZIP_DEFLATED) as z:\n for f in sorted(p.rglob('*')):\n  if f.is_file(): z.write(f,'trading-stage-1/'+str(f.relative_to(p)))`]);
} finally { await vite.close(); }
