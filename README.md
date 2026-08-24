# Exhibition Visitor Interest Tracker

RFID visitor tracking for a multi-hall exhibition centre. Every visitor badge
carries a passive UHF tag; every exhibitor stand carries its own antennas. From
that, the system works out which stands each visitor genuinely stopped at, what
categories they care about, collects the e-catalogues they scan during the day,
and emails each of them one pack that evening — with a table of the stands in
their own categories they never reached.

.NET 8 · ASP.NET Core Razor Pages · EF Core on SQL Server.

---

## What it does

**Tracks interest, not footfall.** A badge that pauses at a stand for 45 seconds
is interest; six seconds is someone walking past. Both are recorded, and only one
is reported as a lead.

**Puts the sensing on the exhibitors, not the building.** Antennas are issued to
each stand in proportion to its floor area — a 9 m² shell scheme gets one, a
72 m² island gets six — mounted on the stand itself. That is how the hardware is
really quoted and installed, and it means sensing density follows the stands
automatically as the floor plan changes.

**Collects e-catalogues by QR.** Each stand's printed code resolves to this
system, which identifies the visitor from their badge and adds that exhibitor to
their pack. At close of play the day's catalogues are zipped, uploaded, and
linked from one email.

**Tells visitors what they missed.** The part that earns its keep: for each
category a visitor spent real time in, the evening report lists the stands in that
category their badge never reached, with stand number, hall, zone and website.

**Lets the organiser rearrange the forms.** The visitor registration form and the
exhibitor profile form are built in the admin console — add fields, relabel them,
reorder them, move them between sections, switch them off — and each exhibition
can have its own layout, versioned so a bad edit can be rolled back in one click.

**Puts the whole show in the visitor's pocket.** An Android and iOS app —
`MOBILE APP/`, one Flutter codebase — where a visitor signs in with the email
address they registered with, searches exhibitors, categories, sub-categories,
halls and the meetings-and-lectures programme, and scans stand QR codes straight
into their evening pack. It talks to the versioned API at `/api/v1`.

---

## Running it

You need .NET 8 and a SQL Server instance.

```bash
dotnet run --project src/Exb.Web
```

Then open <http://localhost:5080>. On first run the application creates its
schema, writes default settings and form layouts, and — against a genuinely
empty database — seeds a demonstration exhibition of 3 halls, 8 categories, a few
hundred exhibitors on real stand rows, and 140 registered visitors, so every
screen has something in it.

Set the connection string in `src/Exb.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "ExhibitionDb": "Server=YOURSERVER;Database=ExhibitionTracker;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true"
}
```

Turn the demo data off before entering real exhibitors: `"Seed:DemoExhibition": false`.

### Deploying the schema

Migrations run automatically at startup. To deploy by hand instead — which is
what a DBA will want — the idempotent script is checked in:

```bash
sqlcmd -S YOURSERVER -d ExhibitionTracker -i src/Exb.Data/Sql/schema.sql
```

Regenerate it after a model change:

```bash
dotnet ef migrations script --idempotent --project src/Exb.Data --startup-project src/Exb.Data -o src/Exb.Data/Sql/schema.sql
```

---

## Two defaults that deliberately send nothing

Both are inert until you turn them on, because the alternative is a rehearsal
against real registration data that emails three hundred people by accident.

| | Default | What it does | To go live |
| --- | --- | --- | --- |
| **Email** | `outbox` | Every message is written to the Outbox table and shown in the admin console. Nothing is sent. | Settings › Delivery › mail provider `smtp` |
| **Pack delivery** | `local` | Packs are served from this system on a long random link, with an expiry. Nothing leaves the venue. | Settings › Delivery › WeTransfer or a generic endpoint |

`Settings › Delivery` also has a **redirect-all-to** address: with it set, every
generated message goes to one inbox with the intended recipient in the subject.
That is the safe way to rehearse the evening run against the real registration
list.

---

## How the tracking works

The RF model and the position solver are ported from the warehouse locator in
`../EXB`, with one substantive change: the coefficients are now resolved per
antenna mounting height rather than once globally, because stand antennas hang at
about 3.2 m and any aisle grid at about 6 m, and both can hear the same badge. A
single global height would have quietly biased every mixed fix.

**The link model.** Antennas face down, so `cos(theta) = dz/d` for a badge at
lanyard height, and the path-loss and beam terms collapse into a single
log-distance law `E(d) = A - B·log10(d)`. Inversion is then exact and closed
form, and Gauss-Newton gets a true analytic Jacobian.

**The solver.** Weighted Gauss-Newton on the RSSI residuals, seeded from a
power-weighted centroid. The fit is done in dB rather than in converted ranges,
because the measurement noise is Gaussian in dB; converting each read to a range
first would bias the answer toward the antennas that happen to read weakly. The
uncertainty circle is `sqrt(trace(covariance))` from the same solve, so it
reflects the actual geometry.

**Reads are buffered, not sampled.** A reader dwells on one antenna port at a
time, so a given antenna only revisits a badge about once per port cycle. Reads
are pooled over a 2.5 s sliding window, keeping the peak RSSI per antenna.

### From a position to an interest

A badge is attributed to the stand whose **footprint** it is nearest — not whose
centre, which matters more than it sounds: visitors stand at the open edge of a
stand, and a large island's centre can be five metres from where anyone actually
stands, so centre distance would systematically hand visitors to the small stand
across the aisle.

Attribution also reports a **margin**: how much closer the winner was than the
runner-up. On a tight row of 3 m shell schemes the honest answer is often "one of
these two", and where the mean margin is below the configured threshold the visit
is reported **one level lower** rather than guessed. Downgrading is the honest
move — discarding would throw away a real visit, and keeping it would sell an
exhibitor a lead that might be their neighbour's.

| Dwell | Level |
| --- | --- |
| under 20 s | passed by (recorded, never reported as interest) |
| 20 s | browsed |
| 45 s | interested |
| 3 min | strong interest |

All four thresholds are editable in Settings, and the daily report quotes the
values that produced it back to the visitor.

---

## What it actually delivers

Measured by the test suite, not estimated. The simulator holds the true positions
privately and emits only reads; the engine solves from those alone, and the two
are compared afterwards.

| Measure | Result |
| --- | --- |
| Mean position error | **0.53 m** |
| 95th percentile error | **1.94 m** |
| Correct hall | **100%** |
| True position inside the reported uncertainty circle | 69% |
| Badges attributed to some stand | **98.0%** |
| Attributed to the **right** stand | **96.4%** |
| …where the margin was good | 98.0% |
| …where the margin was ambiguous | 85.7% |

That last pair is the one worth reading twice. The margin genuinely predicts when
attribution is unreliable, which is what makes downgrading an ambiguous visit
engineering rather than superstition.

### Coverage, and why the default is what it is

Coverage is measured by sampling every square metre of floor against the real
antenna positions at startup, and reported separately for stand floor and whole
floor — because only the first governs whether interest data can be trusted.

Antenna density was chosen from measurement, not taste (`CoverageSweep` in the
test project reproduces this):

| m² per antenna | Antennas | Stand floor with a full fix |
| --- | --- | --- |
| 16 | 79 | 83.2% |
| 12 | 87 | 87.0% |
| **10 (default)** | **108** | **98.5%** |
| 8 | 133 | 99.5% |
| 6 | 162 | 100% |

Below 8 m² the return flattens and you are buying antennas for nothing. There is
also a free alternative if the venue is rigging a gantry anyway: at 4.0 m instead
of 3.2 m the read radius grows from 3.47 m to 4.37 m, reaching 97.7% at the
cheaper 12 m² density — about 20% fewer antennas. Only raise it if the stands can
genuinely carry it.

---

## Connecting real readers

The simulator drives the floor until any reader endpoint is configured, and is
then ignored entirely — a cabled venue never gets synthetic visitors mixed into
its live data.

1. **Settings › Readers** lists the readers the system derived from the floor
   plan, and which antennas hang off each. Enter a host against each one.
2. `src/Exb.Core/Tracking/Drivers/LlrpDriver.cs` speaks LLRP (ISO 24791-5) on TCP
   5084 — the protocol used by Impinj Speedway/R700, Zebra FX7500/FX9600 and most
   other fixed UHF readers. It handles connection management, framing,
   keepalives, reconnection, and decoding `RO_ACCESS_REPORT` into EPC + antenna +
   peak RSSI.

Two things must be tuned on site before commissioning:

- **The ROSpec template**, at the bottom of `LlrpDriver.cs`. Transmit power, Gen2
  session (S2 suits dense fixed installs) and search mode are vendor-specific.
- **The radio model**, in Settings › Tracking. `RefRssiAt1M`,
  `PathLossExponent` and `SensitivityDbm` must be calibrated against your readers
  in a *built* hall, not an empty one — stands, people and metal all raise the
  exponent well above free space, and every position downstream inherits it. This
  calibration is what turns good antenna geometry into good accuracy.

Antennas are assigned to ports in the order shown on the Readers screen. If the
cabling runs differently, recable to match — otherwise positions will be wrong in
a way that still looks plausible.

---

## Layout

```
src/Exb.Core/          domain and algorithms, no database dependency
  Tracking/            RF model, Gauss-Newton locator, engine, LLRP + simulator drivers
  Facility/            halls, stands, per-stand antenna provisioning, coverage measurement
  Dwell/               stand attribution and dwell sessions
  Interest/            category rollup and the missed-stand engine
  Forms/               the modular form schema, validation and rearrangement
  Qr/                  QR encoder (Reed-Solomon, masking) with SVG and PNG output
  Packaging/           e-catalogue pack builder
  Delivery/ Mail/      transfer providers and SMTP
  Reports/             the daily report

src/Exb.Data/          EF Core on SQL Server
  Entities/            18 tables
  Services/            settings, facility, badges, visits, interest queries, end-of-day
  Migrations/ Sql/     migrations and an idempotent deploy script
  Seed/                the demonstration exhibition

src/Exb.Web/           Razor Pages admin console, SignalR live floor, visitor pages
  Api/                 the mobile app's versioned REST surface, /api/v1
tests/Exb.Tests/       129 tests

MOBILE APP/            the visitor's Android and iOS app, in Flutter
```

### A note on the QR encoder

It is written from ISO/IEC 18004 rather than taken off the shelf, for a practical
reason: every stand needs a printable code, and the usual .NET QR libraries
render through `System.Drawing`, which is Windows-only in .NET 8 and awkward in a
container. This produces SVG for print and PNG built straight from the deflate
stream, so the web app carries no imaging dependency at all.

It is validated by decoding its own output — the test suite unmasks the matrix,
walks the zigzag, de-interleaves the blocks and checks the Reed-Solomon syndromes
across every version. That test found a real bug during development: the
generator polynomial was indexed one position off, so the ECC codewords were
wrong while the data still decoded, which would have produced codes that many
scanners reject.

---

## Privacy

- **Tracking consent is per visitor.** Without it the badge is still located for
  headcount and safety, but no visit rows are written and no interest report is
  produced.
- **Email consent is separate.** Without it no pack and no report are sent, even
  for stands the visitor scanned.
- Visitors can see their own day and remove a stand from their evening pack from
  the page behind the QR code on their badge.
- The daily report states plainly how the dwell times were measured and what the
  thresholds were, rather than presenting inferred numbers as fact.
- Stand QR tokens, pack download links and visitor page links are random tokens
  from the cryptographic RNG, and download links expire.

---

## Commands

```bash
dotnet run --project src/Exb.Web        # run it
dotnet test                             # 129 tests
dotnet test --filter CoverageSweep -v n # reproduce the antenna density table
```

The mobile app is built separately — see [MOBILE APP/README.md](MOBILE%20APP/README.md):

```bash
cd "MOBILE APP" && ./setup.ps1          # generate android/ and ios/, fetch packages
```
