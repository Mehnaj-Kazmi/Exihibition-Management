import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import 'models.dart';

/// Anything the app should show the visitor rather than crash on.
///
/// The message is always something a person standing in a hall can act on —
/// "the code has expired, ask for a new one" — never a status code. The
/// server's own wording is preferred when it sends one, because it knows more
/// about why than we do.
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode, this.isNetwork = false});

  final String message;
  final int? statusCode;
  final bool isNetwork;

  /// The token is gone or revoked: the app must return to the sign-in screen.
  bool get isUnauthorised => statusCode == 401;

  @override
  String toString() => message;
}

/// The typed wrapper around the exhibition's mobile API.
///
/// One instance is shared for the life of the app. It holds the bearer token
/// and nothing else — no caching, because a visitor standing at a stand needs
/// the truth about that stand, not a copy from twenty minutes ago, and the
/// payloads are small enough that refetching costs less than getting it wrong.
class ApiClient {
  ApiClient({required String baseUrl, http.Client? httpClient})
      : _baseUrl = _normalise(baseUrl),
        _http = httpClient ?? http.Client();

  final String _baseUrl;
  final http.Client _http;

  String? _token;

  /// Fired when the server rejects our token, so the app can sign out cleanly
  /// from wherever it happens to be rather than each screen handling it.
  void Function()? onUnauthorised;

  String get baseUrl => _baseUrl;
  bool get hasToken => _token != null;

  set token(String? value) => _token = value;

  static String _normalise(String url) {
    var trimmed = url.trim();
    while (trimmed.endsWith('/')) {
      trimmed = trimmed.substring(0, trimmed.length - 1);
    }
    return trimmed;
  }

  /// Conference wifi is the worst network most apps ever meet, so the timeout
  /// is generous enough to survive a slow hall and short enough that a dead
  /// connection is reported rather than spun on.
  static const Duration _timeout = Duration(seconds: 20);

  Map<String, String> get _headers => {
        'Accept': 'application/json',
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
      };

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final cleaned = <String, String>{};
    query?.forEach((key, value) {
      if (value == null) return;
      final text = value.toString();
      if (text.isEmpty) return;
      cleaned[key] = text;
    });

    return Uri.parse('$_baseUrl/api/v1$path')
        .replace(queryParameters: cleaned.isEmpty ? null : cleaned);
  }

  Future<dynamic> _send(
    String method,
    String path, {
    Map<String, dynamic>? query,
    Object? body,
  }) async {
    final uri = _uri(path, query);

    try {
      final request = http.Request(method, uri)..headers.addAll(_headers);
      if (body != null) request.body = jsonEncode(body);

      final streamed = await _http.send(request).timeout(_timeout);
      final response = await http.Response.fromStream(streamed);

      return _decode(response);
    } on ApiException {
      rethrow;
    } on SocketException {
      throw ApiException(
        'Cannot reach the exhibition system. Check that you are on the venue '
        'wifi and try again.',
        isNetwork: true,
      );
    } on TimeoutException {
      throw ApiException(
        'The exhibition system is not responding. The network in the hall may '
        'be busy — try again in a moment.',
        isNetwork: true,
      );
    } on http.ClientException catch (e) {
      throw ApiException('Connection problem: ${e.message}', isNetwork: true);
    }
  }

  dynamic _decode(http.Response response) {
    final status = response.statusCode;

    if (status == 401) {
      // Let the app tear the session down once, centrally, rather than in every
      // screen that happens to make the unlucky request.
      onUnauthorised?.call();
    }

    dynamic parsed;
    if (response.body.isNotEmpty) {
      try {
        parsed = jsonDecode(utf8.decode(response.bodyBytes));
      } on FormatException {
        parsed = null;
      }
    }

    if (status >= 200 && status < 300) return parsed;

    final serverSaid =
        parsed is Map<String, dynamic> ? parsed['error']?.toString() : null;

    throw ApiException(
      serverSaid ?? _fallbackMessage(status),
      statusCode: status,
    );
  }

  String _fallbackMessage(int status) => switch (status) {
        400 => 'That request was not understood by the exhibition system.',
        401 => 'Please sign in again.',
        404 => 'That is no longer available.',
        429 => 'Too many attempts. Wait a moment and try again.',
        >= 500 => 'The exhibition system had a problem. Try again shortly.',
        _ => 'Something went wrong (error $status).',
      };

  Map<String, dynamic> _object(dynamic value) =>
      value is Map<String, dynamic> ? value : const {};

  // --- signing in ----------------------------------------------------------

  Future<LoginCodeRequest> requestLoginCode(String email) async {
    final json = await _send('POST', '/auth/request-code', body: {
      'email': email,
    });
    return LoginCodeRequest.fromJson(_object(json));
  }

  /// Exchanges the emailed code for a device token, which is set on this client
  /// immediately so the caller does not have to remember to.
  Future<({String token, Visitor visitor})> verifyLoginCode({
    required String email,
    required String code,
    String? platform,
    String? deviceName,
    String? appVersion,
  }) async {
    final json = _object(await _send('POST', '/auth/verify', body: {
      'email': email,
      'code': code,
      'platform': platform,
      'deviceName': deviceName,
      'appVersion': appVersion,
    }));

    final token = json['token']?.toString();
    if (token == null || token.isEmpty) {
      throw ApiException('The exhibition system did not return a sign-in.');
    }

    _token = token;
    return (token: token, visitor: Visitor.fromJson(_object(json['visitor'])));
  }

  Future<void> logout() async {
    try {
      await _send('POST', '/auth/logout');
    } on ApiException {
      // Signing out locally must succeed even when the server cannot be
      // reached; the token expires on its own regardless.
    }
    _token = null;
  }

  Future<Visitor> me() async => Visitor.fromJson(_object(await _send('GET', '/me')));

  Future<Visitor> updateConsent({bool? email, bool? tracking}) async =>
      Visitor.fromJson(_object(await _send('PATCH', '/me/consent', body: {
        'consentEmail': email,
        'consentTracking': tracking,
      })));

  // --- the exhibition ------------------------------------------------------

  /// One call at start-up: name, halls, the whole category tree, the countries
  /// and the programme's days. Everything the filter pickers need.
  Future<Exhibition> exhibition() async =>
      Exhibition.fromJson(_object(await _send('GET', '/exhibition')));

  Future<List<Hall>> halls() async {
    final json = await _send('GET', '/halls');
    return (json as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(Hall.fromJson)
        .toList();
  }

  Future<HallDetail> hall(int id, {int page = 1, int pageSize = 50}) async =>
      HallDetail.fromJson(_object(await _send('GET', '/halls/$id', query: {
        'page': page,
        'pageSize': pageSize,
      })));

  Future<List<Category>> categories() async {
    final json = await _send('GET', '/categories');
    return (json as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(Category.fromJson)
        .toList();
  }

  // --- exhibitors ----------------------------------------------------------

  Future<Paged<Exhibitor>> exhibitors({
    String? query,
    int? categoryId,
    int? subCategoryId,
    int? hallId,
    String? country,
    int page = 1,
    int pageSize = 25,
  }) async =>
      Paged.fromJson(
        _object(await _send('GET', '/exhibitors', query: {
          'q': query,
          'categoryId': categoryId,
          'subCategoryId': subCategoryId,
          'hallId': hallId,
          'country': country,
          'page': page,
          'pageSize': pageSize,
        })),
        Exhibitor.fromJson,
      );

  Future<ExhibitorDetail> exhibitor(int id) async =>
      ExhibitorDetail.fromJson(_object(await _send('GET', '/exhibitors/$id')));

  Future<SearchResults> searchEverything(String query) async =>
      SearchResults.fromJson(
          _object(await _send('GET', '/search', query: {'q': query})));

  // --- meetings and lectures -----------------------------------------------

  Future<Paged<Session>> sessions({
    String? query,
    DateTime? date,
    String? kind,
    int? hallId,
    int? categoryId,
    int? subCategoryId,
    bool bookmarkedOnly = false,
    int page = 1,
    int pageSize = 50,
  }) async =>
      Paged.fromJson(
        _object(await _send('GET', '/sessions', query: {
          'q': query,
          'date': date == null ? null : _dateParam(date),
          'kind': kind,
          'hallId': hallId,
          'categoryId': categoryId,
          'subCategoryId': subCategoryId,
          'bookmarked': bookmarkedOnly ? 'true' : null,
          'page': page,
          'pageSize': pageSize,
        })),
        Session.fromJson,
      );

  Future<SessionDetail> session(int id) async =>
      SessionDetail.fromJson(_object(await _send('GET', '/sessions/$id')));

  Future<void> setBookmarked(int sessionId, bool bookmarked) => _send(
        bookmarked ? 'POST' : 'DELETE',
        '/sessions/$sessionId/bookmark',
      );

  Future<Paged<Session>> agenda() async =>
      Paged.fromJson(_object(await _send('GET', '/me/agenda')), Session.fromJson);

  // --- e-catalogues --------------------------------------------------------

  /// Records a scanned stand QR code against the signed-in visitor. [scanned]
  /// is whatever the camera read — the server takes the token out of it, so the
  /// app does not have to know the URL shape.
  Future<ScanResult> scan(String scanned) async =>
      ScanResult.fromJson(_object(await _send('POST', '/me/scan', body: {
        'token': scanned,
      })));

  Future<List<ScannedStand>> myCatalogues() async {
    final json = _object(await _send('GET', '/me/catalogues'));
    return (json['items'] as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(ScannedStand.fromJson)
        .toList();
  }

  /// Requests a catalogue from an exhibitor's page instead of at the stand.
  /// Goes through the same path a scan does, so doing both does not put the
  /// exhibitor in the evening pack twice.
  Future<int> requestCatalogue(int kioskId) async {
    final json = _object(await _send('POST', '/me/catalogues', body: {
      'kioskId': kioskId,
    }));
    return _int(json['todayCount']);
  }

  Future<void> setCatalogueIncluded(int kioskId, bool included) => _send(
        'PATCH',
        '/me/catalogues/$kioskId',
        body: {'included': included},
      );

  static int _int(dynamic value) =>
      value is int ? value : int.tryParse(value?.toString() ?? '') ?? 0;

  // --- the visitor's day ---------------------------------------------------

  Future<VisitorDay> myDay() async =>
      VisitorDay.fromJson(_object(await _send('GET', '/me/day')));

  static String _dateParam(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}';

  void close() => _http.close();
}
