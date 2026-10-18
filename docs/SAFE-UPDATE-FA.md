# دریافت تغییرات بدون بازنویسی کار محلی

شاخهٔ تحویل **arena/d5f0785f-hadron** است، نه `arena/a9fe3573-hadron`. هیچ PR یا force push ساخته/اجرا نشده است. بدون دیدن وضعیت سیستم شما نمی‌توان تضمین نبود conflict داد. راه‌های زیر به‌جای بازنویسی اجباری، در وضعیت ناسازگار متوقف می‌شوند.

## روش پیشنهادی: clone جدا (پوشهٔ فعلی دست‌نخورده می‌ماند)

از پوشهٔ والد پروژه، در PowerShell یا ترمینال:

```powershell
git clone --single-branch --branch arena/d5f0785f-hadron https://github.com/sepehrsylvanus/hadron.git hadron-nx-guide
cd hadron-nx-guide
```

نام `hadron-nx-guide` باید هنوز وجود نداشته باشد. سپس `nx-erp-lab/implementation-guide.html` را باز کنید. این کار دیتابیس یا Docker را راه‌اندازی/حذف نمی‌کند. از داخل clone قدیمی اجرا نکنید تا clone تودرتو نسازید.

## اگر حتماً می‌خواهید همان clone فعلی را pull کنید

ابتدا از تغییرات محلی و `.env`ها نسخهٔ امن خارج از ریپو بگیرید. بعد داخل **ریشهٔ همان clone**، در PowerShell:

```powershell
$ErrorActionPreference = 'Stop'
$branch = git branch --show-current
if ($LASTEXITCODE -ne 0) { throw 'Not a Git repository.' }
if ($branch.Trim() -ne 'arena/d5f0785f-hadron') {
  throw 'Wrong branch. Do not pull another branch into this one; use a separate clone.'
}
$dirty = git status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git status.' }
if ($dirty) { throw 'Local changes exist. Stop and use a separate clone, or preserve/reconcile them manually first.' }
git pull --ff-only origin arena/d5f0785f-hadron
if ($LASTEXITCODE -ne 0) { throw 'Update stopped. Do not reset, force, or resolve by discarding local files.' }
git status --short --branch
```

معادل کوتاه برای working tree **تمیز و شاخهٔ صحیح**:

```bash
git pull --ff-only origin arena/d5f0785f-hadron
```

اگر `Not possible to fast-forward` یا هشدار overwrite دیدید، **توقف کنید** و clone جدا بگیرید. دستورهای `reset --hard`، `clean -fd` و `push --force` راه‌حل این تحویل نیستند. `stash pop` هم تضمین بدون conflict ندارد.

## پس از دریافت

- کد قبلی اکنون `main-erp/` است؛ تمام دستورهای README قبلی از داخل این پوشه اجرا می‌شوند.
- `run-hadron.bat` ریشه فقط به لانچر داخلی ارجاع می‌دهد.
- `.env`، `production.env` و سایر فایل‌های ignored شخصی در pull منتقل نمی‌شوند. در همان clone ممکن است در مسیر قدیمی باقی بمانند؛ بدون چاپ یا commit کردن secret، برای اپ فعلی در `main-erp/.env` تنظیمشان کنید. قبل از کپی، فایل مقصد موجود را حفظ کنید. فایل‌های محلی دیگری مثل `appsettings.Development.json` نیز بررسی شوند.
- پروژهٔ Compose اپ اصلی همچنان `hadron-erp` است. هیچ `docker compose down -v` اجرا نکنید. به‌دلیل تغییر مسیر، bind mountهای محلی را قبل از اجرا بررسی کنید. Task/Scheduled Taskهای ویندوز که مسیر مطلق قدیمی دارند باید از helperهای داخل `main-erp/scripts/windows` بازتنظیم شوند.
- آزمایش جدید فقط دیتابیس `hadron_nx_lab` روی پورت `5544` و volume مستقل `hadron-nx-lab-pg` دارد. فایل تنظیمات قبلی را برای آزمایش جدید کپی نکنید.
- نیاز Node اپ قبلی 26.x است؛ دستورهای راهنمای Nx با Node 22.22.3 بررسی شده‌اند. هنگام تعویض پروژه نسخهٔ runtime را بررسی کنید.
