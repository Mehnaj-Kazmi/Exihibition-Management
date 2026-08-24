import 'package:flutter/material.dart';

import 'screens/home_shell.dart';
import 'screens/login_screen.dart';
import 'state/app_scope.dart';
import 'state/app_state.dart';
import 'theme.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ExhibitionCompanionApp());
}

class ExhibitionCompanionApp extends StatefulWidget {
  const ExhibitionCompanionApp({super.key});

  @override
  State<ExhibitionCompanionApp> createState() => _ExhibitionCompanionAppState();
}

class _ExhibitionCompanionAppState extends State<ExhibitionCompanionApp> {
  late final AppState _state;

  @override
  void initState() {
    super.initState();
    _state = AppState();

    // Restoring the saved session is what decides the first screen, so it
    // starts immediately rather than from the first frame of a screen that
    // might be the wrong one.
    _state.restore();
  }

  @override
  void dispose() {
    _state.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AppScope(
      state: _state,
      child: MaterialApp(
        title: 'Exhibition Companion',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: ThemeMode.system,
        home: const _Root(),
      ),
    );
  }
}

/// Chooses between the sign-in screen and the app itself, and switches back
/// automatically if the session ends while the visitor is inside.
class _Root extends StatelessWidget {
  const _Root();

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);

    return switch (state.stage) {
      AuthStage.starting => const _Splash(),
      AuthStage.signedOut => const LoginScreen(),
      AuthStage.signedIn => const HomeShell(),
    };
  }
}

class _Splash extends StatelessWidget {
  const _Splash();

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      body: Center(child: CircularProgressIndicator()),
    );
  }
}
