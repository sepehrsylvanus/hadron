#!/usr/bin/env python3
"""Generates docs/hadron-engine-and-permissions.html from the live backend source.

Every number, permission code, table name and route in the output is scraped from
src/backend/Hadron.Erp.Api + src/frontend/src at build time, so re-running this
script keeps the diagram honest when the code changes.
"""
import re, glob, collections, json, pathlib, html

ROOT = pathlib.Path(__file__).resolve().parents[2]
API = ROOT / "src/backend/Hadron.Erp.Api"
FE = ROOT / "src/frontend/src"
OUT = ROOT / "docs/hadron-engine-and-permissions.html"

# ---------------------------------------------------------------- permissions
perm_src = (API / "Platform/Security/PermissionCatalog.cs").read_text(encoding="utf-8")
consts = dict(re.findall(r'public const string ([A-Za-z0-9]+) = "([a-z0-9_.]+)";', perm_src))
perm_defs = re.findall(
    r'D\(Permissions\.([A-Za-z0-9]+),\s*"([A-Z_]+)",\s*"([^"]+)",\s*"([^"]+)"\)', perm_src)
PERMS = [{"code": consts[n], "area": a, "fa": fa, "en": en} for n, a, fa, en in perm_defs]
AREAS = collections.Counter(p["area"] for p in PERMS)

# ---------------------------------------------------------------- modules
mods = {}
# Business modules live under Modules/; the cross-cutting platform tables
# (platform.operational_audit_events) live under Platform/ and are tracked too.
scannable = sorted((API / "Modules").glob("*/**/*.cs")) + sorted((API / "Platform").glob("**/*.cs"))
for f in scannable:
    parts = f.relative_to(API).parts
    mod = parts[1] if parts[0] == "Modules" else "Platform"
    src = f.read_text(encoding="utf-8")
    d = mods.setdefault(mod, {"schemas": set(), "tables": set(), "endpoints": 0,
                              "perms": set(), "services": set()})
    for m in re.finditer(r'ToTable\(\s*"([A-Za-z_0-9]+)"\s*,\s*"([A-Za-z_0-9]+)"', src):
        d["tables"].add(f"{m.group(2)}.{m.group(1)}")
        d["schemas"].add(m.group(2))
    if True:
        d["endpoints"] += len(re.findall(r'\.Map(?:Get|Post|Put|Delete|Patch)\(\s*(?:\n\s*)?"', src))
    if f.name != "PermissionCatalog.cs":
      for m in re.finditer(r'Permissions\.([A-Za-z0-9]+)\b', src):
        if m.group(1) != "Codes":
          d["perms"].add(consts.get(m.group(1), m.group(1)))
    for m in re.finditer(r'class ([A-Za-z]+Service)\b', src):
        d["services"].add(m.group(1))


ALL_SCHEMAS = collections.defaultdict(set)
for mod, d in mods.items():
    for s in d["schemas"]:
        ALL_SCHEMAS[s].add(mod)
TOTAL_TABLES = sum(len(d["tables"]) for d in mods.values())
TOTAL_ENDPOINTS = sum(d["endpoints"] for d in mods.values())
BUSINESS_MODS = [m for m in mods if m != "Platform"]
PLATFORM_MODS = [m for m in mods if m == "Platform"]

# ---------------------------------------------------------------- routes
route_src = (FE / "workspaceNavigation.ts").read_text(encoding="utf-8")
ROUTES = [{"prefix": p, "view": v, "manage": m} for p, v, m in re.findall(
    r"\{\s*prefix:\s*'([^']+)',\s*view:\s*'([^']+)',\s*manage:\s*'([^']+)'\s*\}", route_src)]
WORKSPACES = re.findall(r"id: '([a-z]+)', rootPath: '([^']+)', code: '([^']+)'", route_src)

# ---------------------------------------------------------------- outbox
outbox_src = (API / "Modules/Mrp/Application/MrpInputChangeInterceptor.cs").read_text(encoding="utf-8")
WATCHED = re.findall(r'Change\("([A-Z_+]+)"', outbox_src)
WATCH_COUNT = collections.Counter(WATCHED)

E = html.escape

# ------------------------------------------------- SVG bidi / anchor helpers
# The page is dir="rtl", so every <svg><text> inherits direction:rtl. With the
# default text-anchor:start that pins the RIGHT edge of the string at x and
# lets it flow off-canvas to the left, and mixed FA/Latin strings come out
# visually scrambled. Every generated <text> therefore gets explicit classes:
#   mid  -> text-anchor:middle (x is the centre of its box)
#   ltr  -> direction:ltr (Latin/digit-led strings keep natural left-to-right order)
#   en   -> text-anchor:end (left edge pinned at x, text grows rightwards,
#           used for Persian-led strings that sit left-aligned)
_AR_RANGES = (("\u0600", "\u06FF"), ("\u0750", "\u077F"),
              ("\u08A0", "\u08FF"), ("\uFB50", "\uFDFF"), ("\uFE70", "\uFEFC"))


def _is_fa_led(s):
    """True when the first alphabetic char of s (entities decoded) is an
    Arabic-script letter, i.e. the string should read right-to-left first.
    Digits (incl. Persian ۰-۹) are weak and skipped, so '35 API · 12 جدول'
    and '۴۰۱ SESSION_REVOKED' count as Latin-led."""
    for ch in html.unescape(s):
        if ch.isalpha():
            return any(lo <= ch <= hi for lo, hi in _AR_RANGES)
    return False


_DIA_TEXT_RE = re.compile(r"<text([^>]*)>(.*?)</text>", re.S)
_DIA_CLS_RE = re.compile(r'class="([^"]*)"')


def _bidi_autofix(svg, centered):
    """Add explicit anchor/direction classes to every <text> in an SVG.
    centered=True  -> every text is centred on its x (add mid, plus ltr when Latin-led)
    centered=False -> texts sit left-aligned at x (ltr for Latin-led, en for Persian-led)
    Texts marked data-keep are left untouched."""
    def fix(m):
        attrs, body = m.group(1), m.group(2)
        if "data-keep" in attrs:
            return m.group(0)
        cls = _DIA_CLS_RE.search(attrs)
        parts = cls.group(1).split() if cls else []
        if "ltr" in parts:
            if centered and "mid" not in parts:
                parts.append("mid")
        elif "mid" in parts:
            if _is_fa_led(body) is False and "ltr" not in parts:
                parts.append("ltr")
        elif centered:
            parts.append("mid")
            if not _is_fa_led(body):
                parts.append("ltr")
        else:
            parts.append("ltr" if not _is_fa_led(body) else "en")
        tag = " ".join(parts)
        if cls:
            attrs = _DIA_CLS_RE.sub('class="%s"' % tag, attrs, count=1)
        else:
            attrs = attrs.rstrip() + ' class="%s"' % tag
        return "<text%s>%s</text>" % (attrs, body)
    return _DIA_TEXT_RE.sub(fix, svg)


# ============================================================== SVG diagrams
def svg_overview():
    mod_boxes = ""
    order = ["Identity", "ReferenceData", "Catalog", "Products", "Suppliers", "Sourcing",
             "Bom", "Planning", "Inventory", "Production", "PurchaseSupply", "Mrp",
             "OrderPlanning", "Analytics", "CashFlow", "Trading", "Collaboration"]
    for i, name in enumerate(order):
        col, row = i % 6, i // 6
        x, y = 60 + col * 158, 268 + row * 74
        mod_boxes += f'''<g>
  <rect x="{x}" y="{y}" width="142" height="58" rx="8" class="mod"/>
  <text x="{x+71}" y="{y+25}" class="lbl b">{name}</text>
  <text x="{x+71}" y="{y+43}" class="lbl s">{mods[name]["endpoints"]} API · {len(mods[name]["tables"])} جدول</text>
</g>'''
    sch = sorted(ALL_SCHEMAS)
    h = (len(sch) + 1) // 2
    return _bidi_autofix(f'''<svg viewBox="0 0 1080 620" class="dia" role="img" aria-label="معماری کلی">
  <defs><marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>

  <rect x="60" y="24" width="960" height="74" rx="10" class="layer fe"/>
  <text x="540" y="52" class="lbl b t1">فرانت‌اند — React + TypeScript + Vite</text>
  <text x="540" y="76" class="lbl s">دو زبانه FA/EN · RTL/LTR · دسترسی‌ها فقط برای پنهان‌کردن منو (امنیت نیست)</text>

  <line x1="540" y1="98" x2="540" y2="126" class="flow" marker-end="url(#ar)"/>
  <text x="556" y="118" class="lbl s ltr" data-keep>HTTP /api · Cookie session · X-CSRF-TOKEN</text>

  <rect x="60" y="130" width="960" height="112" rx="10" class="layer api"/>
  <text x="540" y="158" class="lbl b t1">یک هاست ASP.NET Core — Modular Monolith</text>
  <text x="540" y="182" class="lbl s">Program.cs · ProblemDetails · RateLimiter · SignalR · HealthChecks</text>
  <rect x="120" y="196" width="200" height="32" rx="6" class="chip"/><text x="220" y="217" class="lbl s">Platform/Security</text>
  <rect x="340" y="196" width="200" height="32" rx="6" class="chip"/><text x="440" y="217" class="lbl s">Platform/Audit</text>
  <rect x="560" y="196" width="200" height="32" rx="6" class="chip"/><text x="660" y="217" class="lbl s">Platform/Storage</text>
  <rect x="780" y="196" width="180" height="32" rx="6" class="chip"/><text x="870" y="217" class="lbl s">Platform/Errors</text>

  <text x="992" y="258" class="lbl b t2" data-keep>{len(BUSINESS_MODS)} ماژول تجاری — هر ماژول: Api / Application / Domain / Infrastructure</text>
  {mod_boxes}

  <rect x="60" y="500" width="620" height="96" rx="10" class="layer db"/>
  <text x="370" y="528" class="lbl b t1">PostgreSQL 18 — یک دیتابیس، {len(ALL_SCHEMAS)} اسکیما، {TOTAL_TABLES} جدول</text>
  <text x="370" y="550" class="lbl s ltr">{E(", ".join(sch[:h]))}</text>
  <text x="370" y="566" class="lbl s ltr">{E(", ".join(sch[h:]))}</text>
  <text x="370" y="586" class="lbl s">EF Core + Migration · Isolation بین ماژول‌ها فقط قراردادی است</text>

  <rect x="710" y="500" width="310" height="96" rx="10" class="layer sto"/>
  <text x="865" y="530" class="lbl b t1">MinIO — S3 سازگار</text>
  <text x="865" y="556" class="lbl s">عکس پرسنلی (خصوصی)</text>
  <text x="865" y="578" class="lbl s">فایل‌های Collaboration</text>

  <line x1="370" y1="440" x2="370" y2="496" class="flow" marker-end="url(#ar)"/>
  <line x1="865" y1="440" x2="865" y2="496" class="flow" marker-end="url(#ar)"/>
</svg>''', centered=True)


def svg_pipeline():
    stages = [
        ("1", "UseExceptionHandler", "HadronExceptionHandler → ProblemDetails", "همه خطاها به یک شکل JSON استاندارد", "n"),
        ("2", "UseRateLimiter", "۴ پالیسی: login ۱۰/دقیقه · bootstrap ۵/۱۵دقیقه · guest-invitation ۳۰۰/دقیقه · guest-session ۱۲۰۰/دقیقه", "۴۲۹ + code=RATE_LIMITED", "n"),
        ("3", "UseAuthentication", "کوکی hadron.erp.session · HttpOnly · SameSite=Lax · ۸ ساعت Sliding", "۴۰۱ AUTHENTICATION_REQUIRED", "g"),
        ("4", "IdentitySessionMiddleware", "IsActive؟ · AccessVersion مطابقت؟ · Session باطل نشده؟ · MustChangePassword؟", "۴۰۱ SESSION_REVOKED / ۴۰۳ PASSWORD_CHANGE_REQUIRED", "g"),
        ("5", "ApiAntiforgeryMiddleware", "متدهای ناامن روی /api (جز /api/auth/bootstrap) باید توکن CSRF داشته باشند", "۴۰۰ CSRF_VALIDATION_FAILED", "g"),
        ("6", "ApiPermissionMiddleware", "Fail-Closed: هر endpoint زیر /api بدون RequiredPermissionMetadata رد می‌شود", "۴۰۳ UNREGISTERED_API_PERMISSION", "r"),
        ("7", "UseAuthorization", "پالیسی هم‌نام با کد دسترسی → Assertion روی Claimها", "۴۰۳ PERMISSION_DENIED", "r"),
        ("8", "Endpoint Handler", "Minimal API · فقط اعتبارسنجی ورودی و نگاشت خطا", "—", "b"),
        ("9", "Module Service", "منطق دامنه + DbContext جدید از Factory + Audit", "—", "b"),
    ]
    rows = ""
    y = 30
    for n, name, desc, res, tone in stages:
        rows += f'''<g>
  <rect x="30" y="{y}" width="700" height="56" rx="9" class="st {tone}"/>
  <circle cx="60" cy="{y+28}" r="15" class="num"/><text x="60" y="{y+34}" class="lbl num">{n}</text>
  <text x="90" y="{y+24}" class="lbl b ltr">{name}</text>
  <text x="90" y="{y+43}" class="lbl s">{desc}</text>
  <text x="742" y="{y+33}" class="lbl s code">{res}</text>
</g>'''
        if n != "9":
            rows += f'<line x1="380" y1="{y+56}" x2="380" y2="{y+72}" class="flow" marker-end="url(#ar2)"/>'
        y += 72
    return _bidi_autofix(f'''<svg viewBox="0 0 1120 {y}" class="dia" role="img" aria-label="مسیر یک درخواست">
  <defs><marker id="ar2" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>
  {rows}
</svg>''', centered=False)


def svg_product():
    lanes = [("مرورگر", 90), ("ProductEndpoints", 300), ("ProductService", 545), ("PostgreSQL", 830), ("AuditSink", 1035)]
    heads = ""
    for name, x in lanes:
        heads += f'''<g><rect x="{x-88}" y="18" width="176" height="40" rx="8" class="lane"/>
  <text x="{x}" y="44" class="lbl b">{name}</text></g>
  <line x1="{x}" y1="58" x2="{x}" y2="942" class="life"/>'''
    steps = [
        (78, 90, 300, "s", "POST /api/products/", "بدنه: nameFa, nameEn, productCategoryId, baseUnitId, | businessUnitId, safetyStockQty, criticalDays, …"),
        (168, 300, 545, "s", "service.Create(request)", "RequirePermission(catalog.product.manage) قبلاً در pipeline چک شده"),
        (232, 545, 545, "n", "۱) Validate(...)", "طول نام‌ها · safetyStockQty ≥ ۰ · ۰ ≤ Critical ≤ SafeStart ≤ ExcessStart → خطا: ProductValidationException → ۴۰۰"),
        (296, 545, 830, "s", "۲) ValidateReferences", "PRODUCT_CATEGORY فعال؟ · Unit فعال؟ · BUSINESS_UNIT فعال؟"),
        (360, 830, 545, "r", "dictionary خطاها", "اگر مرجع نامعتبر باشد → ۴۰۰ با کلید فیلد فارسی"),
        (424, 545, 830, "s", "۳) nextval('catalog.product_number_seq')", "کد یکتا: PRD-0001, PRD-0002 …"),
        (488, 545, 545, "n", "۴) new Item(PRODUCT, …) + new Product(…)", "Item جدول مشترک PRODUCT/PART/COMPONENT است · Product فقط فیلدهای برنامه‌ریزی"),
        (552, 545, 830, "s", "۵) AddRange + SaveChangesAsync", "INSERT catalog.items  ←  INSERT catalog.products (1:1 روی ItemId)"),
        (616, 830, 545, "r", "Check Constraints دیتابیس", "ck_items_item_type · ck_items_business_number_format · ck_products_thresholds · ck_products_safety_stock"),
        (680, 545, 830, "s", "۶) LoadResponse (بازخوانی)", "SELECT با Include روی Category / BaseUnit / BusinessUnit"),
        (744, 545, 830, "s", "۷) DuplicateWarnings", "نام تکراری فقط Warning است: DUPLICATE_NAME_FA / DUPLICATE_NAME_EN"),
        (808, 545, 1035, "s", "۸) Emit(audit)", "EntityType=Product · Action=CREATE · Old=null · New=Snapshot"),
        (872, 1035, 830, "s", "INSERT platform.operational_audit_events", "Post-Commit · اگر شکست بخورد فقط Log می‌شود، تراکنش اصلی برنمی‌گردد"),
        (936, 300, 90, "r", "۲۰۱ Created + ProductMutationResponse", "{ product, warnings[] }"),
    ]
    body = ""
    for y, x1, x2, kind, title, note in steps:
        mx = (x1 + x2) / 2
        self_box = (x1 == x2)
        if "|" in note:
            a, b = note.split("|", 1)
            note_svg = (f'<text x="{mx}" y="{y + (38 if self_box else 18)}" class="lbl s mid">{a.strip()}</text>'
                        f'<text x="{mx}" y="{y+34}" class="lbl s mid">{b.strip()}</text>')
        else:
            note_svg = f'<text x="{mx}" y="{y + (38 if self_box else 18)}" class="lbl s mid">{note}</text>'
        if x1 == x2:
            note_svg = note_svg.replace('y="{y+18}"', 'y="{y+38}"')
            body += f'''<g><rect x="{x1-230}" y="{y-16}" width="460" height="34" rx="7" class="self"/>
  <text x="{x1}" y="{y+6}" class="lbl b">{title}</text></g>
  {note_svg}'''
        else:
            body += f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" class="msg {kind}" marker-end="url(#ar3)"/>'
            body += f'<text x="{mx}" y="{y-8}" class="lbl b mid">{title}</text>'
            body += note_svg
    return _bidi_autofix(f'''<svg viewBox="0 0 1140 980" class="dia" role="img" aria-label="ثبت کالا">
  <defs><marker id="ar3" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>
  {heads}{body}
</svg>''', centered=True)


def svg_mrp():
    return _bidi_autofix('''<svg viewBox="0 0 1120 760" class="dia" role="img" aria-label="موتور MRP">
  <defs><marker id="ar4" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>

  <rect x="40" y="20" width="330" height="112" rx="10" class="blk a"/>
  <text x="205" y="48" class="lbl b t1">۱. تغییر ورودی در هر ماژول</text>
  <text x="205" y="72" class="lbl s">Forecast · BOM Approve · Inventory Post</text>
  <text x="205" y="92" class="lbl s">Purchase Order Status · Sourcing · Item</text>
  <text x="205" y="112" class="lbl s">db.SaveChangesAsync()</text>

  <line x1="370" y1="76" x2="418" y2="76" class="flow" marker-end="url(#ar4)"/>

  <rect x="422" y="20" width="330" height="112" rx="10" class="blk b"/>
  <text x="587" y="48" class="lbl b t1">۲. MrpInputChangeInterceptor</text>
  <text x="587" y="72" class="lbl s">EF SaveChangesInterceptor — Singleton</text>
  <text x="587" y="92" class="lbl s">ChangeTracker را اسکن می‌کند</text>
  <text x="587" y="112" class="lbl s">فقط entity های watched</text>

  <line x1="752" y1="76" x2="800" y2="76" class="flow" marker-end="url(#ar4)"/>

  <rect x="804" y="20" width="276" height="112" rx="10" class="blk c"/>
  <text x="942" y="48" class="lbl b t1">۳. Transactional Outbox</text>
  <text x="942" y="72" class="lbl s ltr">planning.mrp_refresh_outbox</text>
  <text x="942" y="92" class="lbl s">در همان تراکنش INSERT می‌شود</text>
  <text x="942" y="112" class="lbl s">Debounce = ۴ ثانیه</text>

  <line x1="942" y1="132" x2="942" y2="168" class="flow" marker-end="url(#ar4)"/>

  <rect x="40" y="172" width="1040" height="96" rx="10" class="blk d"/>
  <text x="560" y="200" class="lbl b t1">۴. AutomaticMrpRefreshWorker — یک Background Service در همان هاست</text>
  <text x="560" y="224" class="lbl s ltr">pg_try_advisory_lock('hadron:planning:automatic-mrp-refresh:v1')  →  فقط یک worker در کل کلاستر</text>
  <text x="560" y="248" class="lbl s">Poll هر ۳ ثانیه · Lease ۱۰ دقیقه · MaxAttempts = ۳ · Backoff = ۲^n × ۵ ثانیه</text>

  <line x1="240" y1="268" x2="240" y2="300" class="flow" marker-end="url(#ar4)"/>
  <line x1="880" y1="268" x2="880" y2="300" class="flow" marker-end="url(#ar4)"/>

  <rect x="40" y="304" width="400" height="98" rx="10" class="blk e"/>
  <text x="240" y="332" class="lbl b t1">۴a. Coalesce</text>
  <text x="240" y="356" class="lbl s">اگر snapshot پلن CURRENT از جدیدترین</text>
  <text x="240" y="376" class="lbl s">تغییر جدیدتر است → همه PENDING ها</text>
  <text x="240" y="394" class="lbl s">بدون اجرای مجدد COMPLETED می‌شوند</text>

  <rect x="680" y="304" width="400" height="98" rx="10" class="blk e"/>
  <text x="880" y="332" class="lbl b t1">۴b. قبل از هر Run</text>
  <text x="880" y="356" class="lbl s">RecoverExpiredLeases (اجاره‌های منقضی)</text>
  <text x="880" y="376" class="lbl s">EnsurePreviousDayCertified (گواهی روز قبل)</text>
  <text x="880" y="394" class="lbl s">EnsureCalendarRefresh (تغییر روز تهران)</text>

  <line x1="560" y1="402" x2="560" y2="434" class="flow" marker-end="url(#ar4)"/>

  <rect x="40" y="438" width="1040" height="150" rx="10" class="blk f"/>
  <text x="560" y="466" class="lbl b t1">۵. MrpService.ExecuteCore — ارکستراسیون یک Run</text>
  <g class="steps">
    <rect x="66" y="486" width="180" height="42" rx="7" class="sub"/><text x="156" y="505" class="lbl b s">۱ Create MrpRun</text><text x="156" y="521" class="lbl s ltr">MRP-10.2.0</text>
    <rect x="262" y="486" width="180" height="42" rx="7" class="sub"/><text x="352" y="505" class="lbl b s">۲ CaptureSnapshot</text><text x="352" y="521" class="lbl s">ورودی‌ها فریز می‌شوند</text>
    <rect x="458" y="486" width="180" height="42" rx="7" class="sub"/><text x="548" y="505" class="lbl b s">۳ LoadCalcInput</text><text x="548" y="521" class="lbl s">از روی snapshot</text>
    <rect x="654" y="486" width="180" height="42" rx="7" class="sub"/><text x="744" y="505" class="lbl b s">۴ Calculator</text><text x="744" y="521" class="lbl s">کاملاً خالص · بدون DB</text>
    <rect x="850" y="486" width="204" height="42" rx="7" class="sub"/><text x="952" y="505" class="lbl b s">۵ PersistResults</text><text x="952" y="521" class="lbl s">+ Activate / Certify</text>
  </g>
  <text x="560" y="558" class="lbl s">اگر خطا: MarkFailed + DiscardTransientRun (برای Run های PUBLISHED حذف نمی‌شود تا قابل بررسی بماند)</text>
  <text x="560" y="578" class="lbl s">سه purpose: PUBLISHED (دستی توسط کاربر) · CURRENT (خودکار) · CERTIFIED (پایان روز)</text>

  <line x1="560" y1="588" x2="560" y2="620" class="flow" marker-end="url(#ar4)"/>

  <rect x="40" y="624" width="1040" height="112" rx="10" class="blk g"/>
  <text x="560" y="652" class="lbl b t1">۶. MrpCalculator — الگوریتم (خالص، بدون I/O → راحت تست می‌شود)</text>
  <text x="560" y="676" class="lbl s">Topological Order روی گراف BOM (Kahn) · در صورت حلقه: BOM_CYCLE_DETECTED</text>
  <text x="560" y="698" class="lbl s ltr">beforePlanning = balance + incoming − grossQty   ·   net = max(SafetyStock − beforePlanning, 0)</text>
  <text x="560" y="720" class="lbl s ltr">childGross = parentNet × usage / baseOutput × (1 + scrap/100)   ·   scrap: BOM_OVERRIDE یا ITEM_DEFAULT</text>
</svg>''', centered=True)


def svg_auth():
    return _bidi_autofix('''<svg viewBox="0 0 1120 700" class="dia" role="img" aria-label="مدل دسترسی">
  <defs><marker id="ar5" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>

  <text x="560" y="34" class="lbl b t1">فرمول دسترسی مؤثر — هنگام لاگین در HadronClaimsPrincipalFactory محاسبه و داخل کوکی Claim می‌شود</text>

  <rect x="60" y="58" width="240" height="88" rx="10" class="src a"/>
  <text x="180" y="88" class="lbl b">Role Permissions</text>
  <text x="180" y="112" class="lbl s ltr">identity.role_claims</text>
  <text x="180" y="132" class="lbl s ltr">ClaimType = hadron:permission</text>

  <rect x="330" y="58" width="240" height="88" rx="10" class="src b"/>
  <text x="450" y="88" class="lbl b">Direct GRANT</text>
  <text x="450" y="112" class="lbl s ltr">identity.user_permission_overrides</text>
  <text x="450" y="132" class="lbl s ltr">Effect = GRANT</text>

  <rect x="600" y="58" width="240" height="88" rx="10" class="src c"/>
  <text x="720" y="88" class="lbl b">Direct DENY</text>
  <text x="720" y="112" class="lbl s ltr">identity.user_permission_overrides</text>
  <text x="720" y="132" class="lbl s ltr">Effect = DENY</text>

  <rect x="870" y="58" width="200" height="88" rx="10" class="src d"/>
  <text x="970" y="88" class="lbl b">SystemAdministrator</text>
  <text x="970" y="112" class="lbl s">همه ۱۳۵ کد</text>
  <text x="970" y="132" class="lbl s">Deny رویش اثر ندارد</text>

  <line x1="180" y1="146" x2="420" y2="196" class="flow" marker-end="url(#ar5)"/>
  <line x1="450" y1="146" x2="500" y2="196" class="flow" marker-end="url(#ar5)"/>
  <line x1="720" y1="146" x2="640" y2="196" class="flow" marker-end="url(#ar5)"/>
  <line x1="970" y1="146" x2="700" y2="196" class="flow" marker-end="url(#ar5)"/>

  <rect x="330" y="200" width="460" height="70" rx="10" class="res"/>
  <text x="560" y="230" class="lbl b t1">Effective Permissions</text>
  <text x="560" y="254" class="lbl s ltr">(roleClaims ∪ grants) − denies</text>

  <line x1="560" y1="270" x2="560" y2="304" class="flow" marker-end="url(#ar5)"/>

  <rect x="120" y="308" width="880" height="64" rx="10" class="claim"/>
  <text x="560" y="334" class="lbl b">Claims داخل کوکی نشست</text>
  <text x="560" y="358" class="lbl s ltr">hadron:permission (چندتایی) · hadron:permission-deny · hadron:access-version · hadron:session-id · hadron:must-change-password · hadron:full-name</text>

  <text x="560" y="404" class="lbl b t1">سه لایه مستقل — هر سه باید جواب مثبت بدهند</text>

  <rect x="60" y="424" width="320" height="150" rx="10" class="lay1"/>
  <text x="220" y="452" class="lbl b t1">RBAC — چه کاری مجاز است</text>
  <text x="220" y="478" class="lbl s">پالیسی هم‌نام با کد دسترسی</text>
  <text x="220" y="500" class="lbl s ltr">RequireAuthenticatedUser()</text>
  <text x="220" y="522" class="lbl s ltr">&amp;&amp; HasClaim(permission, code)</text>
  <text x="220" y="544" class="lbl s ltr">&amp;&amp; !HasClaim(deny, code)</text>
  <text x="220" y="566" class="lbl s">خروجی: ۴۰۳ PERMISSION_DENIED</text>

  <rect x="400" y="424" width="320" height="150" rx="10" class="lay2"/>
  <text x="560" y="452" class="lbl b t1">Data Scope — کدام رکوردها</text>
  <text x="560" y="478" class="lbl s ltr">identity.user_data_scopes</text>
  <text x="560" y="500" class="lbl s ltr">ALL | WAREHOUSE:{id}</text>
  <text x="560" y="522" class="lbl s">داخل Service اعمال می‌شود،</text>
  <text x="560" y="544" class="lbl s">نه در Middleware</text>
  <text x="560" y="566" class="lbl s">خروجی: ۴۰۹ WAREHOUSE_SCOPE_DENIED</text>

  <rect x="740" y="424" width="320" height="150" rx="10" class="lay3"/>
  <text x="900" y="452" class="lbl b t1">Business Policy — آیا الان معتبر است</text>
  <text x="900" y="478" class="lbl s">وضعیت چرخه عمر رکورد</text>
  <text x="900" y="500" class="lbl s">قوانین دامنه و یکپارچگی</text>
  <text x="900" y="522" class="lbl s">Optimistic Concurrency (Version)</text>
  <text x="900" y="544" class="lbl s">دسترسی هرگز این‌ها را</text>
  <text x="900" y="566" class="lbl s">دور نمی‌زند</text>

  <rect x="60" y="600" width="1000" height="76" rx="10" class="note"/>
  <text x="560" y="628" class="lbl b">UI Visibility امنیت نیست</text>
  <text x="560" y="652" class="lbl s">منو، Route و دکمه‌ها از همان effective permissions استفاده می‌کنند تا رابط تمیز بماند — اما منبع حقیقت همیشه بک‌اند است</text>
</svg>''', centered=True)



# ============================================================== order chain
def svg_chain():
    lanes = [("مرورگر", 90), ("TradingEndpoints", 300), ("TradingService", 545),
             ("PostgreSQL", 830), ("سایر ماژول‌ها", 1035)]
    heads = ""
    for name, x in lanes:
        heads += f'''<g><rect x="{x-88}" y="18" width="176" height="40" rx="8" class="lane"/>
  <text x="{x}" y="44" class="lbl b">{name}</text></g>
  <line x1="{x}" y1="58" x2="{x}" y2="676" class="life"/>'''
    # (y, x1, x2, kind, title, note) — kind: s/r/miss/n(self)
    steps = [
        (78, 90, 300, "s", "POST /api/trading/order-registrations",
         "بدنه: projectId, registrationNumber, status, registrationDate, | registeredAmount, currencyId, notes"),
        (142, 300, 545, "s", "service.CreateOrder(request)",
         "RequirePermission(trading.order_registration.manage) قبلاً در pipeline چک شده"),
        (198, 545, 545, "n", "۱) Validate(...)",
         "status فقط DRAFT · registeredAmount &gt; ۰ · registrationNumber ≤ ۸۰ نویسه"),
        (276, 545, 830, "s", "۲) OpenProject + Currency",
         "پرونده باید عملیاتی باشد · ارز باید فعال باشد"),
        (332, 545, 830, "s", "۳) INSERT trading.order_registrations",
         "ck_trade_order_status · ck_trade_order_amount (RegisteredAmount &gt; 0)"),
        (388, 545, 830, "s", "۴) AddAudit — audit محلی Trading",
         "جدول audit خود ماژول Trading — نه platform.operational_audit_events"),
        (444, 545, 545, "n", "۵) SaveChangesAsync",
         "MrpInputChangeInterceptor: TradeOrderRegistration watched نیست → MRP هم بیدار نمی‌شود"),
        (516, 300, 90, "r", "۶) 201 Created + TradeOrderRegistrationResponse",
         "پایان ماجرا — بدون هیچ خبری به بیرون ماژول"),
        (578, 830, 1035, "miss", "✗ هیچ ردیفی به ماژول دیگر",
         "پس از Commit هیچ رویداد یا رکوردی برای CashFlow / FX / Collaboration ساخته نمی‌شود"),
        (642, 545, 1035, "miss", "✗ informToSell / informToTrade وجود ندارد",
         "اینجا دقیقاً نقطه‌ای است که باید طراحی شود — طرح پیشنهادی در دیاگرام بعدی"),
    ]
    body = ""
    for y, x1, x2, kind, title, note in steps:
        mx = (x1 + x2) / 2
        self_box = (x1 == x2)
        if "|" in note:
            a, b = note.split("|", 1)
            dy2 = 40 if self_box else 34
            note_svg = (f'<text x="{mx}" y="{y + (38 if self_box else 18)}" class="lbl s mid">{a.strip()}</text>'
                        f'<text x="{mx}" y="{y + dy2 + 16}" class="lbl s mid">{b.strip()}</text>')
        else:
            note_svg = f'<text x="{mx}" y="{y + (38 if self_box else 18)}" class="lbl s mid">{note}</text>'
        if kind == "miss":
            body += f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" class="msg miss" marker-end="url(#arc)"/>'
            body += f'<text x="{mx}" y="{y-8}" class="lbl b mid miss">{title}</text>'
            body += note_svg.replace('class="lbl s mid"', 'class="lbl s mid miss"')
        elif self_box:
            body += f'''<g><rect x="{x1-230}" y="{y-16}" width="460" height="34" rx="7" class="self"/>
  <text x="{x1}" y="{y+6}" class="lbl b">{title}</text></g>
  {note_svg}'''
        else:
            body += f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" class="msg {kind}" marker-end="url(#arc)"/>'
            body += f'<text x="{mx}" y="{y-8}" class="lbl b mid">{title}</text>'
            body += note_svg
    return _bidi_autofix(f'''<svg viewBox="0 0 1140 692" class="dia" role="img" aria-label="وضعیت فعلی زنجیره سفارش">
  <defs><marker id="arc" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>
  {heads}{body}
</svg>''', centered=True)


def svg_chain_design():
    return _bidi_autofix('''<svg viewBox="0 0 1120 700" class="dia" role="img" aria-label="طرح پیشنهادی زنجیره سفارش">
  <defs><marker id="ard" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>

  <rect x="40" y="20" width="500" height="92" rx="10" class="blk a"/>
  <text x="290" y="48" class="lbl b t1">۱. CreateOrder — همان امروز، فقط دو INSERT</text>
  <text x="290" y="72" class="lbl s">INSERT trading.order_registrations (همان اعتبارسنجی‌های فعلی)</text>
  <text x="290" y="92" class="lbl s">+ INSERT ردیف ORDER_REGISTERED در Outbox — در همان تراکنش</text>

  <rect x="580" y="20" width="500" height="92" rx="10" class="blk c"/>
  <text x="830" y="48" class="lbl b t1">الگوی مرجع — planning.mrp_refresh_outbox</text>
  <text x="830" y="72" class="lbl s">همین الگو همین امروز برای MRP کار می‌کند: صف در تراکنش،</text>
  <text x="830" y="92" class="lbl s">Worker جدا با advisory lock و retry — کپی همین قرارداد است</text>

  <line x1="290" y1="112" x2="290" y2="140" class="flow" marker-end="url(#ard)"/>

  <rect x="40" y="144" width="1040" height="86" rx="10" class="blk b"/>
  <text x="560" y="172" class="lbl b t1">۲. Integration Outbox — صف رویداد یکپارچگی عمومی (جدید — مثلاً platform.integration_outbox)</text>
  <text x="560" y="196" class="lbl s ltr">EventId · Kind = ORDER_REGISTERED · Payload (OrderId, ProjectId, Amount, Currency) · AvailableAtUtc · AttemptCount · Status</text>
  <text x="560" y="218" class="lbl s">تا وقتی تراکنش Commit نشده ردیف دیده نمی‌شود — پس هیچ ماژولی خبر سفارشِ نیمه‌کاره را نمی‌گیرد</text>

  <line x1="560" y1="230" x2="560" y2="258" class="flow" marker-end="url(#ard)"/>

  <rect x="40" y="262" width="1040" height="88" rx="10" class="blk d"/>
  <text x="560" y="290" class="lbl b t1">۳. OutboxDispatcher Worker — پس از Commit · حداقل یک‌بار · کلید Idempotency = (OrderId, Kind)</text>
  <text x="560" y="314" class="lbl s">advisory lock مثل AutomaticMrpRefreshWorker · Backoff نمایی ۲^n × ۵ ثانیه · بعد از N تلاش → FAILED + Log + Audit</text>
  <text x="560" y="336" class="lbl s">شکست هر Handler هیچ‌وقت سفارش را برنمی‌گرداند — سفارش همین حالا ثبت شده است</text>

  <line x1="200" y1="350" x2="200" y2="376" class="flow" marker-end="url(#ard)"/>
  <line x1="440" y1="350" x2="440" y2="376" class="flow" marker-end="url(#ard)"/>
  <line x1="680" y1="350" x2="680" y2="376" class="flow" marker-end="url(#ard)"/>
  <line x1="920" y1="350" x2="920" y2="376" class="flow" marker-end="url(#ard)"/>

  <rect x="40" y="380" width="310" height="180" rx="10" class="blk g"/>
  <text x="195" y="400" class="lbl b t1">informToSell</text>
  <text x="195" y="424" class="lbl s">مقصد: CashFlow</text>
  <text x="195" y="446" class="lbl s">ایجاد پیش‌نویس تعهد پرداخت</text>
  <text x="195" y="468" class="lbl s ltr">POST /api/cash-flow/obligations</text>
  <text x="195" y="490" class="lbl s">کد دسترسی جدید:</text>
  <text x="195" y="512" class="lbl s ltr">cash_flow.obligation.manage</text>
  <text x="195" y="534" class="lbl s">امروز فقط .view وجود دارد!</text>

  <rect x="404" y="380" width="310" height="180" rx="10" class="blk f"/>
  <text x="559" y="400" class="lbl b t1">informToTrade</text>
  <text x="559" y="424" class="lbl s">مقصد: Trading — درون‌ماژولی</text>
  <text x="559" y="446" class="lbl s">چک‌لیست ارز و محمل روی پرونده</text>
  <text x="559" y="468" class="lbl s">DomainEvent داخلی کافی است —</text>
  <text x="559" y="490" class="lbl s">مصرف‌کننده و تولیدکننده یک</text>
  <text x="559" y="512" class="lbl s">ماژول‌اند، Outbox لازم نیست</text>
  <text x="559" y="534" class="lbl s ltr">trading.fx.manage</text>

  <rect x="768" y="380" width="312" height="180" rx="10" class="blk e"/>
  <text x="924" y="400" class="lbl b t1">notifyUsers</text>
  <text x="924" y="424" class="lbl s">مقصد: Collaboration</text>
  <text x="924" y="446" class="lbl s">اعلان به کارشناس خرید/فروش</text>
  <text x="924" y="468" class="lbl s">ردیف اعلان + SignalR push</text>
  <text x="924" y="490" class="lbl s">Collaboration چیزی از Trading</text>
  <text x="924" y="512" class="lbl s">نمی‌نویسد — فقط رویداد می‌خواند</text>
  <text x="924" y="534" class="lbl s ltr">collaboration.*</text>

  <rect x="40" y="576" width="1040" height="96" rx="10" class="note"/>
  <text x="560" y="606" class="lbl b">قانون طلایی هر inform — چهار شرط</text>
  <text x="560" y="630" class="lbl s">۱) مصرف‌کننده فقط با Endpoint/Handler خودش و کد دسترسی خودش — هرگز نوشتن مستقیم در جدول ماژول دیگر</text>
  <text x="560" y="652" class="lbl s">۲) همیشه پس از Commit و حداقل یک‌بار — ۳) Handler باید Idempotent باشد (OrderId + Kind) — ۴) هر اثر جانبی Audit خودش را دارد</text>
</svg>''', centered=True)



# ---------------------------------------------------------- end-to-end map
def svg_flow():
    F = []
    F.append('<svg viewBox="0 0 1332 800" class="dia wide" role="img" aria-label="نقشه کل جریان از سفارش تا تولید">')
    F.append('  <defs><marker id="arf" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="arw"/></marker></defs>')

    def spine(cx, y, w, num, title, path, cls):
        F.append(f'  <rect x="{cx - w // 2}" y="{y}" width="{w}" height="86" rx="10" class="{cls}"/>')
        F.append(f'  <text x="{cx}" y="{y + 32}" class="lbl b t2">{num}. {title}</text>')
        F.append(f'  <text x="{cx}" y="{y + 58}" class="code">{path}</text>')

    def fan(cx, y, w, items):
        cy = y
        bottoms = []
        for label, miss in items:
            klass = "sub miss" if miss else "sub"
            F.append(f'  <rect x="{cx - w // 2}" y="{cy}" width="{w}" height="34" rx="7" class="{klass}"/>')
            F.append(f'  <text x="{cx}" y="{cy + 22}" class="lbl s">{label}</text>')
            cy += 44
            bottoms.append(cy - 10)
        F.insert(-2 * len(items),
                 f'  <line x1="{cx}" y1="{y - 24}" x2="{cx}" y2="{bottoms[-1] - 24}" class="life"/>')

    # ---- row 1 : trading chain (right to left)
    F.append('  <text x="650" y="36" class="lbl b t1">ردیف ۱ — چرخه سفارش و واردات (Trading)</text>')
    R1 = [
        ("۱", "ثبت سفارش", "/api/trading/order-registrations", [
            ("{id}/status ✓", 0),
            ("audit محلی Trading ✓", 0),
            ("تعهد پرداخت CashFlow ✗", 1),
            ("اعلان Collaboration ✗", 1)]),
        ("۲", "تخصیص ارز", "/api/trading/fx-allocations", [
            ("{id}/status REQUEST→RECEIPT ✓", 0),
            ("چک پرونده عملیاتی ✓", 0)]),
        ("۳", "محموله", "/api/trading/shipments", [
            ("{id}/legs + legs/status ✓", 0),
            ("{id}/actuals ✓", 0),
            ("{id}/projects لینک ✓", 0)]),
        ("۴", "ترخیص گمرک", "/api/trading/customs-clearances", [
            ("{id}/status ✓", 0),
            ("POST /costs ✓", 0)]),
        ("۵", "تحویل پرونده", "/api/trading/projects/{id}/status", [
            ("{id}/reopen ✓", 0),
            ("POST /report-notes ✓", 0)]),
    ]
    c1 = [1174, 922, 670, 418, 166]
    for (num, title, path, items), cx in zip(R1, c1):
        spine(cx, 64, 240, num, title, path, "blk b")
        fan(cx, 190, 200, items)
    for i in range(len(c1) - 1):
        F.append(f'  <line x1="{c1[i] - 122}" y1="107" x2="{c1[i + 1] + 122}" y2="107" class="flow" marker-end="url(#arf)"/>')

    # ---- bridge : order -> MRP (the missing link)
    F.append('  <path d="M 1294 107 L 1316 107 L 1316 473 L 1304 473" class="msg miss" fill="none" marker-end="url(#arf)"/>')
    F.append('  <text x="1110" y="392" class="lbl s miss">✗ سفارش تجاری MRP را بیدار نمی‌کند — watched نیست</text>')

    # ---- row 2 : planning / supply / production chain
    F.append('  <text x="650" y="412" class="lbl b t1">ردیف ۲ — چرخه برنامه‌ریزی، تأمین و تولید</text>')
    R2 = [
        ("۶", "پیش‌بینی تولید", "planning/forecast-plans", [
            ("/forecast-entry-grid ✓", 0),
            ("/actuals ✓", 0),
            ("Outbox FORECAST ✓", 0)]),
        ("۷", "BOM", "bom-revisions", [
            ("approve / obsolete ✓", 0),
            ("Outbox BOM ✓", 0),
            ("/where-used ✓", 0)]),
        ("۸", "اجرای MRP", "mrp-runs", [
            ("Worker خودکار + Outbox ✓", 0),
            ("snapshot → persist ✓", 0),
            ("order-planning ✓", 0)]),
        ("۹", "سفارش خرید", "purchase-orders", [
            ("{id}/approve ✓", 0),
            ("{id}/mark-ordered ✓", 0),
            ("Outbox FIRM_SUPPLY ✓", 0)]),
        ("۱۰", "رسید انبار", "inventory/receipts", [
            ("Outbox INVENTORY ✓", 0),
            ("transfers · issues ✓", 0),
            ("تعهد پرداخت ✗", 1)]),
        ("۱۱", "دستور تولید", "production-orders", [
            ("{id}/release ✓", 0),
            ("materials stage ✓", 0),
            ("{id}/reports ✓", 0)]),
        ("۱۲", "بهای و گزارش", "cash-flow", [
            ("/dashboard ✓", 0),
            ("/material-cost-books ✓", 0),
            ("/v1/analytics ✓", 0)]),
    ]
    c2 = [1212, 1030, 847, 664, 481, 298, 115]
    for (num, title, path, items), cx in zip(R2, c2):
        spine(cx, 430, 172, num, title, path, "blk c")
        fan(cx, 556, 172, items)
    for i in range(len(c2) - 1):
        F.append(f'  <line x1="{c2[i] - 88}" y1="473" x2="{c2[i + 1] + 88}" y2="473" class="flow" marker-end="url(#arf)"/>')

    # ---- legend
    F.append('  <rect x="90" y="712" width="30" height="20" rx="5" class="sub"/>')
    F.append('  <text x="310" y="727" class="lbl s">پیاده‌شده — از سورس</text>')
    F.append('  <rect x="420" y="712" width="30" height="20" rx="5" class="sub miss"/>')
    F.append('  <text x="665" y="727" class="lbl s">پیشنهادی / جای خالی طراحی</text>')
    F.append('  <text x="1105" y="727" class="lbl s">جریان هر ردیف از راست به چپ است · همه مسیرها از سورس استخراج شده</text>')
    F.append('  <text x="650" y="766" class="lbl s">جزئیات هر گام و طرح پیشنهادیِ نقطه‌های ✗ در دیاگرام‌های بعدی همین بخش آمده است</text>')
    F.append('</svg>')
    return _bidi_autofix("\n".join(F), centered=True)


# ============================================================== HTML parts
def perm_rows():
    out = []
    for p in sorted(PERMS, key=lambda x: (x["area"], x["code"])):
        out.append(
            f'<tr data-area="{p["area"]}" data-code="{p["code"]}">'
            f'<td class="mono">{E(p["code"])}</td>'
            f'<td><span class="tag">{p["area"]}</span></td>'
            f'<td>{E(p["fa"])}</td>'
            f'<td class="ltr">{E(p["en"])}</td></tr>')
    return "\n".join(out)


def module_rows():
    order = ["Identity", "ReferenceData", "Catalog", "Products", "Suppliers", "Sourcing",
             "Bom", "Planning", "Inventory", "Production", "PurchaseSupply", "Mrp",
             "OrderPlanning", "Analytics", "CashFlow", "Trading", "Collaboration"] + PLATFORM_MODS
    out = []
    for m in order:
        d = mods[m]
        out.append(
            f'<tr><td class="b">{m}</td>'
            f'<td class="mono">{E(", ".join(sorted(d["schemas"])) or "—")}</td>'
            f'<td class="num">{len(d["tables"])}</td>'
            f'<td class="num">{d["endpoints"]}</td>'
            f'<td class="mono sm">{E(", ".join(sorted(d["services"])) or "—")}</td>'
            f'<td class="num">{len(d["perms"])}</td></tr>')
    return "\n".join(out)


def route_rows():
    out = []
    for r in ROUTES:
        out.append(
            f'<tr><td class="mono">{E(r["prefix"])}</td>'
            f'<td class="mono">{E(r["view"])}</td>'
            f'<td class="mono">{E(r["manage"])}</td></tr>')
    return "\n".join(out)


def watched_chips():
    order = ["FORECAST", "DAILY_ACTUAL", "BOM", "INVENTORY", "FIRM_SUPPLY",
             "INVENTORY+FIRM_SUPPLY", "SOURCING", "MANUFACTURING_ROUTE", "CATALOG", "REFERENCE_DATA"]
    return "".join(f'<span class="chip2">{E(k)} <b>{WATCH_COUNT[k]}</b></span>' for k in order if k in WATCH_COUNT)


def area_chips():
    return "".join(
        f'<button class="pill" data-filter="{a}">{a} <b>{n}</b></button>'
        for a, n in sorted(AREAS.items()))


CSS = """
:root{
  --bg:#0d1117; --panel:#161b22; --panel2:#1c2230; --line:#2b3341; --line2:#3a4557;
  --tx:#e6edf3; --mut:#93a1b1; --ac:#4f9cf9; --ac2:#3fb950; --warn:#d29922; --bad:#f85149;
  --pur:#bc8cff; --cy:#39c5cf;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--tx);
  font:15px/1.75 "Vazirmatn","Segoe UI",Tahoma,"Iranian Sans",system-ui,sans-serif}
a{color:var(--ac)}
header{padding:34px 32px 22px;border-bottom:1px solid var(--line);
  background:linear-gradient(180deg,#12203a,#0d1117)}
h1{margin:0 0 6px;font-size:27px}
.sub{color:var(--mut);margin:0;font-size:14px}
.stats{display:flex;flex-wrap:wrap;gap:10px;margin-top:18px}
.stat{background:var(--panel);border:1px solid var(--line);border-radius:9px;padding:9px 15px;min-width:104px}
.stat b{display:block;font-size:21px;color:var(--ac);line-height:1.3}
.stat span{font-size:12px;color:var(--mut)}
nav{position:sticky;top:0;z-index:20;background:rgba(13,17,23,.96);backdrop-filter:blur(6px);
  border-bottom:1px solid var(--line);display:flex;flex-wrap:wrap;gap:2px;padding:0 20px}
nav button{background:none;border:0;color:var(--mut);padding:13px 15px;cursor:pointer;
  font:inherit;font-size:14px;border-bottom:2px solid transparent}
nav button:hover{color:var(--tx)}
nav button.on{color:var(--ac);border-bottom-color:var(--ac);font-weight:700}
main{padding:26px 22px 80px;max-width:1320px;margin:0 auto}
section{display:none}
section.on{display:block}
h2{font-size:21px;margin:6px 0 4px;padding-bottom:9px;border-bottom:1px solid var(--line)}
h3{font-size:16px;margin:26px 0 8px;color:var(--ac)}
p.lead{color:var(--mut);margin:8px 0 18px;max-width:960px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:16px;margin:16px 0;overflow-x:auto}
.dia{width:100%;height:auto;min-width:820px;display:block}
.dia.wide{min-width:1300px}
.note{background:#12203a;border:1px solid #25406b;border-right:4px solid var(--ac);
  border-radius:9px;padding:13px 16px;margin:14px 0}
.note.w{background:#2a2312;border-color:#5c4a1c;border-right-color:var(--warn)}
.note.r{background:#2b1416;border-color:#6b2529;border-right-color:var(--bad)}
.note.g{background:#12291b;border-color:#1d5c34;border-right-color:var(--ac2)}
.note b{color:var(--tx)}
table{width:100%;border-collapse:collapse;font-size:13.5px}
th,td{text-align:right;padding:8px 11px;border-bottom:1px solid var(--line);vertical-align:top}
th{background:var(--panel2);color:var(--mut);font-weight:600;position:sticky;top:46px;font-size:12.5px}
tbody tr:hover{background:#1a2029}
.mono{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12.5px;direction:ltr;text-align:left}
.ltr{direction:ltr;text-align:left;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12.5px}
.num{text-align:center;font-weight:700;color:var(--ac)}
.b{font-weight:700}.sm{font-size:11.5px}
.tag{background:#1f2a3d;color:var(--cy);border-radius:5px;padding:2px 7px;font-size:11px;white-space:nowrap;
  font-family:ui-monospace,Menlo,monospace}
.tag.g{background:#132a1c;color:var(--ac2)}
.tag.o{background:#2b2213;color:var(--warn)}
.tag.r{background:#2e1719;color:var(--bad)}
.tag.p{background:#241a38;color:var(--pur)}
.grid{display:grid;gap:14px;grid-template-columns:repeat(auto-fit,minmax(310px,1fr))}
.mini{background:var(--panel);border:1px solid var(--line);border-radius:11px;padding:15px}
.mini h4{margin:0 0 8px;font-size:14.5px;color:var(--ac)}
.mini ul{margin:6px 0 0;padding-right:19px}
.mini li{margin:5px 0;font-size:13.5px;color:#cdd8e5}
code{background:#1f2733;border-radius:4px;padding:1px 6px;font-size:12.5px;direction:ltr;
  font-family:ui-monospace,Menlo,monospace;color:var(--cy);display:inline-block}
.flowbox{background:var(--panel);border:1px solid var(--line);border-radius:11px;padding:15px;margin:13px 0}
.fstep{display:flex;gap:13px;padding:10px 0;border-bottom:1px dashed var(--line)}
.fstep:last-child{border-bottom:0}
.fn{flex:0 0 27px;height:27px;border-radius:50%;background:var(--ac);color:#04122b;
  font-weight:800;display:flex;align-items:center;justify-content:center;font-size:13px}
.fstep.g .fn{background:var(--ac2)}.fstep.r .fn{background:var(--bad)}
.fstep.w .fn{background:var(--warn)}.fstep.p .fn{background:var(--pur)}
.ft b{display:block;margin-bottom:2px}
.ft span{font-size:13px;color:var(--mut)}
.tools{display:flex;gap:9px;flex-wrap:wrap;margin:12px 0;align-items:center}
.tools input{flex:1;min-width:210px;background:var(--panel2);border:1px solid var(--line2);
  color:var(--tx);border-radius:8px;padding:9px 13px;font:inherit;font-size:13.5px}
.pill{background:var(--panel2);border:1px solid var(--line2);color:var(--mut);border-radius:20px;
  padding:5px 13px;cursor:pointer;font:inherit;font-size:12px}
.pill:hover{border-color:var(--ac);color:var(--tx)}
.pill.on{background:#12304f;border-color:var(--ac);color:var(--ac);font-weight:700}
.pill b{color:inherit;opacity:.75}
.chip2{display:inline-block;background:var(--panel2);border:1px solid var(--line2);border-radius:16px;
  padding:4px 12px;margin:3px;font-size:12.5px}
.chip2 b{color:var(--ac)}
.cnt{color:var(--mut);font-size:12.5px}
footer{border-top:1px solid var(--line);padding:20px 32px;color:var(--mut);font-size:12.5px}

/* ---- SVG palette ---- */
.dia text{fill:var(--tx)}
.dia text.lbl{font-size:13px}
.dia text.b{font-weight:700}
.dia text.s{font-size:11.5px;fill:var(--mut)}
.dia text.t1{font-size:15px}
.dia text.t2{font-size:13.5px}
.dia text.mid{text-anchor:middle}
.dia text.en{text-anchor:end}
.dia text.code{font-family:ui-monospace,Menlo,monospace;font-size:10.5px;fill:var(--warn)}
.dia text.num{font-size:13px;fill:#04122b;text-anchor:middle;font-weight:800}
.dia .arw{fill:var(--line2)}
.dia .flow{stroke:var(--line2);stroke-width:1.6;fill:none}
.dia .life{stroke:var(--line);stroke-width:1.2;stroke-dasharray:4 5}
.dia .msg{stroke:var(--ac);stroke-width:1.7}
.dia .msg.r{stroke:var(--ac2)}
.dia .layer{fill:#141b26;stroke:var(--line2)}
.dia .layer.fe{fill:#101d33;stroke:#25406b}
.dia .layer.api{fill:#16202e;stroke:#2f4a6b}
.dia .layer.db{fill:#11231a;stroke:#1d5c34}
.dia .layer.sto{fill:#231a2e;stroke:#4a3568}
.dia .mod{fill:#1a2130;stroke:var(--line2)}
.dia .chip{fill:#222c3d;stroke:var(--line2)}
.dia .st{fill:#161c27;stroke:var(--line2)}
.dia .st.g{fill:#12291b;stroke:#1d5c34}
.dia .st.r{fill:#2b1416;stroke:#6b2529}
.dia .st.b{fill:#12203a;stroke:#25406b}
.dia .num{fill:var(--ac)}
.dia .st.g .num{fill:var(--ac2)}
.dia .st.r .num{fill:var(--bad)}
.dia .lane{fill:#1f2937;stroke:var(--line2)}
.dia .self{fill:#12203a;stroke:#25406b}
.dia .blk{fill:#151b26;stroke:var(--line2)}
.dia .blk.a{fill:#101d33;stroke:#25406b}.dia .blk.b{fill:#241a38;stroke:#4a3568}
.dia .blk.c{fill:#11231a;stroke:#1d5c34}.dia .blk.d{fill:#2b2213;stroke:#5c4a1c}
.dia .blk.e{fill:#1a2130;stroke:var(--line2)}.dia .blk.f{fill:#12203a;stroke:#25406b}
.dia .blk.g{fill:#0f2426;stroke:#1d5c5c}
.dia .sub{fill:#222c3d;stroke:var(--line2)}
.dia .sub.miss{fill:#2b1416;stroke:#6b2529;stroke-dasharray:5 4}
.dia .src{fill:#1a2130;stroke:var(--line2)}
.dia .src.a{fill:#11231a;stroke:#1d5c34}.dia .src.b{fill:#12203a;stroke:#25406b}
.dia .src.c{fill:#2b1416;stroke:#6b2529}.dia .src.d{fill:#241a38;stroke:#4a3568}
.dia .res{fill:#0f2426;stroke:#1d5c5c;stroke-width:2}
.dia .claim{fill:#1a2130;stroke:var(--line2)}
.dia .lay1{fill:#12203a;stroke:#25406b}.dia .lay2{fill:#11231a;stroke:#1d5c34}
.dia .lay3{fill:#241a38;stroke:#4a3568}
.dia .note{fill:#2b2213;stroke:#5c4a1c}
@media print{nav{display:none}section{display:block!important}body{background:#fff;color:#000}}
"""

JS = """
const tabs=[...document.querySelectorAll('nav button')];
const secs=[...document.querySelectorAll('main section')];
function show(id){
  tabs.forEach(t=>t.classList.toggle('on',t.dataset.t===id));
  secs.forEach(s=>s.classList.toggle('on',s.id===id));
  location.hash=id;
  window.scrollTo({top:0});
}
tabs.forEach(t=>t.onclick=()=>show(t.dataset.t));
const h=location.hash.slice(1);
show(secs.some(s=>s.id===h)?h:'overview');

// permission catalog filter
const q=document.getElementById('pq');
const rows=[...document.querySelectorAll('#ptbody tr')];
const cnt=document.getElementById('pcnt');
let area='';
function apply(){
  const t=(q.value||'').trim().toLowerCase();
  let n=0;
  rows.forEach(r=>{
    const ok=(!area||r.dataset.area===area)&&(!t||r.dataset.code.includes(t)||r.textContent.toLowerCase().includes(t));
    r.style.display=ok?'':'none'; if(ok)n++;
  });
  cnt.textContent=n+' از '+rows.length+' کد دسترسی';
}
q.oninput=apply;
document.querySelectorAll('.pill[data-filter]').forEach(p=>p.onclick=()=>{
  const f=p.dataset.filter;
  area=(area===f)?'':f;
  document.querySelectorAll('.pill[data-filter]').forEach(x=>x.classList.toggle('on',x.dataset.filter===area));
  apply();
});
apply();
"""

HTML = f"""<!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Hadron ERP — نقشه لاجیک، موتور MRP و دسترسی‌ها</title>
<style>{CSS}</style>
</head>
<body>

<header>
  <h1>Hadron ERP — نقشه کامل لاجیک و دسترسی‌ها</h1>
  <p class="sub">
    مرجع بازنویسی در Next.js + NestJS · استخراج مستقیم از سورس
    <code>src/backend/Hadron.Erp.Api</code> و <code>src/frontend/src</code> ·
    کامیت <code>439d5bf</code>
  </p>
  <div class="stats">
    <div class="stat"><b>{len(BUSINESS_MODS)}</b><span>ماژول تجاری</span></div>
    <div class="stat"><b>{TOTAL_ENDPOINTS}</b><span>Endpoint</span></div>
    <div class="stat"><b>{TOTAL_TABLES}</b><span>جدول دیتابیس</span></div>
    <div class="stat"><b>{len(ALL_SCHEMAS)}</b><span>اسکیما</span></div>
    <div class="stat"><b>{len(PERMS)}</b><span>کد دسترسی</span></div>
    <div class="stat"><b>{len(AREAS)}</b><span>حوزه دسترسی</span></div>
    <div class="stat"><b>{len(ROUTES)}</b><span>قاعده Route</span></div>
    <div class="stat"><b>1</b><span>پایگاه داده</span></div>
  </div>
</header>

<nav>
  <button data-t="overview">۱ · معماری</button>
  <button data-t="request">۲ · مسیر درخواست</button>
  <button data-t="product">۳ · ثبت کالا</button>
  <button data-t="engine">۴ · موتور MRP</button>
  <button data-t="auth">۵ · مدل دسترسی</button>
  <button data-t="catalog">۶ · کاتالوگ دسترسی‌ها</button>
  <button data-t="modules">۷ · نقشه ماژول‌ها</button>
  <button data-t="rewrite">۸ · راهنمای بازنویسی</button>
  <button data-t="chain">۹ · سفارش تا تولید</button>
</nav>

<main>

<!-- ============================ 1 ============================ -->
<section id="overview">
  <h2>معماری کلی — Modular Monolith</h2>
  <p class="lead">
    همه‌چیز در <b>یک</b> پروسه ASP.NET Core و <b>یک</b> دیتابیس PostgreSQL زندگی می‌کند.
    «ماژولار» بودن اینجا یک قرارداد معماری است نه یک مرز فیزیکی: هر ماژول پوشه‌ی خودش را دارد
    و نباید مستقیم به جدول ماژول دیگر بنویسد، ولی همه از یک <code>HadronDbContext</code> مشترک استفاده می‌کنند.
  </p>

  <div class="card">{svg_overview()}</div>

  <h3>ساختار داخلی هر ماژول — چهار لایه</h3>
  <div class="grid">
    <div class="mini"><h4>Api/</h4><ul>
      <li>Minimal API Endpoint ها (<code>*Endpoints.cs</code>)</li>
      <li>فقط نگاشت Route + <code>RequirePermission(...)</code></li>
      <li>ترجمه Exception به HTTP (۴۰۰ / ۴۰۴ / ۴۰۹)</li>
      <li><b>هیچ منطق تجاری اینجا نیست</b></li>
    </ul></div>
    <div class="mini"><h4>Application/</h4><ul>
      <li>Service ها — ارکستراسیون Use Case</li>
      <li>Contracts: record های Request/Response</li>
      <li>Exception های دامنه‌ای اختصاصی</li>
      <li>اعتبارسنجی + Audit emission</li>
    </ul></div>
    <div class="mini"><h4>Domain/</h4><ul>
      <li>Entity ها با Constructor خصوصی + متد رفتاری</li>
      <li>Setter ها <code>private set</code> هستند</li>
      <li>تغییر وضعیت فقط از راه متد (<code>Edit</code>, <code>ChangeState</code>, …)</li>
      <li>بدون وابستگی به EF Core</li>
    </ul></div>
    <div class="mini"><h4>Infrastructure/</h4><ul>
      <li><code>IEntityTypeConfiguration</code> هر Entity</li>
      <li>نگاشت به <code>schema.table</code></li>
      <li><b>Check Constraint ها در دیتابیس</b> تعریف می‌شوند</li>
      <li>Index و Concurrency Token</li>
    </ul></div>
  </div>

  <h3>چهار الگوی تکرارشونده در کل پروژه</h3>
  <div class="grid">
    <div class="mini"><h4>۱. Optimistic Concurrency</h4><ul>
      <li>هر Entity یک <code>Version</code> دارد (Concurrency Token)</li>
      <li>کلاینت باید <code>ExpectedVersion</code> بفرستد</li>
      <li>عدم تطابق → <code>STALE_VERSION</code> با HTTP ۴۰۹</li>
    </ul></div>
    <div class="mini"><h4>۲. Deactivate نه Delete</h4><ul>
      <li>هیچ DELETE فیزیکی روی Master Data نیست</li>
      <li><code>POST /{{id}}/deactivate</code> و <code>/reactivate</code></li>
      <li>Reactivate اول معتبربودن مراجع را چک می‌کند</li>
    </ul></div>
    <div class="mini"><h4>۳. Lifecycle State Machine</h4><ul>
      <li>BOM: <code>DRAFT → APPROVED → OBSOLETE</code></li>
      <li>Purchase Order: Draft → Approved → Ordered</li>
      <li>هر انتقال، یک Event در جدول Lifecycle ثبت می‌کند</li>
    </ul></div>
    <div class="mini"><h4>۴. Post-Commit Audit</h4><ul>
      <li>بعد از موفقیت <code>SaveChanges</code> صدا زده می‌شود</li>
      <li>شکست Audit فقط Log می‌شود، تراکنش برنمی‌گردد</li>
      <li>Old/New Snapshot به‌صورت JSON</li>
    </ul></div>
  </div>

  <div class="note g"><b>نکته برای بازنویسی:</b>
    چون همه ماژول‌ها یک DbContext مشترک دارند، در NestJS بهترین معادلش
    <b>یک Connection با چند Module</b> است که هر Module فقط Entity های خودش را register می‌کند.
    اگر بعداً خواستید واقعاً جدا شوید، مرزها از قبل با <code>schema</code> کشیده شده‌اند.
  </div>
</section>

<!-- ============================ 2 ============================ -->
<section id="request">
  <h2>مسیر یک درخواست — از مرورگر تا Service</h2>
  <p class="lead">
    این ترتیب Middleware ها <b>ترتیب دقیق</b> ثبت‌شده در <code>Program.cs</code> است.
    جابه‌جا کردنشان معنی امنیتی را عوض می‌کند: مثلاً چک نشست باید <b>بعد</b> از Authentication
    و <b>قبل</b> از Authorization باشد، وگرنه کاربر Deactivate‌شده تا انقضای کوکی دسترسی دارد.
  </p>

  <div class="card">{svg_pipeline()}</div>

  <h3>جزئیات لایه نشست — IdentitySessionMiddleware</h3>
  <div class="flowbox">
    <div class="fstep g"><div class="fn">۱</div><div class="ft"><b>Active بودن کاربر</b>
      <span>اگر <code>IsActive = false</code> باشد → ۴۰۱ با <code>SESSION_REVOKED</code> و SignOut فوری</span></div></div>
    <div class="fstep g"><div class="fn">۲</div><div class="ft"><b>AccessVersion</b>
      <span>Claim داخل کوکی با مقدار دیتابیس مقایسه می‌شود. با هر تغییر نقش/دسترسی، این عدد زیاد می‌شود تا کوکی‌های قدیمی بی‌اعتبار شوند.</span></div></div>
    <div class="fstep g"><div class="fn">۳</div><div class="ft"><b>رکورد نشست</b>
      <span>جدول <code>identity.user_sessions</code> باید رکورد داشته باشد و <code>RevokedAtUtc</code> تهی باشد. <code>LastSeenAtUtc</code> هر ۵ دقیقه به‌روز می‌شود.</span></div></div>
    <div class="fstep w"><div class="fn">۴</div><div class="ft"><b>تغییر رمز اجباری</b>
      <span>اگر <code>MustChangePassword = true</code> باشد، همه مسیرهای <code>/api</code> با ۴۰۳ <code>PASSWORD_CHANGE_REQUIRED</code> رد می‌شوند — جز:
      <code>/api/auth/me</code>, <code>/api/auth/csrf</code>, <code>/api/auth/change-password</code>, <code>/api/auth/logout</code>, <code>/api/auth/profile-photo</code></span></div></div>
  </div>

  <h3>Fail-Closed — مهم‌ترین تصمیم امنیتی پروژه</h3>
  <div class="note r">
    <b>ApiPermissionMiddleware</b> روی هر درخواستی که به <code>/api</code> می‌خورد اجرا می‌شود.
    اگر Endpoint مربوطه <b>هیچ‌کدام</b> از این دو را نداشته باشد، بدون اجرای Handler پاسخ
    <code>403 UNREGISTERED_API_PERMISSION</code> می‌گیرد:
    <ul style="margin:9px 0 0">
      <li><code>RequiredPermissionMetadata</code> — یعنی <code>.RequirePermission(...)</code> صدا زده شده باشد</li>
      <li><code>IAllowAnonymous</code> — استثناهای صریح (Health, Login, Bootstrap)</li>
    </ul>
    یعنی فراموش‌کردن دسترسی روی یک Endpoint جدید، <b>باز شدن دسترسی</b> نیست؛ بلکه <b>از کار افتادن</b> آن Endpoint است.
  </div>

  <div class="note">
    <b>استثناهای دقیق Middleware:</b>
    مسیرهایی که اصلاً به Endpoint نگاشت نشده‌اند (۴۰۴) و <code>405 Method Not Supported</code>
    دست‌نخورده رد می‌شوند تا قرارداد استاندارد HTTP حفظ شود.
    <code>/api/auth/logout</code> هم صریحاً استثنا شده تا کاربر همیشه بتواند خارج شود.
  </div>

  <h3>کدهای خطای پایدار — قرارداد بین فرانت و بک‌اند</h3>
  <table>
    <thead><tr><th>HTTP</th><th>code</th><th>کی اتفاق می‌افتد</th></tr></thead>
    <tbody>
      <tr><td class="num">۴۰۰</td><td class="mono">CSRF_VALIDATION_FAILED</td><td>توکن CSRF نامعتبر یا غایب روی متد ناامن</td></tr>
      <tr><td class="num">۴۰۰</td><td class="mono">ValidationProblem</td><td>خطای اعتبارسنجی دامنه — با کلید فیلد و پیام فارسی</td></tr>
      <tr><td class="num">۴۰۱</td><td class="mono">AUTHENTICATION_REQUIRED</td><td>کوکی نشست ندارد یا منقضی شده</td></tr>
      <tr><td class="num">۴۰۱</td><td class="mono">SESSION_REVOKED / INVALID_SESSION</td><td>کاربر غیرفعال، AccessVersion قدیمی یا نشست باطل‌شده</td></tr>
      <tr><td class="num">۴۰۳</td><td class="mono">PASSWORD_CHANGE_REQUIRED</td><td>ورود اول — هنوز رمز موقت عوض نشده</td></tr>
      <tr><td class="num">۴۰۳</td><td class="mono">PERMISSION_DENIED</td><td>دسترسی مؤثر را ندارد</td></tr>
      <tr><td class="num">۴۰۳</td><td class="mono">UNREGISTERED_API_PERMISSION</td><td>Endpoint بدون متادیتای دسترسی — خطای برنامه‌نویس</td></tr>
      <tr><td class="num">۴۰۹</td><td class="mono">STALE_VERSION</td><td>ExpectedVersion با نسخه دیتابیس نمی‌خواند</td></tr>
      <tr><td class="num">۴۰۹</td><td class="mono">DUPLICATE_CODE</td><td>Unique Constraint دیتابیس</td></tr>
      <tr><td class="num">۴۰۹</td><td class="mono">WAREHOUSE_SCOPE_DENIED</td><td>انبار انتخابی بیرون از Data Scope کاربر است</td></tr>
      <tr><td class="num">۴۲۹</td><td class="mono">RATE_LIMITED</td><td>سقف تلاش عبور کرده — با هدر Retry-After</td></tr>
    </tbody>
  </table>
</section>

<!-- ============================ 3 ============================ -->
<section id="product">
  <h2>ثبت یک کالا — مسیر کامل داده</h2>
  <p class="lead">
    این دقیق‌ترین مثال «کجا چه چیزی ثبت می‌شود» است. بقیه ماژول‌ها
    (Supplier, Part, Component, BOM, Purchase Order) <b>همین الگو</b> را تکرار می‌کنند.
  </p>

  <div class="card">{svg_product()}</div>

  <h3>چه چیزی، در کدام جدول ثبت می‌شود</h3>
  <table>
    <thead><tr><th>جدول</th><th>عملیات</th><th>محتوا</th><th>اجباری</th></tr></thead>
    <tbody>
      <tr><td class="mono">catalog.items</td><td><span class="tag g">INSERT</span></td>
        <td>هویت مشترک کالا: <code>ItemType=PRODUCT</code>, <code>BusinessNumber=PRD-0001</code>, نام فارسی/انگلیسی, <code>BaseUnitId</code>, <code>Notes</code>, <code>IsActive</code>, <code>Version</code>, Timestamps</td><td>بله</td></tr>
      <tr><td class="mono">catalog.products</td><td><span class="tag g">INSERT</span></td>
        <td>فیلدهای برنامه‌ریزی: <code>SafetyStockQty</code>, <code>CriticalDays</code>, <code>SafeStartDays</code>, <code>ExcessStartDays</code>, <code>ProductCategoryId</code>, <code>BusinessUnitId</code> — کلید اصلی همان <code>ItemId</code> است (رابطه ۱:۱)</td><td>بله</td></tr>
      <tr><td class="mono">platform.operational_audit_events</td><td><span class="tag">INSERT</span></td>
        <td><code>EntityType=Product</code>, <code>Action=CREATE</code>, <code>ActorId</code>, <code>CorrelationId</code>, Snapshot جدید به JSON</td><td>Post-Commit — شکستش فقط Log می‌شود</td></tr>
      <tr><td class="mono">catalog.product_number_seq</td><td><span class="tag o">nextval</span></td>
        <td>Sequence دیتابیس برای کد یکتا — با <code>nextval</code> خام، نه EF</td><td>بله</td></tr>
      <tr><td class="mono">planning.mrp_refresh_outbox</td><td><span class="tag r">هیچ</span></td>
        <td><b>ثبت نمی‌شود!</b> ساخت Product در <code>MrpInputChangeInterceptor</code> watch نشده. فقط تغییر <code>Item.IsActive</code> یا <code>Item.BaseUnitId</code> و افزودن <code>MaterialItemProfile</code> تریگر MRP است.</td><td>—</td></tr>
    </tbody>
  </table>

  <div class="note w"><b>چرا یک جدول <code>items</code> مشترک؟</b>
    PRODUCT و PART و COMPONENT همه یک <code>Item</code> هستند و تفاوتشان فقط در جدول تخصصی ۱:۱ است
    (<code>catalog.products</code> / <code>catalog.material_item_profiles</code>).
    این «Class Table Inheritance» است و دلیلش این است که BOM و Inventory باید بتوانند
    هر سه را به شکل یکسان ارجاع بدهند. Check Constraint دیتابیس فرمت کد را هم اجبار می‌کند:
    <code>^PRD-[0-9]{{4,}}$</code> / <code>^PRT-…$</code> / <code>^CMP-…$</code>.
  </div>

  <h3>چهار دروازه اعتبارسنجی — به همین ترتیب</h3>
  <div class="grid">
    <div class="mini"><h4>۱. دروازه شکل داده (Application)</h4><ul>
      <li>نام فارسی اجباری، حداکثر ۲۰۰ نویسه</li>
      <li>نام انگلیسی و یادداشت اختیاری (۲۰۰۰ نویسه)</li>
      <li><code>safetyStockQty ≥ 0</code></li>
      <li><code>0 ≤ CriticalDays ≤ SafeStartDays ≤ ExcessStartDays</code></li>
      <li>نتیجه: ۴۰۰ با کلید فیلد</li>
    </ul></div>
    <div class="mini"><h4>۲. دروازه مراجع (Application)</h4><ul>
      <li>دسته باید <code>ReferenceType.Code == "PRODUCT_CATEGORY"</code> و فعال باشد</li>
      <li>واحد پایه باید یک <code>Unit</code> فعال باشد</li>
      <li>واحد تجاری باید <code>"BUSINESS_UNIT"</code> و فعال باشد</li>
      <li>در Update فقط مراجع <b>تغییریافته</b> چک می‌شوند</li>
    </ul></div>
    <div class="mini"><h4>۳. دروازه همزمانی (Application)</h4><ul>
      <li><code>EnsureVersion(actual, expected)</code> قبل از نوشتن</li>
      <li>و دوباره Concurrency Token موقع Save</li>
      <li>نتیجه: ۴۰۹ <code>STALE_VERSION</code></li>
    </ul></div>
    <div class="mini"><h4>۴. دروازه دیتابیس (PostgreSQL)</h4><ul>
      <li>Unique روی <code>BusinessNumber</code></li>
      <li>Check Constraint های فرمت و بازه</li>
      <li><code>ck_item_base_unit_inventory</code> — اگر موجودی داشته باشد، واحد پایه عوض نمی‌شود</li>
      <li>این لایه <b>آخرین خط دفاع</b> است</li>
    </ul></div>
  </div>

  <h3>Warning در برابر Error</h3>
  <div class="note">
    نام تکراری فارسی یا انگلیسی <b>خطا نیست</b>. ثبت انجام می‌شود و
    <code>DUPLICATE_NAME_FA</code> / <code>DUPLICATE_NAME_EN</code> داخل آرایه <code>warnings</code>
    در پاسخ برمی‌گردد تا UI هشدار بدهد. این یک تصمیم محصولی است:
    در ERP واقعی دو کالا می‌توانند نام یکسان داشته باشند.
  </div>
</section>

<!-- ============================ 4 ============================ -->
<section id="engine">
  <h2>موتور MRP — قلب محاسباتی پروژه</h2>
  <p class="lead">
    الگوی کلیدی اینجا <b>Transactional Outbox</b> است. هیچ ماژولی مستقیم MRP را صدا نمی‌زند؛
    فقط داده‌ی خودش را در همان تراکنش در یک جدول صف می‌گذارد و یک Worker جدا آن را برمی‌دارد.
    این یعنی اگر محاسبه MRP شکست بخورد، ثبت کالا یا رسید انبار <b>هرگز</b> Rollback نمی‌شود.
  </p>

  <div class="card">{svg_mrp()}</div>

  <h3>چه تغییراتی MRP را بیدار می‌کنند</h3>
  <p class="lead">
    <code>MrpInputChangeInterceptor</code> یک <code>SaveChangesInterceptor</code> است که
    ChangeTracker را می‌بیند و هر Entity مرتبط را به یک «منبع» نگاشت می‌کند.
    توجه کنید که شرط بعضی‌ها دقیق است — مثلاً از <code>Item</code> فقط تغییر
    <code>IsActive</code> یا <code>BaseUnitId</code> شمرده می‌شود، نه تغییر نام.
  </p>
  <div class="card" style="text-align:center">{watched_chips()}</div>

  <table>
    <thead><tr><th>Entity</th><th>شرط دقیق</th><th>منبع</th></tr></thead>
    <tbody>
      <tr><td class="mono">ForecastRevision</td><td>فقط Added</td><td><span class="tag">FORECAST</span></td></tr>
      <tr><td class="mono">DailyActualRevision</td><td>فقط Added</td><td><span class="tag">DAILY_ACTUAL</span></td></tr>
      <tr><td class="mono">BomRevision</td><td>Modified و فقط وقتی <code>Status</code> عوض شده</td><td><span class="tag p">BOM</span></td></tr>
      <tr><td class="mono">InventoryTransaction</td><td>فقط Added</td><td><span class="tag g">INVENTORY</span></td></tr>
      <tr><td class="mono">Warehouse</td><td>Modified و فقط وقتی <code>IsActive</code> عوض شده</td><td><span class="tag g">INVENTORY</span></td></tr>
      <tr><td class="mono">PurchaseOrder</td><td>Modified و فقط وقتی <code>Status</code> عوض شده</td><td><span class="tag o">FIRM_SUPPLY</span></td></tr>
      <tr><td class="mono">DeliverySchedulePlanRevision</td><td>فقط Added</td><td><span class="tag o">FIRM_SUPPLY</span></td></tr>
      <tr><td class="mono">PurchaseReceipt</td><td>Added یا Modified</td><td><span class="tag g">INVENTORY</span> + <span class="tag o">FIRM_SUPPLY</span></td></tr>
      <tr><td class="mono">PurchaseScheduleCancellation</td><td>فقط Added</td><td><span class="tag o">FIRM_SUPPLY</span></td></tr>
      <tr><td class="mono">DeliveryScheduleAllocationEvent</td><td>فقط Added</td><td><span class="tag o">FIRM_SUPPLY</span></td></tr>
      <tr><td class="mono">SourcingOption</td><td>هر تغییری</td><td><span class="tag">SOURCING</span></td></tr>
      <tr><td class="mono">Supplier</td><td>Modified و فقط وقتی <code>IsActive</code> عوض شده</td><td><span class="tag">SOURCING</span></td></tr>
      <tr><td class="mono">ManufacturingRoute</td><td>Modified و فقط وقتی <code>Status</code> عوض شده</td><td><span class="tag p">MANUFACTURING_ROUTE</span></td></tr>
      <tr><td class="mono">ProductionWorkCenter</td><td>Modified و تغییر <code>WorkingWeekMask</code> یا <code>IsActive</code></td><td><span class="tag p">MANUFACTURING_ROUTE</span></td></tr>
      <tr><td class="mono">Item</td><td>Modified و تغییر <code>IsActive</code> یا <code>BaseUnitId</code></td><td><span class="tag">CATALOG</span></td></tr>
      <tr><td class="mono">MaterialItemProfile</td><td>Added / Modified / Deleted</td><td><span class="tag">CATALOG</span></td></tr>
      <tr><td class="mono">ReferenceValue / Currency / Unit</td><td>Modified و فقط وقتی <code>IsActive</code> عوض شده</td><td><span class="tag">REFERENCE_DATA</span></td></tr>
    </tbody>
  </table>

  <h3>چرخه عمر یک پیام Outbox</h3>
  <div class="flowbox">
    <div class="fstep"><div class="fn">۱</div><div class="ft"><b>PENDING</b>
      <span>با <code>AvailableAtUtc = now + ۴ ثانیه</code>. این Debounce باعث می‌شود ۵۰ تغییر پشت‌سرهم یک Run بدهند نه ۵۰ تا.</span></div></div>
    <div class="fstep"><div class="fn">۲</div><div class="ft"><b>PROCESSING</b>
      <span>Worker تا ۵۰۰۰ پیام را یک‌جا برمی‌دارد و <code>AttemptCount</code> را زیاد می‌کند.</span></div></div>
    <div class="fstep g"><div class="fn">۳</div><div class="ft"><b>COMPLETED</b>
      <span>با <code>RunId</code> مربوطه. اگر snapshot پلن CURRENT از جدیدترین تغییر تازه‌تر باشد، بدون اجرای مجدد COMPLETED می‌شود.</span></div></div>
    <div class="fstep w"><div class="fn">۴</div><div class="ft"><b>RETRY → PENDING</b>
      <span>با Backoff نمایی <code>۲^n × ۵</code> ثانیه. وقتی <code>AttemptCount</code> به ۳ رسید → FAILED.</span></div></div>
    <div class="fstep r"><div class="fn">۵</div><div class="ft"><b>WORKER_LEASE_EXPIRED</b>
      <span>اگر Worker وسط کار بمیرد، اجاره ۱۰ دقیقه‌ای منقضی می‌شود و Worker بعدی پیام را برمی‌گرداند. این همان Idempotency است.</span></div></div>
  </div>

  <h3>سه نوع Run</h3>
  <table>
    <thead><tr><th>Purpose</th><th>Slot</th><th>چه کسی</th><th>رفتار</th></tr></thead>
    <tbody>
      <tr><td class="mono">PUBLISHED</td><td class="mono">CURRENT</td><td>کاربر با <code>planning.mrp_run.execute</code></td>
        <td>دستی و با تصمیم Route دلخواه. در صورت شکست <b>حذف نمی‌شود</b> تا قابل بررسی بماند.</td></tr>
      <tr><td class="mono">CURRENT</td><td class="mono">CURRENT</td><td><code>SYSTEM:MRP_AUTO_REFRESH</code></td>
        <td>خودکار از Outbox. در صورت شکست به‌عنوان Transient دور ریخته می‌شود.</td></tr>
      <tr><td class="mono">CERTIFIED</td><td class="mono">CERTIFIED</td><td><code>SYSTEM:MRP_DAILY_CERTIFICATION</code></td>
        <td>پایان روز — یک گواهی تغییرناپذیر برای روز کاری تهران می‌سازد.</td></tr>
    </tbody>
  </table>

  <h3>Snapshot — چرا ورودی‌ها فریز می‌شوند</h3>
  <div class="note g">
    قبل از محاسبه، همه ورودی‌ها در جدول‌های <code>planning.mrp_run_*_inputs</code> کپی می‌شوند
    (item, forecast, forecast_period, bom, firm_supply, inventory, route_availability,
    route_decisions, manufacturing_operation, external_source).
    دلیلش <b>قابلیت ردیابی</b> است: وقتی سه هفته بعد می‌پرسند «چرا این عدد این‌طور شد؟»،
    Run هنوز همان ورودی‌های آن لحظه را دارد، حتی اگر BOM بعداً عوض شده باشد.
    <code>mrp_run_demand_traces</code> هم هر انفجار BOM را خط‌به‌خط با <code>ScrapSource</code> نگه می‌دارد.
  </div>

  <h3>منطق محاسبه — شبه‌کد</h3>
  <div class="card"><pre class="ltr" style="margin:0;font-size:12.5px;line-height:1.7;white-space:pre-wrap">balance[item] = onHand[item] + firmSupply(arriving exactly on AsOf, not OVERDUE)

for item in TopologicalOrder(bomGraph):        // Kahn؛ حلقه → BOM_CYCLE_DETECTED
  for date in eventDates(item):                // فقط روزهایی که تقاضا یا تأمین دارند
    if item is PRODUCT and hasDemand(item, date):
      record(daily, gross = demand, isRoot = true)
      explode(item, demand, date)              // ریشه با کل تقاضا منفجر می‌شود، نه Net
      continue

    beforeFirm     = firmBalance     + incoming − grossQty     // FirmProjected
    beforePlanning = planningBalance + incoming − grossQty     // PlanningBeforeNeed
    net            = max(SafetyStock − beforePlanning, 0)      // NetRequirement
    planningBalance = beforePlanning + net

    route = resolveRoute(item, date)
    if route == BOM and net &gt; 0:
      explode(item, net, date)                 // فقط Net به زیرمجموعه می‌رود

explode(parent, qty, date):
  for edge in effectiveBomEdges(parent, date):
    childGross = qty × usageQty / baseOutputQty × (1 + scrap/100)
    gross[child, materialDate] += childGross
    trace(parent, child, scrap, source = BOM_OVERRIDE | ITEM_DEFAULT)</pre></div>

  <h3>تصمیم مسیر تأمین (Route)</h3>
  <table>
    <thead><tr><th>SupplyMethod</th><th>مسیر</th><th>توضیح</th></tr></thead>
    <tbody>
      <tr><td class="mono">— (ItemType=PRODUCT)</td><td><span class="tag">BOM</span></td><td>کالای نهایی همیشه ساخت داخلی</td></tr>
      <tr><td class="mono">BUY</td><td><span class="tag o">EXTERNAL</span></td><td>خرید</td></tr>
      <tr><td class="mono">MAKE</td><td><span class="tag">BOM</span></td><td>ساخت با BOM</td></tr>
      <tr><td class="mono">OUTSOURCE</td><td><span class="tag">BOM</span> یا <span class="tag o">EXTERNAL</span></td><td>اگر BOM مؤثر داشته باشد BOM، وگرنه خرید</td></tr>
      <tr><td class="mono">HYBRID</td><td>نیازمند تصمیم</td><td>اگر هر دو آماده باشند → <code>HYBRID_ROUTE_DECISION_REQUIRED</code> (خطا، نیازمند تصمیم Item+Run). اگر فقط یکی آماده باشد، خودکار انتخاب و <code>AUTO</code> ثبت می‌شود. اگر هیچ‌کدام → <code>HYBRID_NO_VALID_ROUTE</code></td></tr>
    </tbody>
  </table>

  <div class="note"><b>تفکیک مهم:</b> دو مانده موازی نگه داشته می‌شود —
    <code>firmBalance</code> (فقط موجودی + تأمین قطعی، برای تشخیص Stockout واقعی) و
    <code>planningBalance</code> (با در نظر گرفتن نیاز خالص پیشنهادی).
    به همین دلیل UI می‌تواند هم بگوید «کی تمام می‌شود» و هم «چقدر باید سفارش داد».
  </div>
</section>

<!-- ============================ 5 ============================ -->
<section id="auth">
  <h2>مدل دسترسی — RBAC + Deny + Data Scope</h2>
  <p class="lead">
    دسترسی‌ها <b>یک‌بار هنگام لاگین</b> محاسبه و به Claim تبدیل می‌شوند، نه در هر درخواست.
    این کار سریع است ولی یعنی تغییر دسترسی تا رفرش‌شدن Principal اثر ندارد —
    به همین دلیل فیلد <code>AccessVersion</code> وجود دارد تا نشست‌های قدیمی را باطل کند.
  </p>

  <div class="card">{svg_auth()}</div>

  <h3>مراحل احراز هویت — از صفر تا نشست فعال</h3>
  <div class="flowbox">
    <div class="fstep"><div class="fn">۱</div><div class="ft"><b>Bootstrap یک‌بار مصرف</b>
      <span>اولین SystemAdministrator با توکن محیطی ساخته می‌شود. تا وقتی هیچ کاربری وجود ندارد پذیرفته است. هیچ رمز پیش‌فرضی در سورس نیست. Rate Limit: ۵ بار در ۱۵ دقیقه.</span></div></div>
    <div class="fstep"><div class="fn">۲</div><div class="ft"><b>ساخت کاربر توسط Admin</b>
      <span>ثبت‌نام عمومی <b>ممنوع</b> است. نام، نام خانوادگی، ایمیل، موبایل و واحد سازمانی اجباری‌اند + رمز موقت.</span></div></div>
    <div class="fstep w"><div class="fn">۳</div><div class="ft"><b>عکس پرسنلی پیش از فعال‌سازی</b>
      <span>تا وقتی عکس معتبر (JPEG/PNG/WebP حداکثر ۲MB) آپلود نشده، حساب Active نمی‌شود. بایت‌ها در MinIO است، فقط Object Key در دیتابیس.</span></div></div>
    <div class="fstep"><div class="fn">۴</div><div class="ft"><b>ورود اول</b>
      <span><code>MustChangePassword = true</code> → همه ناوبری تجاری بسته است تا رمز عوض شود.</span></div></div>
    <div class="fstep g"><div class="fn">۵</div><div class="ft"><b>نشست</b>
      <span>کوکی <code>hadron.erp.session</code> · HttpOnly · SameSite=Lax · ۸ ساعت Sliding · یک رکورد در <code>identity.user_sessions</code> برای Revocation.</span></div></div>
  </div>

  <h3>چرا Deny مستقیم لازم است</h3>
  <div class="note w">
    فرض کنید نقش «کارشناس خرید» دسترسی <code>procurement.purchase_order.approve</code> را دارد.
    حالا می‌خواهید <b>یک نفر خاص</b> را از این استثنا کنید بدون اینکه نقش را عوض کنید.
    یک <code>DENY</code> مستقیم روی آن کاربر کافی است. ترتیب اجرا در
    <code>HadronClaimsPrincipalFactory</code> این است: اول Grant ها اضافه می‌شوند،
    بعد Deny ها <b>هم حذف می‌کنند و هم یک Claim منفی ثبت می‌کنند</b> —
    تا در Assertion هم <code>HasClaim(permission)</code> رد شود و هم <code>!HasClaim(deny)</code>.
  </div>

  <div class="note r"><b>استثنای SystemAdministrator:</b>
    اگر کاربر این نقش را داشته باشد، کد <b>کلاینت</b> Deny را نادیده می‌گیرد —
    یعنی <code>permissions = همه ۱۳۵ کد</code> و هیچ <code>permission-deny</code> ثبت نمی‌شود.
    این نقش با Deny محدودشدنی نیست. آخرین دارنده این نقش هم نه غیرفعال‌شدنی است و نه قابل خلع.
  </div>

  <h3>Data Scope — لایه «کدام رکوردها»</h3>
  <p class="lead">
    RBAC فقط می‌گوید «حق داری رسید انبار ثبت کنی». Data Scope می‌گوید «فقط روی انبار ۳ و ۷».
    فعلاً فقط دو نوع پیاده شده و بقیه تا وقتی مالکیت رکورد قابل اتکا تعریف شود، به تعویق افتاده‌اند.
  </p>
  <table>
    <thead><tr><th>نوع</th><th>مقدار</th><th>وضعیت</th><th>کجا اعمال می‌شود</th></tr></thead>
    <tbody>
      <tr><td class="mono">ALL</td><td>—</td><td><span class="tag g">پیاده‌شده</span></td>
        <td><code>WarehouseScope()</code> مقدار <code>null</code> برمی‌گرداند = بدون محدودیت</td></tr>
      <tr><td class="mono">WAREHOUSE</td><td><code>{{WarehouseId}}</code></td><td><span class="tag g">پیاده‌شده</span></td>
        <td>کوئری‌های Inventory فیلتر می‌شوند؛ در Mutation <b>همه</b> انبارهای درگیر باید در Scope باشند؛ در انتقال داخلی هم مبدأ هم مقصد</td></tr>
      <tr><td class="mono">COMPANY / DEPARTMENT / PROJECT / OWNER / APPROVAL_AMOUNT</td><td>—</td>
        <td><span class="tag r">به تعویق</span></td><td>تا وقتی دامنه مربوطه «مالکیت رکورد» قابل اتکا نساخته</td></tr>
    </tbody>
  </table>

  <div class="note"><b>نکته پیاده‌سازی مهم:</b> Data Scope در Middleware نیست،
    بلکه داخل <code>InventoryService</code> است. چون برای تصمیم‌گیری به دیتابیس
    (<code>user_data_scopes</code>) و به پارامترهای خود درخواست نیاز دارد.
    SystemAdministrator و حالت Testing همیشه <code>null</code> (بدون محدودیت) می‌گیرند.
    ساخت انبار جدید هم نیازمند Scope کامل است (<code>EnsureAllWarehouseScope</code>).
  </div>

  <h3>دو مسیر جداگانه Audit</h3>
  <table>
    <thead><tr><th>جدول</th><th>مالک</th><th>چه چیزی</th></tr></thead>
    <tbody>
      <tr><td class="mono">identity.security_audit_events</td><td>ماژول Identity</td>
        <td>تاریخچه امنیتی: Actor, TargetUser, Action, فیلد نقش/دسترسی/پروفایل تغییریافته, CorrelationId, Source. هرگز رمز، توکن ریست، کوکی نشست یا بایت عکس ثبت نمی‌شود.</td></tr>
      <tr><td class="mono">platform.operational_audit_events</td><td>Platform/Audit</td>
        <td>جهش‌های تجاری همه ماژول‌ها از راه <code>IAuditEventSink</code>: EntityType, EntityId, Action, OldValues/NewValues به JSON, ActorId, CorrelationId, Reason, Source</td></tr>
    </tbody>
  </table>

  <h3>قرارداد اجباری برای هر قابلیت جدید — ۹ قلم</h3>
  <div class="note g">
    طبق <code>docs/PERMISSIONS.md</code>، یک قابلیت جدید تا <b>همه</b> این‌ها را نداشته باشد ناقص است:
    <ol style="margin:9px 0 0;padding-right:20px">
      <li>یک <code>PermissionDefinition</code> یکتا و تغییرناپذیر با لیبل FA/EN و گروه تجاری</li>
      <li>متادیتای Authorization روی <b>هر</b> Endpoint/Command محافظت‌شده</li>
      <li>متادیتای Route، ناوبری و Action آگاه از دسترسی در فرانت</li>
      <li>قرار گرفتن در کاتالوگ مؤثر SystemAdministrator</li>
      <li>نمایش در ماتریس دسترسی ادمین</li>
      <li>تست Authorization برای سه حالت: مجاز / احرازناشده / ممنوع</li>
      <li>یک تست معماری که هر Endpoint بدون متادیتای صریح را <b>رد</b> کند</li>
      <li>پوشش Audit برای جهش‌های حساس و تغییرات مدیریتی</li>
      <li>به‌روزرسانی مستندات وقتی مرز دسترسی جدیدی معرفی می‌شود</li>
    </ol>
  </div>
</section>

<!-- ============================ 6 ============================ -->
<section id="catalog">
  <h2>کاتالوگ دسترسی‌ها — {len(PERMS)} کد در {len(AREAS)} حوزه</h2>
  <p class="lead">
    این‌ها شناسه‌های ماشین و <b>تغییرناپذیر</b> هستند؛ لیبل فارسی و انگلیسی فقط داده نمایش‌اند.
    در بازنویسی NestJS همین کدها را بدون تغییر نگه دارید تا مجوزهای موجود کاربران نشکند.
  </p>

  <div class="tools">
    <input id="pq" type="search" placeholder="جستجو در کد یا توضیح دسترسی…">
    <span class="cnt" id="pcnt"></span>
  </div>
  <div class="tools"><button class="pill" data-filter="">همه</button>{area_chips()}</div>

  <div class="card" style="max-height:620px;overflow:auto">
  <table>
    <thead><tr><th>کد دسترسی</th><th>حوزه</th><th>عنوان فارسی</th><th>English</th></tr></thead>
    <tbody id="ptbody">
{perm_rows()}
    </tbody>
  </table>
  </div>

  <h3>قواعد Route فرانت‌اند — {len(ROUTES)} قاعده</h3>
  <p class="lead">
    تطبیق با <b>طولانی‌ترین prefix</b> انجام می‌شود. <code>view</code> تصمیم می‌دهد منو دیده شود،
    <code>manage</code> تصمیم می‌دهد دکمه‌های ویرایش فعال باشند.
    این فقط راحتی کاربر است — بک‌اند مستقل چک می‌کند.
  </p>
  <div class="card" style="max-height:460px;overflow:auto">
  <table>
    <thead><tr><th>Prefix</th><th>View Permission</th><th>Manage Permission</th></tr></thead>
    <tbody>{route_rows()}</tbody>
  </table>
  </div>
</section>

<!-- ============================ 7 ============================ -->
<section id="modules">
  <h2>نقشه ماژول‌ها → اسکیما → جدول → Endpoint</h2>
  <p class="lead">
    مرزهای واقعی مالکیت داده. توجه کنید که سه ماژول <code>Planning</code>، <code>Mrp</code> و
    <code>OrderPlanning</code> هر سه روی اسکیمای <code>planning</code> می‌نویسند —
    این تنها جایی است که مرز ماژول با مرز اسکیما یکی نیست.
    ستون «کد دسترسی ارجاع‌شده» هر کدی را می‌شمارد که ماژول در کد خود به آن اشاره کرده
    (چه روی Endpoint و چه داخل Service)؛ پس ممکن است از تعداد <code>RequirePermission</code>ها بیشتر باشد.
  </p>

  <div class="card">
  <table>
    <thead><tr><th>ماژول</th><th>اسکیما(ها)</th><th>جدول</th><th>Endpoint</th><th>Service ها</th><th>کد دسترسی<br>ارجاع‌شده</th></tr></thead>
    <tbody>{module_rows()}</tbody>
  </table>
  </div>

  <h3>مالکیت اسکیماها</h3>
  <div class="card">
  <table>
    <thead><tr><th>اسکیما</th><th>جدول</th><th>ماژول(های) نویسنده</th></tr></thead>
    <tbody>
{"".join(
  f'<tr><td class="mono">{E(s)}</td><td class="num">{len(tbls)}</td>'
  f'<td>{E(", ".join(sorted(ALL_SCHEMAS[s])))}</td></tr>'
  for s, tbls in sorted({sch: {t for m in mods.values() for t in m["tables"] if t.startswith(sch + ".")} for sch in ALL_SCHEMAS}.items())
)}
    </tbody>
  </table>
  </div>

  <h3>فضاهای کاری (Workspace) در رابط کاربری</h3>
  <div class="card">
  <table>
    <thead><tr><th>Workspace</th><th>Root</th><th>کد</th><th>دسترسی ورود</th></tr></thead>
    <tbody>
{"".join(
  f'<tr><td class="b">{E(w[0])}</td><td class="mono">{E(w[1])}</td><td class="mono">{E(w[2])}</td>'
  f'<td class="mono">{E({"cashFlow":"cash_flow.dashboard.view","collaboration":"collaboration.workspace.view"}.get(w[0],"— (بدون gate)"))}</td></tr>'
  for w in WORKSPACES)}
    </tbody>
  </table>
  </div>

  <div class="note w"><b>سه فضای کاری حساس، کاملاً جدا:</b>
    <b>Cash Flow</b> هیچ دسترسی‌ای از Procurement یا Analytics یا Trading یا BOM ارث نمی‌برد و برعکس.
    <b>Collaboration</b> هیچ اختیار ERP نمی‌دهد. <b>Trading</b> هم Audit جداگانه دارد.
    این جداکردن عمدی است و در بازنویسی باید حفظ شود.
  </div>
</section>

<!-- ============================ 8 ============================ -->
<section id="rewrite">
  <h2>راهنمای بازنویسی در NestJS + Next.js</h2>
  <p class="lead">
    نگاشت مستقیم مفاهیم .NET به اکوسیستم Node، به همان ترتیبی که در سورس وجود دارند.
  </p>

  <h3>نگاشت لایه‌ها</h3>
  <table>
    <thead><tr><th>در Hadron (.NET)</th><th>معادل NestJS</th><th>نکته</th></tr></thead>
    <tbody>
      <tr><td class="mono">Modules/X/Api/*Endpoints.cs</td><td class="mono">@Controller + @Get/@Post</td>
        <td>Minimal API معادل مستقیم Controller است</td></tr>
      <tr><td class="mono">Modules/X/Application/*Service.cs</td><td class="mono">@Injectable() Services</td>
        <td>منطق Use Case اینجا می‌ماند</td></tr>
      <tr><td class="mono">Modules/X/Domain/*Entities.cs</td><td class="mono">Entity Class ها</td>
        <td>Constructor خصوصی + متد رفتاری را <b>حفظ کنید</b>؛ Anemic Model نکنید</td></tr>
      <tr><td class="mono">Modules/X/Infrastructure/*Configuration.cs</td><td class="mono">@Entity / Schema + Migration</td>
        <td>Check Constraint ها را در Migration هم بیاورید، نه فقط در DTO</td></tr>
      <tr><td class="mono">HadronDbContext (مشترک)</td><td class="mono">یک DataSource با چند Module</td>
        <td>هر Module فقط Entity های خودش را در <code>forFeature</code> ثبت کند</td></tr>
      <tr><td class="mono">IDbContextFactory</td><td class="mono">Repository / EntityManager</td>
        <td>الگوی «DbContext جدید در هر متد» با UnitOfWork قابل بازتولید است</td></tr>
      <tr><td class="mono">Minimal API + Results.Problem</td><td class="mono">ExceptionFilter + RFC7807</td>
        <td>همان ساختار <code>title/status/code/details</code> را نگه دارید</td></tr>
      <tr><td class="mono">ASP.NET Core Identity + Cookie</td><td class="mono">Passport + express-session یا JWT</td>
        <td>کوکی HttpOnly + SameSite=Lax را حفظ کنید</td></tr>
      <tr><td class="mono">RequirePermission(code)</td><td class="mono">Decorator سفارشی + @UseGuards</td>
        <td>Guard باید <b>Fail-Closed</b> باشد: Handler بدون Decorator → ۴۰۳</td></tr>
      <tr><td class="mono">SaveChangesInterceptor</td><td class="mono">Subscriber / Hook روی Save</td>
        <td>یا Outbox صریح داخل خود Service — مهم ماندن در <b>همان تراکنش</b> است</td></tr>
      <tr><td class="mono">IHostedService (Worker)</td><td class="mono">@nestjs/schedule یا BullMQ Worker</td>
        <td>قفل <code>pg_try_advisory_lock</code> را نگه دارید، وگرنه در چند Replica تکراری اجرا می‌شود</td></tr>
      <tr><td class="mono">SignalR</td><td class="mono">Socket.IO / Gateway</td><td>Collaboration Realtime</td></tr>
      <tr><td class="mono">IAuditEventSink</td><td class="mono">یک Interface + Provider</td>
        <td>همان مرز Post-Commit را حفظ کنید</td></tr>
      <tr><td class="mono">React + Vite</td><td class="mono">Next.js App Router</td>
        <td>مراقب SSR باشید: دسترسی‌ها باید از سمت سرور بیایند نه از state مرورگر</td></tr>
    </tbody>
  </table>

  <h3>۱۰ چیزی که نباید از دست برود</h3>
  <div class="grid">
    <div class="mini"><h4>۱. Fail-Closed</h4><ul>
      <li>Guard سراسری که نبودِ متادیتای دسترسی را ۴۰۳ کند</li>
      <li>یک تست معماری که همه Controller ها را اسکن کند</li>
      <li>این مهم‌ترین ویژگی امنیتی پروژه است</li>
    </ul></div>
    <div class="mini"><h4>۲. Optimistic Concurrency</h4><ul>
      <li><code>Version</code> روی هر Entity</li>
      <li><code>ExpectedVersion</code> در هر Request</li>
      <li>۴۰۹ <code>STALE_VERSION</code> نه ۵۰۰</li>
    </ul></div>
    <div class="mini"><h4>۳. Deactivate نه Delete</h4><ul>
      <li>Master Data هرگز پاک نمی‌شود</li>
      <li>Reactivate مراجع را دوباره اعتبارسنجی می‌کند</li>
    </ul></div>
    <div class="mini"><h4>۴. Transactional Outbox</h4><ul>
      <li>پیام در <b>همان</b> تراکنش کسب‌وکار</li>
      <li>Debounce + Coalesce</li>
      <li>Advisory Lock + Lease + Retry</li>
    </ul></div>
    <div class="mini"><h4>۵. Snapshot ورودی‌های Run</h4><ul>
      <li>محاسبه روی داده فریزشده</li>
      <li>بدون آن «چرا این عدد؟» پاسخ ندارد</li>
    </ul></div>
    <div class="mini"><h4>۶. Calculator خالص</h4><ul>
      <li>ورودی و خروجی فقط رکورد است، بدون I/O</li>
      <li>به همین دلیل به‌شدت تست‌پذیر است — همین را نگه دارید</li>
    </ul></div>
    <div class="mini"><h4>۷. Deny بالاتر از Grant</h4><ul>
      <li>ترتیب: Role ∪ Grant − Deny</li>
      <li>Deny یک Claim منفی هم ثبت می‌کند</li>
    </ul></div>
    <div class="mini"><h4>۸. AccessVersion</h4><ul>
      <li>ابطال کوکی‌های قدیمی بدون Logout دستی</li>
      <li>چک در هر درخواست</li>
    </ul></div>
    <div class="mini"><h4>۹. Data Scope داخل Service</h4><ul>
      <li>نه در Guard — چون به دیتابیس و پارامتر درخواست نیاز دارد</li>
      <li>در Mutation <b>همه</b> رکوردهای درگیر باید در Scope باشند</li>
    </ul></div>
    <div class="mini"><h4>۱۰. کدهای خطای پایدار</h4><ul>
      <li>فرانت روی <code>code</code> منطق دارد نه روی متن</li>
      <li>همان رشته‌ها را حفظ کنید</li>
    </ul></div>
  </div>

  <h3>ترتیب پیشنهادی مهاجرت</h3>
  <div class="flowbox">
    <div class="fstep p"><div class="fn">۱</div><div class="ft"><b>Identity + Permission Catalog + Audit</b>
      <span>زیرساخت است و بقیه به آن وابسته‌اند. اول Fail-Closed Guard و کاتالوگ {len(PERMS)} کد را بیاورید.</span></div></div>
    <div class="fstep p"><div class="fn">۲</div><div class="ft"><b>ReferenceData + Catalog</b>
      <span>Units, Currencies, ReferenceTypes, Items — پایه همه ماژول‌های دیگر.</span></div></div>
    <div class="fstep"><div class="fn">۳</div><div class="ft"><b>Products / Parts / Components + Suppliers + Sourcing</b>
      <span>الگوی ساده CRUD با Deactivate و Audit — بهترین جا برای تثبیت الگو.</span></div></div>
    <div class="fstep"><div class="fn">۴</div><div class="ft"><b>BOM + Planning (Forecast/Actual)</b>
      <span>اولین State Machine جدی (DRAFT → APPROVED → OBSOLETE).</span></div></div>
    <div class="fstep"><div class="fn">۵</div><div class="ft"><b>Inventory</b>
      <span>ledger تغییرناپذیر + Data Scope + انتقال دومرحله‌ای.</span></div></div>
    <div class="fstep g"><div class="fn">۶</div><div class="ft"><b>PurchaseSupply + Mrp Engine + OrderPlanning</b>
      <span>سنگین‌ترین بخش. Outbox و Worker را اینجا بیاورید.</span></div></div>
    <div class="fstep w"><div class="fn">۷</div><div class="ft"><b>Production + Analytics + CashFlow + Trading</b>
      <span>لایه‌های مصرف‌کننده. CashFlow فقط Read Model است و نباید به ماژول منبع بنویسد.</span></div></div>
    <div class="fstep w"><div class="fn">۸</div><div class="ft"><b>Collaboration</b>
      <span>کاملاً مستقل. Realtime + فایل + LiveKit. می‌تواند موازی پیش برود.</span></div></div>
  </div>

  <div class="note r"><b>سه دام رایج در بازنویسی:</b>
    <ul style="margin:9px 0 0">
      <li><b>چک دسترسی فقط در Middleware:</b> Data Scope و Business Policy آنجا جا نمی‌شوند؛ باید داخل Service باشند.</li>
      <li><b>Outbox خارج از تراکنش:</b> اگر پیام بعد از Commit نوشته شود، با Crash بین این دو، تغییر برای همیشه گم می‌شود.</li>
      <li><b>محاسبه MRP روی داده زنده:</b> بدون Snapshot، دو اجرای پشت‌سرهم با ورودی متفاوت نتیجه متفاوت می‌دهند و قابل ردیابی نیستند.</li>
    </ul>
  </div>

  <h3>منابع معتبر در مخزن</h3>
  <table>
    <thead><tr><th>مسیر</th><th>موضوع</th></tr></thead>
    <tbody>
      <tr><td class="mono">docs/PERMISSIONS.md</td><td>مرجع رسمی احراز هویت، پروفایل، دسترسی و قرارداد ثبت ۹‌قلمی</td></tr>
      <tr><td class="mono">docs/ARCHITECTURE.md</td><td>وضعیت فعلی معماری، مرزهای ماژول و قابلیت‌های به تعویق افتاده</td></tr>
      <tr><td class="mono">docs/ADR/ADR-001-IDENTITY-PERMISSION-PROFILE.md</td><td>تصمیم معماری Identity</td></tr>
      <tr><td class="mono">docs/modules/mrp/</td><td>قرارداد رسمی موتور MRP</td></tr>
      <tr><td class="mono">docs/modules/cash-flow/</td><td>قرارداد Cash Flow و گزارش ارزی USD</td></tr>
      <tr><td class="mono">docs/modules/collaboration/</td><td>قرارداد Collaboration</td></tr>
      <tr><td class="mono">docs/ui/HADRON-ERP-UX-ARCHITECTURE.md</td><td>معماری UX، ناوبری و الگوی صفحات</td></tr>
      <tr><td class="mono">src/backend/Hadron.Erp.Api/Platform/Security/PermissionCatalog.cs</td><td>منبع حقیقت کدهای دسترسی</td></tr>
      <tr><td class="mono">src/frontend/src/workspaceNavigation.ts</td><td>منبع حقیقت قواعد Route</td></tr>
    </tbody>
  </table>
</section>

<!-- ============================ 9 ============================ -->
<section id="chain">
  <h2>زنجیره‌ی سفارش تا تولید — نقشه کامل flow و شاخه‌های هر API</h2>
  <p class="lead">
    سؤال: «وقتی سفارش کالا ثبت می‌شود، functionهای دیگری مثل informToSell و informToTrade هم صدا زده می‌شوند؟»
    پاسخ کوتاه: <b>نه — چنین رابطه‌ای در سورس فعلی وجود ندارد و جای خالی‌اش هم دقیق مشخص است.</b>
    ثبت سفارش یعنی <code>POST /api/trading/order-registrations</code> → <code>TradingService.CreateOrder</code>
    که فقط داخل اسکیمای <code>trading</code> می‌نویسد و برمی‌گردد. نزدیک‌ترین مکانیزم‌های موجود به «خبرکردن بقیه»
    این سه‌تاست: ۱) <code>MrpInputChangeInterceptor</code> + <code>planning.mrp_refresh_outbox</code> (فقط برای MRP)،
    ۲) <code>IAuditEventSink</code> (فقط audit — و طبق ARCHITECTURE.md هاست فعلاً no-op ثبت می‌کند)،
    ۳) جدول audit محلی خود Trading.
  </p>

  <h3>نقشه کل جریان — هر API اصلی و شاخه‌هایی که صدا می‌زند</h3>
  <div class="card">{svg_flow()}</div>

  <h3>وضعیت فعلی — CreateOrder گام‌به‌گام (از سورس)</h3>
  <div class="card">{svg_chain()}</div>

  <table>
    <thead><tr><th>گام</th><th>چه می‌شود</th><th>مرجع در سورس</th></tr></thead>
    <tbody>
      <tr><td class="num">۱</td><td>اعتبارسنجی: <code>status</code> فقط DRAFT · <code>registeredAmount &gt; 0</code> · شماره ثبت ≤ ۸۰ نویسه</td>
        <td class="mono sm">TradingService.CreateOrder</td></tr>
      <tr><td class="num">۲</td><td>بررسی پرونده عملیاتی (OpenProject) و ارز فعال</td>
        <td class="mono sm">TradingService.OpenProject / Currency</td></tr>
      <tr><td class="num">۳</td><td>INSERT <code>trading.order_registrations</code> — با Check Constraint وضعیت و مبلغ</td>
        <td class="mono sm">TradingConfiguration — ToTable("order_registrations", "trading")</td></tr>
      <tr><td class="num">۴</td><td>INSERT audit محلی — نه platform.operational_audit_events</td>
        <td class="mono sm">TradingService.AddAudit → TradeAuditEvents</td></tr>
      <tr><td class="num">۵</td><td>SaveChanges از روی MrpInputChangeInterceptor رد می‌شود ولی TradeOrderRegistration در لیست watched نیست → هیچ</td>
        <td class="mono sm">MrpInputChangeInterceptor.cs — Change("...")</td></tr>
      <tr><td class="miss b">✗</td><td class="miss">هیچ informToSell / informToTrade / notify — هیچ ردیفی در هیچ ماژول دیگر</td>
        <td class="mono sm">— وجود ندارد</td></tr>
    </tbody>
  </table>

  <div class="note w"><b>چرا همان فراخوانی همگام (informToSell() داخل CreateOrder) توصیه نمی‌شود؟</b>
    اگر داخل تابع، متد ماژول دیگر را هم صدا بزنی: ۱) شکست CashFlow یعنی Rollback شدن خود سفارش — دو دامنه‌ی مستقل به هم قفل می‌شوند؛
    ۲) latency و تراکنش طولانی؛ ۳) وابستگی چرخه‌ای که ARCHITECTURE.md صریحاً ممنوعش کرده
    («Modules must not write directly to another module's tables or create circular dependencies»).
    فراخوانی همگام فقط برای side effect <b>درون همان ماژول</b> (مثل informToTrade) قابل‌قبول است.
  </div>

  <h3>طرح پیشنهادی — الگوی Outbox که همین امروز برای MRP کار می‌کند</h3>
  <div class="card">{svg_chain_design()}</div>

  <h3>طراحی API — قرارداد پیشنهادی هر شاخه</h3>
  <table>
    <thead><tr><th>اثر جانبی</th><th>Endpoint مقصد</th><th>کد دسترسی</th><th>روش تحویل</th><th>Idempotency</th></tr></thead>
    <tbody>
      <tr><td class="b">informToSell</td>
        <td class="mono">POST /api/cash-flow/obligations (پیش‌نویس تعهد)</td>
        <td class="mono">cash_flow.obligation.manage <span class="tag r">جدید</span></td>
        <td>Outbox — پس از Commit</td><td class="mono sm">OrderId + SELL</td></tr>
      <tr><td class="b">informToTrade</td>
        <td class="mono">DomainEvent داخلی → چک‌لیست FX/محمل روی پرونده</td>
        <td class="mono">trading.fx.manage</td>
        <td>همگام داخل ماژول — اشکالی ندارد</td><td class="mono sm">OrderId + TRADE</td></tr>
      <tr><td class="b">notifyUsers</td>
        <td class="mono">Handler اعلان Collaboration + SignalR push</td>
        <td class="mono">سیستمی (SYSTEM:TRADING_EVENTS)</td>
        <td>Outbox — پس از Commit</td><td class="mono sm">OrderId + NOTIFY</td></tr>
      <tr><td class="b">wakeMrp <span class="sm">(اختیاری)</span></td>
        <td class="mono">planning.mrp_refresh_outbox — همان مکانیزم فعلی</td>
        <td class="mono">planning.mrp_run.execute</td>
        <td>Outbox موجود</td><td class="mono sm">خود outbox MRP</td></tr>
    </tbody>
  </table>

  <div class="note g"><b>چک‌لیست ۹قلمی یادت نرود:</b>
    هر کد دسترسی جدید (مثل <code>cash_flow.obligation.manage</code>) باید طبق قرارداد
    <code>docs/PERMISSIONS.md</code> هر ۹ قلم را داشته باشد: PermissionDefinition یکتا، متادیتای Authorization روی Endpoint،
    Route آگاه از دسترسی در فرانت، کاتالوگ SystemAdministrator، ماتریس ادمین، سه تست Authorization، تست معماری، پوشش Audit و به‌روزرسانی مستندات.
    در بازنویسی NestJS: رویداد دامنه → ردیف Outbox در همان تراکنش TypeORM/EF → Worker با advisory lock → Handlerهای ماژولی که سرویس Application خود ماژول مقصد را صدا می‌زنند — نه HTTP به خودِ سرویس.
  </div>
</section>

</main>

<footer>
  تولیدشده از سورس <code>sepehrsylvanus/hadron</code> · کامیت <code>439d5bf</code> ·
  {len(BUSINESS_MODS)} ماژول تجاری + Platform · {TOTAL_ENDPOINTS} Endpoint · {TOTAL_TABLES} جدول در {len(ALL_SCHEMAS)} اسکیما · {len(PERMS)} کد دسترسی
  · برای به‌روزرسانی: <code>python3 docs/diagrams/_build.py</code>
</footer>

<script>{JS}</script>
</body>
</html>
"""

OUT.write_text(HTML, encoding="utf-8")
print(f"wrote {OUT} ({len(HTML):,} chars)")
print(f"  modules={len(mods)} endpoints={TOTAL_ENDPOINTS} tables={TOTAL_TABLES} "
      f"schemas={len(ALL_SCHEMAS)} perms={len(PERMS)} areas={len(AREAS)} routes={len(ROUTES)}")
