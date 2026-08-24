import 'package:flutter/material.dart';

import '../api/models.dart';
import '../screens/exhibitor_detail_screen.dart';
import '../screens/hall_detail_screen.dart';
import '../screens/session_detail_screen.dart';
import '../theme.dart';

/// One exhibitor in a list.
///
/// The stand number is given as much weight as the company name, because a
/// visitor searching an exhibitor list at a show is almost always trying to
/// work out where to walk.
class ExhibitorTile extends StatelessWidget {
  const ExhibitorTile({super.key, required this.exhibitor, this.onReturn});

  final Exhibitor exhibitor;

  /// Called after the detail screen closes, so a list showing catalogue state
  /// can pick up a scan made from inside it.
  final VoidCallback? onReturn;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListTile(
      leading: CircleAvatar(
        backgroundColor: theme.colorScheme.primaryContainer,
        child: Text(
          exhibitor.companyName.isEmpty
              ? '?'
              : exhibitor.companyName.characters.first.toUpperCase(),
          style: TextStyle(color: theme.colorScheme.onPrimaryContainer),
        ),
      ),
      title: Text(exhibitor.companyName,
          maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(height: 2),
          Text(
            exhibitor.location,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.primary),
          ),
          if (exhibitor.categoryName != null)
            Text(
              exhibitor.subCategoryName == null
                  ? exhibitor.categoryName!
                  : '${exhibitor.categoryName} › ${exhibitor.subCategoryName}',
              style: theme.textTheme.bodySmall,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
        ],
      ),
      isThreeLine: exhibitor.categoryName != null,
      trailing: exhibitor.catalogueCount > 0
          ? Tooltip(
              message: '${exhibitor.catalogueCount} e-catalogue(s)',
              child: Icon(Icons.picture_as_pdf_outlined,
                  size: 20, color: theme.colorScheme.outline),
            )
          : null,
      onTap: () async {
        await Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) => ExhibitorDetailScreen(exhibitorId: exhibitor.id),
          ),
        );
        onReturn?.call();
      },
    );
  }
}

/// One programme item in a list. The time is the first thing read, so it is the
/// first thing on the row.
class SessionTile extends StatelessWidget {
  const SessionTile({
    super.key,
    required this.session,
    this.showDate = false,
    this.onChanged,
  });

  final Session session;

  /// True in the agenda and in search results, where rows span several days.
  final bool showDate;

  /// Called when the detail screen changed the bookmark, so the list can update.
  final VoidCallback? onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = sessionKindStyle(session.kind, theme.colorScheme);

    return ListTile(
      leading: SizedBox(
        width: 52,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              formatMinutes(session.startsAtMinutes),
              style: theme.textTheme.titleSmall
                  ?.copyWith(fontWeight: FontWeight.w600),
            ),
            Text(
              '${session.durationMinutes}m',
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ],
        ),
      ),
      title: Text(session.title, maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(height: 2),
          Row(
            children: [
              Icon(style.icon, size: 14, color: style.colour),
              const SizedBox(width: 4),
              Text(session.kind, style: theme.textTheme.bodySmall),
              if (showDate) ...[
                Text(' · ', style: theme.textTheme.bodySmall),
                Text(
                  _shortDate(session.eventDate),
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ],
          ),
          Text(
            session.speakerName == null
                ? session.where
                : '${session.speakerName} · ${session.where}',
            style: theme.textTheme.bodySmall,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ],
      ),
      isThreeLine: true,
      trailing: session.bookmarked
          ? Icon(Icons.bookmark, size: 20, color: theme.colorScheme.primary)
          : null,
      onTap: () async {
        await Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) => SessionDetailScreen(sessionId: session.id),
          ),
        );
        onChanged?.call();
      },
    );
  }

  static String _shortDate(DateTime date) {
    const months = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ];
    return '${date.day} ${months[date.month - 1]}';
  }
}

class HallTile extends StatelessWidget {
  const HallTile({super.key, required this.hall});

  final Hall hall;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListTile(
      leading: CircleAvatar(
        backgroundColor: theme.colorScheme.secondaryContainer,
        child: Text(
          hall.code,
          style: TextStyle(
            fontSize: 12,
            color: theme.colorScheme.onSecondaryContainer,
          ),
        ),
      ),
      title: Text(hall.name),
      subtitle: Text(
        '${hall.exhibitorCount} exhibitors · ${hall.standCount} stands · '
        '${hall.widthM.toStringAsFixed(0)} × ${hall.depthM.toStringAsFixed(0)} m',
      ),
      trailing: const Icon(Icons.chevron_right),
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute<void>(builder: (_) => HallDetailScreen(hall: hall)),
      ),
    );
  }
}
