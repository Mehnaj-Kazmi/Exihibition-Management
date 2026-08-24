import 'dart:convert';

import 'package:exhibition_companion/api/api_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

/// The client's behaviour around the network, which is the part that decides
/// whether a visitor sees something they can act on or a stack trace.
void main() {
  ApiClient clientReturning(
    Object? body, {
    int status = 200,
    void Function(http.Request request)? inspect,
  }) {
    final mock = MockClient((request) async {
      inspect?.call(request);
      return http.Response(
        body == null ? '' : jsonEncode(body),
        status,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });

    return ApiClient(baseUrl: 'https://exhibition.example', httpClient: mock);
  }

  test('trailing slashes on the configured address do not double up', () {
    final client = ApiClient(baseUrl: 'https://exhibition.example///');
    expect(client.baseUrl, 'https://exhibition.example');
  });

  test('sends the bearer token once signed in, and not before', () async {
    final seen = <String?>[];
    final client = clientReturning(
      {'visitorId': 1, 'fullName': 'Sara Khan', 'email': 'sara@example.com'},
      inspect: (request) => seen.add(request.headers['Authorization']),
    );

    await client.me();
    client.token = 'TOKEN123';
    await client.me();

    expect(seen.first, isNull);
    expect(seen.last, 'Bearer TOKEN123');
  });

  test('empty and null filters are left off the query string entirely', () async {
    Uri? sent;
    final client = clientReturning(
      {'items': [], 'total': 0},
      inspect: (request) => sent = request.url,
    );

    await client.exhibitors(query: '', categoryId: null, hallId: 3, page: 2);

    expect(sent!.queryParameters.containsKey('q'), isFalse);
    expect(sent!.queryParameters.containsKey('categoryId'), isFalse);
    expect(sent!.queryParameters['hallId'], '3');
    expect(sent!.queryParameters['page'], '2');
  });

  test('dates are sent in the form the server parses as DateOnly', () async {
    Uri? sent;
    final client = clientReturning(
      {'items': [], 'total': 0},
      inspect: (request) => sent = request.url,
    );

    await client.sessions(date: DateTime(2026, 8, 7));

    expect(sent!.queryParameters['date'], '2026-08-07');
  });

  test("the server's own wording is what the visitor is shown", () async {
    final client = clientReturning(
      {'error': 'That code has expired. Ask for a new one.'},
      status: 401,
    );

    await expectLater(
      client.me(),
      throwsA(isA<ApiException>()
          .having((e) => e.message, 'message', contains('expired'))
          .having((e) => e.isUnauthorised, 'isUnauthorised', isTrue)),
    );
  });

  test('a status with no body still produces something actionable', () async {
    final client = clientReturning(null, status: 500);

    await expectLater(
      client.me(),
      throwsA(isA<ApiException>()
          .having((e) => e.message, 'message', contains('Try again'))),
    );
  });

  test('a rejected token fires the callback that signs the app out', () async {
    var signedOut = false;
    final client = clientReturning({'error': 'nope'}, status: 401)
      ..onUnauthorised = () => signedOut = true;

    await expectLater(client.me(), throwsA(isA<ApiException>()));
    expect(signedOut, isTrue);
  });

  test('a 404 does not sign anybody out', () async {
    var signedOut = false;
    final client = clientReturning({'error': 'gone'}, status: 404)
      ..onUnauthorised = () => signedOut = true;

    await expectLater(client.exhibitor(9), throwsA(isA<ApiException>()));
    expect(signedOut, isFalse);
  });

  test('verifying a code sets the token on the client itself', () async {
    final client = clientReturning({
      'token': 'NEWTOKEN',
      'expiresUtc': '2026-09-16T00:00:00Z',
      'visitor': {
        'visitorId': 4,
        'fullName': 'Sara Khan',
        'email': 'sara@example.com',
        'consentEmail': true,
        'consentTracking': true,
        'hasBadge': true,
      },
    });

    expect(client.hasToken, isFalse);

    final result =
        await client.verifyLoginCode(email: 'sara@example.com', code: '123456');

    expect(result.token, 'NEWTOKEN');
    expect(result.visitor.fullName, 'Sara Khan');
    expect(client.hasToken, isTrue);
  });

  test('a verify response without a token is an error, not a silent sign-in',
      () async {
    final client = clientReturning({'visitor': {}});

    await expectLater(
      client.verifyLoginCode(email: 'sara@example.com', code: '123456'),
      throwsA(isA<ApiException>()),
    );
    expect(client.hasToken, isFalse);
  });

  test('signing out locally succeeds even when the server cannot be reached',
      () async {
    final client = clientReturning({'error': 'boom'}, status: 500)
      ..token = 'TOKEN';

    await client.logout();

    expect(client.hasToken, isFalse);
  });

  test('the scanned value is posted as read, for the server to unpack',
      () async {
    String? body;
    final client = clientReturning(
      {
        'outcome': 'added',
        'message': 'Added.',
        'stand': {'kioskId': 1, 'exhibitorId': 2, 'exhibitorName': 'X'},
        'todayCount': 1,
      },
      inspect: (request) => body = request.body,
    );

    await client.scan('https://exhibition.example/s/ABC123');

    expect(
      jsonDecode(body!)['token'],
      'https://exhibition.example/s/ABC123',
    );
  });

  test('removing a bookmark uses DELETE and adding one uses POST', () async {
    final methods = <String>[];
    final client = clientReturning(
      {'bookmarked': true},
      inspect: (request) => methods.add(request.method),
    );

    await client.setBookmarked(5, true);
    await client.setBookmarked(5, false);

    expect(methods, ['POST', 'DELETE']);
  });
}
