import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';
import 'exhibitor_list_screen.dart';
import 'programme_screen.dart';

/// One hall: its size, who is in it, and what is on in it.
///
/// The exhibitor list here is the first page only, with a link to the full one.
/// A hall of three hundred stands scrolled inside a detail screen is not a
/// useful way to find anything, and the filtered list screen already does it
/// properly.
class HallDetailScreen extends StatefulWidget {
  const HallDetailScreen({super.key, required this.hall});

  final Hall hall;

  @override
  State<HallDetailScreen> createState() => _HallDetailScreenState();
}

class _HallDetailScreenState extends State<HallDetailScreen> {
  HallDetail? _detail;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final detail =
          await AppScope.read(context).api.hall(widget.hall.id, pageSize: 12);
      if (!mounted) return;
      setState(() {
        _detail = detail;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hall = _detail?.hall ?? widget.hall;

    return Scaffold(
      appBar: AppBar(title: Text(hall.name)),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? EmptyState(
                  icon: Icons.cloud_off,
                  title: 'Could not load this hall',
                  detail: _error,
                  actionLabel: 'Try again',
                  onAction: _load,
                )
              : RefreshIndicator(
                  onRefresh: _load,
                  child: ListView(
                    children: [
                      Padding(
                        padding: const EdgeInsets.all(16),
                        child: Row(
                          children: [
                            _Stat(
                              value: '${hall.exhibitorCount}',
                              label: 'exhibitors',
                            ),
                            _Stat(
                              value: '${hall.standCount}',
                              label: 'stands',
                            ),
                            _Stat(
                              value: '${_detail?.sessionCount ?? 0}',
                              label: 'sessions',
                            ),
                            _Stat(
                              value: '${(hall.widthM * hall.depthM).round()}',
                              label: 'm² floor',
                            ),
                          ],
                        ),
                      ),
                      if (hall.notes != null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                          child: MessageBanner(message: hall.notes!),
                        ),
                      const Divider(),
                      ListTile(
                        leading: const Icon(Icons.event_note_outlined),
                        title: const Text('What is on in this hall'),
                        subtitle: Text(
                            '${_detail?.sessionCount ?? 0} meetings and lectures'),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => Navigator.of(context).push(
                          MaterialPageRoute<void>(
                            builder: (_) => ProgrammeScreen(
                              fixedHallId: hall.id,
                              fixedHallName: hall.name,
                            ),
                          ),
                        ),
                      ),
                      const Divider(),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                        child: Text(
                          'Exhibitors',
                          style: theme.textTheme.labelLarge
                              ?.copyWith(color: theme.colorScheme.primary),
                        ),
                      ),
                      for (final exhibitor
                          in _detail?.exhibitors ?? const <Exhibitor>[])
                        ExhibitorTile(exhibitor: exhibitor),
                      if ((_detail?.exhibitors.length ?? 0) <
                          hall.exhibitorCount)
                        Padding(
                          padding: const EdgeInsets.all(16),
                          child: OutlinedButton(
                            onPressed: () => Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => ExhibitorListScreen(
                                  title: hall.name,
                                  subtitle:
                                      '${hall.exhibitorCount} exhibitors',
                                  hallId: hall.id,
                                ),
                              ),
                            ),
                            child: Text(
                                'See all ${hall.exhibitorCount} exhibitors'),
                          ),
                        ),
                      const SizedBox(height: 24),
                    ],
                  ),
                ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Expanded(
      child: Column(
        children: [
          Text(
            value,
            style: theme.textTheme.headlineSmall
                ?.copyWith(fontWeight: FontWeight.w600),
          ),
          Text(
            label,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }
}
