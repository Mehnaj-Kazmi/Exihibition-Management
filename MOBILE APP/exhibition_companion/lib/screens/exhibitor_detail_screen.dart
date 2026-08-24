import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';

/// Everything about one exhibitor: where they are, what they do, how to reach
/// them, and what they are speaking at.
class ExhibitorDetailScreen extends StatefulWidget {
  const ExhibitorDetailScreen({super.key, required this.exhibitorId});

  final int exhibitorId;

  @override
  State<ExhibitorDetailScreen> createState() => _ExhibitorDetailScreenState();
}

class _ExhibitorDetailScreenState extends State<ExhibitorDetailScreen> {
  ExhibitorDetail? _detail;
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
          await AppScope.read(context).api.exhibitor(widget.exhibitorId);
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

  Future<void> _open(String scheme, String value) async {
    final uri = switch (scheme) {
      'web' => Uri.parse(
          value.startsWith('http') ? value : 'https://$value'),
      'mail' => Uri(scheme: 'mailto', path: value),
      _ => Uri(scheme: 'tel', path: value),
    };

    if (!await launchUrl(uri, mode: LaunchMode.externalApplication)) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not open $value')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detail = _detail;

    return Scaffold(
      appBar: AppBar(
        title: Text(detail?.exhibitor.companyName ?? 'Exhibitor'),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : detail == null
              ? EmptyState(
                  icon: Icons.storefront_outlined,
                  title: 'This exhibitor is not available',
                  detail: _error ??
                      'They may have withdrawn from the exhibition.',
                  actionLabel: 'Try again',
                  onAction: _load,
                )
              : RefreshIndicator(
                  onRefresh: _load,
                  child: ListView(
                    padding: const EdgeInsets.only(bottom: 32),
                    children: [
                      _header(theme, detail),
                      if (detail.exhibitor.summary != null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                          child: Text(
                            detail.exhibitor.summary!,
                            style: theme.textTheme.bodyLarge,
                          ),
                        ),
                      _catalogueCard(theme, detail),
                      if (detail.exhibitor.stands.isNotEmpty) ...[
                        _sectionTitle(theme, 'Where to find them'),
                        for (final stand in detail.exhibitor.stands)
                          ListTile(
                            leading: const Icon(Icons.place_outlined),
                            title: Text('Stand ${stand.standNumber}'),
                            subtitle: Text(stand.hallName),
                          ),
                      ],
                      if (detail.sessions.isNotEmpty) ...[
                        _sectionTitle(theme, 'Speaking at'),
                        for (final session in detail.sessions)
                          SessionTile(session: session, showDate: true),
                      ],
                      _sectionTitle(theme, 'Contact'),
                      if (detail.website != null)
                        ListTile(
                          leading: const Icon(Icons.language),
                          title: Text(detail.website!),
                          onTap: () => _open('web', detail.website!),
                        ),
                      if (detail.email != null)
                        ListTile(
                          leading: const Icon(Icons.mail_outline),
                          title: Text(detail.email!),
                          onTap: () => _open('mail', detail.email!),
                        ),
                      if (detail.phone != null)
                        ListTile(
                          leading: const Icon(Icons.phone_outlined),
                          title: Text(detail.phone!),
                          onTap: () => _open('tel', detail.phone!),
                        ),
                      if (detail.contactName != null)
                        ListTile(
                          leading: const Icon(Icons.person_outline),
                          title: Text(detail.contactName!),
                          subtitle: const Text('Stand contact'),
                        ),
                      if (detail.website == null &&
                          detail.email == null &&
                          detail.phone == null)
                        const Padding(
                          padding: EdgeInsets.all(16),
                          child: MessageBanner(
                            message: 'This exhibitor has not published contact '
                                'details. Visit the stand, or request their '
                                'e-catalogue above.',
                          ),
                        ),
                    ],
                  ),
                ),
    );
  }

  Widget _header(ThemeData theme, ExhibitorDetail detail) {
    final exhibitor = detail.exhibitor;

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            exhibitor.companyName,
            style:
                theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (exhibitor.categoryName != null)
                Chip(
                  label: Text(exhibitor.categoryName!),
                  visualDensity: VisualDensity.compact,
                ),
              if (exhibitor.subCategoryName != null)
                Chip(
                  label: Text(exhibitor.subCategoryName!),
                  visualDensity: VisualDensity.compact,
                ),
              if (exhibitor.country != null)
                Chip(
                  avatar: const Icon(Icons.public, size: 16),
                  label: Text(exhibitor.country!),
                  visualDensity: VisualDensity.compact,
                ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            exhibitor.location,
            style: theme.textTheme.titleMedium
                ?.copyWith(color: theme.colorScheme.primary),
          ),
        ],
      ),
    );
  }

  /// Requesting the e-catalogue without walking to the stand.
  ///
  /// The same request the QR code makes, from the same endpoint — this is a
  /// convenience, not a second mechanism, so a visitor who does both does not
  /// end up with the catalogue twice in their evening pack.
  Widget _catalogueCard(ThemeData theme, ExhibitorDetail detail) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Icon(
                detail.catalogueRequested
                    ? Icons.check_circle
                    : Icons.picture_as_pdf_outlined,
                color: detail.catalogueRequested
                    ? theme.colorScheme.primary
                    : theme.colorScheme.onSurfaceVariant,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      detail.catalogueRequested
                          ? 'On tonight’s list'
                          : 'E-catalogue',
                      style: theme.textTheme.titleSmall,
                    ),
                    Text(
                      detail.catalogueRequested
                          ? 'You will get this in your pack this evening.'
                          : detail.exhibitor.catalogueCount > 0
                              ? '${detail.exhibitor.catalogueCount} document(s) available.'
                              : 'No documents uploaded yet — you can still register your interest.',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              if (!detail.catalogueRequested)
                FilledButton.tonal(
                  onPressed: _requestCatalogue,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(88, 40),
                  ),
                  child: const Text('Add'),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _requestCatalogue() async {
    final detail = _detail;
    if (detail == null || detail.exhibitor.stands.isEmpty) return;

    final state = AppScope.read(context);
    final messenger = ScaffoldMessenger.of(context);

    try {
      // An exhibitor with several stands is one company with one catalogue, so
      // their first stand is as good as any to record the request against.
      final count =
          await state.api.requestCatalogue(detail.exhibitor.stands.first.kioskId);

      state.setCatalogueCount(count);
      if (!mounted) return;

      setState(() => _detail = ExhibitorDetail(
            exhibitor: detail.exhibitor,
            contactName: detail.contactName,
            email: detail.email,
            phone: detail.phone,
            website: detail.website,
            sessions: detail.sessions,
            catalogueRequested: true,
          ));

      messenger.showSnackBar(SnackBar(
        content: Text('${detail.exhibitor.companyName} added to your list.'),
      ));
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Widget _sectionTitle(ThemeData theme, String text) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 20, 16, 4),
        child: Text(
          text,
          style: theme.textTheme.labelLarge
              ?.copyWith(color: theme.colorScheme.primary),
        ),
      );
}
