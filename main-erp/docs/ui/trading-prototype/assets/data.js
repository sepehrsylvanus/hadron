window.TRADING_SPEC = {
  "models": {
    "TradeReferenceValue": {
      "table": "trade_reference_values",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "referenceType": {
          "type": "String",
          "optional": false,
          "label": "نوع دیکشنری (مثلاً COLLATERAL_TYPE)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "code": {
          "type": "String",
          "optional": false,
          "label": "کد ثابت برای ارجاع از بقیهٔ جدول‌ها",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "nameFa": {
          "type": "String",
          "optional": false,
          "label": "نام فارسی برای نمایش",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "nameEn": {
          "type": "String",
          "optional": true,
          "label": "نام انگلیسی",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "symbol": {
          "type": "String",
          "optional": true,
          "label": "نماد (برای واحد/ارز/بسته‌بندی)",
          "enum": null,
          "maxLength": 20,
          "date": false,
          "precision": null,
          "scale": null
        },
        "sortOrder": {
          "type": "Int",
          "optional": false,
          "label": "ترتیب نمایش در لیست‌ها",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isActive": {
          "type": "Boolean",
          "optional": false,
          "label": "فعال/غیرفعال — حذف نمی‌شود، غیرفعال می‌شود",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "countryId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.countries.Id (برای PORT)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "extra": {
          "type": "Json",
          "optional": true,
          "label": "فیلدهای اضافهٔ همان نوع (مثلاً نوع پورت)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه — کنترل هم‌زمانی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradeParty": {
      "table": "trade_parties",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ داخلی طرف (مثلاً PTY-00012)",
          "enum": null,
          "maxLength": 32,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyType": {
          "type": "PartyType",
          "optional": false,
          "label": "شخص حقوقی / حقیقی / خارجی",
          "enum": [
            "LEGAL_ENTITY",
            "NATURAL_PERSON",
            "FOREIGN_ENTITY"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "legalNameFa": {
          "type": "String",
          "optional": false,
          "label": "نام رسمی فارسی",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "legalNameEn": {
          "type": "String",
          "optional": true,
          "label": "نام رسمی انگلیسی (برای طرف خارجی)",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "nationalId": {
          "type": "String",
          "optional": true,
          "label": "شناسهٔ ملی / کد ملی",
          "enum": null,
          "maxLength": 20,
          "date": false,
          "precision": null,
          "scale": null
        },
        "registrationNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ ثبت شرکت",
          "enum": null,
          "maxLength": 30,
          "date": false,
          "precision": null,
          "scale": null
        },
        "economicCode": {
          "type": "String",
          "optional": true,
          "label": "کد اقتصادی",
          "enum": null,
          "maxLength": 30,
          "date": false,
          "precision": null,
          "scale": null
        },
        "countryId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.countries.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "province": {
          "type": "String",
          "optional": true,
          "label": "استان",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "city": {
          "type": "String",
          "optional": true,
          "label": "شهر",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "address": {
          "type": "String",
          "optional": true,
          "label": "نشانی",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "phone": {
          "type": "String",
          "optional": true,
          "label": "تلفن ثابت",
          "enum": null,
          "maxLength": 50,
          "date": false,
          "precision": null,
          "scale": null
        },
        "mobile": {
          "type": "String",
          "optional": true,
          "label": "موبایل",
          "enum": null,
          "maxLength": 50,
          "date": false,
          "precision": null,
          "scale": null
        },
        "email": {
          "type": "String",
          "optional": true,
          "label": "ایمیل",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "website": {
          "type": "String",
          "optional": true,
          "label": "وب‌سایت",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "contactName": {
          "type": "String",
          "optional": true,
          "label": "نام شخص رابط",
          "enum": null,
          "maxLength": 150,
          "date": false,
          "precision": null,
          "scale": null
        },
        "contactPhone": {
          "type": "String",
          "optional": true,
          "label": "تلفن شخص رابط",
          "enum": null,
          "maxLength": 50,
          "date": false,
          "precision": null,
          "scale": null
        },
        "rating": {
          "type": "Int",
          "optional": true,
          "label": "امتیاز عملکرد ۱ تا ۵ (ارزیابی)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "priority": {
          "type": "Int",
          "optional": true,
          "label": "اولویت انتخاب در فرم‌ها (کمتر = بالاتر)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isActive": {
          "type": "Boolean",
          "optional": false,
          "label": "فعال",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isBlocked": {
          "type": "Boolean",
          "optional": false,
          "label": "مسدود — نباید در فرم جدید انتخاب شود",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "blockReason": {
          "type": "String",
          "optional": true,
          "label": "دلیل مسدودی",
          "enum": null,
          "maxLength": 500,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — آخرین ویرایش‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradePartyRole": {
      "table": "trade_party_roles",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "roleCode": {
          "type": "String",
          "optional": false,
          "label": "کد نقش از دیکشنری PARTY_ROLE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isPrimary": {
          "type": "Boolean",
          "optional": false,
          "label": "نقش اصلی این طرف",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "validFrom": {
          "type": "DateTime",
          "optional": true,
          "label": "شروع اعتبار نقش",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "validTo": {
          "type": "DateTime",
          "optional": true,
          "label": "پایان اعتبار نقش",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "isActive": {
          "type": "Boolean",
          "optional": false,
          "label": "فعال",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradePartyLicense": {
      "table": "trade_party_licenses",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "licenseTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع مجوز از دیکشنری LICENSE_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "licenseNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ پروانه",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issuer": {
          "type": "String",
          "optional": true,
          "label": "مرجع صدور",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "expiryDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انقضا (برای هشدار سیستمی)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "LicenseStatus",
          "optional": false,
          "label": "وضعیت: صادرنشده/در جریان/معتبر/نزدیک انقضا/منقضی/معلق/باطل",
          "enum": [
            "NOT_ISSUED",
            "IN_PROGRESS",
            "VALID",
            "EXPIRING_SOON",
            "EXPIRED",
            "SUSPENDED",
            "REVOKED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentKey": {
          "type": "String",
          "optional": true,
          "label": "کلید فایل تصویر پروانه در انبار اسناد",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "BusinessCard": {
      "table": "business_cards",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id — صاحب کارت",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "cardNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ کارت بازرگانی",
          "enum": null,
          "maxLength": 50,
          "date": false,
          "precision": null,
          "scale": null
        },
        "registrationNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ ثبت همراه کارت",
          "enum": null,
          "maxLength": 30,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "expiryDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انقضا (محاسبهٔ سیستمی هشدار)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "LicenseStatus",
          "optional": false,
          "label": "وضعیت کارت",
          "enum": [
            "NOT_ISSUED",
            "IN_PROGRESS",
            "VALID",
            "EXPIRING_SOON",
            "EXPIRED",
            "SUSPENDED",
            "REVOKED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "allocationPercent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد تخصیص پیش‌فرض این کارت",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "allocationAmount": {
          "type": "Decimal",
          "optional": true,
          "label": "مبلغ تخصیص پیش‌فرض",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partners": {
          "type": "Json",
          "optional": true,
          "label": "شرکای کارت: [{name, sharePercent, nationalId}]",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentKey": {
          "type": "String",
          "optional": true,
          "label": "کلید فایل تصویر کارت",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradePartyAllocationRule": {
      "table": "trade_party_allocation_rules",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "roleCode": {
          "type": "String",
          "optional": true,
          "label": "نقش مرتبط (مثلاً CUSTOMS_BROKER)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "mode": {
          "type": "AllocationRuleMode",
          "optional": false,
          "label": "درصد ثابت / مبلغ ثابت / متغیر",
          "enum": [
            "FIXED_PERCENT",
            "FIXED_AMOUNT",
            "VARIABLE"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "percent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد سهم (وقتی mode = FIXED_PERCENT)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "amount": {
          "type": "Decimal",
          "optional": true,
          "label": "مبلغ سهم (وقتی mode = FIXED_AMOUNT)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "unitId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.units.Id — واحد اندازه‌گیری",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "validFrom": {
          "type": "DateTime",
          "optional": true,
          "label": "شروع اعتبار قاعده",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "validTo": {
          "type": "DateTime",
          "optional": true,
          "label": "پایان اعتبار قاعده",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "isActive": {
          "type": "Boolean",
          "optional": false,
          "label": "فعال",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "BankCollateralTerm": {
      "table": "bank_collateral_terms",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "bankPartyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id (نقش BANK)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "collateralTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع وثیقه از دیکشنری COLLATERAL_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isRequired": {
          "type": "Boolean",
          "optional": false,
          "label": "اجباری است؟",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "percent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد پوشش وثیقه (مثلاً ۱۱۰٪ مبلغ ارز)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "maxAmount": {
          "type": "Decimal",
          "optional": true,
          "label": "سقف مبلغ",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradeProject": {
      "table": "trade_projects",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ یکتای پرونده (TRD-1405-0001)",
          "enum": null,
          "maxLength": 32,
          "date": false,
          "precision": null,
          "scale": null
        },
        "titleFa": {
          "type": "String",
          "optional": false,
          "label": "موضوع پرونده",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "titleEn": {
          "type": "String",
          "optional": true,
          "label": "عنوان انگلیسی (اختیاری)",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "goodsSummary": {
          "type": "String",
          "optional": true,
          "label": "خلاصهٔ کالا برای نمایش در لیست",
          "enum": null,
          "maxLength": 500,
          "date": false,
          "precision": null,
          "scale": null
        },
        "customerPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id — مشتری",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "supplierPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id — تأمین‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "businessCardId": {
          "type": "String",
          "optional": true,
          "label": "FK → BusinessCard.Id — کارت بازرگانی استفاده‌شده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "priorityCode": {
          "type": "String",
          "optional": true,
          "label": "اولویت از دیکشنری PRIORITY: NORMAL عادی / URGENT فوری / CRITICAL اورژانس",
          "enum": null,
          "maxLength": 20,
          "date": false,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "TradeProjectStatus",
          "optional": false,
          "label": "وضعیت کل پرونده",
          "enum": [
            "DRAFT",
            "ACTIVE",
            "WAITING_ORDER",
            "FX_PROCESS",
            "READY_TO_SHIP",
            "IN_TRANSIT",
            "IN_CUSTOMS",
            "CLEARED",
            "DELIVERED",
            "CLOSED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "currentStage": {
          "type": "TradeStage",
          "optional": true,
          "label": "مرحلهٔ جاری (برای نمایش سریع)",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول پیگیری",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "registrationDate": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت پرونده — سیستمی و غیرقابل ویرایش",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "targetDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ هدف (مهلت)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "eightDigitCode": {
          "type": "String",
          "optional": true,
          "label": "کد ۸ رقمی بانک — کنار نام پرونده نمایش داده می‌شود",
          "enum": null,
          "maxLength": 8,
          "date": false,
          "precision": null,
          "scale": null
        },
        "codeIssuedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور کد ۸ رقمی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "codeExpiresAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انقضای کد ۸ رقمی (سیستمی)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "isLocked": {
          "type": "Boolean",
          "optional": false,
          "label": "قفل پرونده — بعد از کد ۸ رقمی true می‌شود",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "lockedAtUtc": {
          "type": "DateTime",
          "optional": true,
          "label": "زمان قفل شدن",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "lockReason": {
          "type": "String",
          "optional": true,
          "label": "دلیل قفل (مثلاً «صدور کد ۸ رقمی»)",
          "enum": null,
          "maxLength": 500,
          "date": false,
          "precision": null,
          "scale": null
        },
        "closedAtUtc": {
          "type": "DateTime",
          "optional": true,
          "label": "زمان بستن پرونده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "closedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — بستن‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات کلی پرونده",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — آخرین ویرایش‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه — کنترل هم‌زمانی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ProjectStage": {
      "table": "project_stages",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": false,
          "label": "کدام مرحله",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "StageStatus",
          "optional": false,
          "label": "وضعیت مرحله",
          "enum": [
            "NOT_STARTED",
            "OPEN",
            "IN_PROGRESS",
            "BLOCKED",
            "DONE",
            "SKIPPED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — انجام‌دهنده/مسئول این مرحله",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "sortOrder": {
          "type": "Int",
          "optional": false,
          "label": "ترتیب نمایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "startedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ شروع مرحله",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "dueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "مهلت مرحله",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "completedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ پایان مرحله",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "isLocked": {
          "type": "Boolean",
          "optional": false,
          "label": "قفل مرحله (پس از تکمیل یا صدور کد)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات مخصوص همین مرحله",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — آخرین ویرایش‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ProjectParty": {
      "table": "project_parties",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id — طرف باید در رجیستری ثبت شده باشد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "roleCode": {
          "type": "String",
          "optional": false,
          "label": "نقش از دیکشنری PARTY_ROLE (CARD_OWNER, OTHER, ...)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "displayName": {
          "type": "String",
          "optional": true,
          "label": "نام نمایشی — برای طرف ثبت‌نشده",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isPrimary": {
          "type": "Boolean",
          "optional": false,
          "label": "طرف اصلی این نقش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ProjectKeyDate": {
      "table": "project_key_dates",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "dateTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع تاریخ از دیکشنری PROJECT_DATE_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "dateValue": {
          "type": "DateTime",
          "optional": false,
          "label": "مقدار تاریخ (میلادی ذخیره، شمسی نمایش)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "isSystem": {
          "type": "Boolean",
          "optional": false,
          "label": "سیستمی (کاربر نمی‌تواند تغییر دهد)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "sourceRef": {
          "type": "String",
          "optional": true,
          "label": "مرجع سند/کد مربوطه",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ProjectDocument": {
      "table": "project_documents",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": true,
          "label": "مرحلهٔ مربوطه",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع مدرک (پروفرما، بارنامه، بیمه‌نامه، ...)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "title": {
          "type": "String",
          "optional": false,
          "label": "عنوان سند",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fileName": {
          "type": "String",
          "optional": false,
          "label": "نام فایل اصلی",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "storageKey": {
          "type": "String",
          "optional": false,
          "label": "کلید فایل در انبار اسناد",
          "enum": null,
          "maxLength": 500,
          "date": false,
          "precision": null,
          "scale": null
        },
        "mimeType": {
          "type": "String",
          "optional": true,
          "label": "نوع فایل",
          "enum": null,
          "maxLength": 120,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fileSize": {
          "type": "Int",
          "optional": true,
          "label": "حجم فایل (بایت)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "uploadedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — بارگذارنده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "uploadedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "زمان بارگذاری",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "OrderRegistration": {
      "table": "order_registrations",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ ثبت سفارش (سامانه جامع تجارت)",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "registrationDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ ثبت سفارش",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "OrderStatus",
          "optional": false,
          "label": "وضعیت سند",
          "enum": [
            "DRAFT",
            "SUBMITTED",
            "CONFIRMED",
            "EXPIRED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "bankPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش BANK) — بانک عامل",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id — ارز",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "declaredValueAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ ثبت سفارش (ارزش کالا)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مقدار ارز تخصیصی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "exchangeRate": {
          "type": "Decimal",
          "optional": true,
          "label": "نرخ ارز مبنای ثبت سفارش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 8
        },
        "incotermCode": {
          "type": "String",
          "optional": true,
          "label": "اینکوترم از دیکشنری INCOTERM (FOB/CIF/...)",
          "enum": null,
          "maxLength": 10,
          "date": false,
          "precision": null,
          "scale": null
        },
        "paymentMethodCode": {
          "type": "String",
          "optional": true,
          "label": "روش پرداخت از دیکشنری PAYMENT_METHOD",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول ثبت",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "OrderRegistrationLine": {
      "table": "order_registration_lines",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderRegistrationId": {
          "type": "String",
          "optional": false,
          "label": "FK → OrderRegistration.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "lineNumber": {
          "type": "Int",
          "optional": false,
          "label": "شمارهٔ ردیف",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "catalogItemId": {
          "type": "String",
          "optional": true,
          "label": "FK → catalog.items.Id (اختیاری، اگر کالا در ماستر باشد)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "goodsTitle": {
          "type": "String",
          "optional": false,
          "label": "عنوان کالا",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "goodsDescription": {
          "type": "String",
          "optional": true,
          "label": "شرح تکمیلی",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "hsCode": {
          "type": "String",
          "optional": true,
          "label": "کد تعرفهٔ گمرکی",
          "enum": null,
          "maxLength": 20,
          "date": false,
          "precision": null,
          "scale": null
        },
        "brand": {
          "type": "String",
          "optional": true,
          "label": "برند",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "model": {
          "type": "String",
          "optional": true,
          "label": "مدل",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "originCountryId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.countries.Id — کشور مبدأ",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "quantity": {
          "type": "Decimal",
          "optional": false,
          "label": "تعداد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "unitId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.units.Id — واحد سنجش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "unitPrice": {
          "type": "Decimal",
          "optional": false,
          "label": "قیمت واحد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "lineTotal": {
          "type": "Decimal",
          "optional": false,
          "label": "جمع ردیف (تعداد × قیمت واحد)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "OrderPermit": {
      "table": "order_permits",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderRegistrationId": {
          "type": "String",
          "optional": false,
          "label": "FK → OrderRegistration.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "permitTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع مجوز از دیکشنری PERMIT_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "permitNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ مجوز",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issuedBy": {
          "type": "String",
          "optional": true,
          "label": "مرجع صدور",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "expiryDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انقضا",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "PermitStatus",
          "optional": false,
          "label": "در انتظار / تأیید / رد / منقضی",
          "enum": [
            "PENDING",
            "APPROVED",
            "REJECTED",
            "EXPIRED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "decidedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — تصمیم‌گیرنده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "decidedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "زمان تصمیم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "rejectionReason": {
          "type": "String",
          "optional": true,
          "label": "دلیل رد",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "FxFile": {
      "table": "fx_files",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ پروندهٔ ارزی",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "bankPartyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id (نقش BANK) — بانک عامل",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxSourceCode": {
          "type": "String",
          "optional": false,
          "label": "منبع تأمین ارز از دیکشنری FX_SOURCE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id — ارز",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "requestDate": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ شروع ثبت/درخواست",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "allocationNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ تخصیص",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "allocationNumberDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ شمارهٔ تخصیص",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "allocatedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "جمع مبلغ تخصیص‌یافته (جمع خطوط)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "receivedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "جمع مبلغ دریافت‌شده (جمع رسیدها)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "paidRialAmount": {
          "type": "Decimal",
          "optional": true,
          "label": "مبلغ ریال پرداختی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "grossRate": {
          "type": "Decimal",
          "optional": true,
          "label": "نرخ ناخالص = ریال ÷ تخصیص (محاسبهٔ سرور)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 8
        },
        "netRate": {
          "type": "Decimal",
          "optional": true,
          "label": "نرخ خالص = ریال ÷ دریافت (محاسبهٔ سرور)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 8
        },
        "status": {
          "type": "FxStatus",
          "optional": false,
          "label": "وضعیت پروندهٔ ارزی",
          "enum": [
            "DRAFT",
            "OPEN",
            "IN_PROGRESS",
            "CLOSED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "receiptStatus": {
          "type": "ReceiptStatus",
          "optional": false,
          "label": "وضعیت دریافت: دریافت‌نشده/بخشی/دریافت‌شده",
          "enum": [
            "NOT_RECEIVED",
            "PARTIAL",
            "RECEIVED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول ارزی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "FxAllocation": {
      "table": "fx_allocations",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxFileId": {
          "type": "String",
          "optional": false,
          "label": "FK → FxFile.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderRegistrationId": {
          "type": "String",
          "optional": true,
          "label": "FK → OrderRegistration.Id (اختیاری)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "allocatedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ تخصیص‌یافتهٔ این خط",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "allocationDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ تخصیص",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "bankReferenceNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ پیگیری/ارجاع بانک",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "FxReceipt": {
      "table": "fx_receipts",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxFileId": {
          "type": "String",
          "optional": false,
          "label": "FK → FxFile.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "receivedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ دریافتی این رسید",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "receivedAt": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ دریافت",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "bankReferenceNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ ارجاع بانک",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "Collateral": {
      "table": "collaterals",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxFileId": {
          "type": "String",
          "optional": false,
          "label": "FK → FxFile.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "bankPartyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id (نقش BANK)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "collateralTypeCode": {
          "type": "String",
          "optional": false,
          "label": "نوع وثیقه از دیکشنری COLLATERAL_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "instrumentNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ چک/سفته/ضمانت‌نامه",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "amount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ وثیقه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "dueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ سررسید",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "heldByBank": {
          "type": "Boolean",
          "optional": false,
          "label": "دست بانک است؟ (خیر = نزد ما)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "heldAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ تحویل به بانک",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "CollateralStatus",
          "optional": false,
          "label": "وضعیت وثیقه",
          "enum": [
            "ACTIVE",
            "HELD_BY_BANK",
            "RELEASED",
            "MATURED",
            "RETURNED",
            "LOST"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "releasedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ آزادسازی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "documentKey": {
          "type": "String",
          "optional": true,
          "label": "کلید فایل تصویر سند",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "InsurancePolicy": {
      "table": "insurance_policies",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": true,
          "label": "FK → Shipment.Id (اگر بیمهٔ محموله است)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "insurerPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش INSURER) — شرکت بیمه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "policyNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ بیمه‌نامه",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "coverageTypeCode": {
          "type": "String",
          "optional": true,
          "label": "نوع پوشش (CIF، باربری، ...)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "premiumRatePercent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد حق بیمه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "premiumAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ حق بیمه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "issueDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ صدور",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "startDate": {
          "type": "DateTime",
          "optional": true,
          "label": "شروع پوشش",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "expiryDate": {
          "type": "DateTime",
          "optional": true,
          "label": "پایان پوشش (محاسبهٔ هشدار سیستمی)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "InsuranceStatus",
          "optional": false,
          "label": "وضعیت بیمه‌نامه",
          "enum": [
            "DRAFT",
            "ACTIVE",
            "EXPIRED",
            "CANCELLED",
            "CLAIMED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentKey": {
          "type": "String",
          "optional": true,
          "label": "کلید فایل بیمه‌نامه",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "Inspection": {
      "table": "inspections",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": true,
          "label": "FK → Shipment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "inspectorPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش INSPECTOR) — شرکت بازرسی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "requestDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ درخواست بازرسی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "inspectionDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انجام بازرسی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "certificateNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ گواهی بازرسی",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "statusCode": {
          "type": "String",
          "optional": false,
          "label": "وضعیت از دیکشنری INSPECTION_STATUS",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "resultCode": {
          "type": "String",
          "optional": true,
          "label": "نتیجه از دیکشنری INSPECTION_RESULT",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "resultNotes": {
          "type": "String",
          "optional": true,
          "label": "شرح نتیجه",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentKey": {
          "type": "String",
          "optional": true,
          "label": "کلید فایل گواهی",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول پیگیری",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "Shipment": {
      "table": "shipments",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentNumber": {
          "type": "String",
          "optional": false,
          "label": "شمارهٔ محموله",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "title": {
          "type": "String",
          "optional": true,
          "label": "عنوان/خلاصهٔ محموله",
          "enum": null,
          "maxLength": 250,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeProject.Id — پروندهٔ اصلی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "transportMode": {
          "type": "TransportMode",
          "optional": false,
          "label": "روش حمل",
          "enum": [
            "SEA",
            "AIR",
            "ROAD",
            "RAIL",
            "MULTIMODAL"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "forwarderPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش FORWARDER) — فورواردر",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "carrierPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش CARRIER) — حامل اصلی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "originPortCode": {
          "type": "String",
          "optional": true,
          "label": "کد پورت مبدأ از دیکشنری PORT",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "destinationPortCode": {
          "type": "String",
          "optional": true,
          "label": "کد پورت مقصد از دیکشنری PORT",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "originText": {
          "type": "String",
          "optional": true,
          "label": "مبدأ به‌صورت متنی (اسنپ‌شات)",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "destinationText": {
          "type": "String",
          "optional": true,
          "label": "مقصد به‌صورت متنی (اسنپ‌شات)",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "hasInsurance": {
          "type": "Boolean",
          "optional": false,
          "label": "بیمه دارد؟",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "billOfLadingNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ بارنامه (اختیاری)",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "packageCount": {
          "type": "Int",
          "optional": true,
          "label": "تعداد بسته",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "packageTypeCode": {
          "type": "String",
          "optional": true,
          "label": "نوع بسته‌بندی از دیکشنری PACKAGE_TYPE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "grossWeight": {
          "type": "Decimal",
          "optional": true,
          "label": "وزن ناخالص",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "weightUnitId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.units.Id — واحد وزن",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "volume": {
          "type": "Decimal",
          "optional": true,
          "label": "حجم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "volumeUnitId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.units.Id — واحد حجم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "estimatedDeparture": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ حرکت (تخمینی)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "estimatedArrival": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ رسیدن (تخمینی)",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "actualDeparture": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ حرکت واقعی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "actualArrival": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ رسیدن واقعی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "ShipmentStatus",
          "optional": false,
          "label": "وضعیت محموله",
          "enum": [
            "DRAFT",
            "READY",
            "IN_TRANSIT",
            "ARRIVED",
            "IN_CUSTOMS",
            "PARTIALLY_CLEARED",
            "CLEARED",
            "DELIVERED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول حمل",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ShipmentItem": {
      "table": "shipment_items",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": false,
          "label": "FK → Shipment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeProject.Id — اگر محموله چندپروژه‌ای است",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderRegistrationLineId": {
          "type": "String",
          "optional": true,
          "label": "FK → OrderRegistrationLine.Id — ردیف سفارش مبدأ",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "lineNumber": {
          "type": "Int",
          "optional": false,
          "label": "شمارهٔ ردیف داخل محموله",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "goodsTitle": {
          "type": "String",
          "optional": false,
          "label": "عنوان کالا",
          "enum": null,
          "maxLength": 300,
          "date": false,
          "precision": null,
          "scale": null
        },
        "quantity": {
          "type": "Decimal",
          "optional": false,
          "label": "تعداد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "unitId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.units.Id — واحد سنجش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "grossWeight": {
          "type": "Decimal",
          "optional": true,
          "label": "وزن این قلم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "volume": {
          "type": "Decimal",
          "optional": true,
          "label": "حجم این قلم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "packageCount": {
          "type": "Int",
          "optional": true,
          "label": "تعداد بستهٔ این قلم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "packageTypeCode": {
          "type": "String",
          "optional": true,
          "label": "نوع بسته‌بندی این قلم",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ShipmentParty": {
      "table": "shipment_parties",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": false,
          "label": "FK → Shipment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "partyId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeParty.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "roleCode": {
          "type": "String",
          "optional": false,
          "label": "نقش از دیکشنری PARTY_ROLE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isPrimary": {
          "type": "Boolean",
          "optional": false,
          "label": "طرف اصلی این نقش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "contactName": {
          "type": "String",
          "optional": true,
          "label": "نام رابط",
          "enum": null,
          "maxLength": 150,
          "date": false,
          "precision": null,
          "scale": null
        },
        "phone": {
          "type": "String",
          "optional": true,
          "label": "تلفن رابط",
          "enum": null,
          "maxLength": 50,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ShipmentLeg": {
      "table": "shipment_legs",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": false,
          "label": "FK → Shipment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "sequence": {
          "type": "Int",
          "optional": false,
          "label": "ترتیب مرحله",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fromPortCode": {
          "type": "String",
          "optional": true,
          "label": "کد پورت مبدأ از دیکشنری PORT",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "toPortCode": {
          "type": "String",
          "optional": true,
          "label": "کد پورت مقصد از دیکشنری PORT",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fromText": {
          "type": "String",
          "optional": true,
          "label": "مبدأ متنی",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "toText": {
          "type": "String",
          "optional": true,
          "label": "مقصد متنی",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "transportMode": {
          "type": "TransportMode",
          "optional": false,
          "label": "روش حمل این مرحله",
          "enum": [
            "SEA",
            "AIR",
            "ROAD",
            "RAIL",
            "MULTIMODAL"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "carrierPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id — حامل این مرحله",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "vehicleOrVessel": {
          "type": "String",
          "optional": true,
          "label": "نام کشتی/شمارهٔ کامیون/پرواز",
          "enum": null,
          "maxLength": 150,
          "date": false,
          "precision": null,
          "scale": null
        },
        "trackingNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ رهگیری",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "LegStatus",
          "optional": false,
          "label": "وضعیت مرحله",
          "enum": [
            "PLANNED",
            "DEPARTED",
            "IN_TRANSIT",
            "ARRIVED",
            "DELAYED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "estimatedDeparture": {
          "type": "DateTime",
          "optional": true,
          "label": "حرکت تخمینی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "estimatedArrival": {
          "type": "DateTime",
          "optional": true,
          "label": "رسیدن تخمینی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "actualDeparture": {
          "type": "DateTime",
          "optional": true,
          "label": "حرکت واقعی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "actualArrival": {
          "type": "DateTime",
          "optional": true,
          "label": "رسیدن واقعی",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "ShipmentCustoms": {
      "table": "shipment_customs",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "shipmentId": {
          "type": "String",
          "optional": false,
          "label": "FK → Shipment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "clearanceNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ پرونده/اظهارنامه",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "clearanceType": {
          "type": "CustomsType",
          "optional": false,
          "label": "نوع ترخیص",
          "enum": [
            "DEFINITIVE",
            "TEMPORARY",
            "TRANSIT",
            "RETURN"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "brokerPartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id (نقش CUSTOMS_BROKER) — ترخیص‌کار",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "customsOfficeCode": {
          "type": "String",
          "optional": true,
          "label": "ادارهٔ گمرک از دیکشنری CUSTOMS_OFFICE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "declarationDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ اظهار",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "releaseDate": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ ترخیص",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "documentsCompletedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ تکمیل مدارک",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "dutyAmount": {
          "type": "Decimal",
          "optional": true,
          "label": "حقوق و عوارض گمرکی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "dutyCurrencyId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "CustomsStatus",
          "optional": false,
          "label": "وضعیت پروندهٔ گمرکی",
          "enum": [
            "NOT_STARTED",
            "DOCUMENTS_PENDING",
            "IN_PROGRESS",
            "PARTIAL",
            "COMPLETE",
            "BLOCKED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 4000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "Commitment": {
      "table": "commitments",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "orderRegistrationId": {
          "type": "String",
          "optional": true,
          "label": "FK → OrderRegistration.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "fxFileId": {
          "type": "String",
          "optional": true,
          "label": "FK → FxFile.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "eightDigitCode": {
          "type": "String",
          "optional": true,
          "label": "کد ۸ رقمی مرتبط با این تعهد",
          "enum": null,
          "maxLength": 8,
          "date": false,
          "precision": null,
          "scale": null
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "committedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ تعهد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "dischargedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ تسویه‌شده (جمع تسویه‌ها)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "deadlineDate": {
          "type": "DateTime",
          "optional": false,
          "label": "مهلت تعهد",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "extendedToDate": {
          "type": "DateTime",
          "optional": true,
          "label": "مهلت پس از تمدید",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "extensionCount": {
          "type": "Int",
          "optional": false,
          "label": "تعداد تمدید",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "status": {
          "type": "CommitmentStatus",
          "optional": false,
          "label": "وضعیت تعهد",
          "enum": [
            "OPEN",
            "PARTIAL",
            "DONE",
            "EXPIRED",
            "EXTENDED",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "completedAt": {
          "type": "DateTime",
          "optional": true,
          "label": "تاریخ انجام کامل",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "CommitmentDischarge": {
      "table": "commitment_discharges",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "commitmentId": {
          "type": "String",
          "optional": false,
          "label": "FK → Commitment.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "dischargeDate": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ تسویه",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "amount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ تسویه‌شده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "methodCode": {
          "type": "String",
          "optional": true,
          "label": "روش تسویه از دیکشنری PAYMENT_METHOD",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "documentNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ سند/اظهارنامه",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "CostType": {
      "table": "cost_types",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "code": {
          "type": "String",
          "optional": false,
          "label": "کد یکتا (FREIGHT, CUSTOMS_DUTY, ...)",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "nameFa": {
          "type": "String",
          "optional": false,
          "label": "نام فارسی",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "nameEn": {
          "type": "String",
          "optional": true,
          "label": "نام انگلیسی",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "natureCode": {
          "type": "String",
          "optional": true,
          "label": "ماهیت هزینه از دیکشنری COST_NATURE",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": true,
          "label": "مرحله‌ای که این هزینه در آن ثبت می‌شود",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isMandatory": {
          "type": "Boolean",
          "optional": false,
          "label": "ثبت آن در مرحله اجباری است؟",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "allocationBasis": {
          "type": "CostAllocationBasis",
          "optional": false,
          "label": "مبنای تخصیص بین بخش‌ها",
          "enum": [
            "PROJECT_ONLY",
            "BY_GOODS_VALUE",
            "BY_WEIGHT",
            "BY_VOLUME",
            "BY_QUANTITY",
            "MANUAL"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "defaultCurrencyId": {
          "type": "String",
          "optional": true,
          "label": "FK → reference_data.currencies.Id — ارز پیش‌فرض",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "sortOrder": {
          "type": "Int",
          "optional": false,
          "label": "ترتیب نمایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isActive": {
          "type": "Boolean",
          "optional": false,
          "label": "فعال",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradeCost": {
      "table": "trade_costs",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "projectId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeProject.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "costTypeId": {
          "type": "String",
          "optional": false,
          "label": "FK → CostType.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": true,
          "label": "مرحلهٔ ثبت هزینه",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "description": {
          "type": "String",
          "optional": false,
          "label": "شرح هزینه",
          "enum": null,
          "maxLength": 500,
          "date": false,
          "precision": null,
          "scale": null
        },
        "amount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ به ارز اصلی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "effectiveDate": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ هزینه",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "exchangeRateSnapshot": {
          "type": "Decimal",
          "optional": true,
          "label": "نرخ تسعیر لحظهٔ ثبت",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 8
        },
        "paidRialAmount": {
          "type": "Decimal",
          "optional": true,
          "label": "مبلغ ریالی پرداختی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "rialEquivalent": {
          "type": "Decimal",
          "optional": true,
          "label": "معادل ریالی محاسبه‌شده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "taxPercent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد مالیات",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "dutyPercent": {
          "type": "Decimal",
          "optional": true,
          "label": "درصد عوارض",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "paymentStatus": {
          "type": "CostPaymentStatus",
          "optional": false,
          "label": "وضعیت پرداخت",
          "enum": [
            "UNPAID",
            "PARTIAL",
            "PAID",
            "CANCELLED"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "paymentMethodCode": {
          "type": "String",
          "optional": true,
          "label": "روش پرداخت از دیکشنری COST_PAYMENT_METHOD",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "payeePartyId": {
          "type": "String",
          "optional": true,
          "label": "FK → TradeParty.Id — دریافت‌کننده (دیدبان/پیمانکار)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "referenceNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ فاکتور/رسید",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "responsibleUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — مسئول هزینه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isVoided": {
          "type": "Boolean",
          "optional": false,
          "label": "ابطال‌شده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "voidedAtUtc": {
          "type": "DateTime",
          "optional": true,
          "label": "زمان ابطال",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "voidedByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ابطال‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "voidReason": {
          "type": "String",
          "optional": true,
          "label": "دلیل ابطال",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیحات",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "updatedAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ آخرین ویرایش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "version": {
          "type": "Int",
          "optional": false,
          "label": "نسخه",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "CostAllocation": {
      "table": "cost_allocations",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "costId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeCost.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "scopeType": {
          "type": "AllocationScopeType",
          "optional": false,
          "label": "نوع بخش: پروژه/سفارش/محموله/قلم/گمرک/مرحله",
          "enum": [
            "PROJECT",
            "ORDER_REGISTRATION",
            "SHIPMENT",
            "SHIPMENT_ITEM",
            "CUSTOMS_CASE",
            "STAGE"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "scopeId": {
          "type": "String",
          "optional": true,
          "label": "شناسهٔ آن بخش",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": true,
          "label": "اگر scopeType = STAGE",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "allocationPercent": {
          "type": "Decimal",
          "optional": false,
          "label": "درصد سهم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 9,
          "scale": 4
        },
        "allocatedAmount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ سهم",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "CostPayment": {
      "table": "cost_payments",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "costId": {
          "type": "String",
          "optional": false,
          "label": "FK → TradeCost.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "paidAt": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ پرداخت",
          "enum": null,
          "maxLength": null,
          "date": true,
          "precision": null,
          "scale": null
        },
        "amount": {
          "type": "Decimal",
          "optional": false,
          "label": "مبلغ پرداخت",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": 24,
          "scale": 6
        },
        "currencyId": {
          "type": "String",
          "optional": false,
          "label": "FK → reference_data.currencies.Id",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "methodCode": {
          "type": "String",
          "optional": false,
          "label": "روش پرداخت از دیکشنری COST_PAYMENT_METHOD",
          "enum": null,
          "maxLength": 40,
          "date": false,
          "precision": null,
          "scale": null
        },
        "isReturn": {
          "type": "Boolean",
          "optional": false,
          "label": "عودت؟ (true = برگشت وجه)",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "referenceNumber": {
          "type": "String",
          "optional": true,
          "label": "شمارهٔ سند/چک",
          "enum": null,
          "maxLength": 100,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "تاریخ ثبت سیستمی",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "createdByUserId": {
          "type": "String",
          "optional": true,
          "label": "FK → identity.users.Id — ثبت‌کننده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    },
    "TradeAuditEvent": {
      "table": "trade_audit_events",
      "fields": {
        "id": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ یکتا",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "entityType": {
          "type": "String",
          "optional": false,
          "label": "نوع موجودیت (TradeProject, TradeCost, ...)",
          "enum": null,
          "maxLength": 80,
          "date": false,
          "precision": null,
          "scale": null
        },
        "entityId": {
          "type": "String",
          "optional": false,
          "label": "شناسهٔ رکورد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "entityLabel": {
          "type": "String",
          "optional": true,
          "label": "برچسب خوانا برای نمایش (مثلاً شمارهٔ پرونده)",
          "enum": null,
          "maxLength": 200,
          "date": false,
          "precision": null,
          "scale": null
        },
        "action": {
          "type": "String",
          "optional": false,
          "label": "عمل (CREATE, UPDATE, STATUS_CHANGE, LOCK, VOID, ...)",
          "enum": null,
          "maxLength": 60,
          "date": false,
          "precision": null,
          "scale": null
        },
        "stage": {
          "type": "TradeStage",
          "optional": true,
          "label": "مرحله‌ای که عمل در آن رخ داده",
          "enum": [
            "PROJECT_SETUP",
            "ORDER",
            "FX",
            "INSURANCE",
            "SHIPPING",
            "CUSTOMS",
            "COMMITMENT",
            "COSTS"
          ],
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "actorUserId": {
          "type": "String",
          "optional": false,
          "label": "FK → identity.users.Id — انجام‌دهنده",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "actorNameSnapshot": {
          "type": "String",
          "optional": true,
          "label": "نام انجام‌دهنده در لحظهٔ عمل (اسنپ‌شات)",
          "enum": null,
          "maxLength": 150,
          "date": false,
          "precision": null,
          "scale": null
        },
        "previousJson": {
          "type": "Json",
          "optional": true,
          "label": "وضعیت قبل",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "currentJson": {
          "type": "Json",
          "optional": true,
          "label": "وضعیت بعد",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        },
        "notes": {
          "type": "String",
          "optional": true,
          "label": "توضیح انجام‌دهنده",
          "enum": null,
          "maxLength": 2000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "reason": {
          "type": "String",
          "optional": true,
          "label": "دلیل تغییر (برای اصلاح/بازگشت)",
          "enum": null,
          "maxLength": 1000,
          "date": false,
          "precision": null,
          "scale": null
        },
        "occurredAtUtc": {
          "type": "DateTime",
          "optional": false,
          "label": "زمان وقوع",
          "enum": null,
          "maxLength": null,
          "date": false,
          "precision": null,
          "scale": null
        }
      }
    }
  },
  "enums": {
    "TradeProjectStatus": [
      "DRAFT",
      "ACTIVE",
      "WAITING_ORDER",
      "FX_PROCESS",
      "READY_TO_SHIP",
      "IN_TRANSIT",
      "IN_CUSTOMS",
      "CLEARED",
      "DELIVERED",
      "CLOSED",
      "CANCELLED"
    ],
    "TradeStage": [
      "PROJECT_SETUP",
      "ORDER",
      "FX",
      "INSURANCE",
      "SHIPPING",
      "CUSTOMS",
      "COMMITMENT",
      "COSTS"
    ],
    "StageStatus": [
      "NOT_STARTED",
      "OPEN",
      "IN_PROGRESS",
      "BLOCKED",
      "DONE",
      "SKIPPED",
      "CANCELLED"
    ],
    "PartyType": [
      "LEGAL_ENTITY",
      "NATURAL_PERSON",
      "FOREIGN_ENTITY"
    ],
    "LicenseStatus": [
      "NOT_ISSUED",
      "IN_PROGRESS",
      "VALID",
      "EXPIRING_SOON",
      "EXPIRED",
      "SUSPENDED",
      "REVOKED"
    ],
    "AllocationRuleMode": [
      "FIXED_PERCENT",
      "FIXED_AMOUNT",
      "VARIABLE"
    ],
    "OrderStatus": [
      "DRAFT",
      "SUBMITTED",
      "CONFIRMED",
      "EXPIRED",
      "CANCELLED"
    ],
    "PermitStatus": [
      "PENDING",
      "APPROVED",
      "REJECTED",
      "EXPIRED"
    ],
    "FxStatus": [
      "DRAFT",
      "OPEN",
      "IN_PROGRESS",
      "CLOSED",
      "CANCELLED"
    ],
    "ReceiptStatus": [
      "NOT_RECEIVED",
      "PARTIAL",
      "RECEIVED"
    ],
    "CollateralStatus": [
      "ACTIVE",
      "HELD_BY_BANK",
      "RELEASED",
      "MATURED",
      "RETURNED",
      "LOST"
    ],
    "InsuranceStatus": [
      "DRAFT",
      "ACTIVE",
      "EXPIRED",
      "CANCELLED",
      "CLAIMED"
    ],
    "TransportMode": [
      "SEA",
      "AIR",
      "ROAD",
      "RAIL",
      "MULTIMODAL"
    ],
    "ShipmentStatus": [
      "DRAFT",
      "READY",
      "IN_TRANSIT",
      "ARRIVED",
      "IN_CUSTOMS",
      "PARTIALLY_CLEARED",
      "CLEARED",
      "DELIVERED",
      "CANCELLED"
    ],
    "LegStatus": [
      "PLANNED",
      "DEPARTED",
      "IN_TRANSIT",
      "ARRIVED",
      "DELAYED",
      "CANCELLED"
    ],
    "CustomsType": [
      "DEFINITIVE",
      "TEMPORARY",
      "TRANSIT",
      "RETURN"
    ],
    "CustomsStatus": [
      "NOT_STARTED",
      "DOCUMENTS_PENDING",
      "IN_PROGRESS",
      "PARTIAL",
      "COMPLETE",
      "BLOCKED",
      "CANCELLED"
    ],
    "CommitmentStatus": [
      "OPEN",
      "PARTIAL",
      "DONE",
      "EXPIRED",
      "EXTENDED",
      "CANCELLED"
    ],
    "CostPaymentStatus": [
      "UNPAID",
      "PARTIAL",
      "PAID",
      "CANCELLED"
    ],
    "CostAllocationBasis": [
      "PROJECT_ONLY",
      "BY_GOODS_VALUE",
      "BY_WEIGHT",
      "BY_VOLUME",
      "BY_QUANTITY",
      "MANUAL"
    ],
    "AllocationScopeType": [
      "PROJECT",
      "ORDER_REGISTRATION",
      "SHIPMENT",
      "SHIPMENT_ITEM",
      "CUSTOMS_CASE",
      "STAGE"
    ]
  },
  "pages": [
    {
      "id": "project",
      "file": "01-project.html",
      "title": "ایجاد پروژه جدید",
      "short": "پرونده",
      "number": "۱",
      "stage": "PROJECT_SETUP",
      "subtitle": "یک بار ثبت اطلاعات پایه؛ یک پرونده برای تمام مسیر بازرگانی.",
      "groups": [
        {
          "model": "TradeProject",
          "title": "هویت پرونده",
          "fields": [
            {
              "key": "titleFa",
              "label": "موضوع پروژه",
              "required": true,
              "wide": true,
              "placeholder": "مثلاً تأمین قطعات شارژر — سفارش پاییز ۱۴۰۵"
            },
            {
              "key": "goodsSummary",
              "label": "خلاصهٔ کالا",
              "wide": true
            },
            {
              "key": "responsibleUserId",
              "label": "مسئول پیگیری",
              "required": true
            },
            {
              "key": "targetDate",
              "label": "مهلت هدف"
            }
          ],
          "description": "شمارهٔ پرونده پس از ثبت توسط سرور ساخته می‌شود؛ با کد هشت‌رقمی متفاوت است."
        },
        {
          "model": "ProjectStage",
          "title": "شروع و ارجاع",
          "fields": [
            {
              "key": "startedAt",
              "label": "تاریخ شروع / ارجاع",
              "required": true
            },
            {
              "key": "dueDate",
              "label": "مهلت مرحله"
            }
          ],
          "description": "تاریخ کاری قابل ورود است؛ تاریخ ثبت سیستمی جدا نگه داشته می‌شود."
        },
        {
          "model": "TradeProject",
          "title": "طرفین تجاری",
          "fields": [
            {
              "key": "customerPartyId",
              "label": null
            },
            {
              "key": "businessCardId",
              "label": null
            },
            {
              "key": "supplierPartyId",
              "label": "تأمین‌کننده (اختیاری)"
            }
          ],
          "description": "مشتری و تأمین‌کننده از طرفین مشترک انتخاب می‌شوند؛ کارت بازرگانی موجودیت مستقلی است."
        },
        {
          "model": "TradeProject",
          "title": "یادداشت پرونده",
          "fields": [
            {
              "key": "notes",
              "label": "توضیحات کلی",
              "wide": true
            }
          ],
          "description": "اطلاعات تکمیلی، نکات مهم و دستور پیگیری؛ بدون تکرار عنوان پروژه."
        },
        {
          "model": "ProjectStage",
          "title": "جزئیات مرحلهٔ اول",
          "fields": [
            {
              "key": "notes",
              "label": "توضیحات مخصوص این مرحله",
              "wide": true
            }
          ],
          "advanced": true
        },
        {
          "model": "TradeProject",
          "title": "عنوان انگلیسی",
          "fields": [
            {
              "key": "titleEn",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "orders",
      "file": "02-orders.html",
      "title": "ثبت سفارش",
      "short": "سفارش",
      "number": "۲",
      "stage": "ORDER",
      "subtitle": "اطلاعات سند از اقلام و مجوزها جداست؛ یک سند، چند قلم کالا.",
      "groups": [
        {
          "model": "OrderRegistration",
          "title": "مشخصات ثبت سفارش",
          "fields": [
            {
              "key": "orderNumber",
              "label": null
            },
            {
              "key": "registrationDate",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "declaredValueAmount",
              "label": null
            },
            {
              "key": "currencyAmount",
              "label": null
            },
            {
              "key": "incotermCode",
              "label": null
            },
            {
              "key": "paymentMethodCode",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "description": "بانک و عملیات ارزی در بخش ارز مدیریت می‌شوند. فیلد bankPartyId مدل قدیمی در این فرم تکرار نشده است."
        },
        {
          "model": "OrderRegistrationLine",
          "title": "اقلام سفارش",
          "fields": [
            {
              "key": "goodsTitle",
              "label": null
            },
            {
              "key": "hsCode",
              "label": null
            },
            {
              "key": "quantity",
              "label": null
            },
            {
              "key": "unitId",
              "label": null
            },
            {
              "key": "unitPrice",
              "label": null
            },
            {
              "key": "lineTotal",
              "label": null,
              "readonly": true,
              "computed": true
            }
          ],
          "repeat": true
        },
        {
          "model": "OrderPermit",
          "title": "مجوزهای سفارش",
          "fields": [
            {
              "key": "permitTypeCode",
              "label": null
            },
            {
              "key": "permitNumber",
              "label": null
            },
            {
              "key": "issuedBy",
              "label": null
            },
            {
              "key": "issueDate",
              "label": null
            },
            {
              "key": "expiryDate",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "fx",
      "file": "03-fx.html",
      "title": "عملیات ارزی",
      "short": "ارز و بانک",
      "number": "۳",
      "stage": "FX",
      "subtitle": "پروندهٔ ارزی، تخصیص و دریافت مستقل؛ نرخ‌ها حاصل داده‌اند، نه ورودی دستی.",
      "groups": [
        {
          "model": "FxFile",
          "title": "پروندهٔ ارزی",
          "fields": [
            {
              "key": "bankPartyId",
              "label": null
            },
            {
              "key": "fxSourceCode",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "requestDate",
              "label": null
            },
            {
              "key": "allocationNumber",
              "label": null
            },
            {
              "key": "allocationNumberDate",
              "label": null
            },
            {
              "key": "paidRialAmount",
              "label": null
            },
            {
              "key": "allocatedAmount",
              "label": null,
              "readonly": true,
              "computed": true
            },
            {
              "key": "receivedAmount",
              "label": null,
              "readonly": true,
              "computed": true
            },
            {
              "key": "grossRate",
              "label": null,
              "readonly": true,
              "computed": true
            },
            {
              "key": "netRate",
              "label": null,
              "readonly": true,
              "computed": true
            },
            {
              "key": "receiptStatus",
              "label": null,
              "readonly": true
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "FxAllocation",
          "title": "تخصیص‌های ارز",
          "fields": [
            {
              "key": "allocatedAmount",
              "label": null
            },
            {
              "key": "allocationDate",
              "label": null
            },
            {
              "key": "bankReferenceNumber",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "FxReceipt",
          "title": "دریافت‌های ارز",
          "fields": [
            {
              "key": "receivedAmount",
              "label": null
            },
            {
              "key": "receivedAt",
              "label": null
            },
            {
              "key": "bankReferenceNumber",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "Collateral",
          "title": "وثایق و چک‌های بانک",
          "fields": [
            {
              "key": "bankPartyId",
              "label": null
            },
            {
              "key": "collateralTypeCode",
              "label": null
            },
            {
              "key": "instrumentNumber",
              "label": null
            },
            {
              "key": "amount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "heldByBank",
              "label": null
            },
            {
              "key": "heldAt",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "shipments",
      "file": "04-shipments.html",
      "title": "ثبت و مدیریت محموله‌ها",
      "short": "محموله",
      "number": "۴",
      "stage": "SHIPPING",
      "subtitle": "محتویات محموله در سطح قلم؛ اتصال کالا به سفارش و پرونده روشن می‌ماند.",
      "groups": [
        {
          "model": "Shipment",
          "title": "زمان‌بندی محموله",
          "fields": [
            {
              "key": "estimatedDeparture",
              "label": null
            },
            {
              "key": "estimatedArrival",
              "label": null
            },
            {
              "key": "actualDeparture",
              "label": null
            },
            {
              "key": "actualArrival",
              "label": null
            }
          ],
          "description": "ذخیرهٔ میلادی؛ نمایش هم‌زمان شمسی. تاریخ‌ها در ابتدای صفحه قرار دارند."
        },
        {
          "model": "Shipment",
          "title": "مشخصات حمل",
          "fields": [
            {
              "key": "transportMode",
              "label": null
            },
            {
              "key": "forwarderPartyId",
              "label": null
            },
            {
              "key": "carrierPartyId",
              "label": null
            },
            {
              "key": "originPortCode",
              "label": null
            },
            {
              "key": "destinationPortCode",
              "label": null
            },
            {
              "key": "packageTypeCode",
              "label": null
            },
            {
              "key": "packageCount",
              "label": null
            },
            {
              "key": "grossWeight",
              "label": null
            },
            {
              "key": "weightUnitId",
              "label": null
            },
            {
              "key": "volume",
              "label": null
            },
            {
              "key": "volumeUnitId",
              "label": null
            },
            {
              "key": "hasInsurance",
              "label": null
            },
            {
              "key": "billOfLadingNumber",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "ShipmentItem",
          "title": "محتویات و کالاها",
          "fields": [
            {
              "key": "goodsTitle",
              "label": null
            },
            {
              "key": "orderRegistrationLineId",
              "label": null
            },
            {
              "key": "quantity",
              "label": null
            },
            {
              "key": "unitId",
              "label": null
            },
            {
              "key": "grossWeight",
              "label": null
            },
            {
              "key": "volume",
              "label": null
            },
            {
              "key": "packageCount",
              "label": null
            },
            {
              "key": "packageTypeCode",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ShipmentParty",
          "title": "طرف‌های دیگر حمل",
          "fields": [
            {
              "key": "partyId",
              "label": null
            },
            {
              "key": "roleCode",
              "label": null
            },
            {
              "key": "contactName",
              "label": null
            },
            {
              "key": "phone",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "legs",
      "file": "05-transport.html",
      "title": "مراحل حمل",
      "short": "مسیر حمل",
      "number": "۵",
      "stage": "SHIPPING",
      "subtitle": "هر مسیر یک ردیف مستقل است؛ انتخاب مرحلهٔ ناوبری به معنی تکمیل عملیات نیست.",
      "groups": [
        {
          "model": "ShipmentLeg",
          "title": "مسیرهای محموله",
          "fields": [
            {
              "key": "sequence",
              "label": null
            },
            {
              "key": "fromPortCode",
              "label": null
            },
            {
              "key": "toPortCode",
              "label": null
            },
            {
              "key": "transportMode",
              "label": null
            },
            {
              "key": "carrierPartyId",
              "label": null
            },
            {
              "key": "vehicleOrVessel",
              "label": null
            },
            {
              "key": "trackingNumber",
              "label": null
            },
            {
              "key": "estimatedDeparture",
              "label": null
            },
            {
              "key": "estimatedArrival",
              "label": null
            },
            {
              "key": "actualDeparture",
              "label": null
            },
            {
              "key": "actualArrival",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "repeat": true,
          "description": "این صفحه و محموله، هر دو متعلق به مرحلهٔ دامنهٔ SHIPPING هستند؛ مرحلهٔ دامنهٔ جدیدی اختراع نشده است."
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "customs",
      "file": "06-customs.html",
      "title": "گمرک و ترخیص",
      "short": "گمرک",
      "number": "۶",
      "stage": "CUSTOMS",
      "subtitle": "ردیابی اظهار، تکمیل مدارک و ترخیص بدون مخلوط شدن با حمل.",
      "groups": [
        {
          "model": "ShipmentCustoms",
          "title": "پروندهٔ گمرکی",
          "fields": [
            {
              "key": "clearanceNumber",
              "label": null
            },
            {
              "key": "clearanceType",
              "label": null
            },
            {
              "key": "brokerPartyId",
              "label": null
            },
            {
              "key": "customsOfficeCode",
              "label": null
            },
            {
              "key": "declarationDate",
              "label": null
            },
            {
              "key": "documentsCompletedAt",
              "label": null
            },
            {
              "key": "releaseDate",
              "label": null
            },
            {
              "key": "dutyAmount",
              "label": null
            },
            {
              "key": "dutyCurrencyId",
              "label": null
            },
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "insurance",
      "file": "07-insurance.html",
      "title": "بیمه و بازرسی",
      "short": "بیمه و بازرسی",
      "number": "↗",
      "stage": "INSURANCE",
      "subtitle": "بخش مرتبط با حمل؛ وضعیت و نتیجهٔ بازرسی از دیکشنری خوانده می‌شود، نه enum.",
      "groups": [
        {
          "model": "InsurancePolicy",
          "title": "بیمه‌نامه",
          "fields": [
            {
              "key": "insurerPartyId",
              "label": null
            },
            {
              "key": "policyNumber",
              "label": null
            },
            {
              "key": "coverageTypeCode",
              "label": null
            },
            {
              "key": "premiumAmount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "issueDate",
              "label": null
            },
            {
              "key": "startDate",
              "label": null
            },
            {
              "key": "expiryDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "Inspection",
          "title": "بازرسی",
          "fields": [
            {
              "key": "inspectorPartyId",
              "label": null
            },
            {
              "key": "requestDate",
              "label": null
            },
            {
              "key": "inspectionDate",
              "label": null
            },
            {
              "key": "certificateNumber",
              "label": null
            },
            {
              "key": "statusCode",
              "label": null
            },
            {
              "key": "resultCode",
              "label": null
            },
            {
              "key": "resultNotes",
              "label": null
            }
          ]
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "commitments",
      "file": "08-commitments.html",
      "title": "رفع تعهد ارزی",
      "short": "رفع تعهد",
      "number": "↗",
      "stage": "COMMITMENT",
      "subtitle": "اصل تعهد، مهلت و تسویه‌ها مستقل‌اند؛ جمع فقط در یک ارز انجام می‌شود.",
      "groups": [
        {
          "model": "Commitment",
          "title": "تعهد",
          "fields": [
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "committedAmount",
              "label": null
            },
            {
              "key": "deadlineDate",
              "label": null
            },
            {
              "key": "extendedToDate",
              "label": null
            },
            {
              "key": "dischargedAmount",
              "label": null,
              "readonly": true,
              "computed": true
            },
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "CommitmentDischarge",
          "title": "تسویه‌های تعهد",
          "fields": [
            {
              "key": "dischargeDate",
              "label": null
            },
            {
              "key": "amount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "methodCode",
              "label": null
            },
            {
              "key": "documentNumber",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    },
    {
      "id": "costs",
      "file": "09-costs.html",
      "title": "هزینه‌های بازرگانی",
      "short": "هزینه‌ها",
      "number": "↗",
      "stage": "COSTS",
      "subtitle": "ثبت هزینه، تفکیک سهم بخش‌ها و پرداخت مستقل؛ جمع ارزهای متفاوت ممنوع.",
      "groups": [
        {
          "model": "TradeCost",
          "title": "هزینه",
          "fields": [
            {
              "key": "costTypeId",
              "label": null
            },
            {
              "key": "stage",
              "label": null
            },
            {
              "key": "description",
              "label": null
            },
            {
              "key": "amount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "effectiveDate",
              "label": null
            },
            {
              "key": "payeePartyId",
              "label": null
            },
            {
              "key": "paymentMethodCode",
              "label": null
            },
            {
              "key": "referenceNumber",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ]
        },
        {
          "model": "CostAllocation",
          "title": "تفکیک بخش‌ها",
          "fields": [
            {
              "key": "scopeType",
              "label": null
            },
            {
              "key": "scopeId",
              "label": null
            },
            {
              "key": "stage",
              "label": null
            },
            {
              "key": "allocationPercent",
              "label": null
            },
            {
              "key": "allocatedAmount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            }
          ],
          "repeat": true,
          "description": "درصد سهم‌ها باید در مجموع ۱۰۰ باشد. scopeId باید به موجودیت مشخص‌شده در scopeType اشاره کند."
        },
        {
          "model": "CostPayment",
          "title": "پرداخت‌ها و برگشت وجه",
          "fields": [
            {
              "key": "paidAt",
              "label": null
            },
            {
              "key": "amount",
              "label": null
            },
            {
              "key": "currencyId",
              "label": null
            },
            {
              "key": "methodCode",
              "label": null
            },
            {
              "key": "isReturn",
              "label": null
            },
            {
              "key": "referenceNumber",
              "label": null
            }
          ],
          "repeat": true
        },
        {
          "model": "ProjectStage",
          "title": "مسئول و یادداشت مرحله",
          "fields": [
            {
              "key": "responsibleUserId",
              "label": null
            },
            {
              "key": "startedAt",
              "label": null
            },
            {
              "key": "dueDate",
              "label": null
            },
            {
              "key": "notes",
              "label": null
            }
          ],
          "advanced": true
        }
      ]
    }
  ],
  "referenceCommit": "620c750919584a37ea5f23d06d28bbdc750ee810"
};
