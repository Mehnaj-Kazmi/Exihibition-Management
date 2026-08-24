/// The shapes the API returns.
///
/// These are written by hand rather than generated, because the server owns the
/// contract and there are about a dozen of them; a build_runner step would add a
/// generation dependency to every checkout for no benefit at this size.
///
/// Every parser is defensive about nulls. A field the organiser has not filled
/// in — an exhibitor with no summary, a session with no speaker — is normal, and
/// an app that throws on it would fall over on the first stand that skipped the
/// optional part of the form.
library;

/// Trims and drops empty strings, so the UI can test one thing (`!= null`)
/// rather than two everywhere.
String? _text(dynamic value) {
  if (value == null) return null;
  final text = value.toString().trim();
  return text.isEmpty ? null : text;
}

int _int(dynamic value, [int fallback = 0]) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '') ?? fallback;
}

double _double(dynamic value) {
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? 0;
}

bool _bool(dynamic value) => value == true;

List<T> _list<T>(dynamic value, T Function(Map<String, dynamic>) parse) {
  if (value is! List) return const [];
  return value.whereType<Map<String, dynamic>>().map(parse).toList();
}

/// `DateOnly` arrives as `2026-08-17`.
DateTime _date(dynamic value) =>
    DateTime.tryParse(value?.toString() ?? '') ?? DateTime.now();

/// `TimeOnly` arrives as `11:00:00`. Kept as minutes since midnight, which is
/// all the app ever does with it — display and ordering.
int _minutes(dynamic value) {
  final parts = (value?.toString() ?? '').split(':');
  if (parts.length < 2) return 0;
  return (int.tryParse(parts[0]) ?? 0) * 60 + (int.tryParse(parts[1]) ?? 0);
}

String formatMinutes(int minutes) {
  final h = (minutes ~/ 60).toString().padLeft(2, '0');
  final m = (minutes % 60).toString().padLeft(2, '0');
  return '$h:$m';
}

// --- who is signed in --------------------------------------------------------

class Visitor {
  const Visitor({
    required this.visitorId,
    required this.fullName,
    required this.email,
    required this.registrationCode,
    this.company,
    this.jobTitle,
    this.country,
    required this.consentEmail,
    required this.consentTracking,
    required this.hasBadge,
  });

  final int visitorId;
  final String fullName;
  final String email;
  final String registrationCode;
  final String? company;
  final String? jobTitle;
  final String? country;
  final bool consentEmail;
  final bool consentTracking;

  /// False for someone registered but not yet issued a badge, which is why the
  /// app can show "collect your badge at the desk" rather than an empty day.
  final bool hasBadge;

  factory Visitor.fromJson(Map<String, dynamic> json) => Visitor(
        visitorId: _int(json['visitorId']),
        fullName: _text(json['fullName']) ?? 'Visitor',
        email: _text(json['email']) ?? '',
        registrationCode: _text(json['registrationCode']) ?? '',
        company: _text(json['company']),
        jobTitle: _text(json['jobTitle']),
        country: _text(json['country']),
        consentEmail: _bool(json['consentEmail']),
        consentTracking: _bool(json['consentTracking']),
        hasBadge: _bool(json['hasBadge']),
      );
}

// --- the exhibition itself ---------------------------------------------------

class Exhibition {
  const Exhibition({
    required this.name,
    this.edition,
    this.venue,
    this.organiser,
    this.organiserEmail,
    required this.today,
    required this.halls,
    required this.categories,
    required this.countries,
    required this.programmeDates,
  });

  final String name;
  final String? edition;
  final String? venue;
  final String? organiser;
  final String? organiserEmail;
  final DateTime today;
  final List<Hall> halls;
  final List<Category> categories;
  final List<String> countries;
  final List<DateTime> programmeDates;

  factory Exhibition.fromJson(Map<String, dynamic> json) => Exhibition(
        name: _text(json['name']) ?? 'Exhibition',
        edition: _text(json['edition']),
        venue: _text(json['venue']),
        organiser: _text(json['organiser']),
        organiserEmail: _text(json['organiserEmail']),
        today: _date(json['today']),
        halls: _list(json['halls'], Hall.fromJson),
        categories: _list(json['categories'], Category.fromJson),
        countries: (json['countries'] as List? ?? const [])
            .map((c) => c.toString())
            .toList(),
        programmeDates:
            (json['programmeDates'] as List? ?? const []).map(_date).toList(),
      );
}

class Hall {
  const Hall({
    required this.id,
    required this.code,
    required this.name,
    required this.widthM,
    required this.depthM,
    required this.standCount,
    required this.exhibitorCount,
    this.notes,
  });

  final int id;
  final String code;
  final String name;
  final double widthM;
  final double depthM;
  final int standCount;
  final int exhibitorCount;
  final String? notes;

  factory Hall.fromJson(Map<String, dynamic> json) => Hall(
        id: _int(json['id']),
        code: _text(json['code']) ?? '',
        name: _text(json['name']) ?? '',
        widthM: _double(json['widthM']),
        depthM: _double(json['depthM']),
        standCount: _int(json['standCount']),
        exhibitorCount: _int(json['exhibitorCount']),
        notes: _text(json['notes']),
      );
}

class HallDetail {
  const HallDetail({
    required this.hall,
    required this.exhibitors,
    required this.sessionCount,
  });

  final Hall hall;
  final List<Exhibitor> exhibitors;
  final int sessionCount;

  factory HallDetail.fromJson(Map<String, dynamic> json) => HallDetail(
        hall: Hall.fromJson(
            (json['summary'] as Map<String, dynamic>?) ?? const {}),
        exhibitors: _list(json['exhibitors'], Exhibitor.fromJson),
        sessionCount: _int(json['sessionCount']),
      );
}

/// A node of the two-level taxonomy. Top-level categories carry their
/// sub-categories in [children]; sub-categories have none.
class Category {
  const Category({
    required this.id,
    required this.code,
    required this.name,
    this.colour,
    this.description,
    required this.exhibitorCount,
    required this.children,
  });

  final int id;
  final String code;
  final String name;
  final String? colour;
  final String? description;
  final int exhibitorCount;
  final List<Category> children;

  factory Category.fromJson(Map<String, dynamic> json) => Category(
        id: _int(json['id']),
        code: _text(json['code']) ?? '',
        name: _text(json['name']) ?? '',
        colour: _text(json['colour']),
        description: _text(json['description']),
        exhibitorCount: _int(json['exhibitorCount']),
        children: _list(json['children'], Category.fromJson),
      );
}

// --- exhibitors and stands ---------------------------------------------------

class Stand {
  const Stand({
    required this.kioskId,
    required this.standNumber,
    required this.hallId,
    required this.hallCode,
    required this.hallName,
  });

  final int kioskId;
  final String standNumber;
  final int hallId;
  final String hallCode;
  final String hallName;

  factory Stand.fromJson(Map<String, dynamic> json) => Stand(
        kioskId: _int(json['kioskId']),
        standNumber: _text(json['standNumber']) ?? '',
        hallId: _int(json['hallId']),
        hallCode: _text(json['hallCode']) ?? '',
        hallName: _text(json['hallName']) ?? '',
      );
}

class Exhibitor {
  const Exhibitor({
    required this.id,
    required this.code,
    required this.companyName,
    this.country,
    this.summary,
    this.categoryId,
    this.categoryName,
    this.subCategoryId,
    this.subCategoryName,
    required this.stands,
    required this.catalogueCount,
  });

  final int id;
  final String code;
  final String companyName;
  final String? country;
  final String? summary;
  final int? categoryId;
  final String? categoryName;
  final int? subCategoryId;
  final String? subCategoryName;
  final List<Stand> stands;
  final int catalogueCount;

  /// "Hall 1 · H1-014, H1-015" — what a visitor needs to walk there.
  String get location {
    if (stands.isEmpty) return 'Stand not yet allocated';
    final halls = stands.map((s) => s.hallName).toSet().join(', ');
    final numbers = stands.map((s) => s.standNumber).join(', ');
    return '$halls · $numbers';
  }

  factory Exhibitor.fromJson(Map<String, dynamic> json) => Exhibitor(
        id: _int(json['id']),
        code: _text(json['code']) ?? '',
        companyName: _text(json['companyName']) ?? '',
        country: _text(json['country']),
        summary: _text(json['summary']),
        categoryId: json['categoryId'] == null ? null : _int(json['categoryId']),
        categoryName: _text(json['categoryName']),
        subCategoryId:
            json['subCategoryId'] == null ? null : _int(json['subCategoryId']),
        subCategoryName: _text(json['subCategoryName']),
        stands: _list(json['stands'], Stand.fromJson),
        catalogueCount: _int(json['catalogueCount']),
      );
}

class ExhibitorDetail {
  const ExhibitorDetail({
    required this.exhibitor,
    this.contactName,
    this.email,
    this.phone,
    this.website,
    required this.sessions,
    required this.catalogueRequested,
  });

  final Exhibitor exhibitor;
  final String? contactName;
  final String? email;
  final String? phone;
  final String? website;
  final List<Session> sessions;

  /// Whether today's e-catalogue request is already on the visitor's list.
  final bool catalogueRequested;

  factory ExhibitorDetail.fromJson(Map<String, dynamic> json) =>
      ExhibitorDetail(
        exhibitor: Exhibitor.fromJson(
            (json['summary'] as Map<String, dynamic>?) ?? const {}),
        contactName: _text(json['contactName']),
        email: _text(json['email']),
        phone: _text(json['phone']),
        website: _text(json['website']),
        sessions: _list(json['sessions'], Session.fromJson),
        catalogueRequested: _bool(json['catalogueRequested']),
      );
}

// --- meetings and lectures ---------------------------------------------------

class Session {
  const Session({
    required this.id,
    required this.code,
    required this.title,
    required this.kind,
    this.speakerName,
    this.speakerTitle,
    this.speakerOrganisation,
    required this.eventDate,
    required this.startsAtMinutes,
    required this.endsAtMinutes,
    this.hallId,
    this.hallName,
    this.roomName,
    this.categoryId,
    this.categoryName,
    this.subCategoryName,
    this.exhibitorId,
    this.exhibitorName,
    required this.requiresBooking,
    required this.capacity,
    this.language,
    required this.bookmarked,
  });

  final int id;
  final String code;
  final String title;

  /// "Lecture", "Meeting", "Workshop", "Panel", "Demo" or "Ceremony".
  final String kind;

  final String? speakerName;
  final String? speakerTitle;
  final String? speakerOrganisation;
  final DateTime eventDate;
  final int startsAtMinutes;
  final int endsAtMinutes;
  final int? hallId;
  final String? hallName;
  final String? roomName;
  final int? categoryId;
  final String? categoryName;
  final String? subCategoryName;
  final int? exhibitorId;
  final String? exhibitorName;
  final bool requiresBooking;
  final int capacity;
  final String? language;

  /// Saved to this visitor's agenda. Not a reservation, and the app says so.
  final bool bookmarked;

  String get timeRange =>
      '${formatMinutes(startsAtMinutes)}–${formatMinutes(endsAtMinutes)}';

  int get durationMinutes => endsAtMinutes - startsAtMinutes;

  /// Where to go. Falls back through room, hall, then an honest blank.
  String get where {
    if (roomName != null && hallName != null) return '$roomName · $hallName';
    return roomName ?? hallName ?? 'Location to be confirmed';
  }

  Session copyWith({bool? bookmarked}) => Session(
        id: id,
        code: code,
        title: title,
        kind: kind,
        speakerName: speakerName,
        speakerTitle: speakerTitle,
        speakerOrganisation: speakerOrganisation,
        eventDate: eventDate,
        startsAtMinutes: startsAtMinutes,
        endsAtMinutes: endsAtMinutes,
        hallId: hallId,
        hallName: hallName,
        roomName: roomName,
        categoryId: categoryId,
        categoryName: categoryName,
        subCategoryName: subCategoryName,
        exhibitorId: exhibitorId,
        exhibitorName: exhibitorName,
        requiresBooking: requiresBooking,
        capacity: capacity,
        language: language,
        bookmarked: bookmarked ?? this.bookmarked,
      );

  factory Session.fromJson(Map<String, dynamic> json) => Session(
        id: _int(json['id']),
        code: _text(json['code']) ?? '',
        title: _text(json['title']) ?? '',
        kind: _text(json['kind']) ?? 'Lecture',
        speakerName: _text(json['speakerName']),
        speakerTitle: _text(json['speakerTitle']),
        speakerOrganisation: _text(json['speakerOrganisation']),
        eventDate: _date(json['eventDate']),
        startsAtMinutes: _minutes(json['startsAt']),
        endsAtMinutes: _minutes(json['endsAt']),
        hallId: json['hallId'] == null ? null : _int(json['hallId']),
        hallName: _text(json['hallName']),
        roomName: _text(json['roomName']),
        categoryId: json['categoryId'] == null ? null : _int(json['categoryId']),
        categoryName: _text(json['categoryName']),
        subCategoryName: _text(json['subCategoryName']),
        exhibitorId:
            json['exhibitorId'] == null ? null : _int(json['exhibitorId']),
        exhibitorName: _text(json['exhibitorName']),
        requiresBooking: _bool(json['requiresBooking']),
        capacity: _int(json['capacity']),
        language: _text(json['language']),
        bookmarked: _bool(json['bookmarked']),
      );
}

class SessionDetail {
  const SessionDetail({required this.session, this.abstractText});

  final Session session;

  /// Named around Dart's reserved `abstract`; the JSON field is `abstract`.
  final String? abstractText;

  factory SessionDetail.fromJson(Map<String, dynamic> json) => SessionDetail(
        session: Session.fromJson(
            (json['summary'] as Map<String, dynamic>?) ?? const {}),
        abstractText: _text(json['abstract']),
      );
}

// --- e-catalogue requests ----------------------------------------------------

/// A stand as the scanner resolves it, and as it appears on the visitor's list.
class ScannedStand {
  const ScannedStand({
    required this.kioskId,
    required this.standNumber,
    required this.exhibitorId,
    required this.exhibitorName,
    required this.hallName,
    this.categoryName,
    this.subCategoryName,
    this.summary,
    this.website,
    required this.catalogueFileCount,
  });

  final int kioskId;
  final String standNumber;
  final int exhibitorId;
  final String exhibitorName;
  final String hallName;
  final String? categoryName;
  final String? subCategoryName;
  final String? summary;
  final String? website;
  final int catalogueFileCount;

  factory ScannedStand.fromJson(Map<String, dynamic> json) => ScannedStand(
        kioskId: _int(json['kioskId']),
        standNumber: _text(json['standNumber']) ?? '',
        exhibitorId: _int(json['exhibitorId']),
        exhibitorName: _text(json['exhibitorName']) ?? '',
        hallName: _text(json['hallName']) ?? '',
        categoryName: _text(json['categoryName']),
        subCategoryName: _text(json['subCategoryName']),
        summary: _text(json['summary']),
        website: _text(json['website']),
        catalogueFileCount: _int(json['catalogueFileCount']),
      );
}

enum ScanOutcome { added, alreadyRequested }

class ScanResult {
  const ScanResult({
    required this.outcome,
    required this.message,
    required this.stand,
    required this.todayCount,
  });

  final ScanOutcome outcome;
  final String message;
  final ScannedStand stand;

  /// How many catalogues are on tonight's pack after this scan.
  final int todayCount;

  factory ScanResult.fromJson(Map<String, dynamic> json) => ScanResult(
        outcome: _text(json['outcome']) == 'alreadyRequested'
            ? ScanOutcome.alreadyRequested
            : ScanOutcome.added,
        message: _text(json['message']) ?? 'Added to your list.',
        stand: ScannedStand.fromJson(
            (json['stand'] as Map<String, dynamic>?) ?? const {}),
        todayCount: _int(json['todayCount']),
      );
}

// --- the visitor's own day ---------------------------------------------------

class VisitedStand {
  const VisitedStand({
    required this.exhibitorId,
    required this.exhibitorName,
    required this.location,
    this.categoryName,
    required this.dwellText,
    required this.levelText,
    required this.catalogueRequested,
  });

  final int exhibitorId;
  final String exhibitorName;
  final String location;
  final String? categoryName;
  final String dwellText;
  final String levelText;
  final bool catalogueRequested;

  factory VisitedStand.fromJson(Map<String, dynamic> json) => VisitedStand(
        exhibitorId: _int(json['exhibitorId']),
        exhibitorName: _text(json['exhibitorName']) ?? '',
        location: _text(json['location']) ?? '',
        categoryName: _text(json['categoryName']),
        dwellText: _text(json['dwellText']) ?? '',
        levelText: _text(json['levelText']) ?? '',
        catalogueRequested: _bool(json['catalogueRequested']),
      );
}

class CategoryInterest {
  const CategoryInterest({
    required this.categoryName,
    required this.dwellText,
    required this.standCount,
    required this.sharePct,
  });

  final String categoryName;
  final String dwellText;
  final int standCount;
  final double sharePct;

  factory CategoryInterest.fromJson(Map<String, dynamic> json) =>
      CategoryInterest(
        categoryName: _text(json['categoryName']) ?? '',
        dwellText: _text(json['dwellText']) ?? '',
        standCount: _int(json['standCount']),
        sharePct: _double(json['sharePct']),
      );
}

class MissedStand {
  const MissedStand({
    required this.exhibitorId,
    required this.exhibitorName,
    required this.location,
    this.categoryName,
    this.website,
    required this.reason,
  });

  final int exhibitorId;
  final String exhibitorName;
  final String location;
  final String? categoryName;
  final String? website;
  final String reason;

  factory MissedStand.fromJson(Map<String, dynamic> json) => MissedStand(
        exhibitorId: _int(json['exhibitorId']),
        exhibitorName: _text(json['exhibitorName']) ?? '',
        location: _text(json['location']) ?? '',
        categoryName: _text(json['categoryName']),
        website: _text(json['website']),
        reason: _text(json['reason']) ?? '',
      );
}

/// The day as the evening report will describe it.
///
/// [trackingConsent] false is not an error and not an empty day — it means the
/// visitor turned stand tracking off, and the app must say that rather than
/// implying they walked past everything.
class VisitorDay {
  const VisitorDay({
    required this.trackingConsent,
    this.message,
    this.totalDwellText,
    this.standsWithInterest,
    this.visited = const [],
    this.categories = const [],
    this.missed = const [],
  });

  final bool trackingConsent;
  final String? message;
  final String? totalDwellText;
  final int? standsWithInterest;
  final List<VisitedStand> visited;
  final List<CategoryInterest> categories;
  final List<MissedStand> missed;

  factory VisitorDay.fromJson(Map<String, dynamic> json) {
    if (!_bool(json['trackingConsent'])) {
      return VisitorDay(
        trackingConsent: false,
        message: _text(json['message']),
      );
    }

    final day = (json['day'] as Map<String, dynamic>?) ?? const {};
    return VisitorDay(
      trackingConsent: true,
      totalDwellText: _text(day['totalDwellText']),
      standsWithInterest: _int(day['standsWithInterest']),
      visited: _list(day['visited'], VisitedStand.fromJson),
      categories: _list(day['categories'], CategoryInterest.fromJson),
      missed: _list(day['missed'], MissedStand.fromJson),
    );
  }
}

// --- paging and unified search ----------------------------------------------

class Paged<T> {
  const Paged({
    required this.items,
    required this.total,
    required this.pageNumber,
    required this.pageSize,
    required this.hasMore,
  });

  final List<T> items;
  final int total;
  final int pageNumber;
  final int pageSize;
  final bool hasMore;

  static Paged<T> fromJson<T>(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) parse,
  ) =>
      Paged<T>(
        items: _list(json['items'], parse),
        total: _int(json['total']),
        pageNumber: _int(json['pageNumber'], 1),
        pageSize: _int(json['pageSize'], 25),
        hasMore: _bool(json['hasMore']),
      );

  static Paged<T> empty<T>() => Paged<T>(
        items: const [],
        total: 0,
        pageNumber: 1,
        pageSize: 25,
        hasMore: false,
      );
}

class SearchResults {
  const SearchResults({
    required this.exhibitors,
    required this.sessions,
    required this.categories,
    required this.halls,
    required this.exhibitorTotal,
    required this.sessionTotal,
  });

  final List<Exhibitor> exhibitors;
  final List<Session> sessions;
  final List<Category> categories;
  final List<Hall> halls;
  final int exhibitorTotal;
  final int sessionTotal;

  bool get isEmpty =>
      exhibitors.isEmpty &&
      sessions.isEmpty &&
      categories.isEmpty &&
      halls.isEmpty;

  factory SearchResults.fromJson(Map<String, dynamic> json) => SearchResults(
        exhibitors: _list(json['exhibitors'], Exhibitor.fromJson),
        sessions: _list(json['sessions'], Session.fromJson),
        categories: _list(json['categories'], Category.fromJson),
        halls: _list(json['halls'], Hall.fromJson),
        exhibitorTotal: _int(json['exhibitorTotal']),
        sessionTotal: _int(json['sessionTotal']),
      );
}

/// What the sign-in screen learns from asking for a code.
class LoginCodeRequest {
  const LoginCodeRequest({
    required this.message,
    required this.expiresInSeconds,
    this.developmentCode,
  });

  final String message;
  final int expiresInSeconds;

  /// Non-null only when the server is not actually sending mail, so that a
  /// system on its default settings can still be signed in to for testing.
  final String? developmentCode;

  factory LoginCodeRequest.fromJson(Map<String, dynamic> json) =>
      LoginCodeRequest(
        message: _text(json['message']) ?? 'Check your email for the code.',
        expiresInSeconds: _int(json['expiresInSeconds'], 900),
        developmentCode: _text(json['developmentCode']),
      );
}
