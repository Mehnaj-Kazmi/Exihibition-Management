import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';

/// A paged exhibitor list under a fixed filter — everyone in a sub-category,
/// everyone in a hall.
///
/// Separate from the search screen because the filter here is the title of the
/// screen and cannot be changed from inside it: a visitor who drilled into
/// "Weaving" wants the back button to take them to the taxonomy, not to a
/// filter bar they now have to reset.
class ExhibitorListScreen extends StatefulWidget {
  const ExhibitorListScreen({
    super.key,
    required this.title,
    this.subtitle,
    this.categoryId,
    this.subCategoryId,
    this.hallId,
  });

  final String title;
  final String? subtitle;
  final int? categoryId;
  final int? subCategoryId;
  final int? hallId;

  @override
  State<ExhibitorListScreen> createState() => _ExhibitorListScreenState();
}

class _ExhibitorListScreenState extends State<ExhibitorListScreen> {
  final _scroll = ScrollController();
  final List<Exhibitor> _exhibitors = [];

  bool _loading = true;
  bool _loadingMore = false;
  bool _hasMore = false;
  int _page = 1;
  int _total = 0;
  String? _error;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_loadingMore || !_hasMore) return;
      if (_scroll.position.pixels <
          _scroll.position.maxScrollExtent - 400) {
        return;
      }
      _load(next: true);
    });
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _load({bool next = false}) async {
    setState(() {
      if (next) {
        _loadingMore = true;
      } else {
        _loading = true;
        _error = null;
      }
    });

    try {
      final page = await AppScope.read(context).api.exhibitors(
            categoryId: widget.categoryId,
            subCategoryId: widget.subCategoryId,
            hallId: widget.hallId,
            page: next ? _page + 1 : 1,
          );

      if (!mounted) return;
      setState(() {
        if (next) {
          _page++;
          _exhibitors.addAll(page.items);
        } else {
          _page = 1;
          _exhibitors
            ..clear()
            ..addAll(page.items);
        }
        _total = page.total;
        _hasMore = page.hasMore;
        _loading = false;
        _loadingMore = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = next ? null : e.message;
        _loading = false;
        _loadingMore = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(widget.title, style: theme.textTheme.titleLarge),
            if (widget.subtitle != null)
              Text(
                widget.subtitle!,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
              ),
          ],
        ),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? EmptyState(
                  icon: Icons.cloud_off,
                  title: 'Could not load',
                  detail: _error,
                  actionLabel: 'Try again',
                  onAction: _load,
                )
              : _exhibitors.isEmpty
                  ? const EmptyState(
                      icon: Icons.storefront_outlined,
                      title: 'No exhibitors here yet',
                    )
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.separated(
                        controller: _scroll,
                        itemCount: _exhibitors.length + 2,
                        separatorBuilder: (_, __) => const Divider(indent: 72),
                        itemBuilder: (context, index) {
                          if (index == 0) {
                            return Padding(
                              padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                              child: Text(
                                '$_total exhibitor${_total == 1 ? '' : 's'}',
                                style: theme.textTheme.labelLarge?.copyWith(
                                  color: theme.colorScheme.onSurfaceVariant,
                                ),
                              ),
                            );
                          }
                          if (index == _exhibitors.length + 1) {
                            return SizedBox(
                              height: 72,
                              child: Center(
                                child: _loadingMore
                                    ? const CircularProgressIndicator()
                                    : const SizedBox.shrink(),
                              ),
                            );
                          }
                          return ExhibitorTile(
                              exhibitor: _exhibitors[index - 1]);
                        },
                      ),
                    ),
    );
  }
}
