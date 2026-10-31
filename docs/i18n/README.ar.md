<!-- canonical: ../../README.md -->
<!-- sync: ef2a485dfffb -->

<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../../docs/assets/logo-dark-theme.png">
  <source media="(prefers-color-scheme: light)" srcset="../../docs/assets/logo-light-theme.png">
  <img src="../../docs/assets/logo.png" alt="Env Manager 标志" width="120" height="120">
</picture>
<p align="center">
  <img src="../../docs/assets/brand/hero.gif" alt="Env Manager mini hero" width="100%">
</p>


# Env Manager

مدير حديث وخفيف لمتغيرات بيئة Windows — بوضعين: CLI وGUI، مستوحى من Microsoft PowerToys لكنه مستقل وصديق للوكلاء.

**«يتكيف بسلاسة مع كل بيئة.»**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **العربية** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## العروض التوضيحية

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>


يوضح العرض التوضيحي أوامر CLI للقراءة فقط أثناء العمل: agents --summary وpath health وget PATH وagents --json. أعد إنشاءه باستخدام `node scripts/demo/capture.mjs`.

*مخرجات الطرفية في العرض أعلاه تستخدم بيانات محاكاة.*


<div dir="rtl">

### معرض الصور

| | | |
|:-:|:-:|:-:|
| ![عرض المتغيرات](../../docs/assets/gallery/01-variables.png)<br>**المتغيرات** — بحث، نطاق، إجراءات لكل صف | ![نطاق النظام](../../docs/assets/gallery/02-variables-system.png)<br>**نطاق النظام** — العناصر المحمية مقفلة | ![محرر PATH](../../docs/assets/gallery/03-path-editor.png)<br>**محرر PATH** — كشف الإدخالات الميتة والمكررة |
| ![الملفات الشخصية](../../docs/assets/gallery/04-profiles.png)<br>**الملفات الشخصية** — تطبيق / تشغيل / وراثة | ![السجل والاستيراد](../../docs/assets/gallery/05-history.png)<br>**السجل والاستيراد** — تغييرات قابلة للتراجع | ![الحماية](../../docs/assets/gallery/06-protection.png)<br>**الحماية** — أقفال مخصصة |
| ![سجل التدقيق](../../docs/assets/gallery/07-audit.png)<br>**سجل التدقيق** — متسلسل بالهاش وقابل للتحقق | ![مدير الخدمة](../../docs/assets/gallery/08-service.png)<br>**مدير الخدمة** — نقاط تحميل الأسرار | ![الإعدادات](../../docs/assets/gallery/09-settings-i18n.png)<br>**الإعدادات** — i18n بعشر لغات |
| ![نافذة التحرير](../../docs/assets/gallery/10-edit-dialog.png)<br>**نافذة التحرير** — معاينة القيمة الموسعة | ![السمة الفاتحة](../../docs/assets/gallery/11-variables-light.png)<br>**السمة الفاتحة** — نسخة السمة الفاتحة | ![CLI](../../docs/assets/gallery/12-cli-terminal.png)<br>**CLI** — JSON أولًا، صديق للوكلاء |

</div>


## الميزات

- CLI أصلي للوكلاء — أكثر من 18 أمرًا بعقد آلي من الدرجة الأولى: يكشف env-manager-cli agents --json عن مواصفات أوامر منظمة، وكل إمكانية موثقة في دليل موجّه للوكلاء (AGENTS.cli.md) يُوزَّع مع الملف التنفيذي.
- الملفات الشخصية والإعدادات — تُطبَّق الملفات الشخصية العامة (Global profiles) على السجل؛ بينما تحقن ملفات التشغيل (Launch profiles) كتلة بيئة معزولة في عملية واحدة (لا تلمس السجل أبدًا ولا تبث WM_SETTINGCHANGE). تشمل الوراثة ومعاينة التعارضات وتراجعًا آمنًا بترتيب عكسي.
- 8 موفّري أسرار — DPAPI وCredential Manager وSecretStore وHashiCorp Vault وSOPS وAzure Key Vault و1Password وAWS Secrets Manager. لا تُحفظ الأسرار إلا في صورة أغلفة مشفّرة (ciphertext envelopes)؛ والحدود الدقيقة لتعرّض النص الصريح مبيّنة في جدول [الأمان](#الأمان).
- محمي افتراضيًا — لا يمكن حذف متغيرات النظام وإدخالات PATH أو إعادة تسميتها؛ كل كتابة هي عقد متسلسل من ثلاث طبقات (mutex + قفل كتابة + تحقق قبل الاستبدال).
- فحص صحة PATH — يكتشف الإدخالات المكررة والميتة، مع خيارات --fix / --dry-run.
- سجل التدقيق — تاريخ للإضافة فقط (append-only) مربوط بسلسلة تجزئة SHA256، مع تراجع وتصدير للاسترداد من الكوارث.
- وضعان مزدوجان CLI + GUI — CLI بلغة C# للنصوص البرمجية/CI؛ وGUI أصلي بتقنية Tauri 2 + Svelte للتحرير التفاعلي. كلاهما يمر عبر عقدي السجل نفسيهما. دعم i18n بعشر لغات.

## لوكلاء الذكاء الاصطناعي

صُمم Env Manager ليعمل به وكلاء LLM، وليس البشر فقط:

- AGENTS.md — تعليمات على مستوى المستودع للوكلاء (البنية، الحدود الصارمة، الاختبار).
- AGENTS.cli.md — يُوزَّع مع الملف التنفيذي للـCLI حتى يتمكن أي وكيل من اكتشاف العقد وقت التشغيل.
- سطح وكيل محدد النطاق بالقدرات — القائمة البيضاء الاختيارية agentCapabilities في secret-providers.json تتيح للبيئات المنتشرة رفض استدعاءات set/delete المتوازية القادمة من الوكلاء.

## الأمان

> الإصدارات الحالية غير موقعة برمجيًا. قد يعرض Windows SmartScreen تحذير تطبيق غير معروف عند التشغيل الأول — انقر «مزيد من المعلومات» ثم «التشغيل على أي حال». تقدمنا بطلب للحصول على توقيع كود مجاني للمشاريع مفتوحة المصدر عبر SignPath Foundation؛ وبمجرد الموافقة، ستُوقَّع جميع نواتج الإصدار (MSI + EXE).

تُعطَّل المتغيرات المحمية وإدخالات PATH قبل حذفها، مع تحقق دقيق من نوع القيمة في السجل عند الاستعادة. تستخدم IPC عبر named pipe أعلامًا مضادة للاستحواذ (anti-squatting) والتحقق من المدخلات (حد أقصى 64 وسيطًا، وسقف 32767 حرفًا، ورفض البايتات الفارغة).

### أين يمكن أن يوجد النص الصريح (جدول حدود الأسرار)

| الموضع | هل يبقى نص صريح؟ | مدة البقاء | التنظيف | الأساس |
|---|---|---|---|---|
| `profiles.json` / `secretMount.json` | لا بالنسبة لأوامر الأسرار — الغلاف `{provider, version, createdAt, ciphertext, targetName}` أو مرجع `mount:<id>` فقط؛ الأمران العامّان `add-var`/`edit-var` يتجاوزان العلامة (انظر [دورة حياة النص الصريح](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | حتى إزالة السر | يحذف `profile remove-secret` الغلاف والحالة لدى الموفّر | حدّ صارم؛ تأكيد العقد `plaintext-never-in-envelope` |
| dpapi-current-user | لا — DPAPI `CryptProtectData` بنطاق CurrentUser | داخل الغلاف | مع الغلاف | حدّ صارم (v0.7.0) |
| credential-manager | لا — يُشفَّر blob الخاص بـ CredMan نفسه عبر DPAPI قبل حفظه | حتى `Delete` لدى الموفّر | `Delete` عند remove-secret | [دليل الموفّرين](../../docs/secret-providers-guide.md) |
| powershell-secretmanagement | يبقى ملف خزنة SecretStore مشفّرًا — لكن النص الصريح يمرّ عبر الوسيط `pwsh -EncodedCommand` (سكربت base64) في كل استدعاء | عمر عملية pwsh؛ ومدخل الخزنة حتى إزالته | يزول الوسيط بانتهاء العملية؛ ويُحذف مدخل الخزنة عند remove-secret | حدّ صارم (v0.7.4) |
| vault-kv2 · azure-keyvault · aws-secretsmanager · 1password | لا نص صريح محليًا — يحتفظ الملف الشخصي بمرجع مسار/معرّف سر فقط؛ ويبقى السر في الواجهة الخلفية البعيدة  الملف الشخصي المحلي بلا نص صريح، لكن قد يظهر النص الصريح مؤقتًا في وسائط العمليات الفرعية ومخرجاتها وأجسام طلبات المزوّد أثناء العمليات.| سياسة المشغّل لدى الواجهة الخلفية | `Delete` لدى الموفّر؛ ويعيد `secret-provider rotate` التشفير | [دليل الموفّرين](../../docs/secret-providers-guide.md) |
| sops | نص مشفّر فقط في الغلاف — **إضافةً إلى** ملفَي `secret.json` / `secret.dec.json` بنص صريح في مجلد خاص بكل استدعاء أثناء التشفير/فك التشفير: `%TEMP%\env-manager-sops-*`، أو مجلد داخل الجذر الذي تملكه الخدمة عندما يكون التدوير مفوَّضًا  قد يؤدي احتفاظ مساعد بالملف بعد نافذة الاسترداد إلى فشل الحذف وبقاء جذر scratch؛ ويظل التنظيف بأفضل جهد.| ثوانٍ لكل استدعاء | `Directory.Delete` لذلك المجلد. عند التفويض من الخدمة، تحذف الخدمة الجذر كله بعد انتهاء العملية الفرعية لـ CLI — بما في ذلك إيقاف أنهى CLI — مع إعادة محاولة محدودة وتسجيل بمستوى error (لا صمت أبدًا) إذا استمر فشل الحذف؛ وعند التشغيل اليدوي يكون الحذف بأفضل جهد وقد يبقى المجلد يتيمًا بعد انهيار. يُنشأ المجلد بصلاحية المالك فقط (التوريث معطّل؛ الهوية الحالية وSYSTEM وAdministrators)، فلا يقرأ اليتيم سوى هذه الهويات — ما لم يفشل تطبيق ذلك الـ DACL، وعندها يحتفظ بالـ ACL الذي كان سيرثه (ACL الخاص بـ `%TEMP%`، أو بجذر scratch الخاص بالخدمة عند التفويض) | حدّ صارم (v0.7.11)؛ `SopsProvider`، `SecretTempDirectory`، `service/src/sops_scratch.rs` |
| ذاكرة العملية المُطلِقة (`env-manager-cli`) | نعم — تبقى البايتات المفكوكة لفترة وجيزة في العملية المُطلِقة  قد تبقى القيم في System.String حتى خروج CLI؛ ويصفّر SecretString نسخته المنفصلة فقط، وقد تبقى نسخ GC والسلاسل الوسيطة.| أجزاء من الثانية، حتى اكتمال الحقن/الكشف | يصفّر `SecretString` مصفوفته `char[]` عند Dispose — **بأفضل جهد**: قد تبقى نسخ GC والسلاسل الوسيطة بعده | تصفير `SecretString`؛ [شبكة canary لانعدام التسريب](../../docs/architecture.md#canary-zero-leak-assertion-net-and-goldensnapshot-layers) |
| كتلة بيئة العملية الفرعية المُطلَقة | نعم — متغيرات البيئة المحقونة نص صريح داخل العملية الفرعية | عمر العملية الفرعية | غير ممكن — كتل البيئة قابلة للقراءة من المستخدم نفسه بحكم تصميم النظام | تصميم ملفات Launch الشخصية |
| المخرج القياسي لـ `profile reveal-secret` | نعم — **عن قصد في التصميم**؛ أحد مسارَي إخراج النص الصريح (`profile reveal-secret`، `profile show --reveal`) | حتى مسح سجل تمرير الطرفية ; قد يحتفظ مستهلك pipe لاحق بنسخته حتى يحذفها.| لا شيء — يستدعيه المستخدم | حدّ صارم؛ تتخطى شبكة canary هذه المواضع عمدًا |
| الملفات المؤقتة لحزمة النجاة (`envmanager-kit-*.json` تحت `%TEMP%\env-manager-kit-<guid>\`، `mount-survival-kit.tmp.json`) | نعم — ملف مؤقت قصير العمر بنص صريح أثناء تجميع الحزمة. يُكتب ملف الحزمة من جانب CLI داخل `%TEMP%\env-manager-kit-<guid>\`، وهو مجلد يُنشأ بصلاحية المالك فقط (التوريث معطّل؛ الهوية الحالية وSYSTEM وAdministrators)، فلا يقرأ اليتيم سوى هذه الهويات — ما لم يفشل تطبيق ذلك الـ DACL، وعندها يحتفظ بـ ACL الموروث من `%TEMP%`. أما `mount-survival-kit.tmp.json` من جانب الخدمة فيقع في مجلد ledger، لا تحت `%TEMP%` | ثوانٍ | حذف بأفضل جهد بعد التشفير، ثم حذف بأفضل جهد للمجلد الذي احتواه: فشل حذف المجلد يترك مجلدًا فارغًا، وإذا فشل حذف الملف أيضًا يبقى الملف ذو النص الصريح كذلك — كلاهما بأفضل جهد، كما في صفوف sops أعلاه | `AuditLedgerMigration.RunAuditExportSurvivalKit`، `SecretTempDirectory.Create`؛ `service/src/audit_ledger.rs` |
| `audit-ledger.jsonl` | لا تُنسخ قيم الأسرار إلى أحداث ledger المرحّلة؛ تُحفظ المعرّفات والبيانات الوصفية وعلامات التنقيح فقط، ولا تُرحّل ملخصات old/new العامة. | إلحاق فقط، تدوير عند 100MB | التدوير | حدّ صارم (v0.9.13)؛ تشفير `AuditCrypto` الثابت |
| ملف `export-state` | لا — طبقتان: حمولة AES-256-GCM + مفتاح DEK مغلّف بـ DPAPI + HMAC-SHA256 | حتى تحذف الملف | يديره المستخدم | `AuditCrypto` (v0.9.13) |
| `logs/env-manager.log` / stderr | لا بحكم التصميم — يُنقّى نص الاستثناءات أولًا من 22 نمطًا حاملًا للأسرار؛ قائمة سماح **بأفضل جهد**: قد يتسرب تنسيق سر غير معروف، ويحدّه الاقتطاع عند 512 حرفًا | مدة الاحتفاظ بالسجلات | — | قائمة سماح `ScrubExceptionMessage`؛ `CanaryRedactionTests` + شبكة canary على 7 مواضع |
| الواجهة الرسومية (WebView2) / خدمة Rust | لا — تعرض الواجهة عناصر نائبة `<encrypted>` ولا تملك عرضًا للكشف؛ ولا تحفظ الخدمة القيم | — | — | حدّ الأسرار في البنية |
| سجل Windows | لا بالنسبة لأوامر الأسرار — لا تستطيع ملفات Launch الشخصية الكتابة في السجل ويرفضها apply؛ وقد تصل إليه ثغرة إعادة التسمية في الأوامر العامة (انظر [دورة حياة النص الصريح](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | — | — | حدّ صارم؛ ثابت `launch-env-injection.Tests.ps1` |

هذا الجدول ملخص موجّه للمستخدم؛ وقائمة السماح المرجعية لكل سطح، بما فيها ثغرة الأوامر العامة، هي قسم Plaintext lifecycle في `docs/architecture.md`.

تصفير الذاكرة وتنقية السجلات إجراءات نظافة بأفضل جهد لا ضمانات — وهو التحفّظ نفسه الذي توثّقه Microsoft لـ `SecureString` (سلاسل غير قابلة للتغيير، ونقل GC، ونسخ interop). وحيث يمكن اختبار الادعاء فهو مختبَر: يفحص `tests/canary-redaction.Tests.ps1` سبعة مواضع إخراج بحثًا عن canary مزروع، وتتحقق مجموعة العقود المشتركة من `plaintext-never-in-envelope` على الموفّرين الثمانية جميعًا.

## التثبيت

### مثبّت MSI

نزّل ملف MSI من GitHub Releases وشغّله. ينشئ اختصارات قائمة ابدأ تلقائيًا. متاح بإصدارات x64 وx86 وARM64.

### Portable

نزّل ملف ZIP المحمول من GitHub Releases. فك الضغط وشغّل env-manager.exe مباشرة. لا حاجة للتثبيت.

### CLI فقط

نزّل ZIP الخاص بـCLI فقط للاستخدام بدون واجهة أو عبر النصوص البرمجية: env-manager-cli.exe بالإضافة إلى ملفات .dll. بدون GUI وبدون اعتماد على WebView2.

### المتطلبات المسبقة

> إصدارات Portable وCLI-Only تعتمد على framework: تتطلب .NET 10 Desktop Runtime على الجهاز المستهدف. يتحقق مثبّت MSI من .NET 10 وقت التثبيت ويطلبها تلقائيًا.
> بيئة WebView2 Runtime (للواجهة GUI) مثبتة مسبقًا على Windows 11 ومتاحة لنظام Windows 10 21H2+ من Microsoft.

بالنسبة للأدوات الخارجية الاختيارية لموفّري الأسرار (SOPS و1Password CLI وVault CLI وAWS CLI وAzure CLI وPowerShell 7)، راجع دليل موفّري الأسرار.

### winget

> توزيع عبر winget مخطط له لكنه غير متاح بعد. تابع المستجدات عبر GitHub Issues.

### من المصدر

يتطلب .NET 10 SDK وNode.js 20+ وRust مستقرًا مع هدف MSVC.

## الاستخدام

### CLI

راجع docs/cli-commands.md للحصول على المرجع الكامل للأوامر.

### GUI

شغّل env-manager.exe. توفر الواجهة GUI قائمة متغيرات في الوقت الفعلي مع بحث وتصفية حسب النطاق وتحرير مضمّن ومحرر PATH لإعادة الترتيب بالسحب والإفلات وإدارة الملفات الشخصية واختيار موفّر الأسرار ولوحة التحكم في الخدمة وسجل التدقيق ودعم i18n بعشر لغات.

## البنية

- CLI: ملف تنفيذي أحادي الملف بلغة C# .NET 10 — طبقة التنسيق وبوابة السجل.
- الخدمة: ملف تنفيذي مستقل بلغة Rust يدير دورة حياة تركيب الأسرار عبر IPC باستخدام named pipe.
- GUI: واجهة أمامية Tauri 2 + Svelte 4 تستخدم عقدي IPC نفسيهما.

## موفّرو الأسرار

8 خلفيات (backends) لموفّري الأسرار مع فحص مسبق للتفعيل — تظهر حالات الفشل كلافتات كهرمانية مضمّنة مباشرة في محرر الملفات الشخصية.

راجع docs/secret-providers-guide.md لمعرفة المتطلبات الخاصة بكل موفّر والإعداد لمرة واحدة وخطوات إصلاح أخطاء التفعيل.

## وضع الخدمة

env-manager-service.exe هو ملف تنفيذي مستقل بلغة Rust يدير دورة حياة تركيب الأسرار عبر IPC باستخدام named pipe:

- RuntimeMode: Service (تديره SCM، عند إقلاع الجهاز)، Background (يُشغَّل بواسطة المستخدم)، Cli (بوابة لمرة واحدة)
- حلقة المطابقة: فحص كامل دوري كل 300 ثانية، معالج لكل عنصر بسلوك idempotent، وتأخير 30 ثانية قبل أول دورة
- سجل التدقيق: audit-ledger.jsonl للإضافة فقط بسلسلة تجزئة، مع تدوير عند 100 ميجابايت واكتشاف العبث
- IPC: علم named pipe مضاد للاستحواذ، سقف 65536 بايت للطلب، بروتوكول JSON مفصول بفواصل الأسطر
- Watchdog: استرداد من طبقتين — إعادة تشغيل تلقائي عبر SCM (وضع Service) + مراقب ping كل 30 ثانية في GUI (وضع Background)

## التوثيق

## القائمون على الصيانة

## المساهمة

نرحب بالمساهمات عبر Issues وPR. اقرأ AGENTS.md أولاً لفهم حدود البنية وسياسة الاختبار.

## الترخيص

Apache-2.0 (c) 2026 Env Manager Contributors.
