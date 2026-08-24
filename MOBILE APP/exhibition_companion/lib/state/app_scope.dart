import 'package:flutter/widgets.dart';

import 'app_state.dart';

/// Makes [AppState] reachable from any widget as `AppScope.of(context)`.
///
/// An `InheritedNotifier` rather than a state-management package: there is one
/// object to share, it is a `ChangeNotifier`, and this is what the framework
/// already provides for exactly that. A dependency here would need keeping
/// current for the life of the app in exchange for nothing.
class AppScope extends InheritedNotifier<AppState> {
  const AppScope({
    super.key,
    required AppState state,
    required super.child,
  }) : super(notifier: state);

  static AppState of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppScope>();
    assert(scope?.notifier != null, 'No AppScope above this widget.');
    return scope!.notifier!;
  }

  /// The state without subscribing to it — for callbacks, which run after the
  /// build and should not cause one.
  static AppState read(BuildContext context) {
    final scope = context.getInheritedWidgetOfExactType<AppScope>();
    assert(scope?.notifier != null, 'No AppScope above this widget.');
    return scope!.notifier!;
  }
}
