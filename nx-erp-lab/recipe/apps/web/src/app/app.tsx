import { useEffect, useState } from 'react';
export default function App() {
  const [status,setStatus] = useState('در حال بررسی اتصال…');
  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/health', { signal:controller.signal })
      .then(async response => { if (!response.ok) throw new Error(); await response.json(); setStatus('بک‌اند و دیتابیس آماده‌اند'); })
      .catch(() => { if (!controller.signal.aborted) setStatus('بک‌اند یا دیتابیس آماده نیست؛ تنظیمات را بررسی کن'); });
    return () => controller.abort();
  }, []);
  return <main dir="rtl" style={{maxWidth:760,margin:'80px auto',padding:24,fontFamily:'Tahoma,sans-serif',lineHeight:2}}>
    <small>HADRON / NX LAB</small><h1>محیط آزمایشی بک‌اند</h1><p>{status}</p>
    <p>این صفحه فقط پوستهٔ React است. صفحات محصولات، BOM، تأمین‌کنندگان، برنامه‌ریزی و بازرگانی در این مرحله ساخته نمی‌شوند.</p>
  </main>;
}
