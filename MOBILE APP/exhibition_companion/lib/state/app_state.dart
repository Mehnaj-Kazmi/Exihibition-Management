import 'dart:async';
import 'dart:io' show Platform;

// 'Category' here is Flutter's dartdoc annotation, which collides with the
// exhibition's own Category model. We never use the annotation.
import 'package:flutter/foundation.dart' hide Category;
import 'package:shared_preferences/shared_preferences.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../config.dart';

enum AuthStage {
  /// Reading the saved token from the device.
  starting,

  /// Nobody signed in.
  signedOut,

  /// Signed in and the exhibition is loaded.
  signedIn,
}

/// The one piece of shared state in the app: who is signed in, which exhibition
/// system we are talking to, and the reference data every screen filters by.
///
/// Screens own their own lists — a search result belongs to the search screen —
/// so this stays small. What lives here is only what more than one screen needs
/// and what must survive navigation: the session, the category tree and hall
/// list behind the filters, and the count of catalogues collected today, which
/// the scanner writes and the tab badge reads.
class AppState extends ChangeNotifier {
  AppState({ApiClient? client}) : _client = client ?? ApiClient(baseUrl: AppConfig.defaultBaseUrl) {
    _client.onUnauthorised = _onServerRejectedToken;
  }

  ApiClient _client;
  ApiClient get api => _client;

  AuthStage _stage = AuthStage.starting;
  AuthStage get stage => _stage;

  Visitor? _visitor;
  Visitor? get visitor => _visitor;

  Exhibition? _exhibition;
  Exhibition? get exhibition => _exhibition;

  String _baseUrl = AppConfig.defaultBaseUrl;
  String get baseUrl => _baseUrl;

  String? _lastEmail;
  String? get lastEmail => _lastEmail;

  /// Set when the token was rejected mid-session, so the sign-in screen can
  /// explain why the visitor is suddenly back there.
  String? _signedOutBecause;
  String? get signedOutBecause => _signedOutBecause;

  int _cataloguesToday = 0;
  int get cataloguesToday => _cataloguesToday;

  List<Hall> get halls => _exhibition?.halls ?? const [];
  List<Category> get categories => _exhibition?.categories ?? const [];
  List<String> get countries => _exhibition?.countries ?? const [];
  List<DateTime> get programmeDates => _exhibition?.programmeDates ?? const [];

  /// Sub-categories of one category, for the second filter dropdown.
  List<Category> subCategoriesOf(int? categoryId) {
    if (categoryId == null) return const [];
    for (final category in categories) {
      if (category.id == categoryId) return category.children;
    }
    return const [];
  }

  // --- start-up ------------------------------------------------------------

  /// Restores the saved session, if the token is still good.
  Future<void> restore() async {
    final prefs = await SharedPreferences.getInstance();

    _baseUrl = prefs.getString(AppConfig.baseUrlKey) ?? AppConfig.defaultBaseUrl;
    _lastEmail = prefs.getString(AppConfig.emailKey);
    _rebuildClient(_baseUrl);

    final token = prefs.getString(AppConfig.tokenKey);
    if (token == null || token.isEmpty) {
      _stage = AuthStage.signedOut;
      notifyListeners();
      return;
    }

    _client.token = token;

    try {
      _visitor = await _client.me();
      await _loadExhibition();
      _stage = AuthStage.signedIn;
    } on ApiException catch (e) {
      // A rejected token means the session is genuinely over. A network
      // failure does not — the venue's wifi being down at 08:55 must not sign
      // every visitor out, so the token is kept and the app retries.
      if (e.isUnauthorised) {
        await _clearToken();
        _stage = AuthStage.signedOut;
      } else {
        _stage = AuthStage.signedOut;
        _signedOutBecause = e.message;
      }
    }

    notifyListeners();
  }

  /// Point the app at a different exhibition system. Signs out, because a token
  /// from one server means nothing to another.
  Future<void> setBaseUrl(String url) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(AppConfig.baseUrlKey, url);
    await prefs.remove(AppConfig.tokenKey);

    _baseUrl = url;
    _rebuildClient(url);
    _visitor = null;
    _exhibition = null;
    _stage = AuthStage.signedOut;
    notifyListeners();
  }

  void _rebuildClient(String url) {
    _client.close();
    _client = ApiClient(baseUrl: url)..onUnauthorised = _onServerRejectedToken;
  }

  // --- signing in ----------------------------------------------------------

  Future<LoginCodeRequest> requestCode(String email) async {
    final result = await _client.requestLoginCode(email.trim());

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(AppConfig.emailKey, email.trim());
    _lastEmail = email.trim();

    return result;
  }

  Future<void> verifyCode(String email, String code) async {
    final result = await _client.verifyLoginCode(
      email: email.trim(),
      code: code.trim(),
      platform: _platformName,
      deviceName: _deviceLabel,
      appVersion: AppConfig.appVersion,
    );

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(AppConfig.tokenKey, result.token);

    _visitor = result.visitor;
    _signedOutBecause = null;

    await _loadExhibition();

    _stage = AuthStage.signedIn;
    notifyListeners();
  }

  Future<void> signOut() async {
    await _client.logout();
    await _clearToken();

    _visitor = null;
    _exhibition = null;
    _cataloguesToday = 0;
    _signedOutBecause = null;
    _stage = AuthStage.signedOut;
    notifyListeners();
  }

  Future<void> _clearToken() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(AppConfig.tokenKey);
    _client.token = null;
  }

  /// The server told us the token is no longer valid, from wherever in the app
  /// that happened. Tear the session down once rather than in every screen.
  void _onServerRejectedToken() {
    if (_stage != AuthStage.signedIn) return;

    _stage = AuthStage.signedOut;
    _visitor = null;
    _exhibition = null;
    _signedOutBecause =
        'Your session has ended. Sign in with your registered email address to continue.';

    unawaited(_clearToken());
    notifyListeners();
  }

  // --- shared data ---------------------------------------------------------

  Future<void> _loadExhibition() async {
    _exhibition = await _client.exhibition();
    await refreshCatalogueCount();
  }

  /// Reloads the reference data. Cheap, and the programme genuinely changes
  /// during a show.
  Future<void> refresh() async {
    await _loadExhibition();
    notifyListeners();
  }

  Future<void> refreshCatalogueCount() async {
    try {
      _cataloguesToday = (await _client.myCatalogues()).length;
      notifyListeners();
    } on ApiException {
      // The badge on a tab is not worth interrupting anyone for.
    }
  }

  /// Called by the scanner, which already knows the new count.
  void setCatalogueCount(int count) {
    _cataloguesToday = count;
    notifyListeners();
  }

  Future<void> updateConsent({bool? email, bool? tracking}) async {
    _visitor = await _client.updateConsent(email: email, tracking: tracking);
    notifyListeners();
  }

  static String get _platformName {
    if (kIsWeb) return 'web';
    if (Platform.isAndroid) return 'android';
    if (Platform.isIOS) return 'ios';
    return Platform.operatingSystem;
  }

  static String get _deviceLabel {
    if (kIsWeb) return 'Browser';
    return Platform.operatingSystemVersion.split('(').first.trim();
  }

  @override
  void dispose() {
    _client.close();
    super.dispose();
  }
}
