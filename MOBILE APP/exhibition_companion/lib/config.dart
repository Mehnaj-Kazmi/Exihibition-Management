/// Where the app finds the exhibition system.
///
/// The default is overridden at build time, so the same source produces the
/// staging build and the store build without an edit that could be committed by
/// accident:
///
/// ```
/// flutter build apk --dart-define=EXB_BASE_URL=https://exhibition.smatech.example
/// ```
///
/// It is also settable at runtime from the sign-in screen. That is not a
/// developer convenience: exhibition systems are frequently deployed on the
/// venue's own network on the morning of the show, and an app that can only be
/// pointed somewhere new by shipping a store release would be useless to the
/// organiser on day one.
library;

class AppConfig {
  const AppConfig._();

  /// Must be reachable from visitors' phones — the same address the stand QR
  /// codes resolve to, which is `Settings › Exhibition › PublicBaseUrl` in the
  /// admin console.
  static const String defaultBaseUrl = String.fromEnvironment(
    'EXB_BASE_URL',
    defaultValue: 'http://10.0.2.2:5080',
  );

  /// Shown on the profile screen and sent with each sign-in, so the organiser
  /// can see which build a visitor is on when something looks wrong.
  static const String appVersion = '1.0.0';

  /// Keys for the small amount we keep on the device. The token is the only
  /// sensitive one; the rest is convenience.
  static const String tokenKey = 'exb.token';
  static const String baseUrlKey = 'exb.baseUrl';
  static const String emailKey = 'exb.email';
}
