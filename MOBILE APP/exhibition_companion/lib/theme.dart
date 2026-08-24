import 'package:flutter/material.dart';

/// The app's look.
///
/// The seed colour is the same blue as the admin console's header, so a visitor
/// who scans a stand code with their phone camera and lands on the web page
/// recognises it as the same system as the app.
///
/// Both light and dark are defined. Exhibition halls are brightly lit and a lot
/// of phones are on automatic, so a visitor may see either within the same hour,
/// and a light-only app is unreadable at a stand at 18:00 for anyone whose phone
/// has already switched.
///
/// Deliberately built from `ColorScheme.fromSeed` and a handful of stable
/// component themes rather than a large hand-tuned theme: the component theme
/// classes are the part of Flutter that changes most between releases, and an
/// app that has to be shipped again next year for a show should not need a
/// theme rewrite to compile.
class AppTheme {
  const AppTheme._();

  static const Color seed = Color(0xFF125EA8);

  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final scheme = ColorScheme.fromSeed(
      seedColor: seed,
      brightness: brightness,
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      appBarTheme: AppBarTheme(
        centerTitle: false,
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
        scrolledUnderElevation: 2,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide.none,
        ),
        contentPadding:
            const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(50),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(12),
          ),
        ),
      ),
      listTileTheme: const ListTileThemeData(
        contentPadding: EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      ),
    );
  }
}

/// Each kind of programme item gets its own icon and colour, so a visitor
/// scanning a long list can pick the workshops out without reading every line.
({IconData icon, Color colour}) sessionKindStyle(String kind, ColorScheme s) =>
    switch (kind.toLowerCase()) {
      'meeting' => (icon: Icons.groups_outlined, colour: s.tertiary),
      'workshop' => (icon: Icons.build_outlined, colour: s.secondary),
      'panel' => (icon: Icons.forum_outlined, colour: s.primary),
      'demo' => (icon: Icons.play_circle_outline, colour: s.tertiary),
      'ceremony' => (icon: Icons.celebration_outlined, colour: s.error),
      _ => (icon: Icons.record_voice_over_outlined, colour: s.primary),
    };
