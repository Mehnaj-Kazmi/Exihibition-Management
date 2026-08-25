# Getting the app onto an iPhone

## Why there is no `.ipa` in this repository

Apple permits iOS apps to be compiled **only on macOS**. No Windows PC can
produce an iPhone build — this is Apple's restriction, not a gap in this
project. The iPhone code is complete and included; it just needs a Mac to
compile, which is what the GitHub workflow in this folder provides for free.

*(Also worth knowing: an iPhone install file is a `.ipa`, not an `.apk`.
`.apk` is Android-only.)*

---

## Free route — test on your own iPhone, no Mac, no payment

This works today and costs nothing.

### Step 1 — Get the `.ipa` built

1. Go to the repository on GitHub → **Actions** tab
2. Click **Build iOS app** → **Run workflow** → **Run workflow**
3. Wait ~5–10 minutes for the green tick
4. Open the finished run → scroll to **Artifacts** → download
   **`ExhibitionCompanion-iOS-unsigned-ipa`**
5. Unzip it — inside is `ExhibitionCompanion.ipa`

### Step 2 — Install it on your iPhone from Windows

The `.ipa` is unsigned, so it needs re-signing with your own Apple ID.
**Sideloadly** does this on Windows, using a **free** Apple ID:

1. Download Sideloadly: **https://sideloadly.io**
2. Install it, plug your iPhone into the PC with a cable, unlock the phone
   and tap **Trust This Computer**
3. Drag `ExhibitionCompanion.ipa` into Sideloadly
4. Enter your Apple ID (a free one is fine — no Developer Program needed)
5. Click **Start**
6. On the iPhone: **Settings → General → VPN & Device Management** → tap your
   Apple ID → **Trust**
7. The app appears on your home screen. Open it.

### Step 3 — Point it at the exhibition system

On the app's login screen, tap **Change** at the bottom and enter your PC's
address, e.g. `http://192.168.1.102:5080`. Phone and PC must be on the same
wifi, and the server must be started with:

```
dotnet run --project src/Exb.Web --urls http://0.0.0.0:5080
```

### The catch with the free route

A free Apple ID signature **expires after 7 days** — after that the app stops
opening until you re-run Sideloadly. Fine for testing; not usable for real
visitors at a show.

---

## Paid route — for the actual exhibition

For visitors to install it from the App Store (or TestFlight), the company
needs the **Apple Developer Program, $99/year**:
https://developer.apple.com/programs/

With that account, signatures last a year and the app can be distributed
normally. The workflow in this folder can then be extended to sign the `.ipa`
during the build instead of leaving it unsigned.

---

## What about a macOS virtual machine?

Apple's licence permits running macOS in a VM **only on Apple hardware**, so a
macOS VM on a Windows PC breaches that licence and cannot be used for anything
the company ships. The GitHub route above is the legitimate equivalent and is
free for this public repository.

Legitimate paid alternatives if an interactive Mac is ever needed:

| Option | Roughly | Notes |
|---|---|---|
| MacinCloud | ~$25–30/month | Rent a real Mac by remote desktop |
| MacStadium | ~$100+/month | Dedicated Mac hardware |
| Mac mini | ~$599 once | Cheapest long term if iOS work continues |

---

## Links

- Sideloadly (free sideloading from Windows): https://sideloadly.io
- Apple Developer Program ($99/yr): https://developer.apple.com/programs/
- GitHub-hosted macOS runners: https://docs.github.com/en/actions/using-github-hosted-runners/using-github-hosted-runners/about-github-hosted-runners
- .NET MAUI iOS publishing: https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/
