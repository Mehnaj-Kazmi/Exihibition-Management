import 'package:exhibition_companion/api/models.dart';
import 'package:flutter_test/flutter_test.dart';

/// The parsers, against the shapes the server actually sends.
///
/// These matter more than they look. Every optional field in the exhibition
/// database is one an organiser is entitled to leave blank, and an app that
/// throws on the first exhibitor who skipped the summary field falls over on
/// real data while looking fine on the demo seed.
void main() {
  group('Exhibitor', () {
    test('reads a complete record', () {
      final exhibitor = Exhibitor.fromJson({
        'id': 12,
        'code': 'EX0012',
        'companyName': 'Meridian Looms',
        'country': 'Germany',
        'summary': 'Rapier and airjet looms.',
        'categoryId': 3,
        'categoryName': 'Textile Machinery',
        'subCategoryId': 7,
        'subCategoryName': 'Weaving',
        'stands': [
          {
            'kioskId': 44,
            'standNumber': 'H1-014',
            'hallId': 1,
            'hallCode': 'H1',
            'hallName': 'Hall 1',
          },
        ],
        'catalogueCount': 2,
      });

      expect(exhibitor.companyName, 'Meridian Looms');
      expect(exhibitor.stands.single.standNumber, 'H1-014');
      expect(exhibitor.location, 'Hall 1 · H1-014');
    });

    test('survives an exhibitor with nothing optional filled in', () {
      final exhibitor = Exhibitor.fromJson({
        'id': 1,
        'companyName': 'Bare Minimum Ltd',
      });

      expect(exhibitor.summary, isNull);
      expect(exhibitor.categoryId, isNull);
      expect(exhibitor.stands, isEmpty);
      expect(exhibitor.location, 'Stand not yet allocated');
    });

    test('treats an empty string as absent, so the UI tests one thing', () {
      final exhibitor = Exhibitor.fromJson({
        'id': 1,
        'companyName': 'Blank Fields Ltd',
        'summary': '   ',
        'country': '',
      });

      expect(exhibitor.summary, isNull);
      expect(exhibitor.country, isNull);
    });

    test('names several stands across halls in one line', () {
      final exhibitor = Exhibitor.fromJson({
        'id': 2,
        'companyName': 'Two Halls Ltd',
        'stands': [
          {'kioskId': 1, 'standNumber': 'H1-001', 'hallId': 1, 'hallName': 'Hall 1'},
          {'kioskId': 2, 'standNumber': 'H2-050', 'hallId': 2, 'hallName': 'Hall 2'},
        ],
      });

      expect(exhibitor.location, 'Hall 1, Hall 2 · H1-001, H2-050');
    });
  });

  group('Session', () {
    Map<String, dynamic> json({Map<String, dynamic> overrides = const {}}) => {
          'id': 5,
          'code': 'S0005',
          'title': 'The state of weaving in 2026',
          'kind': 'Lecture',
          'speakerName': 'Imran Malik',
          'eventDate': '2026-08-17',
          'startsAt': '14:30:00',
          'endsAt': '15:15:00',
          'hallId': 1,
          'hallName': 'Hall 1',
          'roomName': 'Main Theatre',
          'requiresBooking': false,
          'capacity': 120,
          'bookmarked': true,
          ...overrides,
        };

    test('reads the times the server sends as TimeOnly', () {
      final session = Session.fromJson(json());

      expect(session.startsAtMinutes, 14 * 60 + 30);
      expect(session.timeRange, '14:30–15:15');
      expect(session.durationMinutes, 45);
      expect(session.eventDate, DateTime(2026, 8, 17));
      expect(session.bookmarked, isTrue);
    });

    test('names the room and the hall together when it has both', () {
      expect(Session.fromJson(json()).where, 'Main Theatre · Hall 1');
    });

    test('falls back through room, then hall, then an honest blank', () {
      expect(
        Session.fromJson(json(overrides: {'hallId': null, 'hallName': null})).where,
        'Main Theatre',
      );
      expect(
        Session.fromJson(json(overrides: {'roomName': null})).where,
        'Hall 1',
      );
      expect(
        Session.fromJson(
          json(overrides: {'roomName': null, 'hallId': null, 'hallName': null}),
        ).where,
        'Location to be confirmed',
      );
    });

    test('copyWith changes only the bookmark, for the optimistic toggle', () {
      final session = Session.fromJson(json());
      final flipped = session.copyWith(bookmarked: false);

      expect(flipped.bookmarked, isFalse);
      expect(flipped.title, session.title);
      expect(flipped.startsAtMinutes, session.startsAtMinutes);
    });

    test('reads the abstract, whose JSON name is a Dart keyword', () {
      final detail = SessionDetail.fromJson({
        'summary': json(),
        'abstract': 'Forty-five minutes on what buyers are specifying.',
      });

      expect(detail.abstractText, startsWith('Forty-five minutes'));
      expect(detail.session.title, contains('weaving'));
    });
  });

  group('VisitorDay', () {
    test('a visitor who opted out of tracking is not an empty day', () {
      final day = VisitorDay.fromJson({
        'eventDate': '2026-08-17',
        'trackingConsent': false,
        'message': 'Stand tracking is switched off for your badge.',
      });

      expect(day.trackingConsent, isFalse);
      expect(day.message, isNotNull);
      expect(day.visited, isEmpty);
    });

    test('reads the interest report as the evening email will describe it', () {
      final day = VisitorDay.fromJson({
        'eventDate': '2026-08-17',
        'trackingConsent': true,
        'day': {
          'totalDwellSeconds': 1450,
          'totalDwellText': '24 min 10 s',
          'standsWithInterest': 3,
          'passedBy': 11,
          'visited': [
            {
              'exhibitorId': 12,
              'exhibitorName': 'Meridian Looms',
              'location': 'Hall 1 · Stand H1-014 · Zone B',
              'dwellText': '7 min',
              'levelText': 'Strong interest',
              'catalogueRequested': true,
            },
          ],
          'categories': [
            {
              'categoryName': 'Textile Machinery',
              'dwellText': '18 min',
              'standCount': 4,
              'sharePct': 62.5,
            },
          ],
          'missed': [
            {
              'exhibitorId': 30,
              'exhibitorName': 'Nordwind Weaving',
              'location': 'Hall 2 · Stand H2-001 · Zone A',
              'reason': 'Weaving, which you spent 18 min on',
            },
          ],
        },
      });

      expect(day.trackingConsent, isTrue);
      expect(day.totalDwellText, '24 min 10 s');
      expect(day.visited.single.levelText, 'Strong interest');
      expect(day.categories.single.sharePct, 62.5);
      expect(day.missed.single.exhibitorName, 'Nordwind Weaving');
    });
  });

  group('ScanResult', () {
    test('distinguishes a new scan from one already on the list', () {
      Map<String, dynamic> body(String outcome) => {
            'outcome': outcome,
            'message': 'Meridian Looms added.',
            'stand': {
              'kioskId': 44,
              'standNumber': 'H1-014',
              'exhibitorId': 12,
              'exhibitorName': 'Meridian Looms',
              'hallName': 'Hall 1',
              'catalogueFileCount': 2,
            },
            'todayCount': 6,
          };

      expect(ScanResult.fromJson(body('added')).outcome, ScanOutcome.added);
      expect(
        ScanResult.fromJson(body('alreadyRequested')).outcome,
        ScanOutcome.alreadyRequested,
      );
      expect(ScanResult.fromJson(body('added')).todayCount, 6);
    });
  });

  group('Paged', () {
    test('reads a page of results', () {
      final page = Paged.fromJson<Exhibitor>({
        'items': [
          {'id': 1, 'companyName': 'One'},
          {'id': 2, 'companyName': 'Two'},
        ],
        'total': 57,
        'pageNumber': 1,
        'pageSize': 25,
        'hasMore': true,
      }, Exhibitor.fromJson);

      expect(page.items, hasLength(2));
      expect(page.total, 57);
      expect(page.hasMore, isTrue);
    });

    test('an absent items array is an empty page, not a crash', () {
      final page = Paged.fromJson<Exhibitor>({'total': 0}, Exhibitor.fromJson);
      expect(page.items, isEmpty);
      expect(page.pageNumber, 1);
    });
  });

  group('Category', () {
    test('reads the two-level tree with its live counts', () {
      final category = Category.fromJson({
        'id': 1,
        'code': 'TEX',
        'name': 'Textile Machinery',
        'colour': '#2f7ed8',
        'exhibitorCount': 41,
        'children': [
          {'id': 2, 'code': 'TEX-1', 'name': 'Weaving', 'exhibitorCount': 12},
        ],
      });

      expect(category.exhibitorCount, 41);
      expect(category.children.single.name, 'Weaving');
      expect(category.children.single.children, isEmpty);
    });
  });

  group('formatMinutes', () {
    test('pads to a 24-hour clock', () {
      expect(formatMinutes(0), '00:00');
      expect(formatMinutes(9 * 60 + 5), '09:05');
      expect(formatMinutes(23 * 60 + 59), '23:59');
    });
  });
}
