import 'package:flutter/material.dart';

import '../state/app_scope.dart';
import 'my_list_screen.dart';
import 'programme_screen.dart';
import 'scan_screen.dart';
import 'search_screen.dart';
import 'profile_screen.dart';

/// The five things a visitor does, as five tabs.
///
/// Scanning is a tab rather than a floating button because it is the action
/// people take most often and usually one-handed while holding something else;
/// putting it in the middle of the bar makes it the easiest target on the
/// screen. The others are ordered by how often they are opened.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key});

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  /// Each tab keeps its own navigation and scroll position, so a visitor who
  /// scans a stand and comes back to search does not lose the results they had
  /// scrolled to.
  final _pages = const [
    SearchScreen(),
    ProgrammeScreen(),
    ScanScreen(),
    MyListScreen(),
    ProfileScreen(),
  ];

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);

    return Scaffold(
      body: IndexedStack(index: _index, children: _pages),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (index) => setState(() => _index = index),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.search_outlined),
            selectedIcon: Icon(Icons.search),
            label: 'Search',
          ),
          const NavigationDestination(
            icon: Icon(Icons.event_note_outlined),
            selectedIcon: Icon(Icons.event_note),
            label: 'Programme',
          ),
          const NavigationDestination(
            icon: Icon(Icons.qr_code_scanner_outlined),
            selectedIcon: Icon(Icons.qr_code_scanner),
            label: 'Scan',
          ),
          NavigationDestination(
            icon: Badge(
              isLabelVisible: state.cataloguesToday > 0,
              label: Text('${state.cataloguesToday}'),
              child: const Icon(Icons.inbox_outlined),
            ),
            selectedIcon: Badge(
              isLabelVisible: state.cataloguesToday > 0,
              label: Text('${state.cataloguesToday}'),
              child: const Icon(Icons.inbox),
            ),
            label: 'My list',
          ),
          const NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profile',
          ),
        ],
      ),
    );
  }
}
