# Exhibition Companion — the visitor's mobile app

The Android and iOS app for the RFID Exhibition Tracking system. A visitor signs
in with the email address they registered with, searches the show — exhibitors,
categories, sub-categories, halls, meetings and lectures — and scans the QR code
on a stand to have that exhibitor's e-catalogue put in their evening pack.

One Flutter codebase, two store apps. `flutter build appbundle` produces the
Play Store upload and `flutter build ipa` the App Store one, from the same
source.

---

## What a visitor can do

| | |
| --- | --- |
| **Sign in** | Their registered email address, then a six-digit code emailed to it. No password, because visitors never had one — they have a badge and an email. The device stays signed in for 30 days, enough for a multi-day show. |
| **Search** | One box over everything: company names, stand numbers like `H1-014`, product descriptions, countries, talk titles, speakers and rooms. |
| **Filter** | Category, sub-category, hall and country, from the organiser's own taxonomy. Every count shown is live. |
| **Browse** | The category tree with exhibitor counts, and each hall with its stands, its size and what is on in it. |
| **Programme** | Meetings, lectures, workshops, panels, demos and ceremonies — a day at a time in time order, filtered by kind and category, saved to a personal agenda. |
| **Scan** | The camera reads the stand's QR code and records the e-catalogue request. It keeps scanning, because visitors walk a row and scan four stands in ninety seconds. |
| **My list** | Everything collected today, removable before the evening pack is built, plus the day the tracking recorded: where the time went, and which stands in those categories were not reached. |
| **Profile** | The two consents that actually change what the system does — record my stand visits, and email me — both switchable from the phone. |

---

## Setting it up

You need [Flutter](https://docs.flutter.dev/get-started/install) 3.22 or later.
Android Studio for the Android build; Xcode on a Mac for the iOS one.

```powershell
./setup.ps1
```

That generates the `android/` and `ios/` folders with `flutter create`, adds the
camera permission and its usage strings, and fetches packages. It is safe to run
again.

Then point it at your exhibition system and run it:

```bash
flutter run --dart-define=EXB_BASE_URL=http://YOUR-SERVER:5080
```

The address must be the one visitors' phones can reach — the same value as
`Settings › Exhibition › Public base URL` in the admin console, which is what the
stand QR codes resolve to. On the Android emulator the host machine is
`10.0.2.2`, which is the built-in default.

It is also changeable from the sign-in screen. That is not a developer
convenience: exhibition systems are routinely deployed on the venue's own
network on the morning of the show, and an app that could only be repointed by
shipping a store release would be useless to the organiser on day one.

### Signing in before email is configured

The exhibition system ships with its mail provider set to `outbox`, which queues
messages in the database and sends nothing. The API notices, and returns the
sign-in code in the response instead — the app shows it on screen. Set
`Settings › Delivery › mail provider` to `smtp` and it stops doing that
immediately, because at that point the email genuinely arrives.

### Building for the stores

```bash
flutter build appbundle --dart-define=EXB_BASE_URL=https://exhibition.example.com   # Play Store
flutter build ipa       --dart-define=EXB_BASE_URL=https://exhibition.example.com   # App Store
```

Bump `version:` in `pubspec.yaml` before each upload. The number after the `+` is
what both stores order releases by and it must never go backwards.

Two things to do before the first submission:

- **Use HTTPS.** Both platforms block plain HTTP from a store build by default,
  and a bearer token over HTTP on venue wifi is readable by anyone on it.
- **Set the bundle identifier deliberately.** `setup.ps1` uses
  `com.smatech.exhibition_companion`. It cannot be changed later without the
  stores treating it as a different app.

---

## How it fits the rest of the system

The app talks to `/api/v1` on the existing ASP.NET Core application — added in
`src/Exb.Web/Api/`. Nothing about the admin console changed; the API is a
separate versioned surface so the two can move independently.

```
POST   /api/v1/auth/request-code      email  ->  a six-digit code, emailed
POST   /api/v1/auth/verify            code   ->  a device token
POST   /api/v1/auth/logout

GET    /api/v1/exhibition             name, halls, category tree, countries, programme days
GET    /api/v1/exhibitors             ?q= &categoryId= &subCategoryId= &hallId= &country= &page=
GET    /api/v1/exhibitors/{id}
GET    /api/v1/halls  ·  /halls/{id}
GET    /api/v1/categories
GET    /api/v1/search                 ?q=  across all four kinds at once

GET    /api/v1/sessions               ?q= &date= &kind= &hallId= &categoryId= &bookmarked=
GET    /api/v1/sessions/{id}
POST   /api/v1/sessions/{id}/bookmark  ·  DELETE to remove

GET    /api/v1/me  ·  PATCH /api/v1/me/consent
GET    /api/v1/me/agenda
POST   /api/v1/me/scan                a scanned QR code -> an e-catalogue request
GET    /api/v1/me/catalogues  ·  POST to add by stand  ·  PATCH to remove
GET    /api/v1/me/day                 the interest report, live
```

Every route except the three under `/auth` requires
`Authorization: Bearer <token>`.

### What was added to the backend

- **`ProgrammeSession` and `SessionBookmark`.** The meetings and lectures did not
  exist before. They share the stands' category taxonomy on purpose — that is
  what lets a visitor interested in RFID be shown both the stands and the talks
  from one search — and they are edited in the admin console under
  **Meetings & lectures**.
- **`VisitorLoginCode` and `MobileSession`.** Both store only a SHA-256 hash;
  neither the code nor the token is ever written to the database in the clear.
- **`MobileAuthService`, `MobileDirectoryService`, and the endpoints above.**
- A migration, `20260817031104_MobileAppAndProgramme`, and a regenerated
  `src/Exb.Data/Sql/schema.sql`.

### Two decisions worth knowing about

**An unregistered email gets the same answer as a registered one.** Asking for a
code always reports success. An endpoint that says "no such visitor" turns the
attendee list into something anyone with the URL can enumerate.

**Saving a session is not booking one.** It puts the session in the visitor's
agenda in the app, and the app says so on every session page. Capacity is shown
as information. Turning it into a real reservation needs the organiser to manage
seats and cancellations, which is a different feature with a different promise
attached — and a visitor who believes they hold a seat and arrives to a full room
has been misled by us.

---

## Layout

```
exhibition_companion/
  lib/
    main.dart            picks the sign-in screen or the app
    config.dart          the server address, and what is kept on the device
    theme.dart           light and dark, seeded from the console's blue
    api/
      models.dart        the wire shapes, parsed defensively
      api_client.dart    the typed client, and the errors a visitor can act on
    state/
      app_state.dart     session, exhibition reference data, catalogue count
      app_scope.dart     makes it reachable from any widget
    screens/             search · programme · scan · my list · profile, and the
                         detail screens behind them
    widgets/             the list tiles and the banner/empty states
  test/                  parser and client tests — `flutter test`
setup.ps1                generates android/ and ios/, applies permissions,
                         the app name and the removal of the template test
```

`android/` and `ios/` are generated rather than checked in: they are thousands of
lines of Gradle and Xcode project files that Flutter recreates from the SDK you
actually have, and committing the ones from a different SDK version is how a
project stops building on somebody else's machine.

---

## What has been verified, and what has not

Worth being precise about, because "it compiles" and "it is on a phone" are very
different claims.

**Verified on this machine** (Flutter 3.47.1, Dart 3.13.1):

- `flutter analyze` — no issues.
- `flutter test` — 29 tests pass, covering the JSON parsers and the API client.
- `flutter build bundle` — the whole app compiles to a release kernel snapshot.
  This is the strong one: analysis is static, but this runs the real compiler
  over every screen, the client and the plugin registrations.
- The backend it talks to: `dotnet test`, 129 tests, 26 of them driving the
  mobile API end to end — sign-in, code reuse and guess limits, search,
  filtering, the programme, and scanning.

**Not verified here, and what each would take:**

| | Why not | What it needs |
| --- | --- | --- |
| `flutter build apk` | The Android SDK packages need Google's SDK licence accepted, which is an agreement in the owner's name. | `sdkmanager --licenses`, then the build. One command, below. |
| `flutter build ipa` | iOS builds require macOS and Xcode. There is no way around this on Windows. | A Mac, an Apple Developer account, and a signing certificate. |
| Running against a live server | The exhibition system needs its SQL Server database, which is not on this machine. | The connection string, then `dotnet run --project src/Exb.Web`. |

To finish the Android build:

```powershell
$env:JAVA_HOME = "C:\tools\jdk\jdk-17.0.20+8"
$env:ANDROID_HOME = "C:\tools\android"
$env:PATH = "$env:JAVA_HOME\bin;C:\tools\android\cmdline-tools\latest\bin;C:\tools\flutter\bin;$env:PATH"

sdkmanager --sdk_root=C:\tools\android --licenses          # review and accept
sdkmanager --sdk_root=C:\tools\android "platform-tools" "platforms;android-36" "build-tools;35.0.1"
flutter config --android-sdk C:\tools\android

cd "MOBILE APP\exhibition_companion"
flutter build apk --debug --dart-define=EXB_BASE_URL=http://YOUR-SERVER:5080
```

The JDK and the Android command-line tools are already downloaded and extracted
to `C:\tools`; only the licence acceptance and the package install remain.

---

## Commands

```bash
flutter test                       # the Dart tests
flutter analyze                    # the linter, configured in analysis_options.yaml
flutter run --dart-define=EXB_BASE_URL=http://YOUR-SERVER:5080
```

And on the backend, from `EXB1/`:

```bash
dotnet test                        # 129 tests, 33 of them for the mobile API
dotnet run --project src/Exb.Web   # the exhibition system the app talks to
```
