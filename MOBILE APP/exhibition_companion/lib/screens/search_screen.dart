import 'dart:async';

import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/choice.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';
import 'categories_screen.dart';
import 'halls_screen.dart';

/// The main way in: one search box over everything, plus the filters the
/// organiser's taxonomy provides.
///
/// It has two modes rather than one. With no text typed it is a *filtered
/// exhibitor list* — pick a category, pick a hall, page through what is there —
/// because that is how somebody plans a visit. As soon as text is typed it
/// becomes a *unified search* across exhibitors, the programme, categories and
/// halls, because somebody typing "Siemens" does not know or care which of
/// those four their answer lives in.
class SearchScreen extends StatefulWidget {
  const SearchScreen({super.key});

  @override
  State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  final _controller = TextEditingController();
  final _scroll = ScrollController();

  Timer? _debounce;
  String _query = '';

  int? _categoryId;
  int? _subCategoryId;
  int? _hallId;
  String? _country;

  bool _loading = false;
  bool _loadingMore = false;
  String? _error;

  // Browse mode.
  final List<Exhibitor> _exhibitors = [];
  int _total = 0;
  int _page = 1;
  bool _hasMore = false;

  // Search mode.
  SearchResults? _results;

  bool get _searching => _query.isNotEmpty;
  bool get _hasFilters =>
      _categoryId != null ||
      _subCategoryId != null ||
      _hallId != null ||
      _country != null;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _scroll.dispose();
    _controller.dispose();
    super.dispose();
  }

  /// A search per keystroke would put a request in flight for every letter of
  /// "automation" — eleven, of which ten are thrown away — on a network shared
  /// by everyone in the hall.
  void _onQueryChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (!mounted) return;
      setState(() => _query = value.trim());
      _load();
    });
  }

  void _onScroll() {
    if (_searching || _loadingMore || !_hasMore) return;
    if (_scroll.position.pixels < _scroll.position.maxScrollExtent - 400) return;
    _loadMore();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
      _page = 1;
    });

    final api = AppScope.read(context).api;

    try {
      if (_searching) {
        final results = await api.searchEverything(_query);
        if (!mounted) return;
        setState(() {
          _results = results;
          _loading = false;
        });
      } else {
        final page = await api.exhibitors(
          categoryId: _categoryId,
          subCategoryId: _subCategoryId,
          hallId: _hallId,
          country: _country,
          page: 1,
        );
        if (!mounted) return;
        setState(() {
          _exhibitors
            ..clear()
            ..addAll(page.items);
          _total = page.total;
          _hasMore = page.hasMore;
          _results = null;
          _loading = false;
        });
      }
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _loading = false;
      });
    }
  }

  Future<void> _loadMore() async {
    setState(() => _loadingMore = true);

    try {
      final page = await AppScope.read(context).api.exhibitors(
            categoryId: _categoryId,
            subCategoryId: _subCategoryId,
            hallId: _hallId,
            country: _country,
            page: _page + 1,
          );

      if (!mounted) return;
      setState(() {
        _page++;
        _exhibitors.addAll(page.items);
        _hasMore = page.hasMore;
        _loadingMore = false;
      });
    } on ApiException {
      if (!mounted) return;
      // A failed next page is not worth an error screen over the results the
      // visitor already has; scrolling again retries.
      setState(() => _loadingMore = false);
    }
  }

  void _clearFilters() {
    setState(() {
      _categoryId = null;
      _subCategoryId = null;
      _hallId = null;
      _country = null;
    });
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: Text(state.exhibition?.name ?? 'Exhibition'),
        actions: [
          IconButton(
            tooltip: 'Browse categories',
            icon: const Icon(Icons.category_outlined),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(builder: (_) => const CategoriesScreen()),
            ),
          ),
          IconButton(
            tooltip: 'Halls',
            icon: const Icon(Icons.map_outlined),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(builder: (_) => const HallsScreen()),
            ),
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
            child: TextField(
              controller: _controller,
              textInputAction: TextInputAction.search,
              decoration: InputDecoration(
                hintText: 'Exhibitor, stand number, talk, speaker…',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _controller.text.isEmpty
                    ? null
                    : IconButton(
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _controller.clear();
                          _onQueryChanged('');
                          setState(() {});
                        },
                      ),
              ),
              onChanged: (value) {
                setState(() {});
                _onQueryChanged(value);
              },
            ),
          ),
          if (!_searching) _FilterBar(
            categoryId: _categoryId,
            subCategoryId: _subCategoryId,
            hallId: _hallId,
            country: _country,
            onChanged: (category, subCategory, hall, country) {
              setState(() {
                _categoryId = category;
                _subCategoryId = subCategory;
                _hallId = hall;
                _country = country;
              });
              _load();
            },
            onClear: _hasFilters ? _clearFilters : null,
          ),
          Expanded(child: _body(theme)),
        ],
      ),
    );
  }

  Widget _body(ThemeData theme) {
    if (_loading) return const Center(child: CircularProgressIndicator());

    if (_error != null) {
      return EmptyState(
        icon: Icons.cloud_off,
        title: 'Could not load',
        detail: _error,
        actionLabel: 'Try again',
        onAction: _load,
      );
    }

    if (_searching) return _searchResults(theme);
    return _browseResults(theme);
  }

  Widget _browseResults(ThemeData theme) {
    if (_exhibitors.isEmpty) {
      return EmptyState(
        icon: Icons.storefront_outlined,
        title: 'No exhibitors match these filters',
        detail: _hasFilters
            ? 'Try widening the category or looking in another hall.'
            : 'The exhibitor list has not been published yet.',
        actionLabel: _hasFilters ? 'Clear filters' : null,
        onAction: _hasFilters ? _clearFilters : null,
      );
    }

    return RefreshIndicator(
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
                style: theme.textTheme.labelLarge
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
              ),
            );
          }

          if (index == _exhibitors.length + 1) {
            return Padding(
              padding: const EdgeInsets.all(20),
              child: Center(
                child: _loadingMore
                    ? const CircularProgressIndicator()
                    : Text(
                        _hasMore ? '' : 'That is all of them.',
                        style: theme.textTheme.bodySmall,
                      ),
              ),
            );
          }

          return ExhibitorTile(exhibitor: _exhibitors[index - 1]);
        },
      ),
    );
  }

  Widget _searchResults(ThemeData theme) {
    final results = _results;
    if (results == null || results.isEmpty) {
      return EmptyState(
        icon: Icons.search_off,
        title: 'Nothing found for "$_query"',
        detail: 'Try a company name, a stand number like H1-014, a product '
            'type, or a speaker.',
      );
    }

    return ListView(
      children: [
        if (results.exhibitors.isNotEmpty) ...[
          _SectionHeader(
            title: 'Exhibitors',
            trailing: results.exhibitorTotal > results.exhibitors.length
                ? 'showing ${results.exhibitors.length} of ${results.exhibitorTotal}'
                : null,
          ),
          for (final exhibitor in results.exhibitors)
            ExhibitorTile(exhibitor: exhibitor),
        ],
        if (results.sessions.isNotEmpty) ...[
          _SectionHeader(
            title: 'Meetings & lectures',
            trailing: results.sessionTotal > results.sessions.length
                ? 'showing ${results.sessions.length} of ${results.sessionTotal}'
                : null,
          ),
          for (final session in results.sessions)
            SessionTile(session: session, showDate: true),
        ],
        if (results.categories.isNotEmpty) ...[
          const _SectionHeader(title: 'Categories'),
          for (final category in results.categories)
            ListTile(
              leading: const Icon(Icons.category_outlined),
              title: Text(category.name),
              subtitle: Text('${category.exhibitorCount} exhibitors'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () {
                // Dropping into browse mode filtered by the tapped category is
                // what the visitor meant by tapping it.
                _controller.clear();
                setState(() {
                  _query = '';
                  _categoryId = category.children.isEmpty ? null : category.id;
                  _subCategoryId =
                      category.children.isEmpty ? category.id : null;
                  _hallId = null;
                });
                _load();
              },
            ),
        ],
        if (results.halls.isNotEmpty) ...[
          const _SectionHeader(title: 'Halls'),
          for (final hall in results.halls) HallTile(hall: hall),
        ],
        const SizedBox(height: 24),
      ],
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, this.trailing});

  final String title;
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 20, 16, 6),
      child: Row(
        children: [
          Text(
            title,
            style: theme.textTheme.labelLarge
                ?.copyWith(color: theme.colorScheme.primary),
          ),
          const Spacer(),
          if (trailing != null)
            Text(
              trailing!,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
        ],
      ),
    );
  }
}

/// The category / sub-category / hall / country filters.
///
/// Sub-category is disabled until a category is chosen and is cleared when the
/// category changes, because "Weaving" under a different parent is a different
/// filter and leaving it set would silently return nothing.
class _FilterBar extends StatelessWidget {
  const _FilterBar({
    required this.categoryId,
    required this.subCategoryId,
    required this.hallId,
    required this.country,
    required this.onChanged,
    this.onClear,
  });

  final int? categoryId;
  final int? subCategoryId;
  final int? hallId;
  final String? country;
  final void Function(int? category, int? subCategory, int? hall, String? country)
      onChanged;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final subCategories = state.subCategoriesOf(categoryId);

    return SizedBox(
      height: 48,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12),
        children: [
          _FilterChip<int>(
            label: 'Category',
            value: categoryId,
            options: [
              for (final c in state.categories)
                (value: c.id, label: '${c.name} (${c.exhibitorCount})'),
            ],
            onSelected: (value) => onChanged(value, null, hallId, country),
          ),
          const SizedBox(width: 8),
          _FilterChip<int>(
            label: 'Sub-category',
            value: subCategoryId,
            enabled: subCategories.isNotEmpty,
            options: [
              for (final s in subCategories)
                (value: s.id, label: '${s.name} (${s.exhibitorCount})'),
            ],
            onSelected: (value) =>
                onChanged(categoryId, value, hallId, country),
          ),
          const SizedBox(width: 8),
          _FilterChip<int>(
            label: 'Hall',
            value: hallId,
            options: [
              for (final h in state.halls) (value: h.id, label: h.name),
            ],
            onSelected: (value) =>
                onChanged(categoryId, subCategoryId, value, country),
          ),
          const SizedBox(width: 8),
          _FilterChip<String>(
            label: 'Country',
            value: country,
            options: [
              for (final c in state.countries) (value: c, label: c),
            ],
            onSelected: (value) =>
                onChanged(categoryId, subCategoryId, hallId, value),
          ),
          if (onClear != null) ...[
            const SizedBox(width: 8),
            Center(
              child: ActionChip(
                avatar: const Icon(Icons.clear, size: 16),
                label: const Text('Clear'),
                onPressed: onClear,
              ),
            ),
          ],
          const SizedBox(width: 12),
        ],
      ),
    );
  }
}

class _FilterChip<T> extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.value,
    required this.options,
    required this.onSelected,
    this.enabled = true,
  });

  final String label;
  final T? value;
  final List<({T value, String label})> options;
  final ValueChanged<T?> onSelected;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    final selected = value != null;
    final selectedLabel = selected
        ? options
            .where((o) => o.value == value)
            .map((o) => o.label)
            .followedBy([label]).first
        : label;

    return Center(
      child: FilterChip(
        selected: selected,
        label: Text(
          selectedLabel.length > 28
              ? '${selectedLabel.substring(0, 26)}…'
              : selectedLabel,
        ),
        avatar: selected ? null : const Icon(Icons.expand_more, size: 18),
        onSelected: (!enabled || options.isEmpty)
            ? null
            : (_) => _showPicker(context),
      ),
    );
  }

  Future<void> _showPicker(BuildContext context) async {
    final chosen = await showModalBottomSheet<Choice<T>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: 0.6,
        maxChildSize: 0.9,
        builder: (context, controller) => ListView(
          controller: controller,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
              child: Text(label, style: Theme.of(context).textTheme.titleLarge),
            ),
            FilterOptionTile(
              label: 'Any',
              selected: value == null,
              onTap: () => Navigator.pop(context, Choice<T>(null)),
            ),
            for (final option in options)
              FilterOptionTile(
                label: option.label,
                selected: option.value == value,
                onTap: () => Navigator.pop(context, Choice<T>(option.value)),
              ),
          ],
        ),
      ),
    );

    if (chosen != null) onSelected(chosen.value);
  }
}
