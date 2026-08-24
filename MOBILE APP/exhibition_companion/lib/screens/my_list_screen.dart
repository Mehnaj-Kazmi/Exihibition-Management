import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import 'exhibitor_detail_screen.dart';

/// Tonight's e-catalogue pack, and the day the tracking recorded.
///
/// Two tabs because they answer two different questions — "what did I ask for"
/// and "where did I actually spend my time" — and the second one only exists
/// for visitors who consented to tracking.
class MyListScreen extends StatefulWidget {
  const MyListScreen({super.key});

  @override
  State<MyListScreen> createState() => _MyListScreenState();
}

class _MyListScreenState extends State<MyListScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(length: 2, vsync: this);

  List<ScannedStand> _catalogues = const [];
  VisitorDay? _day;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    final api = AppScope.read(context).api;

    try {
      final catalogues = await api.myCatalogues();
      final day = await api.myDay();

      if (!mounted) return;
      setState(() {
        _catalogues = catalogues;
        _day = day;
        _loading = false;
      });

      AppScope.read(context).setCatalogueCount(catalogues.length);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _loading = false;
      });
    }
  }

  /// Removing is a real thing the visitor is entitled to do — it is their pack —
  /// so it takes it out of the evening email rather than just hiding the row.
  Future<void> _remove(ScannedStand stand) async {
    final state = AppScope.read(context);
    final messenger = ScaffoldMessenger.of(context);
    final previous = List<ScannedStand>.from(_catalogues);

    setState(() =>
        _catalogues = _catalogues.where((s) => s.kioskId != stand.kioskId).toList());
    state.setCatalogueCount(_catalogues.length);

    try {
      await state.api.setCatalogueIncluded(stand.kioskId, false);

      messenger.showSnackBar(SnackBar(
        content: Text('${stand.exhibitorName} removed from tonight’s pack.'),
        action: SnackBarAction(
          label: 'Undo',
          onPressed: () => _restore(stand),
        ),
      ));
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _catalogues = previous);
      state.setCatalogueCount(previous.length);
      messenger.showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _restore(ScannedStand stand) async {
    try {
      await AppScope.read(context).api.setCatalogueIncluded(stand.kioskId, true);
      await _load();
    } on ApiException {
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('My list'),
        bottom: TabBar(
          controller: _tabs,
          tabs: [
            Tab(text: 'Catalogues (${_catalogues.length})'),
            const Tab(text: 'My day'),
          ],
        ),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? EmptyState(
                  icon: Icons.cloud_off,
                  title: 'Could not load your list',
                  detail: _error,
                  actionLabel: 'Try again',
                  onAction: _load,
                )
              : TabBarView(
                  controller: _tabs,
                  children: [_cataloguesTab(), _dayTab()],
                ),
    );
  }

  Widget _cataloguesTab() {
    final theme = Theme.of(context);

    if (_catalogues.isEmpty) {
      return const EmptyState(
        icon: Icons.inbox_outlined,
        title: 'Nothing collected yet',
        detail: 'Scan the QR code on a stand, or open an exhibitor and tap Add. '
            'Everything you collect today arrives in one email this evening.',
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        itemCount: _catalogues.length + 1,
        separatorBuilder: (_, __) => const Divider(indent: 16),
        itemBuilder: (context, index) {
          if (index == 0) {
            return Padding(
              padding: const EdgeInsets.all(16),
              child: MessageBanner(
                message: '${_catalogues.length} exhibitor'
                    '${_catalogues.length == 1 ? '' : 's'} in tonight’s pack. '
                    'It goes to your registered email address after the halls close.',
                kind: MessageKind.good,
                icon: Icons.mark_email_read_outlined,
              ),
            );
          }

          final stand = _catalogues[index - 1];

          return Dismissible(
            key: ValueKey(stand.kioskId),
            direction: DismissDirection.endToStart,
            background: Container(
              alignment: Alignment.centerRight,
              padding: const EdgeInsets.only(right: 24),
              color: theme.colorScheme.errorContainer,
              child: Icon(Icons.delete_outline,
                  color: theme.colorScheme.onErrorContainer),
            ),
            onDismissed: (_) => _remove(stand),
            child: ListTile(
              title: Text(stand.exhibitorName),
              subtitle: Text(
                '${stand.hallName} · Stand ${stand.standNumber}'
                '${stand.catalogueFileCount > 0 ? ' · ${stand.catalogueFileCount} document(s)' : ''}',
              ),
              trailing: IconButton(
                icon: const Icon(Icons.close),
                tooltip: 'Remove from tonight’s pack',
                onPressed: () => _remove(stand),
              ),
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) =>
                      ExhibitorDetailScreen(exhibitorId: stand.exhibitorId),
                ),
              ),
            ),
          );
        },
      ),
    );
  }

  Widget _dayTab() {
    final theme = Theme.of(context);
    final day = _day;

    if (day == null) return const SizedBox.shrink();

    if (!day.trackingConsent) {
      return EmptyState(
        icon: Icons.visibility_off_outlined,
        title: 'Stand tracking is off for your badge',
        detail: day.message ??
            'Turn it on under Profile if you would like a record of the stands '
                'you visited.',
      );
    }

    if (day.visited.isEmpty) {
      return const EmptyState(
        icon: Icons.directions_walk,
        title: 'Nothing recorded yet today',
        detail: 'Stands you stop at for more than a few seconds appear here '
            'through the day.',
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: MessageBanner(
              message: '${day.standsWithInterest} stand'
                  '${day.standsWithInterest == 1 ? '' : 's'} of real interest, '
                  '${day.totalDwellText} on the floor.',
              kind: MessageKind.info,
              icon: Icons.insights_outlined,
            ),
          ),
          if (day.categories.isNotEmpty) ...[
            _header(theme, 'What you spent time on'),
            for (final category in day.categories)
              ListTile(
                title: Text(category.categoryName),
                subtitle: Text(
                    '${category.dwellText} across ${category.standCount} stand(s)'),
                trailing: Text('${category.sharePct.round()}%',
                    style: theme.textTheme.titleMedium),
              ),
          ],
          _header(theme, 'Stands you visited'),
          for (final stand in day.visited)
            ListTile(
              title: Text(stand.exhibitorName),
              subtitle: Text('${stand.location}\n${stand.levelText} · ${stand.dwellText}'),
              isThreeLine: true,
              trailing: stand.catalogueRequested
                  ? Icon(Icons.inbox, color: theme.colorScheme.primary, size: 20)
                  : null,
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) =>
                      ExhibitorDetailScreen(exhibitorId: stand.exhibitorId),
                ),
              ),
            ),
          if (day.missed.isNotEmpty) ...[
            _header(theme, 'In your categories, not yet reached'),
            for (final stand in day.missed)
              ListTile(
                leading: const Icon(Icons.explore_outlined),
                title: Text(stand.exhibitorName),
                subtitle: Text('${stand.location}\n${stand.reason}'),
                isThreeLine: true,
                onTap: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) =>
                        ExhibitorDetailScreen(exhibitorId: stand.exhibitorId),
                  ),
                ),
              ),
          ],
          const SizedBox(height: 24),
        ],
      ),
    );
  }

  Widget _header(ThemeData theme, String text) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
        child: Text(
          text,
          style: theme.textTheme.labelLarge
              ?.copyWith(color: theme.colorScheme.primary),
        ),
      );
}
