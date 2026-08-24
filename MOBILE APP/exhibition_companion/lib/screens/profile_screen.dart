import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../config.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';

/// Who is signed in, the two consents they control, and the way out.
///
/// The consents are here and not buried, because they are the visitor's to
/// change and the system acts on them immediately: turning tracking off stops
/// visit rows being written for that badge, and turning email off stops the
/// evening pack. Both say what they actually do rather than "improve your
/// experience".
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  bool _busy = false;

  Future<void> _setConsent({bool? email, bool? tracking}) async {
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _busy = true);

    try {
      await AppScope.read(context).updateConsent(email: email, tracking: tracking);
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _signOut() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Sign out?'),
        content: const Text(
          'You will need a new code emailed to you to sign back in. Your '
          'e-catalogue list is kept — it belongs to your registration, not to '
          'this phone.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Sign out'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;
    await AppScope.read(context).signOut();
  }

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final visitor = state.visitor;
    final theme = Theme.of(context);

    if (visitor == null) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    return Scaffold(
      appBar: AppBar(title: const Text('Profile')),
      body: ListView(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                CircleAvatar(
                  radius: 28,
                  backgroundColor: theme.colorScheme.primaryContainer,
                  child: Text(
                    visitor.fullName.isEmpty
                        ? '?'
                        : visitor.fullName.characters.first.toUpperCase(),
                    style: theme.textTheme.headlineSmall?.copyWith(
                      color: theme.colorScheme.onPrimaryContainer,
                    ),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(visitor.fullName,
                          style: theme.textTheme.titleLarge),
                      Text(visitor.email, style: theme.textTheme.bodyMedium),
                      if (visitor.company != null)
                        Text(
                          [visitor.jobTitle, visitor.company]
                              .whereType<String>()
                              .join(' · '),
                          style: theme.textTheme.bodySmall,
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          if (!visitor.hasBadge)
            const Padding(
              padding: EdgeInsets.fromLTRB(16, 0, 16, 16),
              child: MessageBanner(
                message: 'No badge has been issued against your registration '
                    'yet. Collect it at the registration desk — stand visits '
                    'are recorded from the badge, not from this phone.',
                kind: MessageKind.warning,
              ),
            ),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.badge_outlined),
            title: const Text('Registration code'),
            subtitle: Text(visitor.registrationCode),
          ),
          if (visitor.country != null)
            ListTile(
              leading: const Icon(Icons.public),
              title: const Text('Country'),
              subtitle: Text(visitor.country!),
            ),
          const Divider(),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
            child: Text(
              'Your choices',
              style: theme.textTheme.labelLarge
                  ?.copyWith(color: theme.colorScheme.primary),
            ),
          ),
          SwitchListTile(
            value: visitor.consentTracking,
            onChanged: _busy
                ? null
                : (value) => _setConsent(tracking: value),
            title: const Text('Record the stands I visit'),
            subtitle: const Text(
              'Your badge is located for headcount and safety either way. With '
              'this off, no visit is recorded and you get no interest report.',
            ),
            isThreeLine: true,
          ),
          SwitchListTile(
            value: visitor.consentEmail,
            onChanged: _busy ? null : (value) => _setConsent(email: value),
            title: const Text('Email me my e-catalogues and report'),
            subtitle: const Text(
              'With this off, nothing is emailed to you — including the pack of '
              'catalogues you scan today.',
            ),
            isThreeLine: true,
          ),
          const Divider(),
          if (state.exhibition != null) ...[
            ListTile(
              leading: const Icon(Icons.event_outlined),
              title: Text(state.exhibition!.name),
              subtitle: Text([
                state.exhibition!.venue,
                state.exhibition!.organiser,
              ].whereType<String>().join(' · ')),
            ),
          ],
          ListTile(
            leading: const Icon(Icons.refresh),
            title: const Text('Refresh exhibition data'),
            subtitle: const Text(
                'Halls, categories and the programme, as they are right now.'),
            onTap: () async {
              final messenger = ScaffoldMessenger.of(context);
              try {
                await AppScope.read(context).refresh();
                messenger.showSnackBar(
                    const SnackBar(content: Text('Up to date.')));
              } on ApiException catch (e) {
                messenger.showSnackBar(SnackBar(content: Text(e.message)));
              }
            },
          ),
          const Divider(),
          ListTile(
            leading: Icon(Icons.logout, color: theme.colorScheme.error),
            title: Text('Sign out',
                style: TextStyle(color: theme.colorScheme.error)),
            onTap: _signOut,
          ),
          Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              'Exhibition Companion ${AppConfig.appVersion}\n${state.baseUrl}',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ),
        ],
      ),
    );
  }
}
