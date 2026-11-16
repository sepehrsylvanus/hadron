# گزارش ممیزی مقیاس، تجربه کاربری و احراز آمادگی جلسات

**ماژول:** Collaboration / Meetings  
**تاریخ ارزیابی:** 2026-09-08  
**منطقه زمانی کسب‌وکار:** `Asia/Tehran`  
**نوع گزارش:** ممیزی شواهد، مقایسه با الگوهای رسمی بازار و تعریف دروازه‌های Qualification  
**وضعیت نهایی این گزارش:** **Not Qualified for production media at 150 participants**

## 1. حکم اجرایی

اجرای کنترل‌پل ثبت‌شده برای سناریوی **۵۰ کاربر داخلی + ۱۰۰ مهمان** در سه دور متوالی موفق بوده است. مقدار رسمی مانيفست آن اجرا چنین است:

```text
runOutcome: ComponentPassed
finalQualified: false
finalQualificationStatus: NotQualified
```

این نتیجه فقط ثابت می‌کند که چرخه‌های مدیریتی جلسه ــ دعوت یک‌بارمصرف، ورود مهمان، اتاق انتظار، پذیرش، ایجاد هویت رسانه‌ای مصنوعی، ثبت اتصال/قطع و پایان جلسه ــ در تست یکپارچه کنترل‌پل درست عمل کرده‌اند. این اجرا **۱۵۰ مرورگر واقعی، ۱۵۰ میکروفن/دوربین، ۱۵۰ جریان WebRTC هم‌زمان، کیفیت صدای انسانی، کیفیت تصویر، تأخیر شبکه یا تجربه کاربر واقعی را اندازه‌گیری نکرده است**.

شواهد رسانه‌ای جدید نتیجه‌ای محدود اما مهم اضافه کرده‌اند:

- negative test با شناسه `20260908T201602900Z-6b5cf77c` عمداً credential نامعتبر داشت. با وجود exit code صفر LiveKit CLI، runner خطای semantic احراز هویت/اتصال را تشخیص داد و `runOutcome: Failed` ثبت کرد. secret scan نیز یک مقدار token-like را از artifact حذف و اجرا را fail-closed کرد. این نتیجه، تست سلامت harness است؛ تست کیفیت رسانه نیست.
- اجرای صحیح و نهایی `MediaSmoke` پس از audit parser با شناسه `20260908T202656502Z-1a37ef87` در هر سه loop از نظر process پاس شد. در هر دور ۴۰ از ۴۰ delivery مورد انتظار track مشاهده شد، parser دقیقاً ۱۱ ردیف summary مربوط به subscriber را خواند، delivery برابر ۱۰۰٪ و aggregate reported loss برابر صفر بود؛ هر process حدود `48.7s` طول کشید. هر فایل metrics دقیقاً هفت ردیف یکتای Browser QoE داشت. با این حال latency p95 توسط CLI گزارش نشد و هر هفت معیار Browser QoE در هر loop `NotMeasured` ماندند. بنابراین مانيفست به‌درستی `runOutcome: Partial`، `MediaSmoke: IncompleteOrFailed` و `finalQualified: false` ثبت کرده است.
- `Local150` همچنان `runOutcome: Blocked` است؛ فقط `5.28 GB` حافظه آزاد وجود داشت، در حالی‌که حداقل تعریف‌شده `8 GB` است، و سنجه حافظه Docker نیز در دسترس نبود. توقف به‌صورت fail-closed صحیح بوده است.

عدد ۴۰ در MediaSmoke به معنی ۴۰ انسان یا ۴۰ مرورگر نیست. topology شامل دو publisher صوت/ویدئو و ۱۰ subscriber مصنوعی بوده و ۴۰ delivery اشتراک track تولید کرده است.

بنابراین ادعای «جلسه ۱۵۰ نفره بدون lag صدا و تصویر تأیید شد» در حال حاضر نادرست است. عبارت درست این است:

> کنترل‌پل ۵۰ کاربر داخلی و ۱۰۰ مهمان در سه دور موفق شد؛ processهای MediaSmoke کوچک نیز در سه دور ۴۰/۴۰ track با صفر reported loss داشتند. اما latency رسانه، Browser QoE، Local150، دستگاه واقعی، مسیر TURN/TCP و بازبینی انسانی هنوز پاس نشده‌اند؛ نتیجه نهایی همچنان `NotQualified` است.

## 2. امتیاز فعلی

| محور | امتیاز | معنی |
|---|---:|---|
| کیفیت پیاده‌سازی قابل مشاهده در کد و تست‌های خودکار | **8.3 / 10** | budget واحد ۱۶ tile، انتخاب sticky، semantics چت، locale زمان، race guards و harness سخت‌گیر نقاط قوت‌اند؛ telemetry کامل QoE، accessibility انسانی و recovery مستقل از client هنوز باقی است. |
| آمادگی عملیاتی برای جلسه ۱۵۰ نفره | **4.2 / 10** | کنترل‌پل پاس است و MediaSmoke کوچک delivery/loss موفق دارد؛ اما latency، Browser QoE، Local150، دستگاه بیرونی، TURN/TCP fallback و آزمون انسانی معتبر پاس نشده‌اند. |
| امتیاز ترکیبی انتشار | **6.7 / 10** | برای توسعه و UAT کنترل‌شده مناسب است؛ برای اعلام آمادگی عملیاتی ۱۵۰ نفره یا امتیاز بالای ۹ کافی نیست. |

این امتیازها ارزیابی کیفیت سرویس Google Meet، Teams، Zoom، Webex یا Skyroom نیستند. آن محصولات فقط به‌عنوان **مرجع الگوی تعامل و عملیات** استفاده شده‌اند. امتیاز Hadron بر پایه شواهد همین مخزن و همین اجرا صادر شده است.

## 3. دامنه و واژگان

### 3.1 Control-plane

کنترل‌پل شامل مدیریت جلسه و هویت است: ساخت/شروع/پایان جلسه، دعوت، redeem توکن، waiting room، admit/deny، نقش، مجوز join و attendance. موفقیت کنترل‌پل برای صحت کسب‌وکار ضروری است، ولی کیفیت رسانه را اثبات نمی‌کند.

### 3.2 Media-plane

مدیاپلین شامل WebRTC/SFU، انتشار و دریافت track، تطبیق bitrate، screen share، packet loss، jitter، RTT، freeze، reconnect و fallback شبکه است. این قسمت به اجرای واقعی LiveKit و تولیدکننده بار مستقل نیاز دارد.

### 3.3 Browser QoE

Quality of Experience مرورگر باید از سمت client و در شبکه واقعی اندازه‌گیری شود: موفقیت join، زمان join، RTT، jitter، packet loss، frame drop، freeze ratio، قطع صدا، lip-sync و رفتار دستگاه‌ها. خروجی CLI به‌تنهایی جای این سنجه‌ها را نمی‌گیرد.

### 3.4 Human UX

تست ماشینی می‌تواند API، state و DOM را بررسی کند؛ اما خوانایی فارسی، درک نقش‌ها، اضطراب کاربر در خطا، کیفیت شنیداری، قابل‌فهم بودن reconnect، کار با صفحه‌کلید و screen reader باید با انسان و دستگاه واقعی ارزیابی شود.

یک بازبینی انسانی محدود فرم ایجاد جلسه انجام شده است، اما این بازبینی معادل Human Accessibility Review کامل نیست: مقدار نامعتبر جلالی `14051340` در حالت scheduled پیام فارسی نشان داد و CTA غیرفعال ماند؛ با تغییر نوع به instant، عنوان/زیرعنوان درست تغییر کرد و CTA دیگر به تاریخ stale وابسته نبود. هیچ جلسه‌ای submit نشد، بنابراین persistence و workflow ایجاد جلسه در این بازبینی انسانی آزموده نشد.

در ۲۰۲۶-۰۹-۰۹ مشخص شد این بازبینی با ورود مستقیم به مسیر جلسه، «کشف‌پذیری از صفحه پیام‌ها و پوسته سراسری» را اصلاً پوشش نداده بود. پس از اصلاح، مسیر طبیعی `پیام‌ها -> پنل ارتباطات -> جلسات و تماس‌ها -> جزئیات -> فرم زمان‌بندی/فوری` در مرورگر واقعی پیموده شد و CTAهای متنی و مجوزمحور دیده شدند؛ هیچ جلسه جدیدی submit نشد. این بازآزمایی فقط شکاف discoverability دسکتاپ را می‌بندد و همچنان جای Human Accessibility Review یا UAT چندکاربره را نمی‌گیرد.

### 3.5 «Record» در این گزارش

منظور از ثبت فرایند، ثبت manifest، log، metrics، screenshots مجاز، نسخه کد و نتیجه gate است؛ **محتوای صوتی/تصویری جلسه ضبط نمی‌شود**. ضبط جلسه در دامنه V1 عمداً غیرفعال است و نباید برای Qualification بدون تصمیم مستقل درباره رضایت، نگهداری، سطح دسترسی و حذف داده فعال شود.

## 4. جدول شواهد

| شناسه/مسیر | شواهد قطعی | برداشت مجاز | چیزی که ثابت نمی‌کند |
|---|---|---|---|
| `artifacts/performance/meetings/20260908T202154856Z-9a595f42/manifest.json` | اجرای post-all-fixes با پروفایل `ControlPlane`، سه loop، ۱۲ نتیجه `Passed`، `runOutcome=ComponentPassed`، `finalQualified=false` | harness کنترل‌پل و build/lint پس از همه اصلاحات جاری اجرا شده‌اند | آمادگی رسانه، کیفیت واقعی یا production readiness |
| `control-backend-trx-loop-01..03.json` | در هر loop دو تست منتخب، `2/2 Passed` | سناریوهای انتخاب‌شده deterministic و تکرارپذیر بوده‌اند | پوشش کل backend یا بار HTTP production |
| `MeetingTests.Fifty_internal_users_and_one_hundred_guests_complete_the_control_plane_lifecycle` | ۴۹ کاربر seedشده + host = ۵۰ داخلی؛ ۱۰۰ invite/redeem/admit؛ ۱۵۰ identity/token مصنوعی؛ connect/disconnect و attendance | قواعد چرخه عمر و idempotency کنترل‌پل در TestServer درست بوده‌اند | ۱۵۰ WebRTC endpoint؛ ۱۵۰ مرورگر یا ۱۵۰ کاربر انسانی |
| `MeetingTests.One_hundred_guest_status_pollers_behind_one_address_are_accepted_without_loading_roster` | ۱۰۰ مهمان پشت یک IP آزمایشی؛ ۶ دور poll یعنی ۶۰۰ status request؛ assertion جلوگیری از load کامل roster | endpoint وضعیت مهمان برای NAT مشترک و query محدود طراحی شده است | نرخ login کاربران داخلی پشت NAT یا ظرفیت deployed server |
| `control-frontend-loop-01..03.log` | در artifact نهایی کنترل‌پل، هر loop مجموعه collaboration با `89/89 Passed` | منطق frontend مرتبط پس از همه اصلاحات در سه دور پاس شده است | تعامل واقعی دوربین/میکروفن، screen reader و UX انسانی |
| `control-frontend-lint.log` و `control-frontend-build.log` | lint و production build پاس | snapshot قابل lint/build بوده است | نبود bug اجرایی در مرورگر و شبکه واقعی |
| بازآزمایی مستقل کل frontend پس از همه اصلاحات | کل frontend=`244/244 Passed` | اصلاحات current frontend regression آشکار در مجموعه کامل ایجاد نکرده‌اند | این خروجی مستقل از artifact کنترل‌پل است و media/QoE را نمی‌سنجد |
| بازآزمایی هدفمند UX ایجاد جلسه | `85/85 Passed` | جریان create و stateهای مرتبط در تست ماشینی پاس شده‌اند | usability انسانی، دستگاه واقعی یا accessibility کامل |
| بازآزمایی نهایی room/a11y/scale | `89/89 Passed`؛ budget واحد stage برابر ۱۶، chat semantics/locale و sticky selection پوشش داده شده‌اند | قواعد frontend مربوط به سقف stage و دسترس‌پذیری چت deterministic هستند | ظرفیت واقعی SFU، FPS، مصرف DOM در مرورگر واقعی یا sign-off انسانی |
| بازبینی انسانی محدود فرم create | scheduled با `14051340` پیام فارسی و CTA disabled؛ تغییر به instant عنوان/زیرعنوان را درست کرد و CTA از stale date مستقل شد؛ submit انجام نشد | state و validation قابل مشاهده فرم در این مسیر محدود قابل فهم است | ایجاد واقعی جلسه، persistence، keyboard-only، screen reader یا Human Accessibility Review کامل |
| اصلاح و بازآزمایی discoverability در ۲۰۲۶-۰۹-۰۹ | مسیر واقعی از صفحه پیام‌ها و دک سراسری تا فهرست، جست‌وجوی عنوان، جزئیات و فرم scheduled/instant پیموده شد؛ ورودی پیوسته عنوان، فوکوس آغازین دک و بازگشت فوکوس با Escape در UI و accessibility tree مشاهده شدند؛ کل frontend=`253/253 Passed` و تست یکپارچگی متمرکز Collaboration/Meeting=`21/21 Passed` | مسیر جلسه دیگر به دانستن URL یا تشخیص آیکن مبهم وابسته نیست؛ مجوز UI با endpoint هم‌تراز است و regression متمرکز مشاهده نشد | submit جلسه جدید، keyboard-only/NVDA کامل، 200% zoom، موبایل واقعی، چند دستگاه فیزیکی یا Media QoE |
| `artifact-secret-scan.log` | secret/token-like evidence پیدا نشد | artifact تحویلی secret آشکار ندارد | امنیت جامع برنامه یا absence مطلق secret در کل مخزن |
| `artifacts/test-results/2026-09-09-meetings/full-backend-post-meeting-final.trx` | regression کامل backend با environment صحیح؛ `394/394 Passed`، صفر failed، زمان گزارش‌شده حدود `12m45s` | این TRX مبنای نهایی regression backend این ممیزی است | شواهد media/QoE یا performance عددی endpointهای کنترل‌پل |
| `artifacts/performance/meetings/20260908T201602900Z-6b5cf77c/manifest.json` | negative test عمدی؛ process با exit code صفر ولی semantic auth/connect failure تشخیص داده شد؛ secret scan مقدار حساس احتمالی را حذف و run را fail-closed کرد | runner دیگر exit code صفر را به‌تنهایی موفقیت تلقی نمی‌کند و artifact آلوده را qualified نمی‌کند | کیفیت یا ظرفیت رسانه؛ این اجرا عمداً نامعتبر بوده است |
| `artifacts/performance/meetings/20260908T202656502Z-1a37ef87/manifest.json` | سه process `Passed`، هر دور ۴۰/۴۰ track delivery، ۱۰۰٪ delivery، صفر aggregate reported loss، حدود 48.7s؛ CPU مشاهده‌شده حداکثر 15.98٪ و RAM آزاد حداقل 4.06GB؛ `runOutcome=Partial` و `finalQualified=false` | MediaSmoke کوچک به LiveKit واقعی وصل شده و delivery/loss مشاهده‌شده در topology خودش موفق بوده است | track latency p95، Browser QoE، تجربه انسانی یا ظرفیت ۱۵۰ نفر |
| `media-smoke-loop-01..03-metrics.json` در artifact نهایی | در هر loop دقیقاً ۱۱ Subscriber summary row؛ delivery و aggregate reported loss `Measured/Passed`؛ track latency p95=`NotMeasured`؛ دقیقاً هفت ردیف یکتای Browser QoE و همگی `NotMeasured` | parser فقط metric واقعاً موجود را پاس کرده و duplicate QoE row تولید نکرده است | هیچ مقدار latency/QoE نباید از نتیجه process یا صفر loss استنتاج شود |
| `artifacts/performance/meetings/20260908T195246945Z-bd1c08c0/manifest.json` | `Local150`، `runOutcome=Blocked`، `finalQualified=false`؛ 5.28GB آزاد و Docker metric unavailable | preflight از اجرای بی‌اعتبار روی میزبان ناکافی جلوگیری کرده است | ظرفیت ۱۵۰ کاربر رد یا قبول نشده؛ اصلاً اجرا نشده است |
| `thresholds.snapshot.json` و README harness | حدود کنترل، media CLI، browser QoE و host abort snapshot شده‌اند | معیار پذیرش پیش از اجرا مشخص است | aggregate عددی success rate، unexpected rate-limit، 5xx rate و p95/p99 کنترل‌پل در artifact موجود اندازه‌گیری نشده‌اند؛ پاس‌شدن تست functional معادل پاس این اعداد نیست |

### محدودیت تمامیت شواهد

اجرای post-all-fixes کنترل‌پل روی revision `63926b9f122783e07be2f8bb2eab004f80644f01` و یک working tree بسیار dirty ثبت شده است: `3666` مسیر تغییرکرده (`115` modified و `3551` untracked). MediaSmoke نهایی نیز روی همان revision و working tree dirty با `3675` مسیر تغییرکرده (`115` modified و `3560` untracked) اجرا شده است. artifactها مسیر یکتا و hash شواهد دارند، اما immutable نیستند و diff محتوا نیز ذخیره نشده است. بنابراین بازتولید دقیق snapshot از روی commit به‌تنهایی ممکن نیست. این موضوع نتایج مشاهده‌شده را باطل نمی‌کند، ولی برای release evidence قابل حسابرسی یک blocker است.

برای بهداشت شواهد backend، اجرای test بدون environment/configuration لازم Qualification معتبر محسوب نمی‌شود، حتی اگر بخشی از testها شروع شده باشند. تنها `full-backend-post-meeting-final.trx` که با environment صحیح تولید شده، مبنای عدد `394/394` این گزارش است؛ اجرای مستقیم نامعتبر یا ناقص نباید با failure محصول یا با regression نهایی مخلوط شود.

## 5. ۵۰ کاربر داخلی + ۱۰۰ مهمان دقیقاً چه چیزی را آزموده است؟

تست کنترل‌پل از `Testing` authentication و media gateway آزمایشی استفاده می‌کند. درخواست‌ها نیز در batchهای حداکثر ۲۵تایی اجرا می‌شوند. پس عبارت «۱۵۰ کاربر هم‌زمان» برای این تست باید با دقت چنین نوشته شود:

| ادعا | وضعیت |
|---|---|
| ۵۰ identity داخلی و ۱۰۰ identity مهمان در یک lifecycle پردازش شدند | **اثبات‌شده** |
| ۱۰۰ توکن مهمان یک‌بارمصرف redeem و پذیرش شدند | **اثبات‌شده** |
| roster نهایی ۱۵۰ participant داشت | **اثبات‌شده** |
| attendance برای همه ایجاد و بسته شد | **اثبات‌شده** |
| success rate، unexpected rate-limit، 5xx rate و p95/p99 کنترل‌پل به‌صورت عددی زیر بار deployed داخل threshold بوده‌اند | **اندازه‌گیری‌نشده** |
| ۱۵۰ اتصال رسانه‌ای واقعی هم‌زمان برقرار شد | **اثبات‌نشده** |
| ۱۵۰ کاربر واقعی صدا و تصویر بدون lag داشتند | **اثبات‌نشده** |
| شبکه شرکت، اینترنت بیرونی، موبایل، VPN، NAT و firewall درست کار می‌کنند | **اثبات‌نشده** |
| کیفیت UX و accessibility بالای ۹ است | **اثبات‌نشده** |

`ControlPlane: Passed` در مانيفست فعلی به معنی موفقیت تست‌های functional انتخاب‌شده است، نه تأیید عددی targetهای performance کنترل‌پل. MediaSmoke اجراشده نیز مستقیماً SFU را تحت بار محدود قرار داده و چرخه Identity/Invite/Admission ERP را دور زده است. سه دور ۴۰/۴۰ track delivery، نتیجه مفید media-plane در همان topology کوچک است؛ اما معادل ۴۰ participant و به‌ویژه معادل ۱۵۰ participant نیست. Qualification معتبر به **ترکیب** کنترل‌پل، load عددی HTTP، media load، browser QoE و human UAT نیاز دارد؛ هیچ‌کدام جای دیگری را نمی‌گیرد.

## 6. یافته‌های پیاده‌سازی و UX

### نقاط قوی قابل مشاهده

- چرخه رسمی `SCHEDULED → LIVE → ENDED` و مسیر `CANCELLED`، نقش‌های `HOST/COHOST/PRESENTER/ATTENDEE/EXTERNAL_GUEST` و waiting room مهمان با قرارداد V1 هم‌راستا هستند.
- invitation یک‌بارمصرف و admit/deny به‌صورت backend-authoritative پیاده شده‌اند؛ UI صرفاً جایگزین authorization نشده است.
- pre-join شامل preview دوربین، انتخاب microphone/camera/speaker، meter میکروفن و تست speaker است؛ این الگو با green room محصولات مرجع هم‌راستاست.
- کنترل‌های mic، camera، screen share، people، chat، whiteboard، leave/end و shortcutهای اصلی وجود دارند.
- اتصال مجدد، خطاهای permission/playback، پاک‌سازی track و چند محافظ generation/race در frontend دیده می‌شود.
- تغییر زبان یا refresh فهرست participant دیگر Room را بی‌دلیل reconnect نمی‌کند؛ translation و participant index از ref به‌روز استفاده می‌کنند و label/subscription درجا refresh می‌شود.
- جریان مهمان در برابر پاسخ دیررس، تغییر credential، خطای گذرای شبکه، `429` و `5xx` محافظت شده است؛ invalidate فقط برای پاسخ معتبر `401/410` یا expiry محلی معتبر انجام می‌شود.
- نسخه جزئیات جلسه به‌صورت monotonic محافظت می‌شود تا پاسخ قدیمی state جدیدتر را overwrite نکند.
- گزارش connected/disconnected به‌ترتیب serialize و با retry محدود و idempotent انجام می‌شود.
- برای اتاق بزرگ، یک budget واحد `MAX_STAGE_VISUAL_TILES=16` بین local media، screen share، remote camera و placeholder تقسیم می‌شود؛ فهرست شرکت‌کنندگان search دارد و overflow خلاصه می‌شود.
- انتخاب دوربین remote sticky است تا تغییر کوتاه active speaker باعث churn مداوم subscription و tile نشود؛ تست topology معکوس و budget مشترک نیز اضافه شده است.
- chat stream دارای `role="log"` با live announcement کنترل‌شده است؛ متن `dir="auto"` دارد و زمان با locale فعال ERP و منطقه `Asia/Tehran` format می‌شود.
- runner اکنون علاوه بر exit code، failure معنایی LiveKit را تشخیص می‌دهد و در aggregate crash یا artifact حساس fail-closed باقی می‌ماند.
- رنگ تنها حامل وضعیت نیست؛ badge، متن و راهنمای اتصال در کنار tone ارائه می‌شوند و با Design System هادرون هم‌راستا هستند.
- سیاست عدم ضبط جلسه به‌وضوح در UI دیده می‌شود و با دامنه مصوب V1 سازگار است.

### نواقص یا شواهد ناکافی مؤثر بر امتیاز

1. **QoE فقط بخشی اندازه‌گیری شده است:** delivery برابر ۱۰۰٪ و reported loss برابر صفر در MediaSmoke کوچک مشاهده شد؛ اما track latency p95، join success/time، RTT، jitter، packet loss سمت مرورگر، frame drop و freeze همگی اندازه‌گیری نشده‌اند. UI نیز dashboard عملیاتی این سنجه‌ها را ندارد.
2. **سنجه عددی performance کنترل‌پل موجود نیست:** تست functional پاس شده، اما aggregateهای success rate، unexpected 429، 5xx rate، p95 و p99 روی endpoint deployed اندازه‌گیری نشده‌اند.
3. **۱۵۰ endpoint رسانه‌ای آزموده نشده است:** Local150 پیش از اجرا Blocked شد. هیچ نتیجه‌ای درباره ظرفیت ۱۵۰ participant، مصرف bandwidth یا کیفیت هم‌زمان صدا/تصویر مجاز نیست.
4. **budget و sticky selection هنوز به اثبات مرورگر واقعی نیاز دارند:** تست‌های deterministic سقف ۱۶ tile و انتخاب sticky را تأیید می‌کنند؛ رفتار FPS/DOM/decoder و churn در WebRTC واقعی و topology ۱۵۰ نفره هنوز سنجیده نشده است.
5. **whiteboard pointer-only است:** canvas فعلی مسیر کامل keyboard/screen-reader ندارد. تا زمان طراحی ابزار قابل دسترس، باید limitation صریح باشد یا fallback متنی/فرمان‌های keyboard فراهم شود.
6. **آزمون human accessibility انجام نشده:** بهبود chat semantics با تست ماشینی تأیید شده، اما focus order، trap/restore focus، Escape، zoom 200%، reduced motion، contrast، NVDA و keyboard-only باید با انسان تأیید شوند.
7. **ریسک NAT برای login داخلی جداگانه باقی است:** تست ۱۰۰ guest poller، login کاربران داخلی را پوشش نمی‌دهد. rate limit احراز هویت باید با ۵۰ همکار پشت IP مشترک UAT شود، بدون ضعیف‌کردن کنترل امنیتی.
8. **عملیات شبکه تأیید نشده:** HTTPS/WSS، certificate، UDP، TCP fallback و TURN از بیرون شرکت شواهد ندارند.
9. **شواهد release به commit تمیز متصل نیست:** working tree بسیار dirty است؛ بازتولید و audit زنجیره تغییرات محدود شده است.
10. **attendance در hard crash/offline تضمین نهایی ندارد:** retry محدود frontend حالت‌های عادی را پوشش می‌دهد، اما بسته‌شدن اجباری مرورگر، قطع طولانی یا crash می‌تواند callback قطع را از بین ببرد. provider webhook یا backend reaper با idempotency و audit لازم است.
11. **هشدار bundle باقی است:** build موفق است، اما chunk موجود LiveKit حدود `518.58 kB` است. این blocker عملکردی نیست؛ باید با اندازه‌گیری startup/preload و در صورت نیاز lazy-boundary بهینه شود.

مواردی مانند recording، transcript، captions، reaction، raise hand یا persistent whiteboard نسبت به محصولات جهانی feature gap هستند، اما همگی defect V1 محسوب نمی‌شوند. اضافه‌کردن هرکدام باید با تصمیم دامنه، مجوز، حریم خصوصی، retention و هزینه عملیات انجام شود.

## 7. مقایسه با الگوهای رسمی بازار

منابع این بخش فقط مستندات رسمی سازندگان هستند؛ مقایسه کارایی آزمایشگاهی مستقل نیست.

| الگوی مرجع | شاهد رسمی | انتظار حرفه‌ای | وضعیت Hadron |
|---|---|---|---|
| Google Meet — green room | Google امکان انتخاب و تست mic/speaker/camera پیش از join را مستند کرده است. | کاربر پیش از ورود مشکل دستگاه را بفهمد. | تا حد خوبی پیاده شده؛ هنوز مرورگر/دستگاه واقعی qualification نشده است. |
| Google Meet — accessibility | Google screen reader، caption، shortcut، pinning و transcript را مستند کرده است. | مسیر keyboard و assistive technology بخشی از Definition of Done باشد. | shortcut، status semantic، chat log/announcement و locale اصلاح شده‌اند؛ caption و human a11y verification وجود ندارد. |
| Microsoft Teams — lobby policy | Teams تعیین می‌کند چه کسی lobby را bypass کند و چه نقشی admit/deny انجام دهد. | سیاست پذیرش روشن و backend-authoritative باشد. | internal auto-admit و guest waiting/admit/deny با دامنه Hadron مناسب است؛ تنظیمات policy قابل پیکربندی مانند Teams فعلاً در V1 نیست. |
| Microsoft Teams — real-time telemetry | Teams سنجه‌های دستگاه، شبکه، audio/video/content از جمله jitter، loss، RTT، bitrate، frame rate، freeze و CPU را برای troubleshooting مستند می‌کند. | support تیم فناوری بر داده واقعی تکیه کند. | بزرگ‌ترین شکاف: telemetry و dashboard عملیاتی QoE وجود ندارد. |
| Zoom — pre-join audio | Zoom تست speaker و microphone پیش از جلسه را مستند می‌کند. | self-check سریع و قابل فهم باشد. | speaker test و mic meter وجود دارد؛ UAT سخت‌افزار واقعی باقی است. |
| Zoom — quality scores/alerts | Zoom امتیازهای کیفیت و thresholdهای network/CPU را مستند می‌کند. | افت کیفیت قبل از شکایت کاربر قابل مشاهده و هشدارپذیر باشد. | فقط وضعیت کلی SDK نمایش داده می‌شود؛ score/alert/history عملیاتی نداریم. |
| Webex — lobby trust | Webex تمایز داخلی، خارجی و unverified و پذیرش فردی/گروهی را مستند می‌کند. | میزبان بداند چه کسی پشت درخواست ورود است. | نقش `EXTERNAL_GUEST` و لابی وجود دارد؛ نمایش trust/verification می‌تواند غنی‌تر شود. |
| Webex — health checker | Webex health checker و آمار audio/video/content و راهنمای رفع مشکل را مستند می‌کند. | کاربر و پشتیبان علت مشکل را همان زمان ببینند. | راهنمای کلی اتصال وجود دارد؛ diagnostic قابل اقدام و export وجود ندارد. |
| Skyroom — تجربه فارسی مبتنی بر مرورگر | Skyroom در وب‌سایت رسمی خود browser/WebRTC، مهمان، chat فارسی، screen share، whiteboard و cloud recording را معرفی می‌کند. | onboarding فارسی کم‌اصطکاک و ابزارهای جلسه یکپارچه باشند. | زبان و الگوی کلی بومی مناسب است؛ whiteboard موقت و بدون مسیر accessibility کامل است؛ recording عمداً خارج از V1 است. |

### منابع رسمی

- [Google Meet — Connect your video & audio](https://support.google.com/meet/answer/10409699?hl=en)
- [Google Meet — Accessibility](https://support.google.com/meet/answer/16175468?hl=en)
- [Microsoft Teams — Using the lobby](https://support.microsoft.com/en-us/teams/meetings/using-the-lobby-in-microsoft-teams-meetings)
- [Microsoft Teams — Real-time telemetry](https://learn.microsoft.com/en-us/microsoftteams/use-real-time-telemetry-to-troubleshoot-poor-meeting-quality)
- [Zoom — Testing audio settings](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0062765)
- [Zoom — Meeting quality scores and network alerts](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0065975)
- [Webex — Admit people from the lobby](https://help.webex.com/en-us/article/3gt73c/Webex-App-%7C-Let-Someone-into-Your-Meeting)
- [Webex — Check meeting performance](https://help.webex.com/en-us/article/ns63yvy/Check-the-performance-of-your-Webex-meetings)
- [Skyroom — وب‌سایت رسمی](https://www.skyroom.online/)
- [LiveKit — Self-hosted benchmarking](https://docs.livekit.io/transport/self-hosting/benchmark/)

ادعای ظرفیت اعلام‌شده در وب‌سایت Skyroom، ادعای خود فروشنده است و به‌عنوان نتیجه benchmark مستقل استفاده نشده است. مستند LiveKit نیز تأکید می‌کند ظرفیت SFU به تعداد publisher/subscriber/track، CPU و bandwidth وابسته است و load generator باید منابع مستقل کافی داشته باشد؛ بنابراین اجرای ۱۵۰ client روی همان سیستم میزبان سرور، Qualification معتبر production نیست.

## 8. Rubric امتیازدهی پیاده‌سازی

| معیار | وزن | امتیاز / 10 | امتیاز وزنی | دلیل |
|---|---:|---:|---:|---|
| lifecycle و قواعد جلسه | 12% | 9.0 | 10.8 | state/role/attendance و پایان جلسه پوشش مناسب دارند. |
| guest invitation و lobby | 10% | 9.0 | 9.0 | one-use invite، waiting، admit/deny و polling محدود تست شده‌اند. |
| مجوز و امنیت دامنه | 10% | 8.5 | 8.5 | backend authority مناسب است؛ UAT شبکه/ratelimit و review عملیاتی کامل نیست. |
| pre-join و دستگاه‌ها | 10% | 8.5 | 8.5 | preview، selection، meter و speaker test؛ بدون device matrix واقعی. |
| کنترل‌های داخل جلسه | 10% | 8.5 | 8.5 | mic/camera/share/chat/people/whiteboard/leave/end؛ feature parity کامل هدف V1 نیست. |
| مقیاس‌پذیری UI و subscription | 10% | 9.0 | 9.0 | budget واحد ۱۶ tile، overflow summary و sticky selection تست شده‌اند؛ بار مرورگر واقعی باقی است. |
| resilience و race safety | 10% | 8.8 | 8.8 | lifecycle/guest generation، cleanup و monotonic guards قوی‌اند؛ soak/fault injection انجام نشده است. |
| accessibility و RTL/LTR | 10% | 8.0 | 8.0 | chat semantics/announcement/locale اصلاح شده‌اند؛ whiteboard و human review ناقص‌اند. |
| observability و QoE | 12% | 5.0 | 6.0 | delivery/loss کوچک اندازه‌گیری شده؛ latency و Browser QoE و dashboard عملیاتی موجود نیست. |
| test/evidence discipline | 6% | 9.0 | 5.4 | semantic failure و secret scan fail-closed هستند؛ snapshot dirty و پروفایل‌های لازم ناتمام‌اند. |
| **جمع** | **100%** |  | **82.5 / 100 = 8.3 / 10** | این امتیاز کیفیت پیاده‌سازی است، نه Qualification عملیاتی ۱۵۰ نفره. |

## 9. Blockerهای رسیدن به بالای 9/10

همه موارد زیر باید بسته شوند؛ صرف افزایش تعداد unit test امتیاز را به ۹ نمی‌رساند.

1. کامل‌کردن gate `MediaSmoke`: نسخه و checksum ابزار ثبت شده و سه process پاس‌اند، اما باید track latency p95 واقعاً اندازه‌گیری و شرط `≤200ms` پاس شود؛ صفر loss جای latency را نمی‌گیرد.
2. اجرای load واقعی control-plane روی endpoint deployed و ثبت success rate ≥99%، unexpected 429=0، 5xx≤0.5%، p95≤500ms و p99≤1000ms؛ نتیجه integration فعلی این اعداد را تولید نمی‌کند.
3. اجرای `Local150` با load generator مستقل از SFU، منابع کافی و سنجه معتبر Docker؛ سه loop متوالی بدون عبور از limits.
4. اجرای browser QoE و ثبت حداقل این معیارها: join success ≥ 99%، join p95 ≤ 3000ms، RTT p95 ≤ 200ms، audio jitter p95 ≤ 30ms، packet loss p95 ≤ 2%، video frame drop ≤ 3% و freeze ratio ≤ 1%.
5. پوشش حداقل Chrome/Edge/Firefox روی desktop، یک Android و یک iOS واقعی، headset/USB/Bluetooth و تغییر device حین جلسه.
6. آزمون دو مسیر شبکه واقعی: داخل شرکت و اینترنت بیرونی؛ سپس UDP، TCP fallback و TURN با evidence مستقل.
7. تست ۵۰ login داخلی پشت NAT مشترک با rate-limit واقعی و بدون کاهش امنیت.
8. اثبات budget واحد ۱۶ tile و sticky active-speaker selection زیر WebRTC واقعی، topology بزرگ و churn طولانی؛ unit test به‌تنهایی معیار FPS/decoder/DOM نیست.
9. تکمیل keyboard/focus و تعریف fallback قابل دسترس برای whiteboard؛ chat live-region و locale زمان در کد و تست ماشینی اصلاح شده‌اند.
10. human accessibility review با keyboard-only، NVDA، zoom 200%، RTL/LTR، contrast و اعلان reconnect/error.
11. fault injection برای قطع موقت شبکه، reconnect، device revoke، browser sleep/wake، token expiration و disconnect هم‌زمان.
12. پیاده‌سازی provider webhook یا backend reaper برای بستن attendance رهاشده پس از hard crash/offline، با idempotency، audit و تست recovery.
13. soak حداقل دو ساعته با نمونه‌برداری CPU/memory/network و شرط عدم رشد حافظه/DOM/listener.
14. اجرای qualification روی commit تمیز و قابل checkout؛ artifact immutable یا WORM، hash و configuration snapshot.
15. صفر P0/P1 باز با owner، evidence و retest مشخص.
16. تأیید انسانی taskهای اصلی: میزبان، cohost، presenter، attendee و guest؛ هر task با success rate، زمان انجام و خطای کاربر.

## 10. حلقه تکرارپذیر Qualification

هر iteration باید شناسه یکتا داشته باشد و artifact قبلی هرگز overwrite نشود. شکست هر gate، loop جاری را متوقف می‌کند؛ پس از اصلاح، اجرای تازه با runId تازه آغاز می‌شود.

| Gate | ورودی/عمل | معیار پاس | artifact لازم |
|---|---|---|---|
| G0 — Source integrity | checkout commit مشخص، working tree تمیز، snapshot config و tool versions | commit قابل بازتولید؛ secret scan پاس | manifest، git revision، hashes، tool versions |
| G1 — Functional regression | backend full suite، frontend collaboration suite، lint، build | صفر failure | TRX/JUnit، lint/build logs |
| G2a — ControlPlane functional 50+100 | سه loop چرخه داخلی/مهمان، polling، attendance | هر سه loop functional پاس | per-loop TRX/log و query assertions |
| G2b — ControlPlane deployed load | بار HTTP واقعی با concurrency و duration ثبت‌شده | success≥99%؛ unexpected 429=0؛ 5xx≤0.5%؛ p95≤500ms؛ p99≤1000ms | raw requests، histogram و aggregate metrics |
| G3 — MediaSmoke | CLI تأییدشده روی deployment واقعی | سه loop؛ track delivery≥99%، latency p95≤200ms، drops≤2% | CLI output، server/resource metrics |
| G4 — Local150 | ۱۵۰ media client با load generator مستقل | سه loop؛ limits host پاس؛ no crash/leak | topology، bandwidth، CPU/memory، per-loop metrics |
| G5 — Browser QoE | browser agents با ترکیب camera/mic/share و join از ERP | تمام thresholdهای QoE پاس | WebRTC stats time-series و join traces |
| G6 — Network/device UAT | LAN، external Internet، mobile، NAT، UDP/TCP/TURN | join/reconnect موفق و کیفیت داخل limits | network path، device/browser matrix، screenshots/logs |
| G7 — Human UX/a11y | task scripts برای پنج نقش و مهمان، NVDA/keyboard/RTL | success≥95%، خطای critical=0، P0/P1=0 | task timing، observation، issue list، sign-off |
| G8 — Soak/fault | دو ساعت جلسه و fault injection کنترل‌شده | no unbounded growth؛ recovery مطابق SLA تست | time-series، recovery timestamps، leak report |
| G9 — Release decision | تجمیع تمام gateها | `runOutcome=Passed` و `finalQualified=true` فقط در صورت پاس همه الزامات | signed summary + links to immutable evidence |

وضعیت فعلی G3: سه process اجرا شده و delivery/loss پاس‌اند، ولی چون latency p95 `NotMeasured` است، G3 هنوز پاس نیست. وضعیت process و وضعیت qualification دو مفهوم مستقل‌اند.

وضعیت فعلی G2: بخش functional یعنی G2a پاس است؛ G2b هنوز اجرا/اندازه‌گیری نشده است. مانيفست ControlPlane نباید به‌عنوان شاهد p95/p99 یا success/rate-limit عددی معرفی شود.

### قواعد تکرار loop

1. baseline پیش از اصلاح ذخیره می‌شود.
2. فقط defectهای مشاهده‌شده و معیار پذیرش مشخص وارد iteration می‌شوند.
3. تست هدفمند اجرا می‌شود؛ سپس regression کامل.
4. هر failure با `correlationId/runId`، severity، owner و reproduction ثبت می‌شود.
5. بعد از اصلاح، همان سناریو و یک سناریوی مجاور دوباره اجرا می‌شود.
6. سه loop موفق باید **متوالی** باشند؛ یک failure شمارش را به صفر برمی‌گرداند.
7. نتیجه component هرگز به‌طور خودکار `finalQualified=true` نمی‌شود.
8. هیچ عدد QoE از روی احساس، screenshot یا status کلی SDK ساخته نمی‌شود.
9. recording محتوای جلسه خاموش می‌ماند؛ فقط metadata و metrics فنی مجاز ثبت می‌شوند.

## 11. تصمیم انتشار

### مجاز

- ادامه توسعه و تست داخلی کنترل‌شده.
- UAT محدود با تعداد کم کاربر و اعلام صریح «نسخه آزمایشی».
- استفاده از نتیجه ۵۰+۱۰۰ فقط برای تأیید کنترل‌پل و قواعد lifecycle.

### غیرمجاز تا بسته‌شدن gateها

- اعلام رسمی ظرفیت ۱۵۰ کاربر رسانه‌ای.
- اعلام «بدون lag صدا و تصویر».
- امتیاز یا تأیید بالای ۹/۱۰.
- استفاده production بدون تکمیل metricهای MediaSmoke، Local150، Browser QoE، external device/TURN و human accessibility sign-off.

نتیجه نهایی این ممیزی: **کنترل‌پل qualified در محدوده تست خود است و MediaSmoke کوچک delivery/loss موفق مشاهده‌شده دارد؛ اما latency و QoE کامل نیست و محصول جلسه ۱۵۰ نفره از نظر رسانه و عملیات همچنان `NotQualified` است.**
