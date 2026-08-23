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
→ Price History
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
└── MRP
```

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

MRP یک **Workspace / Business Space** داخل Hadron ERP است.

ورود:

```text
Hadron ERP System
        ↓
       MRP
        ↓
MRP Workspace
```

MRP Workspace باید context کاری MRP را حفظ کند و دسترسی به زیرسیستم‌های مرتبط را فراهم کند.

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
│   ├── Forecast & Actual
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
| MRP-07 | Forecast & Actual | برنامه‌ریزی |
| MRP-08 | موجودی | برنامه‌ریزی |
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
  Forecast & Actual
  موجودی
  وضعیت تأمین
  اجرای MRP
  برنامه‌ریزی مواد

تحلیل و گزارش
  گزارش‌ها و تحلیل MRP
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

در صورت پیچیده شدن workflow:

```text
Sourcing Options
→ Price History / Route
→ Detail
```

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

تا زمان Identity:

- Fake User profile ممنوع.
- Fake Notification center ممنوع.
- fake permission-driven menu ممنوع.

---

# 25. Permission-Driven UX — Future

بعد از Identity/RBAC:

Navigation باید role-aware شود.

User باید فقط appها/actions مرتبط را ببیند.

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

## MRP-07 — Forecast & Actual

```text
List Report
→ Product Forecast Object/Period Detail
```

## MRP-08 — Inventory

```text
List Report / Analytical Table
→ Item Inventory Detail
```

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
3. MRP is a workspace/business space inside Hadron ERP.
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
