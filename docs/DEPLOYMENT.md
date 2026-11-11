# استقرار Hadron ERP System

این سند روش استاندارد استقرار نسخه فعلی روی یک میزبان Linux با Docker Compose را تعریف می‌کند. فایل `docker-compose.yml` همچنان مخصوص توسعه محلی است؛ استقرار عملیاتی فقط با `compose.production.yml` انجام می‌شود.

## معماری اجرا

مسیر درخواست به شکل زیر است:

```text
Browser (HTTPS/WSS)
  -> Caddy (TLS termination / reverse proxy)
     -> ERP domain /api/* : ASP.NET Core API
     -> ERP domain /hubs/* : ASP.NET Core SignalR/WebSocket
     -> ERP domain, other paths : Nginx + React SPA
     -> RTC domain : LiveKit signaling

API
  -> PostgreSQL 18
  -> MinIO
  -> Data Protection key volume

Browser (WebRTC media)
  -> LiveKit 7881/TCP fallback or configured UDP media range
```

پورت‌های `80/tcp`، `443/tcp` و `443/udp` برای HTTPS/HTTP3، پورت `7881/tcp` برای WebRTC fallback و بازه تنظیم‌شده `50000-50100/udp` برای رسانه LiveKit روی میزبان منتشر می‌شوند. پورت داخلی `7880` فقط از Caddy قابل‌دسترسی است و نباید مستقیماً public شود. PostgreSQL، MinIO و کنسول MinIO هیچ پورت عمومی ندارند. شبکه `data` داخلی است و Caddy به آن دسترسی ندارد.

## پیش‌نیازها

- Linux پشتیبانی‌شده و به‌روز
- Docker Engine و Docker Compose v2
- DNS دامنه ERP و زیر‌دامنه مستقل RTC که هر دو به IP میزبان اشاره کنند
- دسترسی ورودی به پورت‌های 80 و 443 برای صدور و تمدید TLS
- دسترسی کلاینت‌های مجاز به `7881/tcp` و بازه UDP تنظیم‌شده LiveKit
- فضای جداگانه و مانیتورشده برای volumeها و backupها
- زمان سیستم و NTP صحیح

## تنظیم محیط

فایل نمونه را کپی کنید و تمام مقادیر `CHANGE_ME` را با secret تصادفی و یکتا جایگزین کنید:

```bash
cp deploy/production.env.example deploy/production.env
chmod 600 deploy/production.env
```

فایل `deploy/production.env` توسط Git نادیده گرفته می‌شود و نباید commit، ایمیل یا در پیام‌رسان ارسال شود. برای imageهای عملیاتی از tag تغییرناپذیر استفاده کنید؛ `latest` برای production مجاز نیست.

اعتبارسنجی configuration بدون بالا آوردن سرویس‌ها:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml config --quiet
```

## Build و استقرار اولیه

ابتدا imageهای API، UI، migration و MinIO را بسازید. MinIO از فایل‌های release رسمی GitHub با checksum ثابت ساخته می‌شود؛ دسترسی به GitHub و Docker Hub لازم است:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml \
  --profile maintenance build api web migrate minio maintenance-tools
```

سپس فقط storageها را بالا بیاورید و منتظر سلامت آن‌ها بمانید:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml \
  up -d --wait --wait-timeout 180 postgres minio
```

### Migration کنترل‌شده

API در زمان startup هیچ migrationای اجرا نمی‌کند. اجرای migration یک اقدام صریح اپراتور است:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml \
  --profile maintenance run --rm migrate
```

برای هر ارتقای واقعی، ترتیب الزامی این است: backup معتبر، بررسی migration در Staging/UAT، پنجره تغییر، اجرای migration، smoke test و سپس تأیید rollout. migration مخرب یا ناسازگار با rollback بدون تأیید مالک سامانه مجاز نیست.

پس از migration، کل stack را بالا بیاورید:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml \
  up -d --wait --wait-timeout 240
```

و وضعیت را کنترل کنید:

```bash
docker compose --env-file deploy/production.env -f compose.production.yml ps
docker compose --env-file deploy/production.env -f compose.production.yml logs --tail=200 api caddy livekit
```

درخواست داخلی `GET /health/live` زنده بودن process و `GET /health/ready` دسترسی API به PostgreSQL و object storage خصوصی عکس‌های پرسنلی را بررسی می‌کند. خرابی هر dependency الزامی، readiness را با `503` fail-closed می‌کند، ولی liveness تا زمانی که process زنده است مستقل می‌ماند. endpoint عمومی اصلی باید از مسیر HTTPS دامنه تست شود.

## پایداری session و کلیدها

کلیدهای ASP.NET Core Data Protection در volume دائمی `hadron-data-protection-keys` و مسیر `/home/hadron/.aspnet/DataProtection-Keys` قرار می‌گیرند. `data-protection-init` مالکیت آن را به UID/GID ثابت `10001` می‌دهد. حذف یا تعویض این volume باعث نامعتبر شدن cookieهای ورود و tokenهای مبتنی بر Data Protection می‌شود.

متغیر canonical برنامه یعنی `DATA_PROTECTION_KEYS_PATH` در Compose روی همین مسیر تنظیم شده است و application discriminator نیز در backend به‌صورت ثابت `Hadron.Erp` است. در نتیجه تمام replicaها و restartها از key ring مشترک و پایدار استفاده می‌کنند.

## Backup

اسکریپت backup برای سازگاری داده، API/UI/proxy را کوتاه‌مدت متوقف می‌کند و سپس موارد زیر را در یک پوشه timestampدار ذخیره می‌کند:

- PostgreSQL با فرمت native/custom؛
- bucketهای تعریف‌شده در `MINIO_BACKUP_BUCKETS`؛
- کلیدهای Data Protection؛
- manifest نسخه imageها؛
- checksum تمام فایل‌ها.

اجرا:

```bash
chmod +x scripts/backup-production.sh scripts/restore-production.sh
scripts/backup-production.sh --env deploy/production.env --output /srv/hadron-backups
```

خروجی backup شامل داده حساس و کلید رمزنگاری session است. پوشه باید با encryption به storage خارج از میزبان منتقل شود. اسکریپت عمداً retention یا حذف خودکار ندارد؛ سیاست retention باید در storage پشتیبان و با مسیر دقیق مدیریت شود.

## Restore

Restore داده موجود PostgreSQL را جایگزین و bucketهای موجود در backup را mirror می‌کند؛ بنابراین destructive است و فقط با switch صریح اجرا می‌شود:

```bash
scripts/restore-production.sh \
  --env deploy/production.env \
  --backup /srv/hadron-backups/hadron-erp-YYYYMMDDTHHMMSSZ \
  --confirm-restore
```

قبل از production، restore را در محیط جداگانه Staging انجام دهید، checksum را کنترل کنید و smoke test ورود، عکس پروفایل، master data، BOM، Forecast، Inventory و MRP را اجرا کنید. اگر restore نیمه‌کاره بماند، اسکریپت عمداً API/UI/Caddy را خاموش نگه می‌دارد.

## Rollout و rollback

برای rollout، `API_IMAGE` و `WEB_IMAGE` را به tag جدید تغییر دهید، imageها را build/pull کنید، migration کنترل‌شده را اجرا کنید و سپس سرویس‌ها را recreate کنید. وضعیت health و log باید قبل از اعلام موفقیت پایدار باشد.

Rollback کد با بازگرداندن tag قبلی انجام می‌شود. اگر migration با نسخه قبلی سازگار نباشد، ابتدا سرویس‌های برنامه متوقف و backup قبل از تغییر restore می‌شود. هر backup باید با نسخه image ثبت‌شده در `manifest.txt` نگهداری شود.

## کنترل‌های عملیاتی

- logها روی stdout/stderr هستند و rotation محلی روی 5 فایل 20MB تنظیم شده است؛ آن‌ها را به سامانه مرکزی log ارسال کنید.
- volumeهای PostgreSQL، MinIO، Caddy و Data Protection را مانیتور ظرفیت کنید.
- secretها را دوره‌ای rotate و فایل env را فقط برای حساب deployment قابل‌خواندن نگه دارید.
- MinIO console عمداً منتشر نشده است؛ عملیات مدیریتی باید از شبکه مدیریتی کنترل‌شده انجام شود.
- Caddy data شامل material مربوط به TLS است و volume دائمی دارد، اما backup آن در اسکریپت عمومی قرار نگرفته است؛ Caddy می‌تواند certificate را مجدداً صادر کند و private key نباید وارد backup عملیاتی عادی شود.
- پس از ایجاد اولین System Administrator، `IDENTITY_BOOTSTRAP_TOKEN` را rotate کنید؛ مقدار خالی یا پیش‌فرض در production مجاز نیست.

## ارتباطات، فایل و تماس سازمانی

فایل‌های Collaboration در bucket خصوصی `MINIO_COLLABORATION_BUCKET` ذخیره می‌شوند. این bucket باید به فهرست backup/restore افزوده شود و نباید public شود. محدودیت API برای هر فایل 25MB است؛ reverse proxy نیز باید محدودیت سازگار داشته باشد.

تماس صوتی/تصویری مبتنی بر LiveKit self-hosted است. در Development می‌توان profile محلی را پس از تنظیم secretهای واقعی فعال کرد:

```bash
docker compose --profile collaboration-calls up -d livekit
```

در Production، `LIVEKIT_SITE_ADDRESS` باید یک نام DNS مستقل مانند `rtc.example.com` باشد و `LIVEKIT_URL` دقیقاً با `wss://` به همان نام اشاره کند. Caddy اتصال TLS/WSS را روی این نام خاتمه می‌دهد و signaling را به پورت داخلی `7880` می‌فرستد. `LIVEKIT_NODE_IP` باید IP قابل‌دسترسی همان میزبان از شبکه کاربران باشد؛ IP داخلی container نباید به‌عنوان ICE candidate منتشر شود. `LIVEKIT_API_SECRET` یک secret سمت سرور است و نباید با `VITE_` منتشر شود.

Fallback رسانه روی `7881/tcp` و بازه UDP `50000-50100` باید فقط برای شبکه کاربران مجاز در firewall باز شوند. بازه تنظیم‌شده در env، Compose و firewall باید یکسان باشد. اسکریپت `scripts/windows/Configure-HadronErpCompanyLan.ps1 -Action Enable` این مسیرها را برای UAT ویندوزی همراه frontend، محدود به `HostAddress` و `RemoteSubnet` انتخاب‌شده ایجاد می‌کند و باید یک‌بار در PowerShell دارای دسترسی Administrator اجرا شود.

صفحه ERP نیز باید از HTTPS معتبر بارگذاری شود؛ مرورگر روی HTTP با IP شبکه اجازه میکروفن، دوربین و اشتراک صفحه نمی‌دهد. سیاست Caddy فقط همان origin ERP را برای `camera`، `microphone` و `display-capture` مجاز می‌کند و رضایت صریح کاربر در مرورگر همچنان الزامی است. مسیر `/hubs/*` باید در Caddy مستقیماً به API فرستاده شود تا SignalR و WebSocket به‌جای HTML برنامه پاسخ بگیرند. گواهی خودامضا و هشدار TLS نباید دور زده شود.

این پیکربندی مسیر مستقیم UDP و fallback TCP را برای شبکه داخلی فراهم می‌کند. برای کاربران خارج شرکت، VPNهای محدود یا شبکه‌هایی که UDP و `7881/tcp` را مسدود می‌کنند، TURN/TLS باید جداگانه طراحی و عملیاتی شود؛ بدون آن پذیرش تماس اینترنتی مجاز نیست. پس از هر تغییر شبکه، تماس صوتی و تصویری با دو حساب کاربری متفاوت و دو دستگاه واقعی از همان شبکه آزمون شود. v1 ضبط تماس ندارد؛ افزودن recording نیازمند تصمیم جداگانه حریم خصوصی، retention و ظرفیت storage است.

## معیار پذیرش استقرار

استقرار زمانی آماده UAT است که:

1. `docker compose config --quiet` بدون خطا باشد؛
2. build هر سه target موفق شود؛
3. migration فقط با profile دستی اجرا شود؛
4. همه سرویس‌های عادی healthy/running باشند؛
5. PostgreSQL و MinIO از بیرون میزبان قابل دسترسی نباشند؛
6. HTTPS معتبر و redirect از HTTP برقرار باشد؛
7. پس از restart کامل stack، sessionهای معتبر به دلیل حفظ Data Protection key از بین نروند؛
8. backup، checksum و restore drill در Staging موفق باشند؛
9. smoke test مجوزها و vertical slice اصلی بدون regression عبور کند.
10. صفحه تماس روی HTTPS و signaling روی WSS معتبر باشند و header مرورگر `camera=(self)` و `microphone=(self)` را اعمال کند؛
11. انتشار و دریافت واقعی audio/video از دو دستگاه و دو حساب متفاوت روی شبکه هدف، هم در مسیر UDP و هم fallback TCP، تأیید شود.
12. درخواست `POST /hubs/collaboration/negotiate` از دامنه ERP به API برسد و اتصال WebSocket احراز هویت‌شده پس از reconnect دوباره عضو گروه مجاز شود.
