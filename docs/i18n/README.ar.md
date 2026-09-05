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


يوضح العرض التوضيحي أوامر CLI للقراءة فقط أثناء العمل: agents --summary وpath health وget PATH وagents --json. أعد إنشاءه باستخدام vhs docs/assets/demo.tape.

## الميزات

- CLI أصلي للوكلاء — أكثر من 18 أمرًا بعقد آلي من الدرجة الأولى: يكشف env-manager-cli agents --json عن مواصفات أوامر منظمة، وكل إمكانية موثقة في دليل موجّه للوكلاء (AGENTS.cli.md) يُوزَّع مع الملف التنفيذي.
- الملفات الشخصية والإعدادات — تُطبَّق الملفات الشخصية العامة (Global profiles) على السجل؛ بينما تحقن ملفات التشغيل (Launch profiles) كتلة بيئة معزولة في عملية واحدة (لا تلمس السجل أبدًا ولا تبث WM_SETTINGCHANGE). تشمل الوراثة ومعاينة التعارضات وتراجعًا آمنًا بترتيب عكسي.
- 8 موفّري أسرار، بلا نص صريح — DPAPI وCredential Manager وSecretStore وHashiCorp Vault وSOPS وAzure Key Vault و1Password وAWS Secrets Manager. لا يُحفظ النص الصريح أبدًا على القرص أو في السجلات.
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

تُعطَّل المتغيرات المحمية وإدخالات PATH قبل حذفها، مع تحقق دقيق من نوع القيمة في السجل عند الاستعادة. تُشفَّر قيم الأسرار عبر آليات خاصة بكل موفّر — لا يُحفظ النص الصريح أبدًا على القرص أو في السجلات. تستخدم IPC عبر named pipe أعلامًا مضادة للاستحواذ (anti-squatting) والتحقق من المدخلات (حد أقصى 64 وسيطًا، وسقف 32767 حرفًا، ورفض البايتات الفارغة).

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
- الإقلاع بالشهادات: المصادقة القائمة على الشهادات عبر Vault AppRole وAzure SP تلغي الرموز طويلة العمر
- سجل التدقيق: audit-ledger.jsonl للإضافة فقط بسلسلة تجزئة، مع تدوير عند 100 ميجابايت واكتشاف العبث
- IPC: علم named pipe مضاد للاستحواذ، سقف 65536 بايت للطلب، بروتوكول JSON مفصول بفواصل الأسطر
- Watchdog: استرداد من طبقتين — إعادة تشغيل تلقائي عبر SCM (وضع Service) + مراقب ping كل 30 ثانية في GUI (وضع Background)

## التوثيق

## القائمون على الصيانة

## الإصدارات

تعمل الإصدارات عبر مسار release-please الأحادي: أرسل التزامات بالصيغة التقليدية إلى main، وراجع ثم ادمج PR الإصدار التلقائي `chore(main): release X.Y.Z`، فيطلق وسم `vX.Y.Z` خط إنتاج المخرجات الكامل (portable / CLI فقط / MSI لمعماريات x64/x86/arm64 مع إثباتات بناء المصدر). يُحتفظ بسير العمل اليدوي للطوارئ فقط. التفاصيل في [docs/build-and-release.md](docs/build-and-release.md) قسم "How to Release".

## المساهمة

نرحب بالمساهمات عبر Issues وPR. اقرأ AGENTS.md أولاً لفهم حدود البنية وسياسة الاختبار.

## الترخيص

Apache-2.0 (c) 2026 Env Manager Contributors.
