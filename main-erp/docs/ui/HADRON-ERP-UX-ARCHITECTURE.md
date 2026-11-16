# HADRON ERP UX ARCHITECTURE
## Canonical Enterprise UX Reference for Hadron ERP System

**Version:** v1.0  
**Date:** 2026-08-23  
**Status:** Canonical UX Foundation  
**System Title:** **Hadron ERP System**

---

## 0. Purpose

این سند مرجع Canonical برای **UX، Information Architecture، Navigation، Shell، Page Patterns و Workflow Patterns** در Hadron ERP System است.

این سند **Visual Design System هادرون را جایگزین نمی‌کند**. ظاهر، رنگ، تایپوگرافی، spacing، radius، status colors، logo و سایر تصمیم‌های Visual UI همچنان از **Hadron Digital Design System v1.0** پیروی می‌کنند.

این سند برای رفتار و ساختار ERP از الگوهای رسمی **SAP Fiori for Web Design Guidelines** استفاده می‌کند، با هدف ساخت یک ERP سنگین، قابل‌پیش‌بینی، task-oriented و مناسب workflowهای سازمانی.

### Authority Stack

ترتیب مرجع در پروژه:

1. **Business Logic & Domain Rules**  
   Canonical MRP Packages / Decisions / System Spec

2. **Enterprise UX & Interaction Architecture**  
   این سند — **HADRON ERP UX ARCHITECTURE**

3. **Visual UI / Brand / Design Tokens**  
   **Hadron Digital Design System v1.0**

4. **Actual Repository Reality**  
   Implementation واقعی React / ASP.NET / PostgreSQL

در صورت تعارض:

- UX نباید Business Rule را تغییر دهد.
- Visual UI نباید Workflow را مخفی یا مبهم کند.
- Patternهای SAP Fiori برای Interaction/Navigation مرجع هستند.
- رنگ و ظاهر SAP کپی نمی‌شود؛ ظاهر همیشه Hadron است.

---

# 1. UX Principles

Hadron ERP باید مانند یک **Enterprise Work System** رفتار کند، نه مجموعه‌ای از صفحه‌های مستقل.

اصول پایه:

### 1.1 Task-Oriented, Not Module-Code-Oriented

کاربر نباید عبارت‌هایی مانند:

- MRP-01
- MRP-02
- Package 06
- Feature 13

را در UI ببیند.

این شماره‌ها فقط برای development/governance هستند.

UI باید با زبان کاری کاربر سازمان‌دهی شود:

- محصولات
- قطعات
- تأمین‌کنندگان
- BOM
- منابع تأمین
- برنامه‌ریزی مواد
- سفارش خرید
- گزارش‌ها

---

### 1.2 Context Must Be Preserved

کاربر نباید برای هر عملیات به Home یا Menu اصلی برگردد.

الگوی اصلی:

```text
List
→ Object
→ Related Work
→ Return with context preserved
```

مثال:

```text
محصولات
→ محصول A10
→ BOMهای محصول
→ Revision BOM
→ Line
```

یا:

```text
تأمین‌کنندگان
→ Supplier
→ Contacts
→ Sourcing Options
→ Route / Lead Time
```

---

### 1.3 Predictable Enterprise Structure

صفحه‌های هم‌نوع باید رفتار مشابه داشته باشند.

برای مثال تمام Master Dataها:

```text
List Report
→ Object Page
→ Edit
```

نباید Product یک رفتار، Supplier یک رفتار و Part یک رفتار کاملاً متفاوت داشته باشد.

---

### 1.4 Progressive Disclosure

همه اطلاعات همزمان نمایش داده نشوند.

اطلاعات بر اساس اهمیت و context لایه‌بندی شوند:

```text
Summary
→ Key operational fields
→ Sections
→ Related data
→ History / advanced details
```

---

### 1.5 Heavy Workflow Ready

ERP باید از ابتدا برای داده زیاد و workflow پیچیده آماده باشد:

- server-side filtering
- server-side paging
- persistent context
- clear object identity
- explicit status
- predictable actions
- scalable tables
- deep links
- related-object navigation
- no hidden business state

---

# 2. Global ERP Architecture

## 2.1 System Entry Point

صفحه اصلی باید دقیقاً با عنوان زیر شناخته شود:

# **Hadron ERP System**

این صفحه نقش **Enterprise Launchpad** را دارد.

در نسخه فعلی:

```text
Hadron ERP System
│
├── MRP
├── Production
├── Inventory
└── System Administration
```

`Inventory` یک workspace مستقل است و الگوی آن `Overview → Worklist → Object/Operation` است. مسیرهای اصلی آن شامل موجودی اقلام، دفتر گردش، دریافت، خروج، انتقال دومرحله‌ای، تعدیل، انبارها و موجودی افتتاحیه است. Logistics هنوز workspace عملیاتی نیست و نباید به‌صورت صفحه جعلی نمایش داده شود.

در آینده ممکن است domainهای دیگری اضافه شوند:

```text
Hadron ERP System
│
├── MRP
├── Sales
├── Commercial
├── Finance
├── CRM
├── HR
└── ...
```

### Rule

Feature یا domain آینده تا زمانی که واقعاً implement نشده:

- در production نباید به‌صورت clickable نمایش داده شود.
- نباید Fake Page یا Fake Workflow ساخته شود.
- roadmap development با production navigation یکی نیست.

---

# 3. Global Shell

Shell در تمام ERP ثابت است.

## 3.1 Shell Bar

ساختار پایه:

```text
┌───────────────────────────────────────────────────────────────┐
│ Hadron Logo   Hadron ERP System    [Context]   Search   User │
└───────────────────────────────────────────────────────────────┘
```

### Mandatory

- Official Hadron Logo
- Product title: `Hadron ERP System`
- Current application/workspace context

### Future / Conditional

- Enterprise Search
- Notifications
- User Profile
- Settings

این موارد فقط وقتی نمایش داده شوند که backend/platform واقعی آنها وجود داشته باشد.

### Logo Behavior

Hadron Logo:

```text
Click → Hadron ERP System Home
```

---

# 4. Workspace Model

MRP، Production، Inventory و Cash Flow چهار **Workspace / Business Space** مستقل داخل Hadron ERP هستند.

ورود:

```text
Hadron ERP System
        ├── MRP → MRP Workspace
        └── Production → Production Workspace
```

MRP Workspace باید context برنامه‌ریزی نیازمندی مواد را حفظ کند. Production Workspace در v1 فقط سطح عملیاتی «آمار تولید روزانه» را ارائه می‌کند. Forecast در MRP باقی می‌ماند و ثبت/اصلاح Daily Actual فقط از Production Workspace انجام می‌شود؛ جداسازی Workspace نباید منبع داده یا محاسبات MRP را دوگانه کند.

Cash Flow یک فضای حساس و permission-isolated است و زیرمجموعهٔ MRP، Analytics یا Trading نیست. ساختار فعلی آن:

```text
Cash Flow
├── نمای کلی
├── برنامه پرداخت
│   └── تقویم تعهدات پرداخت
├── بهای اقلام و محصولات
│   └── بهای ساختار محصولات
└── تنظیمات گزارشگری
    └── نرخ‌های ارز
```

نرخ‌های ارز از الگوی `List Report -> Object Page` پیروی می‌کنند. ایجاد/اصلاح مجموعه نرخ یک فرم جدولی در صفحهٔ مستقل است؛ Modal بلند یا Wizard برای آن مجاز نیست. نسخهٔ Published در Display Mode تغییرناپذیر است و Correction، Revision جدید می‌سازد. داشبورد به‌صورت پیش‌فرض USD است ولی مبلغ و ارز اصلی را برای ردیابی حفظ می‌کند.

---

# 5. MRP Information Architecture

ساختار منوی کاربر باید بر اساس Business Domain باشد، نه ترتیب Packageها.

## 5.1 Canonical MRP Navigation

```text
MRP
│
├── نمای کلی
│
│   └── MRP Overview
│
├── اطلاعات پایه
│   ├── محصولات
│   ├── قطعات
│   ├── اجزا
│   └── اطلاعات مرجع
│
├── تأمین‌کنندگان و منابع تأمین
│   ├── تأمین‌کنندگان
│   ├── منابع تأمین
│   ├── برنامه سفارش
│   └── سفارش‌های خرید
│
├── مهندسی
│   └── BOM
│
├── برنامه‌ریزی
│   ├── Production Forecast
│   ├── موجودی
│   ├── وضعیت تأمین
│   ├── اجرای MRP
│   └── برنامه‌ریزی مواد
│
└── تحلیل و گزارش
    └── گزارش‌ها و تحلیل MRP
```

---

# 6. Mapping of MRP Packages to UX

این Mapping برای Development Traceability است و در UI به کاربر نمایش داده نمی‌شود.

| Package | User-Facing Destination | UX Group |
|---|---|---|
| MRP-01 | اطلاعات مرجع | اطلاعات پایه |
| MRP-02 | محصولات | اطلاعات پایه |
| MRP-03 | تأمین‌کنندگان | تأمین‌کنندگان و منابع تأمین |
| MRP-04 | قطعات / اجزا | اطلاعات پایه |
| MRP-05 | منابع تأمین | تأمین‌کنندگان و منابع تأمین |
| MRP-06 | BOM | مهندسی |
| MRP-07 Forecast | Production Forecast | برنامه‌ریزی در MRP |
| MRP-07 Daily Actual | Daily Production Statistics | عملیات تولید در Production |
| MRP-08 | موجودی و انبارها | Inventory workspace مستقل |
| MRP-09 | وضعیت تأمین | برنامه‌ریزی |
| MRP-10 | اجرای MRP | برنامه‌ریزی |
| MRP-11 | برنامه‌ریزی مواد | برنامه‌ریزی |
| MRP-12 | برنامه سفارش | تأمین‌کنندگان و منابع تأمین |
| MRP-13 | سفارش‌های خرید | تأمین‌کنندگان و منابع تأمین |
| MRP-14 | گزارش‌ها و تحلیل MRP | تحلیل و گزارش |

---

# 7. Side Navigation

MRP در Desktop از Side Navigation collapsible استفاده می‌کند.

## Expanded

```text
MRP

نمای کلی

اطلاعات پایه
  محصولات
  قطعات
  اجزا
  اطلاعات مرجع

تأمین‌کنندگان و منابع تأمین
  تأمین‌کنندگان
  منابع تأمین
  برنامه سفارش
  سفارش‌های خرید

مهندسی
  BOM

برنامه‌ریزی
  Production Forecast
  موجودی
  وضعیت تأمین
  اجرای MRP
  برنامه‌ریزی مواد

تحلیل و گزارش
  گزارش‌ها و تحلیل MRP
```

Production Workspace در v1 ناوبری مستقل و حداکثر دو سطحی دارد:

```text
Production

نمای کلی

عملیات تولید
  Daily Production Statistics
```

## Collapsed

در حالت collapsed فقط iconهای consistent نمایش داده می‌شوند.

### Rules

- حداکثر دو سطح Navigation.
- Nested menu بیش از دو level ممنوع.
- active item واضح باشد.
- active section نیز قابل تشخیص باشد.
- menu state هنگام navigation حفظ شود.
- groupها بر اساس work context باشند، نه database/module ownership.

---

# 8. Breadcrumb Model

Breadcrumb برای صفحه‌های عمقی الزامی است.

نمونه:

```text
Hadron ERP System / MRP / اطلاعات پایه / محصولات
```

برای Object:

```text
Hadron ERP System / MRP / اطلاعات پایه / محصولات / A10
```

برای BOM:

```text
Hadron ERP System / MRP / مهندسی / BOM / BOM-000021
```

Breadcrumb جای Back button را کاملاً نمی‌گیرد؛ Back باید history context را حفظ کند.

---

# 9. Canonical Page Patterns

Hadron ERP از تعداد محدودی Page Pattern استفاده می‌کند.

## 9.1 List Report

برای:

- Products
- Parts
- Components
- Suppliers
- Sourcing Options
- Forecast rows
- Inventory views
- Purchase Orders
- Reports list

ساختار:

```text
Page Header
Title + Context + Primary Action

Filter Area
Search + Important Filters + More Filters

Table Toolbar
Result Count + Table Actions

Data Table
Server-side

Pagination / Lazy Paging
```

### Rule

List Report نقطه ورود به datasetهای بزرگ است.

---

## 9.2 Object Page

برای مشاهده و ویرایش یک Business Object.

مناسب برای:

- Product
- Part
- Component
- Supplier
- Sourcing Option
- Purchase Order

ساختار:

```text
Dynamic Header

Object Identity
Status
Key attributes
Global actions

Tabs / Sections
Overview
Planning
Commercial
Related Data
History
```

### Example — Product

```text
A10 Adapter Timer
PRD-0001                         Active

[Edit] [More]

Overview
Planning Policy
BOM
Sourcing
History
```

---

## 9.3 Flexible Column Layout

برای workflowهای master-detail سنگین.

مناسب‌ترین کاربردهای Hadron:

### BOM

```text
┌───────────────┬──────────────────────┬─────────────────┐
│ Revisions     │ BOM Lines            │ Line Detail     │
│               │                      │                 │
│ BOM-000021    │ Part A               │ Usage Qty       │
│ BOM-000020    │ Part B               │ Scrap Override  │
│ BOM-000019    │ Part C               │ Unit            │
└───────────────┴──────────────────────┴─────────────────┘
```

### Sourcing

صفحه اصلی منابع تأمین یک Workbench قلم‌محور و دو سطحی است:

```text
Item (Part / Component)
└── Sourcing Option (Supplier + MOQ + Route + Lead Time + Readiness)
    └── Contextual Detail
```

- سطح اول قلم است و صفحه‌بندی نیز بر مبنای قلم انجام می‌شود؛ گزینه‌های یک قلم نباید بین دو صفحه شکسته شوند.
- سطح دوم فقط گزینه‌های تأمین همان قلم است. این ساختار recursive نیست و نباید با درخت مهندسی BOM اشتباه شود.
- انتخاب هر گزینه، جزئیات رابطه، خط زمانی تأمین و دلیل فارسی آمادگی/عدم‌آمادگی را در پنل کناری نشان می‌دهد.
- Product فقط context و فیلتر مصرف است؛ مالک رابطه تأمین نیست. مالک رابطه همان Item است.
- قیمت و ارز در Sourcing ثبت یا نمایش داده نمی‌شود و به فرایند تجاری/مالی مستقل تعلق دارد.
- Create/Edit/Set Preferred باید علاوه بر محافظت Backend، در UI نیز بر اساس permission مستقل نمایش داده شود.

### Rule

Flexible Column Layout باید context را حفظ کند و از رفت‌وبرگشت بین چند صفحه جلوگیری کند.

---

## 9.4 Dynamic Page

برای صفحه‌هایی با Header context + Filter/Actions + Content.

تمام List Report و Object Pageها باید از منطق Dynamic Header پیروی کنند:

- Header اصلی همیشه context را حفظ کند.
- جزئیات header قابل collapse باشند.
- actions مهم در جای ثابت باقی بمانند.

---

## 9.5 Wizard

Wizard فقط برای taskهای:

- طولانی
- چندمرحله‌ای
- ناآشنا
- با dependency بین مراحل

استفاده شود.

Rule:

```text
Simple Create Form ≠ Wizard
```

Wizard نباید برای هر Create استفاده شود.

حد معمول:

```text
3–8 Steps
```

---

## 9.6 Analytical List Page

برای MRP-11 / MRP-14 و تحلیل‌های decision-oriented.

مناسب برای:

- Material Planning
- Risk Analysis
- Supplier performance
- MRP exceptions
- root-cause analysis

ساختار:

```text
Filters / KPIs
Chart
Table
Transactional Actions
```

کاربر باید بتواند:

```text
Analyze
→ Identify Exception
→ Open Object
→ Act
```

بدون تغییر سیستم یا context غیرضروری.

---

# 10. Current Modules — Required UX Pattern

## Reference Data

Pattern:

```text
List Report + lightweight detail/edit panel
```

نباید برای هر Reference Type صفحه جداگانه ساخته شود.

---

## Products

Pattern:

```text
Product List Report
→ Product Object Page
```

Object Page Sections:

- اطلاعات عمومی
- سیاست برنامه‌ریزی
- BOM
- منابع تأمین
- تاریخچه

بخش‌هایی که backend آنها هنوز وجود ندارد نباید Fake Data نشان دهند.

---

## Suppliers

Pattern:

```text
Supplier List Report
→ Supplier Object Page
```

Sections:

- Overview
- Contacts
- Default Payment Profile
- Evaluation
- Sourcing Options
- History

Contact باید sub-object Supplier باشد، نه صفحه مستقل global.

---

## Parts

Pattern:

```text
Part List Report
→ Part Object Page
```

Sections:

- Overview
- Planning Policy
- BOM
- Sourcing
- History

---

## Components

Pattern:

```text
Component List Report
→ Component Object Page
```

Sections مشابه Part.

---

## Sourcing

Pattern:

```text
Sourcing List Report
→ Sourcing Object Page
```

Sections:

- Relationship
- Commercial
- Route & Lead Time
- Price History
- Readiness
- History

Preferred action باید context-aware باشد و status آن واضح باشد.

---

## BOM

Pattern اصلی:

```text
BOM List
→ Flexible Column Layout
```

Columns:

1. Revision Context
2. Lines
3. Line/Revision Detail

Actions:

- Create Draft
- Copy Revision
- Validate
- Approve
- Obsolete

Approved/Obsolete باید read-only به‌وضوح قابل تشخیص باشند.

---

# 11. Object Identity

هر Object Page باید identity واضح داشته باشد.

مثال:

```text
Product
A10 Adapter Timer
PRD-0001
Active
```

یا:

```text
Supplier
Long Top Electronics
SUP-0012
Inactive
```

یا:

```text
BOM Revision
BOM-000021
Product: A10
Approved
Effective: 2026-09-01 →
```

کاربر نباید برای فهمیدن «کجاست و روی چه objectی کار می‌کند» به table قبلی برگردد.

---

# 12. Display Mode vs Edit Mode

Object Page به‌طور پیش‌فرض:

```text
Display Mode
```

Edit صریح:

```text
[Edit]
→ Edit Mode
```

در Edit Mode:

- Save
- Cancel
- validation
- unsaved changes protection

الزامی‌اند.

### Rule

Long forms در modal قرار نگیرند.

Modal فقط برای:

- confirmation
- short action
- small focused edit
- destructive confirmation

---

# 13. Action Placement

Actions سه سطح دارند:

### Global Object Actions

مثل:

- Edit
- Approve
- Obsolete
- Deactivate

در Object Header.

### Section Actions

مثل:

- Add Contact
- Add BOM Line
- Add Price

در toolbar همان section.

### Row Actions

برای object کوچک یا line-specific action.

### Rule

در هر context فقط یک Primary Action باید غالب باشد.

---

# 14. Filter Architecture

Filter Bar باید consistent باشد.

ترتیب:

1. Search
2. Business-critical filters
3. Status
4. More Filters

مثال Products:

```text
Search | Category | Business Unit | Status | More
```

مثال Sourcing:

```text
Search | Item | Supplier | Readiness | Preferred | Evaluation Date | More
```

### Rules

- Filter change → page reset
- applied filters visible
- clear/reset available
- filtering server-side
- list state هنگام رفتن به detail و برگشت حفظ شود

---

# 15. Table UX

Table برای heavy workflow باید:

- server-side
- sortable where meaningful
- filter-aware
- paged/lazy
- stable columns
- clear status
- clear row selection
- row click / explicit object navigation
- empty state
- loading state
- error state

داشته باشد.

### Numeric / Technical Data

برای:

- Code
- SKU
- Date
- Qty
- Currency
- Number

LTR formatting در محیط RTL مجاز و توصیه‌شده است.

---

# 16. Related Object Navigation

ERP باید relationshipها را قابل navigation کند.

مثال:

Product Object Page:

```text
BOM
→ Open BOM

Sourcing
→ Open Sourcing Option
```

Supplier Object Page:

```text
Sourcing Options
→ Open Sourcing
```

Part:

```text
Used in BOM
→ Related BOM
```

Rule:

Relationship قابل مشاهده نباید فقط text مرده باشد اگر destination واقعی وجود دارد.

---

# 17. Create Experience

Createهای ساده:

```text
Dedicated Create Page
or
Object Page Create Mode
```

Createهای سنگین و چندمرحله‌ای:

```text
Wizard
```

Createهای کوچک sub-object:

```text
Dialog / Inline section form
```

مثال:

- Product Create → Create Page
- Supplier Create → Create Page
- Contact Create → Dialog/Section
- BOM Revision → Create Page / Copy
- BOM Line → inline/detail column
- PO complex creation → potentially Wizard

---

# 18. Validation & Error UX

سه سطح:

## Field Error

زیر field.

## Section Error

Message strip داخل section.

## Page / Transaction Error

Message area واضح نزدیک header/actions.

Structured backend errors باید:

- code
- title/message
- relevant details

را بدون از بین رفتن context نمایش دهند.

Error نباید فقط Toast کوتاه باشد اگر action کاربر را block می‌کند.

---

# 19. Status UX

هر status باید:

- label متنی
- semantic visual cue
- consistent placement

داشته باشد.

Status فقط با رنگ منتقل نشود.

نمونه:

```text
Active
Inactive
Draft
Approved
Obsolete
Ready
Incomplete
Blocked
```

---

# 20. Loading / Empty / No Result

تمام صفحات باید سه state مستقل داشته باشند:

### Loading

Skeleton / controlled progress.

### Empty Dataset

مثلاً:

> هنوز محصولی ثبت نشده است.

با Create action اگر مجاز.

### No Filter Result

مثلاً:

> نتیجه‌ای با فیلترهای فعلی پیدا نشد.

با Reset Filters.

Empty و No Result نباید یکی باشند.

---

# 21. Unsaved Changes

اگر کاربر Edit Mode را با داده تغییرکرده ترک کند:

```text
Save changes?
Discard changes?
Stay on page?
```

Navigation نباید silently data را از بین ببرد.

---

# 22. Heavy Workflow Context Rules

برای workflowهای سنگین:

- list position حفظ شود
- filter state حفظ شود
- selected object حفظ شود
- breadcrumb حفظ شود
- related navigation context حفظ شود
- browser Back رفتار قابل‌پیش‌بینی داشته باشد

هدف:

```text
Minimum Context Loss
```

نه صرفاً minimum click count.

---

# 23. Search Strategy

### Current

هر Module:

```text
Local Search
```

### Future

وقتی backend/search infrastructure واقعی وجود داشت:

```text
Enterprise Search
```

در Shell.

Enterprise Search نباید فقط به‌صورت ظاهری Fake شود.

---

# 24. Notifications & User Profile

Notification و User Profile بخشی از Shell Architecture هستند ولی implementation آنها به foundation واقعی نیاز دارد.

تا زمان پیاده‌سازی Identity مصوب:

- Fake User profile ممنوع.
- Fake Notification center ممنوع.
- fake permission-driven menu ممنوع.

پس از پیاده‌سازی `docs/PERMISSIONS.md`، Shell باید نام کامل و عکس پرسنلی واقعی User احرازشده را از Session سرور نشان دهد. User menu شامل Profile، Change Password و Logout است. عکس یا نام نباید از `localStorage` یا دادهٔ نمایشی ساخته شود.

---

# 25. Permission-Driven UX

معماری Identity/RBAC در `docs/PERMISSIONS.md` Owner-approved است و بعد از implementation آن:

Navigation باید role-aware شود.

User باید فقط appها/actions مرتبط را ببیند.

System Administration باید User Worklist، Personnel Profile، Role، Permission Matrix، Direct Grant/Deny، Effective Access، Session Revocation و Security History را به‌صورت واضح و جدا ارائه کند.

هر route، navigation item و action جدید باید Permission مشخص داشته باشد. Permission جدید باید به‌صورت خودکار در Catalog مدیریتی ظاهر شود؛ کاربر بدون Grant آن را نمی‌بیند و direct URL/API نیز باید block شود.

اما:

```text
Hidden UI ≠ Security
```

Backend authorization همیشه source of truth خواهد بود.

---

# 26. Responsive Strategy

Hadron ERP:

```text
Desktop-first
Tablet-capable
Mobile-safe
```

Heavy operational workflows برای desktop بهینه می‌شوند.

در صفحه کوچک:

- Side nav collapse
- FCL → fewer visible columns
- tables responsive
- nonessential columns hide/stack

اما business data نباید silently حذف شود.

---

# 27. Language and Direction Strategy

Supported interface languages in v1:

```text
fa-IR — Persian / RTL
en    — English / LTR
```

The default language is `fa-IR`.

Language selection is available through one global shell control without country flags. Until Identity and user preferences exist, the selection is stored locally in the browser and must persist across navigation, refresh, and browser restart.

Changing language updates the global document `lang` and `dir` immediately without a full-page reload.

اما technical values مانند:

- Product Code
- Supplier Number
- Currency
- dates
- quantities
- SKU
- machine codes

می‌توانند در cell/field خود LTR باشند.

Technical values may retain LTR direction within either interface language. Changing UI language must not alter `NameFa`, `NameEn`, stored business data, or API payload semantics.

All new UX from Phase 2 onward must use organized translation keys rather than hardcoded user-facing labels.

---

# 28. UI Authority

تمام Visual decisions باید از:

**Hadron Digital Design System v1.0**

بیایند، از جمله:

- Logo
- Hadron Red
- typography
- spacing
- radius
- shadows
- buttons
- status colors
- cards
- tables
- forms
- responsive visual treatment

SAP Fiori فقط Interaction Architecture و UX Pattern authority است.

---

# 29. Prohibited UX Patterns

موارد زیر در Hadron ERP ممنوع‌اند:

- menu بر اساس MRP-01/MRP-02 numbering
- صفحه مستقل برای هر field group بدون نیاز واقعی
- modal بلند برای master form
- navigation بدون breadcrumb/context
- full-page red background برای operational UI
- actions پراکنده و inconsistent
- fake dashboards
- fake search/profile/notifications
- client-only truth برای business state
- loading کل dataset برای dropdown
- first-100-only selectors
- deep nested menu
- fixed-depth BOM navigation
- duplicated UX pattern برای objectهای مشابه
- destructive action بدون explicit confirmation
- workflowی که user را بعد از هر action به Home برگرداند

---

# 30. Current UI Refactor Scope — MRP-01 to MRP-06

قبل از MRP-07، Refactor UI/UX باید روی Foundation انجام شود.

## Phase 1 — Shell

- Hadron ERP System Home
- MRP Workspace
- Global Shell Bar
- MRP Side Navigation
- Breadcrumbs
- routing/context preservation

## Phase 2 — Shared Page Patterns

- List Report shell
- Object Page shell
- Dynamic Header
- Filter Bar
- Table Toolbar
- Status
- Message/Error
- Loading/Empty states
- confirmation dialogs

## Phase 3 — Existing Modules

Refactor:

- Reference Data
- Products
- Suppliers
- Parts
- Components
- Sourcing
- BOM

بدون تغییر Business Logic.

## Phase 4 — Heavy Workflow

- BOM Flexible Column Layout
- Sourcing Object/Price workflow
- Related Object navigation

## Phase 5 — Owner UX Review

بعد از Refactor:

```text
Owner Review
→ Findings
→ UX Fix
→ UX Foundation Freeze
```

سپس MRP-07 آغاز می‌شود.

---

# 31. Rules for MRP-07 to MRP-14

از MRP-07 به بعد، هیچ صفحه جدیدی نباید UX جدید اختراع کند.

هر Feature باید ابتدا یکی از Patternهای موجود را انتخاب کند:

```text
List Report
Object Page
Dynamic Page
Flexible Column Layout
Wizard
Analytical List Page
Worklist
```

اگر هیچ Pattern مناسب نبود، قبل از Implementation باید UX decision جدید به همین سند اضافه شود.

---

# 32. Future Page Mapping

## MRP-07 — Production Forecast / Daily Production Actual

```text
List Report
→ Product Forecast Object/Period Detail
```

Forecast under MRP and Daily Actual under Production are separate user-facing destinations. The Forecast view may show read-only Actual MTD and Remaining Forecast as planning context, but actual entry/correction belongs only to the Production workspace.

## MRP-08 — Inventory

```text
Inventory Overview
→ Item Positions / Warehouse Worklist / Immutable Ledger / Transfer Worklist
→ Item or Warehouse Object / Explicit Posting / Two-Step Transfer Object
```

ورود، خروج، Opening، Adjustment و انتقال باید use-case مستقل و permission-aware باشند؛ یک فرم generic با Type selector الگوی تأییدشده نیست. انتقال داخلی در Object Page وضعیت Dispatch، دریافت‌های جزئی و مانده In-Transit را به‌صورت روشن نمایش می‌دهد.

## MRP-09 — Purchase Supply Foundation

```text
Supply Position List
→ Supply Detail
```

## MRP-10 — MRP Engine

```text
MRP Runs List
→ MRP Run Object Page
```

## MRP-11 — Material Planning UI

Primary pattern:

```text
Analytical List Page
+
Object navigation
```

این یکی از مهم‌ترین صفحات ERP خواهد بود.

## MRP-12 — Order Planning

```text
Worklist / List Report
→ Recommendation Detail
→ Expert Action
```

## MRP-13 — Purchase Orders

```text
PO List Report
→ PO Object Page
```

ممکن است Create PO workflow در صورت پیچیدگی از Wizard استفاده کند.

## MRP-14 — Analytics

```text
Analytical List Page
Dashboard
Reports
```

---

# 33. UX Quality Gate

هر Feature جدید قبل از `CLOSED` شدن باید از UX Audit عبور کند.

Audit minimum:

- صحیح بودن Page Pattern
- context/navigation
- breadcrumb
- list/detail relationship
- filter scalability
- server-side data reachability
- edit/display states
- loading/error/empty states
- status clarity
- related-object navigation
- no fake functionality
- RTL correctness
- conformity with Hadron Design System

---

# 34. Canonical Implementation Rule for Codex / ChatGPT

```text
HADRON ERP UX — CANONICAL

1. The product name is "Hadron ERP System".
2. Hadron ERP Home is the global launchpad.
3. MRP and Production are separate workspace/business spaces inside Hadron ERP; MRP owns Forecast UX and Production owns Daily Actual UX.
4. Never expose MRP package numbers as user navigation.
5. Use business-domain navigation groups.
6. Use SAP Fiori UX patterns as the enterprise interaction reference.
7. Use Hadron Digital Design System as the visual/UI authority.
8. List-heavy masters use List Report.
9. Business objects use Object Page.
10. Heavy master-detail workflows use Flexible Column Layout where context retention benefits users.
11. Long/unfamiliar multistep tasks may use Wizard; simple create forms must not.
12. Analytical operational pages use Analytical List Page where chart + table + action are interdependent.
13. Maintain Shell, breadcrumb, current workspace, filter state, and object context.
14. Do not implement fake Identity, Search, Notifications, Permissions, or future modules.
15. Do not load arbitrary full datasets merely to populate selectors.
16. Do not limit users to an unreachable "first 100" records.
17. Long forms must not live in modal dialogs.
18. Similar business objects must share UX patterns.
19. UX must not change canonical business logic.
20. New UX patterns require explicit architectural approval before implementation.
```

---

# 35. Official SAP UX References

The UX patterns in this document are derived from official SAP Fiori for Web Design Guidelines:

- SAP Fiori Launchpad  
  https://experience.sap.com/fiori-design-web/launchpad/

- Launchpad Shell Bar  
  https://experience.sap.com/fiori-design-web/shell-bar/

- List Report Floorplan  
  https://experience.sap.com/fiori-design-web/list-report-floorplan-sap-fiori-element/

- Object Page Floorplan  
  https://experience.sap.com/fiori-design-web/object-page/

- Flexible Column Layout  
  https://experience.sap.com/fiori-design-web/flexible-column-layout/

- Analytical List Page  
  https://experience.sap.com/fiori-design-web/analytical-list-page/

- Wizard Floorplan  
  https://experience.sap.com/fiori-design-web/wizard/

- Dynamic Page / Page Layouts  
  https://experience.sap.com/fiori-design-web/explore_category/page-layouts/

- When to Use Which Floorplan  
  https://experience.sap.com/fiori-design-web/when-to-use-which-floorplan/

These references define UX interaction patterns. They do **not** override the Hadron brand visual system.

---

# 36. Canonical Repository Location

Store this document at:

```text
docs/ui/HADRON-ERP-UX-ARCHITECTURE.md
```

The Hadron visual system remains at:

```text
docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf
```

Recommended authority statement in `AGENTS.md` / architecture documentation:

```text
For Hadron ERP:
- Business rules: canonical module specifications and locked Decisions.
- Enterprise UX/navigation/page patterns: docs/ui/HADRON-ERP-UX-ARCHITECTURE.md.
- Visual UI/brand/tokens: docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf.
```

---

# 37. Final Principle

Hadron ERP باید از نظر رفتار:

**enterprise, predictable, task-oriented, context-preserving**

و از نظر ظاهر:

**Hadron, calm, precise, engineered, minimal, premium**

باشد.

به‌صورت خلاصه:

```text
SAP Fiori patterns
        ↓
Enterprise UX behavior

Hadron Digital Design System
        ↓
Visual identity and UI

Canonical MRP Business Rules
        ↓
System truth
```

این سه لایه باید مستقل بمانند و در کنار هم Hadron ERP System را بسازند.

---

# 38. Cash Flow Material Costing Pattern

The restricted Cash Flow workspace groups **Product Structure Costing**, **Material Cost Book** and **Purchase Price History** under one Item and Product Costing navigation group. They remain separate pages because they answer different questions: calculated product cost, controlled manual price basis and actual procurement-price history.

Material Cost Book uses List Report -> Object Page. The Object Page header owns book number, name, effective date, revision and lifecycle status. Draft price lines use a server-paged table with one row per Item. Original amount, currency, Pricing Quantity and Price Unit are editable; Item Base Unit, `FactorToBase` and backend-normalized price are visibly read-only. Published revisions are read-only and correction creates a new linked Draft. Publish is a separate permission-aware action with explicit confirmation and partial-coverage disclosure.

Purchase Price History is a read-only List Report with one row per PO Line. Multiple receipts appear as one compact receipt summary, not duplicate rows. Item, Supplier, order-date, currency, PO-status and text filters execute server-side. Original currency is primary. Optional USD comparison is secondary and carries one clearly visible valuation As-Of Date/rate revision for the entire report; it must never look like transaction FX.

Product Structure Costing exposes one explicit basis selector. `PURCHASE_HISTORY` remains the default. `MATERIAL_COST_BOOK` requires a Published revision eligible for the selected valuation date. The chosen basis is preserved in the URL. Missing prices or FX show an incomplete state and null total, never zero or an unlabeled partial total.

For all three surfaces, Persian business date is primary with the Gregorian date in smaller text. Financial input cells use the approved soft-yellow treatment; calculated/system fields use neutral gray; complete results may use the restrained output tint. Status is never communicated by color alone. Validation remains adjacent to the affected field, keyboard focus stays stable during editing, and unsaved Draft changes are protected.

---

# 39. Enterprise Collaboration Pattern

Enterprise Collaboration is a peer workspace with three explicit tasks: Messages, Formal Correspondence and Calls. It must not look like a consumer social network and must not hide formal actions inside chat bubbles.

Messages use a responsive flexible-column layout: conversation list, active timeline and contextual details/composer. On narrow screens, only one task column is primary at a time. The composer remains reachable, keyboard focus is stable, loading does not clear typed content and realtime reconnect performs an authoritative refresh. Files, stickers and reactions are explicit user actions. Edited/deleted state is labelled in text, not color alone.

Formal Correspondence uses List Report -> Object Page. Draft fields and recipients are editable; lifecycle actions are separated and confirmation-worthy. After Issue, the document is visibly read-only and its lifecycle history remains available. Calls expose provider readiness before start and always retain an obvious Leave action; browser device permission failures use local, actionable text.

Hadron character stickers are user message content only. They never communicate system status, approval, security, finance or API truth. Empty/error/loading surfaces follow the companion rules in the brand guidance and keep operational content primary.

## 39.1 Global Communication Center

Authenticated users with Collaboration access retain one persistent Communication Center in the application shell. On desktop it has a clearly labelled, collapsed dock launcher in addition to the compact shell bell; on narrow screens the bell/popover remains the primary compact entry. Its behavior follows the familiar enterprise notification pattern used by professional networks: a bounded unread badge, a compact panel and deep links to the owning task. Its visual expression remains Hadron and must not reproduce another product's branding.

The panel combines Messages, Formal Correspondence and Meetings/Calls while preserving their distinct meanings. Permission-aware quick actions expose Messages, the Meeting worklist, the effective New/Instant Meeting action and Formal Letters without requiring the user to know a route. It also provides All/Message/Letter/Call filters, concise provenance, Tehran-localized time and an explicit action marker. Opening the panel does not mark a message or formal letter as read; acknowledgement occurs only in the owning business context. Active meetings/calls remain visible until ended.

Realtime updates run at authenticated shell scope and fall back to a bounded periodic refresh and refresh-on-window-focus. A new relevant event may produce one restrained, dismissible toast. Historical items never replay as new toasts after login. Failure of the notification surface must not block or paint the owning ERP page as failed.

The shell center is permission-aware and backend-filtered. It must never expose private-channel, formal-letter or call information through frontend hiding alone. Browser/operating-system notifications remain opt-in and are not required for the core workflow.
