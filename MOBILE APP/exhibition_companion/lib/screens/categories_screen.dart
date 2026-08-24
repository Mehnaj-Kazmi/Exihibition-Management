import 'package:flutter/material.dart';

import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import 'exhibitor_list_screen.dart';

/// The organiser's two-level product taxonomy, as an expandable list.
///
/// The exhibitor count is on every row, top level and sub-category alike,
/// because "Automation & Robotics (61)" tells a visitor whether it is worth
/// opening and "Automation & Robotics" does not.
class CategoriesScreen extends StatelessWidget {
  const CategoriesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final categories = state.categories;
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Categories')),
      body: categories.isEmpty
          ? const EmptyState(
              icon: Icons.category_outlined,
              title: 'No categories published yet',
              detail: 'The organiser has not set up the product taxonomy for '
                  'this exhibition.',
            )
          : ListView.builder(
              itemCount: categories.length,
              itemBuilder: (context, index) {
                final category = categories[index];
                final colour = _parseColour(category.colour) ??
                    theme.colorScheme.primary;

                if (category.children.isEmpty) {
                  return ListTile(
                    leading: _Swatch(colour: colour),
                    title: Text(category.name),
                    subtitle: Text('${category.exhibitorCount} exhibitors'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => _open(context, category.id, null,
                        category.name, category.exhibitorCount),
                  );
                }

                return ExpansionTile(
                  leading: _Swatch(colour: colour),
                  title: Text(category.name),
                  subtitle: Text(
                    '${category.exhibitorCount} exhibitors · '
                    '${category.children.length} sub-categories',
                  ),
                  children: [
                    ListTile(
                      contentPadding:
                          const EdgeInsets.only(left: 72, right: 16),
                      title: Text('All ${category.name}'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => _open(context, category.id, null,
                          category.name, category.exhibitorCount),
                    ),
                    for (final sub in category.children)
                      ListTile(
                        contentPadding:
                            const EdgeInsets.only(left: 72, right: 16),
                        title: Text(sub.name),
                        subtitle: Text('${sub.exhibitorCount} exhibitors'),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => _open(context, null, sub.id,
                            sub.name, sub.exhibitorCount,
                            parent: category.name),
                      ),
                  ],
                );
              },
            ),
    );
  }

  void _open(
    BuildContext context,
    int? categoryId,
    int? subCategoryId,
    String title,
    int count, {
    String? parent,
  }) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ExhibitorListScreen(
          title: title,
          subtitle: parent ?? '$count exhibitors',
          categoryId: categoryId,
          subCategoryId: subCategoryId,
        ),
      ),
    );
  }

  /// The organiser sets category colours as `#rrggbb` or `#aarrggbb`. A bad
  /// value falls back to the theme rather than throwing on a colour swatch.
  static Color? _parseColour(String? hex) {
    if (hex == null) return null;
    var value = hex.replaceFirst('#', '').trim();
    if (value.length == 6) value = 'FF$value';
    if (value.length != 8) return null;
    final parsed = int.tryParse(value, radix: 16);
    return parsed == null ? null : Color(parsed);
  }
}

class _Swatch extends StatelessWidget {
  const _Swatch({required this.colour});

  final Color colour;

  @override
  Widget build(BuildContext context) => Container(
        width: 12,
        height: 40,
        decoration: BoxDecoration(
          color: colour,
          borderRadius: BorderRadius.circular(3),
        ),
      );
}
