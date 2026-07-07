---
layout: page
title: Privacy and Cookies
description: How the Gentastic website handles analytics and cookies. Opt-in, anonymous, no ads.
---

This is the documentation and marketing site for **Gentastic**, the free, open-source Windows app for
local image generation. It is a static site with no accounts, no logins, and no advertising. The only
thing that touches your data is opt-in, anonymous usage analytics, explained below.

The Gentastic **application itself** runs entirely on your own PC. It does not send your prompts,
images, or settings anywhere. Downloading models contacts Hugging Face, and an optional update check
contacts GitHub; neither is covered by this website notice. This page is only about the website.

## Who is responsible (data controller)

The data controller for this website is **IDCT Bartosz Pachołek** (idct.tech). Contact:
[bartosz@idct.tech](mailto:bartosz@idct.tech).

## What we collect

Only if you press **Accept** in the cookie banner do we load **Google Analytics 4** to measure
aggregate, anonymous usage: pages viewed, rough geography, device and browser type, referring links,
and a few basic interactions (such as outbound link clicks and file downloads). We use this only to
understand what is useful and improve the site. We do **not**:

- serve ads or use advertising cookies;
- track you across other websites;
- sell or share your data with third parties for marketing;
- attempt to identify you personally.

## When analytics loads

We use a **prior-consent** model. Google Analytics is **not loaded at all** until you choose
**Accept**. If you **Reject**, or ignore the banner, **nothing is sent to Google** and no analytics
cookies are set. The site works fully either way.

## Cookies and storage

Nothing analytics-related is stored or sent until you **Accept**. If you do, Google Analytics sets:

| Cookie | Purpose | Retention |
| --- | --- | --- |
| `_ga` | Distinguishes anonymous visitors | ~2 years |
| `_ga_NG335XP03E` | Maintains the analytics session state | ~2 years |

Independently of your choice, a small **`localStorage`** entry (`gentastic-consent`) records your
Accept or Reject decision and its date so the banner does not reappear every visit. That entry is
strictly functional (not analytics), stays on your device, and we re-ask for consent after about
**180 days**.

## Legal basis

Analytics is processed **only on the basis of your consent** (GDPR Art. 6(1)(a)), which you can
withdraw at any time (see *Your choices* below). Withdrawing consent does not affect processing that
already happened.

## International transfer

When you Accept, analytics data is processed by **Google LLC** in the United States. Google is
certified under the
[EU-U.S. Data Privacy Framework](https://www.dataprivacyframework.gov/), which the transfer relies on
(with Google's Standard Contractual Clauses as a fallback safeguard). GA4 does not store IP addresses.

## Your choices

- **Accept** or **Reject** in the banner. Reject keeps analytics fully off.
- Change your mind anytime via **Cookie preferences** in the footer, or by clearing this site's data
  in your browser.
- Install Google's
  [Analytics opt-out add-on](https://tools.google.com/dlpage/gaoptout).

## Your rights

Under the GDPR you have the right to access, rectify, erase, restrict, or object to processing of your
data, to withdraw consent, and to lodge a complaint with your data-protection supervisory authority
(in Poland, the [UODO](https://uodo.gov.pl/)). To exercise any of these, contact
[bartosz@idct.tech](mailto:bartosz@idct.tech).

## More information

Analytics data is handled per
[Google's Privacy Policy](https://policies.google.com/privacy) and
[data-processing terms](https://business.safety.google/adsprocessorterms/). Questions? Use the
[contact page]({{ '/contact/' | relative_url }}).

---

*Last updated 7 July 2026. This is a plain-language notice for a low-risk static site.*
