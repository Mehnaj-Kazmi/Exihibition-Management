import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../theme.dart';
import '../widgets/message_banner.dart';
import 'exhibitor_detail_screen.dart';

class SessionDetailScreen extends StatefulWidget {
  const SessionDetailScreen({super.key, required this.sessionId});

  final int sessionId;

  @override
  State<SessionDetailScreen> createState() => _SessionDetailScreenState();
}

class _SessionDetailScreenState extends State<SessionDetailScreen> {
  SessionDetail? _detail;
  bool _loading = true;
  bool _saving = false;
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
      final detail = await AppScope.read(context).api.session(widget.sessionId);
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

  Future<void> _toggleBookmark() async {
    final detail = _detail;
    if (detail == null || _saving) return;

    final wanted = !detail.session.bookmarked;
    final messenger = ScaffoldMessenger.of(context);

    // Flipped straight away and rolled back on failure: a bookmark toggle that
    // waits on the venue wifi feels broken, and the cost of being wrong for a
    // moment is one icon.
    setState(() {
      _saving = true;
      _detail = SessionDetail(
        session: detail.session.copyWith(bookmarked: wanted),
        abstractText: detail.abstractText,
      );
    });

    try {
      await AppScope.read(context).api.setBookmarked(widget.sessionId, wanted);
      if (!mounted) return;
      setState(() => _saving = false);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _detail = detail;
      });
      messenger.showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detail = _detail;
    final session = detail?.session;

    return Scaffold(
      appBar: AppBar(title: Text(session?.kind ?? 'Session')),
      floatingActionButton: session == null
          ? null
          : FloatingActionButton.extended(
              onPressed: _saving ? null : _toggleBookmark,
              icon: Icon(
                  session.bookmarked ? Icons.bookmark : Icons.bookmark_border),
              label: Text(session.bookmarked ? 'In your agenda' : 'Save to agenda'),
            ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : session == null
              ? EmptyState(
                  icon: Icons.event_busy,
                  title: 'This session is no longer on the programme',
                  detail: _error,
                  actionLabel: 'Try again',
                  onAction: _load,
                )
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: [
                    Row(
                      children: [
                        Icon(
                          sessionKindStyle(session.kind, theme.colorScheme).icon,
                          size: 18,
                          color: sessionKindStyle(session.kind, theme.colorScheme)
                              .colour,
                        ),
                        const SizedBox(width: 6),
                        Text(session.kind, style: theme.textTheme.labelLarge),
                        if (session.language != null) ...[
                          const Spacer(),
                          Chip(
                            label: Text(session.language!.toUpperCase()),
                            visualDensity: VisualDensity.compact,
                          ),
                        ],
                      ],
                    ),
                    const SizedBox(height: 12),
                    Text(
                      session.title,
                      style: theme.textTheme.headlineSmall
                          ?.copyWith(fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 20),
                    _Row(
                      icon: Icons.schedule,
                      title: session.timeRange,
                      subtitle:
                          '${_longDate(session.eventDate)} · ${session.durationMinutes} minutes',
                    ),
                    _Row(
                      icon: Icons.place_outlined,
                      title: session.roomName ?? 'Location to be confirmed',
                      subtitle: session.hallName,
                    ),
                    if (session.speakerName != null)
                      _Row(
                        icon: Icons.person_outline,
                        title: session.speakerName!,
                        subtitle: [
                          session.speakerTitle,
                          session.speakerOrganisation,
                        ].whereType<String>().join(' · '),
                      ),
                    if (session.categoryName != null)
                      _Row(
                        icon: Icons.category_outlined,
                        title: session.categoryName!,
                        subtitle: session.subCategoryName,
                      ),
                    if (session.capacity > 0)
                      _Row(
                        icon: Icons.event_seat_outlined,
                        title: '${session.capacity} seats',
                        subtitle: session.requiresBooking
                            ? 'Register in advance with the organiser'
                            : 'First come, first served',
                      ),
                    const SizedBox(height: 20),
                    if (detail?.abstractText != null) ...[
                      Text('About', style: theme.textTheme.labelLarge
                          ?.copyWith(color: theme.colorScheme.primary)),
                      const SizedBox(height: 8),
                      Text(detail!.abstractText!,
                          style: theme.textTheme.bodyLarge),
                      const SizedBox(height: 20),
                    ],
                    if (session.exhibitorId != null)
                      Card(
                        child: ListTile(
                          leading: const Icon(Icons.storefront_outlined),
                          title: Text(session.exhibitorName ?? 'Exhibitor'),
                          subtitle: const Text('Hosting this session'),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () => Navigator.of(context).push(
                            MaterialPageRoute<void>(
                              builder: (_) => ExhibitorDetailScreen(
                                  exhibitorId: session.exhibitorId!),
                            ),
                          ),
                        ),
                      ),
                    const SizedBox(height: 16),
                    // Said plainly, because a visitor who thinks they hold a
                    // seat and arrives to a full room has been misled by us.
                    const MessageBanner(
                      message: 'Saving a session puts it in your agenda in this '
                          'app. It does not reserve a seat.',
                    ),
                  ],
                ),
    );
  }

  static String _longDate(DateTime date) {
    const days = [
      'Monday', 'Tuesday', 'Wednesday', 'Thursday',
      'Friday', 'Saturday', 'Sunday',
    ];
    const months = [
      'January', 'February', 'March', 'April', 'May', 'June',
      'July', 'August', 'September', 'October', 'November', 'December',
    ];
    return '${days[date.weekday - 1]} ${date.day} ${months[date.month - 1]}';
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.icon, required this.title, this.subtitle});

  final IconData icon;
  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detail = (subtitle == null || subtitle!.isEmpty) ? null : subtitle;

    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 20, color: theme.colorScheme.onSurfaceVariant),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.titleSmall),
                if (detail != null)
                  Text(
                    detail,
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
