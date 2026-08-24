import 'dart:async';

import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/choice.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';

const _kinds = <String>[
  'Lecture',
  'Meeting',
  'Workshop',
  'Panel',
  'Demo',
  'Ceremony',
];

/// Meetings and lectures: a day at a time, in time order, with filters.
///
/// The day tabs come from the programme itself rather than from the exhibition
/// days, so a show whose conference runs on two of its three days does not
/// present an empty tab.
class ProgrammeScreen extends StatefulWidget {
  const ProgrammeScreen({
    super.key,
    this.fixedHallId,
    this.fixedHallName,
  });

  /// Set when opened from a hall, which pins the filter and changes the title.
  final int? fixedHallId;
  final String? fixedHallName;

  @override
  State<ProgrammeScreen> createState() => _ProgrammeScreenState();
}

class _ProgrammeScreenState extends State<ProgrammeScreen> {
  final _searchController = TextEditingController();
  Timer? _debounce;

  DateTime? _date;
  String? _kind;
  int? _categoryId;
  bool _agendaOnly = false;
  String _query = '';

  List<Session> _sessions = const [];
  bool _loading = true;
  String? _error;

  bool get _isEmbedded => widget.fixedHallId != null;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final state = AppScope.read(context);
      final dates = state.programmeDates;

      // Open on today when the show is running, otherwise on the first day —
      // the visitor looking at this on the train the night before wants day one,
      // not an empty screen.
      final today = state.exhibition?.today;
      _date = dates.firstWhere(
        (d) => today != null && _sameDay(d, today),
        orElse: () => dates.isEmpty ? DateTime.now() : dates.first,
      );

      _load();
    });
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  static bool _sameDay(DateTime a, DateTime b) =>
      a.year == b.year && a.month == b.month && a.day == b.day;

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final page = await AppScope.read(context).api.sessions(
            query: _query.isEmpty ? null : _query,
            // Searching and the agenda both span the whole show; pinning them to
            // one day would hide the result the visitor is looking for.
            date: (_query.isEmpty && !_agendaOnly) ? _date : null,
            kind: _kind,
            hallId: widget.fixedHallId,
            categoryId: _categoryId,
            bookmarkedOnly: _agendaOnly,
          );

      if (!mounted) return;
      setState(() {
        _sessions = page.items;
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

  void _onQueryChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (!mounted) return;
      setState(() => _query = value.trim());
      _load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final dates = state.programmeDates;
    final theme = Theme.of(context);

    final showDayTabs = _query.isEmpty && !_agendaOnly && dates.length > 1;

    return Scaffold(
      appBar: AppBar(
        title: Text(_isEmbedded
            ? 'What is on · ${widget.fixedHallName}'
            : 'Meetings & lectures'),
        automaticallyImplyLeading: _isEmbedded,
        actions: [
          IconButton(
            tooltip: _agendaOnly ? 'Show all sessions' : 'My agenda only',
            icon: Icon(_agendaOnly ? Icons.bookmark : Icons.bookmark_border),
            onPressed: () {
              setState(() => _agendaOnly = !_agendaOnly);
              _load();
            },
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: 'Talk, speaker or room…',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _searchController.text.isEmpty
                    ? null
                    : IconButton(
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _searchController.clear();
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
          if (showDayTabs)
            SizedBox(
              height: 44,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 12),
                itemCount: dates.length,
                separatorBuilder: (_, __) => const SizedBox(width: 8),
                itemBuilder: (context, index) {
                  final date = dates[index];
                  final selected = _date != null && _sameDay(date, _date!);

                  return Center(
                    child: ChoiceChip(
                      selected: selected,
                      label: Text(_dayLabel(date, state.exhibition?.today)),
                      onSelected: (_) {
                        setState(() => _date = date);
                        _load();
                      },
                    ),
                  );
                },
              ),
            ),
          SizedBox(
            height: 48,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 12),
              children: [
                Center(
                  child: FilterChip(
                    selected: _kind != null,
                    label: Text(_kind ?? 'Any kind'),
                    avatar: _kind == null
                        ? const Icon(Icons.expand_more, size: 18)
                        : null,
                    onSelected: (_) => _pickKind(),
                  ),
                ),
                const SizedBox(width: 8),
                Center(
                  child: FilterChip(
                    selected: _categoryId != null,
                    label: Text(_categoryLabel(state.categories)),
                    avatar: _categoryId == null
                        ? const Icon(Icons.expand_more, size: 18)
                        : null,
                    onSelected: (_) => _pickCategory(),
                  ),
                ),
                if (_kind != null || _categoryId != null) ...[
                  const SizedBox(width: 8),
                  Center(
                    child: ActionChip(
                      avatar: const Icon(Icons.clear, size: 16),
                      label: const Text('Clear'),
                      onPressed: () {
                        setState(() {
                          _kind = null;
                          _categoryId = null;
                        });
                        _load();
                      },
                    ),
                  ),
                ],
                const SizedBox(width: 12),
              ],
            ),
          ),
          Expanded(child: _body(theme)),
        ],
      ),
    );
  }

  String _categoryLabel(List<Category> categories) {
    if (_categoryId == null) return 'Any category';
    for (final category in categories) {
      if (category.id == _categoryId) return category.name;
    }
    return 'Category';
  }

  Widget _body(ThemeData theme) {
    if (_loading) return const Center(child: CircularProgressIndicator());

    if (_error != null) {
      return EmptyState(
        icon: Icons.cloud_off,
        title: 'Could not load the programme',
        detail: _error,
        actionLabel: 'Try again',
        onAction: _load,
      );
    }

    if (_sessions.isEmpty) {
      return EmptyState(
        icon: _agendaOnly ? Icons.bookmark_border : Icons.event_busy,
        title: _agendaOnly
            ? 'Your agenda is empty'
            : _query.isNotEmpty
                ? 'Nothing found for "$_query"'
                : 'Nothing on the programme here',
        detail: _agendaOnly
            ? 'Open a talk and tap Save to agenda to keep it here.'
            : 'Try another day, or clear the filters.',
        actionLabel: _agendaOnly ? 'Show all sessions' : null,
        onAction: _agendaOnly
            ? () {
                setState(() => _agendaOnly = false);
                _load();
              }
            : null,
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        itemCount: _sessions.length,
        separatorBuilder: (_, __) => const Divider(indent: 84),
        itemBuilder: (context, index) => SessionTile(
          session: _sessions[index],
          showDate: _query.isNotEmpty || _agendaOnly,
          onChanged: _load,
        ),
      ),
    );
  }

  Future<void> _pickKind() async {
    final chosen = await showModalBottomSheet<Choice<String>>(
      context: context,
      showDragHandle: true,
      builder: (context) => ListView(
        shrinkWrap: true,
        children: [
          FilterOptionTile(
            label: 'Any kind',
            selected: _kind == null,
            onTap: () => Navigator.pop(context, const Choice<String>(null)),
          ),
          for (final kind in _kinds)
            FilterOptionTile(
              label: kind,
              selected: kind == _kind,
              onTap: () => Navigator.pop(context, Choice<String>(kind)),
            ),
        ],
      ),
    );

    if (chosen == null || !mounted) return;
    setState(() => _kind = chosen.value);
    await _load();
  }

  Future<void> _pickCategory() async {
    final categories = AppScope.read(context).categories;

    final chosen = await showModalBottomSheet<Choice<int>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: 0.6,
        builder: (context, controller) => ListView(
          controller: controller,
          children: [
            FilterOptionTile(
              label: 'Any category',
              selected: _categoryId == null,
              onTap: () => Navigator.pop(context, const Choice<int>(null)),
            ),
            for (final category in categories)
              FilterOptionTile(
                label: category.name,
                selected: category.id == _categoryId,
                onTap: () => Navigator.pop(context, Choice<int>(category.id)),
              ),
          ],
        ),
      ),
    );

    if (chosen == null || !mounted) return;
    setState(() => _categoryId = chosen.value);
    await _load();
  }

  static String _dayLabel(DateTime date, DateTime? today) {
    if (today != null && _sameDay(date, today)) return 'Today';

    const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const months = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ];
    return '${days[date.weekday - 1]} ${date.day} ${months[date.month - 1]}';
  }
}
